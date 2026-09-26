import {
  isSafeAnnouncementUrl,
  mapAnnouncementAudienceOption,
  mapAnnouncementDraft,
  mapAnnouncementDetail,
  mapAnnouncementListItem,
  toCreateAnnouncementRequest,
  toCreateAnnouncementDraftRequest,
  toScheduleAnnouncementDraftRequest,
} from './announcements.api';

describe('announcement API adapters', () => {
  it('maps list DTOs without presenting placeholder body as real detail', () => {
    const announcement = mapAnnouncementListItem({
      id: 'announcement-1',
      workspaceId: '11111111-1111-1111-1111-111111111111',
      title: 'Published notice',
      priority: 'Important',
      requiresReadConfirmation: true,
      isRead: false,
      publishedAt: '2026-07-07T00:00:00Z',
      expiresAt: '2026-07-08T00:00:00Z',
    });

    expect(announcement.id).toBe('announcement-1');
    expect(announcement.title).toBe('Published notice');
    expect(announcement.priority).toBe('important');
    expect(announcement.audienceScope).toBe('workspace');
    expect(announcement.body).toBe('');
    expect(announcement.detailState).toBe('notLoaded');
    expect(announcement.capabilities).toEqual(['readAnnouncement']);
    expect(announcement.readState.requiresReadConfirmation).toBe(true);
    expect(announcement.expiresAt).toBe('2026-07-08T00:00:00Z');
    expect(announcement.expiresAtLabel).toBeTruthy();
    expect(announcement.readState).not.toHaveProperty('confirmedAtLabel');
    expect(announcement.attachment).toBeUndefined();
  });

  it('uses the most specific backend scope for channel, group, workspace, and global announcements', () => {
    const workspaceId = '11111111-1111-1111-1111-111111111111';
    const groupId = '22222222-2222-2222-2222-222222222222';
    const channelId = '33333333-3333-3333-3333-333333333333';

    expect(mapAnnouncementListItem({ workspaceId, groupId, channelId }).audienceScope).toBe('channel');
    expect(mapAnnouncementListItem({ workspaceId, groupId }).audienceScope).toBe('group');
    expect(mapAnnouncementListItem({ workspaceId }).audienceScope).toBe('workspace');
    expect(mapAnnouncementListItem({}).audienceScope).toBe('global');
  });

  it('maps only complete authorization-filtered audience options', () => {
    expect(mapAnnouncementAudienceOption({
      key: 'group:22222222-2222-2222-2222-222222222222',
      scopeType: 'group',
      workspaceId: '11111111-1111-1111-1111-111111111111',
      groupId: '22222222-2222-2222-2222-222222222222',
      displayName: 'School / Teachers',
      estimatedRecipientCount: 86,
    })).toEqual({
      key: 'group:22222222-2222-2222-2222-222222222222',
      scope: 'group',
      workspaceId: '11111111-1111-1111-1111-111111111111',
      groupId: '22222222-2222-2222-2222-222222222222',
      channelId: undefined,
      displayName: 'School / Teachers',
      recipientCount: 86,
      scheduleTimeZoneId: 'UTC',
    });

    expect(mapAnnouncementAudienceOption({
      key: 'group:bad',
      scopeType: 'group',
      displayName: 'School / Teachers',
    })).toBeNull();
  });

  it('serializes the reviewed audience ids and critical priority for create', () => {
    expect(toCreateAnnouncementRequest({
      title: 'Safety update',
      body: 'Review this notice',
      priority: 'critical',
      requiresReadConfirmation: true,
      audience: {
        key: 'channel:33333333-3333-3333-3333-333333333333',
        scope: 'channel',
        displayName: 'School / Coglatas / #announcements',
        recipientCount: 32,
        workspaceId: '11111111-1111-1111-1111-111111111111',
        groupId: '22222222-2222-2222-2222-222222222222',
        channelId: '33333333-3333-3333-3333-333333333333',
      },
    })).toEqual({
      workspaceId: '11111111-1111-1111-1111-111111111111',
      groupId: '22222222-2222-2222-2222-222222222222',
      channelId: '33333333-3333-3333-3333-333333333333',
      title: 'Safety update',
      body: 'Review this notice',
      priority: 2,
      isPinned: false,
      requiresReadConfirmation: true,
    });
  });

  it('round-trips CTA and linked attachment fields through create and detail adapters', () => {
    const submission = {
      title: 'Application notice',
      body: 'Review the application details.',
      priority: 'important' as const,
      requiresReadConfirmation: false,
      cta: { label: 'Open application', url: '/forms/application' },
      attachment: { label: 'Guide PDF', url: 'https://example.jp/guide.pdf' },
      audience: {
        key: 'global',
        scope: 'global' as const,
        displayName: 'All tenant members',
        recipientCount: 100,
      },
    };

    expect(toCreateAnnouncementRequest(submission)).toMatchObject({
      cta: { label: 'Open application', url: '/forms/application' },
      attachment: { label: 'Guide PDF', url: 'https://example.jp/guide.pdf' },
    });

    const detail = mapAnnouncementDetail({
      id: 'announcement-cta',
      title: submission.title,
      body: submission.body,
      priority: 'Important',
      requiresReadConfirmation: false,
      isRead: true,
      publishedAt: '2026-08-31T00:00:00Z',
      cta: submission.cta,
      attachment: submission.attachment,
    });

    expect(detail.cta).toEqual(submission.cta);
    expect(detail.attachment).toEqual({ ...submission.attachment, mode: 'linked' });
  });

  it('fails closed when the backend returns an unsafe action URL', () => {
    const detail = mapAnnouncementDetail({
      id: 'announcement-unsafe',
      title: 'Unsafe link',
      body: 'Body',
      priority: 'Normal',
      publishedAt: '2026-08-31T00:00:00Z',
      cta: { label: 'Do not open', url: 'javascript:alert(1)' },
      attachment: { label: 'HTTP file', url: 'http://example.jp/file' },
    });

    expect(detail.cta).toBeUndefined();
    expect(detail.attachment).toBeUndefined();
  });

  it('accepts only app-relative or credential-free HTTPS URLs', () => {
    expect(isSafeAnnouncementUrl('/forms/entry')).toBe(true);
    expect(isSafeAnnouncementUrl('https://example.jp/forms/entry')).toBe(true);
    expect(isSafeAnnouncementUrl('http://example.jp/forms/entry')).toBe(false);
    expect(isSafeAnnouncementUrl('//example.jp/forms/entry')).toBe(false);
    expect(isSafeAnnouncementUrl('https://user:secret@example.jp/forms/entry')).toBe(false);
    expect(isSafeAnnouncementUrl('/safe/../admin')).toBe(false);
  });

  it('maps the legacy backend urgent value to the critical UI semantic', () => {
    const announcement = mapAnnouncementDetail({
      id: 'announcement-1',
      title: 'Published notice',
      body: 'Real backend body',
      priority: 2,
      requiresReadConfirmation: false,
      isRead: true,
      publishedAt: '2026-07-07T00:00:00Z',
    });

    expect(announcement.body).toBe('Real backend body');
    expect(announcement.detailState).toBe('loaded');
    expect(announcement.priority).toBe('critical');
    expect(announcement.readState.isRead).toBe(true);
  });

  it('accepts critical when the API adopts the new semantic name', () => {
    expect(mapAnnouncementListItem({ priority: 'Critical' }).priority).toBe('critical');
  });

  it('serializes only the reviewed authorized target into the durable draft workflow', () => {
    const submission = {
      title: 'Scheduled safety update',
      body: 'Review the school closure details.',
      priority: 'important' as const,
      requiresReadConfirmation: true,
      deliveryMode: 'scheduled' as const,
      scheduledLocalDateTime: '2026-09-02T09:45',
      timeZoneId: 'Asia/Tokyo',
      cta: { label: 'Open details', url: '/announcements/details' },
      attachment: { label: 'Closure guide', url: 'https://example.jp/closure.pdf' },
      audience: {
        key: 'workspace:11111111-1111-1111-1111-111111111111',
        scope: 'workspace' as const,
        displayName: 'School Workspace',
        recipientCount: 1248,
        workspaceId: '11111111-1111-1111-1111-111111111111',
      },
    };

    expect(toCreateAnnouncementDraftRequest(submission)).toEqual({
      content: {
        target: {
          workspaceId: '11111111-1111-1111-1111-111111111111',
          groupId: null,
          channelId: null,
        },
        title: 'Scheduled safety update',
        body: 'Review the school closure details.',
        priority: 1,
        isPinned: false,
        requiresReadConfirmation: true,
        expiresAt: null,
        cta: { label: 'Open details', url: '/announcements/details' },
        attachment: { label: 'Closure guide', url: 'https://example.jp/closure.pdf' },
      },
    });
    expect(toScheduleAnnouncementDraftRequest(7, submission)).toEqual({
      expectedVersion: 7,
      localDateTime: '2026-09-02T09:45',
      timeZoneId: 'Asia/Tokyo',
      ambiguousTimeOffsetMinutes: null,
    });
  });

  it('maps a durable schedule only through a currently authorized audience option', () => {
    const authorizedAudience = {
      key: 'workspace:11111111-1111-1111-1111-111111111111',
      scope: 'workspace' as const,
      displayName: 'School Workspace',
      recipientCount: 1248,
      workspaceId: '11111111-1111-1111-1111-111111111111',
    };
    const dto = {
      id: 'draft-1',
      version: 3,
      status: 'Scheduled',
      workspaceId: authorizedAudience.workspaceId,
      title: 'Scheduled safety update',
      body: 'Review details.',
      priority: 'Important',
      requiresReadConfirmation: true,
      cta: { label: 'Open details', url: '/announcements/details' },
      attachment: { label: 'Guide', url: 'https://example.jp/guide.pdf' },
      scheduledForUtc: '2026-09-02T00:45:00Z',
      scheduleLocalDateTime: '2026-09-02T09:45:00',
      scheduleTimeZoneId: 'Asia/Tokyo',
    };

    expect(mapAnnouncementDraft(dto, [authorizedAudience])).toMatchObject({
      id: 'draft-1',
      version: 3,
      audienceKey: authorizedAudience.key,
      publicationState: 'scheduled',
      cta: dto.cta,
      attachment: dto.attachment,
      scheduledLocalDateTime: '2026-09-02T09:45',
      timeZoneId: 'Asia/Tokyo',
    });
    expect(mapAnnouncementDraft(dto, [])?.audienceKey).toBe('');
  });
});
