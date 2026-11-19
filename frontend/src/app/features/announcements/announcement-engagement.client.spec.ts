import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import {
  AnnouncementEngagementClient,
  type AnnouncementAnalyticsDto,
  mapAnnouncementAnalytics
} from './announcement-engagement.client';

const announcementId = '11111111-1111-4111-8111-111111111111';

function validAnalytics(): AnnouncementAnalyticsDto {
  return {
    announcementId,
    recipientCount: 10,
    readCount: 4,
    readRate: 0.4,
    periodStartUtc: '2026-11-01T00:00:00Z',
    periodEndUtc: '2026-11-02T00:00:00Z',
    denominatorKind: 'frozenDeliveryCohort'
  };
}

describe('Announcement engagement analytics', () => {
  it('retains the complete canonical required projection without inventing optional metrics', () => {
    const result = mapAnnouncementAnalytics(validAnalytics());
    expect(result).not.toBeNull();
    expect(result).toEqual(expect.objectContaining({
      announcementId, recipientCount: 10, readCount: 4, readRate: 0.4,
      denominatorKind: 'frozenDeliveryCohort'
    }));
    expect(result?.periodStartLabel).toEqual(expect.any(String));
    expect(result?.periodEndLabel).toEqual(expect.any(String));
    expect(result).not.toHaveProperty('acknowledgedCount');
    expect(result).not.toHaveProperty('ctaClickCount');
    expect(result).not.toHaveProperty('medianTimeToRecognitionSeconds');
  });

  it('projects valid acknowledgement, CTA and median fields only when pairs are complete', () => {
    const result = mapAnnouncementAnalytics({
      ...validAnalytics(),
      denominatorKind: 'currentAuthorizedAudience',
      acknowledgedCount: 3,
      acknowledgementRate: 0.3,
      ctaClickCount: 2,
      ctaClickThroughRate: 0.2,
      ctaMetric: 'clickThrough',
      medianTimeToRecognitionSeconds: 60.5
    });
    expect(result).toEqual(expect.objectContaining({
      announcementId, acknowledgedCount: 3, acknowledgementRate: 0.3,
      ctaClickCount: 2, ctaClickThroughRate: 0.2, ctaMetric: 'clickThrough',
      medianTimeToRecognitionSeconds: 60.5,
      denominatorKind: 'currentAuthorizedAudience'
    }));
  });

  it.each([
    ['absent announcement', { announcementId: undefined }],
    ['blank announcement', { announcementId: '' }],
    ['nonstring announcement', { announcementId: 123 }],
    ['negative recipients', { recipientCount: -1 }],
    ['fractional recipients', { recipientCount: 2.5 }],
    ['missing recipients', { recipientCount: null }],
    ['negative read count', { readCount: -1 }],
    ['read count above recipients', { readCount: 11 }],
    ['fractional read count', { readCount: 1.5 }],
    ['NaN read count', { readCount: Number.NaN }],
    ['negative read rate', { readRate: -0.01 }],
    ['read rate over 100%', { readRate: 1.01 }],
    ['nonfinite read rate', { readRate: Number.POSITIVE_INFINITY }],
    ['non-numeric read rate', { readRate: '0.4' }],
    ['missing start', { periodStartUtc: undefined }],
    ['nondate start', { periodStartUtc: 0 }],
    ['invalid start', { periodStartUtc: 'not-a-date' }],
    ['invalid end', { periodEndUtc: '2026-20-99' }],
    ['missing denominator', { denominatorKind: null }],
    ['unknown denominator', { denominatorKind: 'unrestricted' }]
  ] as const)('rejects malformed required analytics: %s', (_reason, patch) => {
    expect(mapAnnouncementAnalytics({ ...validAnalytics(), ...patch })).toBeNull();
  });

  it.each([
    ['acknowledgement count without rate', { acknowledgedCount: 2 }],
    ['acknowledgement rate without count', { acknowledgementRate: 0.2 }],
    ['CTA count without rate', { ctaClickCount: 2, ctaMetric: 'clickThrough' }],
    ['CTA rate without count', { ctaClickThroughRate: 0.2, ctaMetric: 'clickThrough' }],
    ['CTA metrics without authority', { ctaClickCount: 2, ctaClickThroughRate: 0.2 }],
    ['CTA metrics with wrong kind', { ctaClickCount: 2, ctaClickThroughRate: 0.2, ctaMetric: 'openRate' }],
    ['count exceeds cohort', { acknowledgedCount: 11, acknowledgementRate: 0.5 }],
    ['negative acknowledgement rate', { acknowledgedCount: 1, acknowledgementRate: -0.1 }],
    ['fractional CTA count', { ctaClickCount: 1.5, ctaClickThroughRate: 0.1, ctaMetric: 'clickThrough' }],
    ['out of range CTA rate', { ctaClickCount: 2, ctaClickThroughRate: 2, ctaMetric: 'clickThrough' }]
  ] as const)('rejects incomplete or untrusted optional metric pairs: %s', (_reason, patch) => {
    expect(mapAnnouncementAnalytics({ ...validAnalytics(), ...patch })).toBeNull();
  });

  it('omits unsupported independently nullable metrics without borrowing unrelated values', () => {
    const result = mapAnnouncementAnalytics({
      ...validAnalytics(),
      acknowledgedCount: -1,
      acknowledgementRate: null,
      ctaClickCount: null,
      ctaClickThroughRate: null,
      ctaMetric: 'clickThrough',
      medianTimeToRecognitionSeconds: -5
    });
    expect(result).not.toBeNull();
    expect(result).not.toHaveProperty('acknowledgedCount');
    expect(result).not.toHaveProperty('ctaClickCount');
    expect(result).not.toHaveProperty('ctaMetric');
    expect(result).not.toHaveProperty('medianTimeToRecognitionSeconds');
  });

  describe('HTTP transport', () => {
    let client: AnnouncementEngagementClient;
    let http: HttpTestingController;

    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [provideHttpClient(), provideHttpClientTesting()]
      });
      client = TestBed.inject(AnnouncementEngagementClient);
      http = TestBed.inject(HttpTestingController);
    });

    afterEach(() => {
      http.verify();
      TestBed.resetTestingModule();
    });

    it('requests analytics with credentials and maps an actual HTTP response', () => {
      const received: unknown[] = [];
      client.analytics(announcementId).subscribe(value => received.push(value));
      const request = http.expectOne('/api/announcements/' + announcementId + '/analytics');
      expect(request.request.method).toBe('GET');
      expect(request.request.withCredentials).toBe(true);
      request.flush(validAnalytics());
      expect(received).toEqual([expect.objectContaining({ announcementId, readCount: 4 })]);
    });

    it('does not turn a malformed analytics response into a valid projection', () => {
      const received: unknown[] = [];
      client.analytics(announcementId).subscribe(value => received.push(value));
      http.expectOne('/api/announcements/' + announcementId + '/analytics').flush({});
      expect(received).toEqual([null]);
    });

    it('preserves credentials and empty bodies for acknowledgement and CTA commands', () => {
      const received: unknown[] = [];
      client.acknowledge(announcementId).subscribe(value => received.push(value));
      const acknowledgement = http.expectOne('/api/announcements/' + announcementId + '/acknowledge');
      expect(acknowledgement.request.method).toBe('POST');
      expect(acknowledgement.request.body).toEqual({});
      expect(acknowledgement.request.withCredentials).toBe(true);
      acknowledgement.flush({ accepted: true });

      client.trackCtaClick(announcementId).subscribe(value => received.push(value));
      const cta = http.expectOne('/api/announcements/' + announcementId + '/cta-click');
      expect(cta.request.method).toBe('POST');
      expect(cta.request.body).toEqual({});
      expect(cta.request.withCredentials).toBe(true);
      cta.flush({ tracked: true });

      expect(received).toEqual([{ accepted: true }, { tracked: true }]);
    });
  });
});
