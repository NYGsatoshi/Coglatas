import { computed, effect, inject, Injectable, signal, untracked } from '@angular/core';
import { firstValueFrom, Subject } from 'rxjs';

import { AuthSessionFacade } from '../auth/auth-session.facade';
import { FrontendFeatureFlagsService } from '../feature-flags/frontend-feature-flags.service';
import { NotificationOpenContextService } from '../notifications/notification-open-context.service';
import { ActiveWorkspaceFacade } from '../workspace/active-workspace.facade';
import { validateDurableRealtimeEvent } from './realtime-event.validator';
import {
  DurableRealtimeEvent,
  RealtimeConnectionState,
  RealtimeDiagnostic,
  RealtimeSubscriptionRequest,
  RealtimeSubscriptionResult
} from './realtime.models';
import { COGLATAS_REALTIME_TRANSPORT, RealtimeTransport, RealtimeTransportStatus } from './realtime-transport';
import { SignalrRealtimeTransport } from './signalr-realtime.transport';

export interface RealtimeCatchUpContext {
  readonly deniedOwners: ReadonlySet<string>;
}

export type RealtimeCatchUpCallback = (context: RealtimeCatchUpContext) => Promise<void> | void;
export type RealtimeStaleEventGuard = (event: DurableRealtimeEvent) => boolean;
export type ProtectedStateClearReason = 'session' | 'tenant' | 'authorization' | 'workspace';
export type ProtectedStateClearer = (reason: ProtectedStateClearReason) => void;

interface SubscriptionEntry {
  readonly request: RealtimeSubscriptionRequest;
  readonly owners: Set<string>;
}

interface SubscriptionAttempt {
  readonly entry: SubscriptionEntry;
  readonly allowed: boolean;
}

interface CatchUpAttempt {
  readonly callback: RealtimeCatchUpCallback;
  readonly denied: boolean;
}

interface PendingRealtimeEvent {
  readonly epoch: number;
  readonly event: DurableRealtimeEvent;
}

const CORE_OWNER = 'core-session';
const MAX_DEDUP_EVENTS = 256;
const MAX_PENDING_EVENTS = 512;

