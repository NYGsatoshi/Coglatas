import { InjectionToken } from '@angular/core';
import { Observable } from 'rxjs';

import { RealtimeSubscriptionRequest, RealtimeSubscriptionResult } from './realtime.models';

export type RealtimeTransportStatus = 'connecting' | 'reconnecting' | 'reconnected' | 'closed';

export interface RealtimeTransport {
  readonly durableEvents$: Observable<unknown>;
  readonly authorizationInvalidations$: Observable<void>;
  readonly statuses$: Observable<RealtimeTransportStatus>;
  start(): Promise<void>;
  stop(): Promise<void>;
  subscribe(request: RealtimeSubscriptionRequest): Promise<RealtimeSubscriptionResult>;
  unsubscribe(request: RealtimeSubscriptionRequest): Promise<RealtimeSubscriptionResult>;
}

export const COGLATAS_REALTIME_TRANSPORT = new InjectionToken<RealtimeTransport>('COGLATAS_REALTIME_TRANSPORT');