/**
 * Coglatas-owned boundary for the one internal SignalR connection. Feature
 * facades register logical resource subscriptions and authoritative catch-up
 * callbacks here; they never receive HubConnection callbacks.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeFacade {
  private readonly authSession = inject(AuthSessionFacade);
  private readonly activeWorkspace = inject(ActiveWorkspaceFacade);
  private readonly notificationOpenContext = inject(NotificationOpenContextService);
  private readonly flags = inject(FrontendFeatureFlagsService);
  private readonly transport = inject(COGLATAS_REALTIME_TRANSPORT, { optional: true }) ?? inject(SignalrRealtimeTransport);
  private readonly state = signal<RealtimeConnectionState>('Degraded');
  private readonly authorizationRevisionState = signal(0);
  private readonly events = new Subject<DurableRealtimeEvent>();
  private readonly diagnosticsSubject = new Subject<RealtimeDiagnostic>();
  // Feature registrations are declarative intent. They survive a transient
  // authorization loss so a later reconnect can ask the server again. The
  // separate set represents only groups authorized on the current transport.
  private readonly desiredSubscriptions = new Map<string, SubscriptionEntry>();
  private readonly authorizedSubscriptionKeys = new Set<string>();
  private readonly authorizationInFlight = new Map<string, Promise<RealtimeSubscriptionResult>>();
  private readonly catchUps = new Map<string, RealtimeCatchUpCallback>();
  private readonly staleGuards = new Map<string, RealtimeStaleEventGuard>();
  private readonly protectedStateClearers = new Map<string, ProtectedStateClearer>();
  private readonly dedup = new Map<string, number>();
  private readonly aggregateVersions = new Map<string, number>();
  private readonly pendingEvents: PendingRealtimeEvent[] = [];
  private tenantId: string | null = null;
  private starting: Promise<void> | null = null;
  private startingEpoch: number | null = null;
  private acceptingEventEpoch: number | null = null;
  private stopping: Promise<void> | null = null;
  private intentionallyStopped = false;
  private synchronizationEpoch = 0;

  readonly connectionState = this.state.asReadonly();
  /**
   * Advances only when server-derived authorization is explicitly invalidated.
   * Transport reconnect/degradation does not change this revision.
   */
  readonly authorizationRevision = this.authorizationRevisionState.asReadonly();
  readonly isEnabled = this.flags.realtimeSignalREnabled;
  readonly isSynchronized = computed(() => this.state() === 'Connected');
  readonly durableEvents$ = this.events.asObservable();
  readonly diagnostics$ = this.diagnosticsSubject.asObservable();

  constructor() {
    this.transport.durableEvents$.subscribe((event) => this.processEvent(event));
    this.transport.authorizationInvalidations$.subscribe(() => this.clearForAuthorizationLoss());
    this.transport.statuses$.subscribe((status) => this.handleTransportStatus(status));

    if (typeof window !== 'undefined') {
      window.addEventListener('offline', () => {
        if (!this.intentionallyStopped) {
          this.invalidateSynchronization();
          this.authorizedSubscriptionKeys.clear();
          this.state.set('Offline');
        }
      });
      window.addEventListener('online', () => {
        if (!this.intentionallyStopped && this.canConnect()) {
          void this.connectAndSynchronize();
        }
      });
    }

    effect(() => {
      const tenant = this.authSession.currentTenant();
      const authenticated = this.authSession.isAuthenticated();
      const enabled = this.flags.realtimeSignalREnabled();
      if (!authenticated) {
        this.stopForSessionBoundary();
        return;
      }

      // AuthSession hydrates the authenticated user (and its canonical current
      // Workspace) before /api/tenants/current completes. That short-lived
      // tenant-null state is not a logout, Tenant switch, or authorization
      // invalidation, so it must not clear protected HTTP-owned state.
      if (!tenant) {
        this.stopRealtimeTransportOnly();
        return;
      }

      if (!tenant.isAvailable) {
        this.stopForSessionBoundary();
        return;
      }

      if (this.tenantId && this.tenantId !== tenant.tenantId) {
        this.clearForTenantBoundary();
      }
      this.tenantId = tenant.tenantId;

      // The rollout flag controls only the SignalR transport. HTTP remains
      // authoritative and its valid feature state is not a session, tenant,
      // or authorization boundary.
      if (!enabled) {
        this.stopRealtimeTransportOnly();
        return;
      }

      void this.connectAndSynchronize(true);
    });
  }

  registerSubscription(owner: string, request: RealtimeSubscriptionRequest): () => void {
    validateSubscription(request);
    const key = subscriptionKey(request);
    const existing = this.desiredSubscriptions.get(key);
    if (existing) {
      existing.owners.add(owner);
    } else {
      this.desiredSubscriptions.set(key, { request, owners: new Set([owner]) });
    }

    if (this.isSynchronized()) {
      const authorizationEpoch = this.synchronizationEpoch;
      void this.authorizeSubscription(request, authorizationEpoch).catch(() =>
        this.recoverTransportAfterGroupOperationFailure(authorizationEpoch));
    }
    return () => this.removeSubscription(owner, request);
  }

  registerCatchUp(owner: string, callback: RealtimeCatchUpCallback): () => void {
    this.catchUps.set(owner, callback);
    return () => this.catchUps.delete(owner);
  }

  registerStaleEventGuard(owner: string, guard: RealtimeStaleEventGuard): () => void {
    this.staleGuards.set(owner, guard);
    return () => this.staleGuards.delete(owner);
  }

  /**
   * Registers a feature-owned clear operation. Authorization loss is handled
   * before transport reauthorization/catch-up, so no protected projection is
   * left visible while an old SignalR group is being replaced.
   */
  registerProtectedStateClearer(owner: string, clear: ProtectedStateClearer): () => void {
    this.protectedStateClearers.set(owner, clear);
    return () => this.protectedStateClearers.delete(owner);
  }

  /**
   * Revalidates mounted protected projections when SignalR could not recover
   * but the authoritative Workspace HTTP request did. Each feature catch-up
   * still performs its own server-authorized HTTP read; this method does not
   * infer resource access from the Workspace list.
   */
  async runAuthoritativeHttpCatchUps(): Promise<void> {
    try {
      await this.runCatchUpsUntilStable(new Set<string>(), new Map<string, CatchUpAttempt>());
    } catch {}
  }

  /** Called by session/tenant lifecycle owners before protected state changes. */
  clearForTenantBoundary(): void {
    // A new Tenant must never inherit Workspace/context/subscription state
    // from the old Tenant. This is distinct from a temporary authorization
    // invalidation, which preserves declarative subscription intent.
    this.invalidateSynchronization();
    this.clearTenantBoundaryState();
    this.intentionallyStopped = true;
    void this.stopTransport();
    this.state.set('Degraded');
  }

  /**
   * Clears the protected projections owned by the previous Workspace without
   * tearing down the authenticated Tenant transport. Resource subscriptions
   * are route/context intent, so they must be removed before a new Workspace
   * can register its own scope. Server-derived user and Tenant subscriptions
   * remain live for the same authenticated Tenant.
   */
  clearForWorkspaceBoundary(): void {
    // A synchronization may already have queued a frame for the Workspace
    // being left. The same Tenant transport remains live, but only frames
    // accepted after this boundary may be replayed into the replacement scope.
    this.pendingEvents.length = 0;
    // Revoke resource intent before feature clearers run. Some clearers release
    // their own registrations; removing the authoritative entries first keeps
    // that cleanup from hiding an already-authorized group from this walk.
    for (const [key, entry] of [...this.desiredSubscriptions.entries()]) {
      if (!isWorkspaceBoundSubscription(entry.request)) {
        continue;
      }

      this.desiredSubscriptions.delete(key);
      for (const owner of entry.owners) {
        // Resource catch-up/ordering callbacks close over the old route
        // context. Tenant/user callbacks use distinct owners and remain live.
        this.catchUps.delete(owner);
        this.staleGuards.delete(owner);
      }
      if (this.authorizedSubscriptionKeys.delete(key)) {
        void this.unsubscribeSafely(entry.request);
      }
    }

    this.clearProtectedApplicationState('workspace');

    // Event ordering is projection-local. A new Workspace must not inherit a
    // previous Workspace's bounded dedupe/version decisions, while the current
    // user/Tenant authorization groups remain intact.
    this.dedup.clear();
    this.aggregateVersions.clear();
  }

  clearForAuthorizationLoss(): void {
    this.invalidateSynchronization();
    const recoveryEpoch = this.synchronizationEpoch;
    this.authorizationRevisionState.update((revision) => revision + 1);
    // Keep declarative subscription intents so a reconnect explicitly
    // reauthorizes each one. Clearing only the map would make a lost group
    // silently disappear without a current HTTP/catch-up decision.
    this.clearAuthorizationInvalidationState();
    this.state.set('Reconnecting');
    this.intentionallyStopped = true;
    void this.stopTransport().then(async () => {
      await this.refreshSessionAfterConnectionFailure();
      if (recoveryEpoch !== this.synchronizationEpoch) {
        return;
      }
      if (!this.canConnect()) {
        this.state.set('Degraded');
        return;
      }

      this.intentionallyStopped = false;
      await this.connectAndSynchronize();
    });
  }

  private async connectAndSynchronize(resumeStopped = false): Promise<void> {
    if (!this.canConnect()) {
      return;
    }
    if (resumeStopped) {
      // The root auth/Tenant/flag effect is the authority for resuming an
      // intentional lifecycle stop. Mark that resume before waiting for a
      // superseded synchronization so its eventual completion can start the
      // current epoch instead of leaving the transport permanently degraded.
      this.intentionallyStopped = false;
    }
    if (this.intentionallyStopped || this.isSynchronized()) {
      return;
    }
    if (this.starting) {
      const pending = this.starting;
      const pendingEpoch = this.startingEpoch;
      await pending;
      if (
        pendingEpoch !== null &&
        pendingEpoch !== this.synchronizationEpoch &&
        !this.intentionallyStopped &&
        !this.isSynchronized() &&
        this.canConnect()
      ) {
        await this.connectAndSynchronize();
      }
      return;
    }

    const epoch = this.synchronizationEpoch;
    const synchronization = this.beginSynchronization(epoch);
    this.starting = synchronization;
    this.startingEpoch = epoch;
    try {
      await synchronization;
    } finally {
      if (this.starting === synchronization) {
        this.starting = null;
        this.startingEpoch = null;
      }
    }
  }

  private async beginSynchronization(epoch: number): Promise<void> {
    try {
      if (this.stopping) {
        await this.stopping;
      }
      if (!this.isSynchronizationCurrent(epoch)) {
        return;
      }
      await this.transport.start();
      if (!this.isSynchronizationCurrent(epoch)) {
        return;
      }
      // Only frames observed after this epoch owns a started transport can be
      // replayed. A replacement lifecycle can set startingEpoch while the
      // previous connection is still draining its stop.
      this.acceptingEventEpoch = epoch;

      const currentUserResult = await this.authorizeSubscription(
        { subscriptionType: 'user' },
        epoch,
      );
      if (!this.isSynchronizationCurrent(epoch)) {
        return;
      }
      if (!currentUserResult.allowed) {
        this.clearForAuthorizationLoss();
        return;
      }

      const deniedOwners = new Set<string>();
      const attemptedEntries = new Map<string, SubscriptionAttempt>();
      const attemptedCatchUps = new Map<string, CatchUpAttempt>();
      await this.synchronizeSubscriptionsAndCatchUps(
        attemptedEntries,
        attemptedCatchUps,
        deniedOwners,
        epoch,
      );
      if (this.isSynchronizationCurrent(epoch)) {
        this.flushPendingEvents(epoch);
      }
      if (this.isSynchronizationCurrent(epoch)) {
        this.state.set('Connected');
      }
    } catch {
      if (!this.isSynchronizationCurrent(epoch)) {
        return;
      }
      this.diagnosticsSubject.next({ code: 'ConnectionFailed' });
      await this.refreshSessionAfterConnectionFailure();
      if (this.isSynchronizationCurrent(epoch)) {
        this.acceptingEventEpoch = null;
        this.pendingEvents.length = 0;
        this.state.set(this.httpIsLikelyAvailable() ? 'Degraded' : 'Offline');
      }
    }
  }

  private async drainDesiredSubscriptions(
    attemptedEntries: Map<string, SubscriptionAttempt>,
    epoch: number,
  ): Promise<void> {
    while (true) {
      if (!this.isSynchronizationCurrent(epoch)) {
        return;
      }
      let found = false;
      for (const [key, entry] of [...this.desiredSubscriptions.entries()]) {
        if (!this.isSynchronizationCurrent(epoch)) {
          return;
        }
        if (entry.request.subscriptionType === 'user') {
          continue;
        }

        const previousAttempt = attemptedEntries.get(key);
        if (previousAttempt?.entry === entry) {
          continue;
        }

        found = true;
        const result = await this.authorizeSubscription(entry.request, epoch);
        if (!this.isSynchronizationCurrent(epoch)) {
          return;
        }
        attemptedEntries.set(key, { entry, allowed: result.allowed });
      }

      if (!found) {
        return;
      }
    }
  }

  private async synchronizeSubscriptionsAndCatchUps(
    attemptedEntries: Map<string, SubscriptionAttempt>,
    attemptedCatchUps: Map<string, CatchUpAttempt>,
    deniedOwners: Set<string>,
    epoch: number,
  ): Promise<void> {
    while (true) {
      await this.drainDesiredSubscriptions(attemptedEntries, epoch);
      if (!this.isSynchronizationCurrent(epoch)) {
        return;
      }
      this.rebuildDeniedOwners(attemptedEntries, deniedOwners);
      await this.runPendingCatchUps(deniedOwners, attemptedCatchUps, epoch);
      if (!this.isSynchronizationCurrent(epoch)) {
        return;
      }

      const hasPendingSubscription = [...this.desiredSubscriptions.entries()].some(
        ([key, entry]) =>
          entry.request.subscriptionType !== 'user' &&
          attemptedEntries.get(key)?.entry !== entry,
      );
      const hasPendingCatchUp = [...this.catchUps.entries()].some(([owner, callback]) => {
        const attempted = attemptedCatchUps.get(owner);
        return attempted?.callback !== callback || attempted.denied !== deniedOwners.has(owner);
      });
      if (!hasPendingSubscription && !hasPendingCatchUp) {
        return;
      }
    }
  }

  private rebuildDeniedOwners(
    attemptedEntries: ReadonlyMap<string, SubscriptionAttempt>,
    deniedOwners: Set<string>,
  ): void {
    deniedOwners.clear();
    for (const [key, entry] of this.desiredSubscriptions) {
      const attempt = attemptedEntries.get(key);
      if (attempt?.entry !== entry || attempt.allowed) {
        continue;
      }
      for (const owner of entry.owners) {
        deniedOwners.add(owner);
      }
    }
  }

  private async authorizeSubscription(
    request: RealtimeSubscriptionRequest,
    epoch?: number,
  ): Promise<RealtimeSubscriptionResult> {
    const key = subscriptionKey(request);
    // A resource registered while Connected can still be authorizing when a
    // stop/reconnect starts the replacement epoch. Serialize the same logical
    // group so a stale grant is fully removed before its fresh authorization
    // can add the group again.
    while (true) {
      const pending = this.authorizationInFlight.get(key);
      if (!pending) {
        break;
      }
      try {
        await pending;
      } catch {
        // The current attempt below remains authoritative for its own epoch.
      }
    }
    if (epoch !== undefined && !this.isSynchronizationCurrent(epoch)) {
      return { allowed: false, code: 'SynchronizationSuperseded' };
    }

    const authorization = this.performAuthorization(request, epoch);
    this.authorizationInFlight.set(key, authorization);
    try {
      return await authorization;
    } finally {
      if (this.authorizationInFlight.get(key) === authorization) {
        this.authorizationInFlight.delete(key);
      }
    }
  }

  private async performAuthorization(
    request: RealtimeSubscriptionRequest,
    epoch?: number,
  ): Promise<RealtimeSubscriptionResult> {
    const result = await this.transport.subscribe(request);
    const key = subscriptionKey(request);
    if (epoch !== undefined && !this.isSynchronizationCurrent(epoch)) {
      if (result.allowed) {
        // A late allowed result can add any server group, including the
        // server-derived user/Tenant groups, after a stop or authorization
        // boundary. Remove every stale grant before a fresh epoch starts.
        await this.removeStaleGrant(request);
      }
      return result;
    }
    if (result.allowed && isWorkspaceBoundSubscription(request) && !this.desiredSubscriptions.has(key)) {
      // The route/Workspace boundary may have removed this intent while the
      // server authorization round-trip was in flight. Do not leave an
      // orphaned resource group authorized after its owner was cleared.
      await this.unsubscribeSafely(request);
      this.authorizedSubscriptionKeys.delete(key);
      return result;
    }
    if (!result.allowed) {
      this.diagnosticsSubject.next({ code: 'SubscriptionDenied' });
      // A denial revokes only the current transport group. The feature owner
      // still exists and must be reauthorized on a later reconnect.
      this.authorizedSubscriptionKeys.delete(key);
    } else {
      this.authorizedSubscriptionKeys.add(key);
    }
    return result;
  }

  private async removeStaleGrant(request: RealtimeSubscriptionRequest): Promise<void> {
    if (!isServerDerivedSubscription(request)) {
      await this.unsubscribeSafely(request);
      return;
    }

    // User and Tenant resource IDs are deliberately server-derived, and the
    // hub exposes no client-callable unsubscribe method for those groups. A
    // transport stop is therefore the only trustworthy way to discard a late
    // grant. If a newer lifecycle boundary already owns the stop/recovery,
    // join it without invalidating that boundary's recovery epoch.
    if (this.intentionallyStopped) {
      await this.stopTransport();
      return;
    }

    this.invalidateSynchronization();
    const restartEpoch = this.synchronizationEpoch;
    const shouldResume = this.canConnect();
    this.intentionallyStopped = true;
    this.clearTransportAuthorizationState();
    this.state.set(this.httpIsLikelyAvailable() ? 'Reconnecting' : 'Offline');
    await this.stopTransport();

    if (restartEpoch !== this.synchronizationEpoch || !shouldResume || !this.canConnect()) {
      return;
    }

    this.intentionallyStopped = false;
    if (!this.httpIsLikelyAvailable()) {
      this.state.set('Offline');
      return;
    }
    void this.connectAndSynchronize();
  }

  private async unsubscribeSafely(request: RealtimeSubscriptionRequest): Promise<void> {
    const cleanupEpoch = this.synchronizationEpoch;
    try {
      await this.transport.unsubscribe(request);
    } catch {
      // A lifecycle stop or a newer synchronization already discarded (or is
      // replacing) the failed operation's groups. A failure on the still-live
      // transport is different: the server may have committed a subscribe or
      // retained an unsubscribe target before the invocation response failed.
      // Reset that transport so no orphaned resource group can remain active.
      await this.recoverTransportAfterGroupOperationFailure(cleanupEpoch);
    }
  }

  private async recoverTransportAfterGroupOperationFailure(operationEpoch: number): Promise<void> {
    if (!this.isSynchronizationCurrent(operationEpoch)) {
      return;
    }

    this.invalidateSynchronization();
    const recoveryEpoch = this.synchronizationEpoch;
    const shouldResume = this.canConnect();
    this.intentionallyStopped = true;
    this.clearTransportAuthorizationState();
    this.state.set(this.httpIsLikelyAvailable() ? 'Reconnecting' : 'Offline');
    await this.stopTransport();

    if (recoveryEpoch !== this.synchronizationEpoch || !shouldResume || !this.canConnect()) {
      return;
    }

    this.intentionallyStopped = false;
    if (!this.httpIsLikelyAvailable()) {
      this.state.set('Offline');
      return;
    }

    await this.connectAndSynchronize();
  }

  private async runCatchUpsUntilStable(
    deniedOwners: Set<string>,
    attemptedCatchUps: Map<string, CatchUpAttempt>,
  ): Promise<void> {
    while (true) {
      const ran = await this.runPendingCatchUps(deniedOwners, attemptedCatchUps);
      const hasPending = [...this.catchUps.entries()].some(([owner, callback]) => {
        const attempted = attemptedCatchUps.get(owner);
        return attempted?.callback !== callback || attempted.denied !== deniedOwners.has(owner);
      });
      if (!ran && !hasPending) {
        return;
      }
    }
  }

  private async runPendingCatchUps(
    deniedOwners: Set<string>,
    attemptedCatchUps: Map<string, CatchUpAttempt>,
    epoch?: number,
  ): Promise<boolean> {
    let failed = false;
    let ran = false;
    // Registration order is the feature-defined authoritative reconciliation order.
    // Every callback must observe a denied owner even when an unrelated HTTP
    // reconciliation fails first, so one feature cannot prevent another from
    // clearing protected state.
    for (const [owner, callback] of [...this.catchUps.entries()]) {
      if (epoch !== undefined && !this.isSynchronizationCurrent(epoch)) {
        return ran;
      }
      // A route can replace its catch-up while an earlier callback is awaited.
      // Never invoke the stale snapshot against the replacement route state;
      // the stable loop will observe and run the current callback next.
      if (this.catchUps.get(owner) !== callback) {
        continue;
      }
      const denied = deniedOwners.has(owner);
      const attempted = attemptedCatchUps.get(owner);
      if (attempted?.callback === callback && attempted.denied === denied) {
        continue;
      }

      ran = true;
      try {
        await callback({ deniedOwners });
      } catch {
        failed = true;
      }
      if (epoch !== undefined && !this.isSynchronizationCurrent(epoch)) {
        return ran;
      }
      attemptedCatchUps.set(owner, { callback, denied });
    }
    if (failed) {
      this.diagnosticsSubject.next({ code: 'CatchUpFailed' });
      throw new Error('Realtime catch-up could not complete.');
    }
    return ran;
  }

  private processEvent(value: unknown): void {
    const tenantId = this.tenantId;
    if (!tenantId) {
      return;
    }

    const event = validateDurableRealtimeEvent(value, tenantId);
    if (!event) {
      this.diagnosticsSubject.next({ code: 'UnsupportedEvent' });
      return;
    }
    // Authorization invalidation is a fail-closed control frame, not a feature
    // projection. It must remain actionable while a reconnect/catch-up is in
    // progress; otherwise a queued revocation could be lost immediately before
    // the facade publishes Connected under stale authority.
    if (event.eventType === 'Security.AuthorizationStateChanged.v1') {
      this.clearForAuthorizationLoss();
      return;
    }
    if (!this.isSynchronized()) {
      if (this.startingEpoch === this.synchronizationEpoch &&
          this.acceptingEventEpoch === this.synchronizationEpoch &&
          !this.intentionallyStopped &&
          this.canConnect()) {
        if (this.pendingEvents.length >= MAX_PENDING_EVENTS) {
          // Losing any post-catch-up event can leave a mounted projection
          // stale. Fail closed into a fresh authorization/HTTP reconciliation
          // instead of silently dropping an unbounded queue.
          this.diagnosticsSubject.next({ code: 'ConnectionFailed' });
          this.clearForAuthorizationLoss();
          return;
        }
        this.pendingEvents.push({ epoch: this.synchronizationEpoch, event });
      }
      // A reconnect/catch-up owns projection recovery. Frames received after
      // the current transport starts are replayed after every catch-up; frames
      // from a stopped/superseded connection are discarded.
      return;
    }
    this.publishEvent(event);
  }

  private flushPendingEvents(epoch: number): void {
    const pending = this.pendingEvents.splice(0);
    for (const item of pending) {
      if (item.epoch !== epoch || !this.isSynchronizationCurrent(epoch)) {
        continue;
      }
      this.publishEvent(item.event);
    }
  }

  private publishEvent(event: DurableRealtimeEvent): void {
    if (this.isDuplicate(event.eventId)) {
      this.diagnosticsSubject.next({ code: 'DuplicateEvent', eventId: event.eventId, eventType: event.eventType });
      return;
    }
    if (this.isStale(event)) {
      this.diagnosticsSubject.next({ code: 'StaleEvent', eventId: event.eventId, eventType: event.eventType });
      return;
    }

    this.events.next(event);
  }

  private isDuplicate(eventId: string): boolean {
    if (this.dedup.has(eventId)) {
      return true;
    }
    this.dedup.set(eventId, Date.now());
    if (this.dedup.size > MAX_DEDUP_EVENTS) {
      this.dedup.delete(this.dedup.keys().next().value as string);
    }
    return false;
  }

  private isStale(event: DurableRealtimeEvent): boolean {
    if ([...this.staleGuards.values()].some((guard) => !guard(event))) {
      return true;
    }
    if (event.aggregateVersion === null) {
      return false;
    }

    const key = `${event.aggregateType}:${event.aggregateId}`;
    const currentVersion = this.aggregateVersions.get(key);
    if (currentVersion !== undefined && event.aggregateVersion <= currentVersion) {
      return true;
    }
    this.aggregateVersions.set(key, event.aggregateVersion);
    return false;
  }

  private removeSubscription(owner: string, request: RealtimeSubscriptionRequest): void {
    const key = subscriptionKey(request);
    const entry = this.desiredSubscriptions.get(key);
    if (!entry) {
      return;
    }
    entry.owners.delete(owner);
    if (entry.owners.size > 0) {
      return;
    }
    this.desiredSubscriptions.delete(key);
    if (this.authorizedSubscriptionKeys.delete(key) &&
        request.subscriptionType !== 'user' &&
        request.subscriptionType !== 'tenant') {
      void this.unsubscribeSafely(request);
    }
  }

  private handleTransportStatus(status: RealtimeTransportStatus): void {
    if (this.intentionallyStopped || this.stopping) {
      return;
    }
    if (status === 'reconnecting' || status === 'connecting') {
      if (status === 'reconnecting') {
        this.invalidateSynchronization();
      }
      this.authorizedSubscriptionKeys.clear();
      this.state.set('Reconnecting');
      return;
    }
    if (status === 'reconnected') {
      this.invalidateSynchronization();
      this.authorizedSubscriptionKeys.clear();
      this.state.set('Reconnecting');
      void this.connectAndSynchronize();
      return;
    }
    if (status === 'closed') {
      this.invalidateSynchronization();
      this.authorizedSubscriptionKeys.clear();
      this.state.set(this.httpIsLikelyAvailable() ? 'Degraded' : 'Offline');
    }
  }

  private clearProtectedApplicationState(reason: ProtectedStateClearReason): void {
    this.activeWorkspace.clearWorkspace();
    this.notificationOpenContext.clear();
    for (const clear of [...this.protectedStateClearers.values()]) {
      try {
        // This can run from the root authorization effect. Feature clearers
        // commonly read and replace their own signals, which must not become
        // dependencies of that effect or re-enter a terminal clear boundary.
        untracked(() => clear(reason));
      } catch {
        // A feature clear must not block the security boundary for another
        // feature. Its next authoritative catch-up will recover its state.
      }
    }
  }

  private clearTransportAuthorizationState(): void {
    this.authorizedSubscriptionKeys.clear();
    this.dedup.clear();
    this.aggregateVersions.clear();
  }

  private clearSessionBoundaryState(): void {
    this.clearProtectedApplicationState('session');
    this.desiredSubscriptions.clear();
    this.clearTransportAuthorizationState();
    this.tenantId = null;
  }

  private clearTenantBoundaryState(): void {
    this.clearProtectedApplicationState('tenant');
    this.desiredSubscriptions.clear();
    this.clearTransportAuthorizationState();
    this.tenantId = null;
  }

  private clearAuthorizationInvalidationState(): void {
    this.clearProtectedApplicationState('authorization');
    this.clearTransportAuthorizationState();
  }

  private stopForSessionBoundary(): void {
    this.invalidateSynchronization();
    this.clearSessionBoundaryState();
    this.intentionallyStopped = true;
    void this.stopTransport();
    this.state.set('Degraded');
  }

  /**
   * Stops only the unavailable transport. A disabled realtime rollout or the
   * authenticated tenant-hydration interval is not a logout, Tenant change,
   * or authorization revocation, so HTTP-owned application state and
   * declarative subscriptions must remain intact.
   */
  private stopRealtimeTransportOnly(): void {
    this.invalidateSynchronization();
    this.intentionallyStopped = true;
    this.clearTransportAuthorizationState();
    void this.stopTransport();
    this.state.set('Degraded');
  }

  private canConnect(): boolean {
    const currentTenant = this.authSession.currentTenant();
    return this.flags.realtimeSignalREnabled() &&
      this.authSession.isAuthenticated() &&
      !!this.tenantId &&
      currentTenant?.isAvailable === true &&
      currentTenant.tenantId === this.tenantId;
  }

  private isSynchronizationCurrent(epoch: number): boolean {
    return epoch === this.synchronizationEpoch && !this.intentionallyStopped && this.canConnect();
  }

  private invalidateSynchronization(): void {
    this.synchronizationEpoch++;
    this.acceptingEventEpoch = null;
    this.pendingEvents.length = 0;
  }

  private async refreshSessionAfterConnectionFailure(): Promise<void> {
    try {
      await firstValueFrom(this.authSession.refreshSessionContext());
    } catch {
      // HTTP remains the authority; a failed probe simply preserves degraded mode.
    }
  }

  private async stopTransport(): Promise<void> {
    if (this.stopping) {
      return this.stopping;
    }

    const stop = this.transport.stop().catch(() => undefined);
    this.stopping = stop;
    try {
      await stop;
    } finally {
      if (this.stopping === stop) {
        this.stopping = null;
      }
    }
  }

  private httpIsLikelyAvailable(): boolean {
    return typeof navigator === 'undefined' || navigator.onLine;
  }
}

function validateSubscription(request: RealtimeSubscriptionRequest): void {
  const requiresResource = request.subscriptionType === 'workspace' || request.subscriptionType === 'conversation' || request.subscriptionType === 'project';
  if (requiresResource && !request.resourceId) {
    throw new Error(`Realtime ${request.subscriptionType} subscription requires an opaque resource ID.`);
  }
  if ((request.subscriptionType === 'user' || request.subscriptionType === 'tenant') && request.resourceId) {
    throw new Error(`Realtime ${request.subscriptionType} subscription is server-derived and does not accept a resource ID.`);
  }
}

function subscriptionKey(request: RealtimeSubscriptionRequest): string {
  return `${request.subscriptionType}:${request.resourceId ?? 'server-derived'}`;
}

function isWorkspaceBoundSubscription(request: RealtimeSubscriptionRequest): boolean {
  return request.subscriptionType === 'workspace' ||
    request.subscriptionType === 'project' ||
    request.subscriptionType === 'conversation';
}

function isServerDerivedSubscription(request: RealtimeSubscriptionRequest): boolean {
  return request.subscriptionType === 'user' || request.subscriptionType === 'tenant';
}
