import { TestBed } from '@angular/core/testing';

import { FrontendFeatureFlagsService } from '../../../../core/feature-flags/frontend-feature-flags.service';
import { COGLATAS_COMPLEX_ADAPTER_FACTORY, CoglatasSyncfusionAdapterRegistry } from './syncfusion-adapter-registry.service';

describe('CoglatasSyncfusionAdapterRegistry', () => {
  it('retains the fallback when the Syncfusion rollout flags are disabled', async () => {
    const factory = { load: vi.fn(async () => 'syncfusion' as const) };
    TestBed.configureTestingModule({
      providers: [{ provide: COGLATAS_COMPLEX_ADAPTER_FACTORY, useValue: factory }]
    });
    const registry = TestBed.inject(CoglatasSyncfusionAdapterRegistry);

    await expect(registry.resolve('data-grid')).resolves.toBe('fallback');
    await expect(registry.resolve('file-uploader')).resolves.toBe('fallback');
    await expect(registry.resolve('kanban')).resolves.toBe('fallback');
    expect(factory.load).not.toHaveBeenCalled();
  });

  it('delegates to the approved implementation when the corresponding rollout flag is enabled', async () => {
    const factory = { load: vi.fn(async () => 'syncfusion' as const) };
    TestBed.configureTestingModule({ providers: [{ provide: COGLATAS_COMPLEX_ADAPTER_FACTORY, useValue: factory }] });
    const flags = TestBed.inject(FrontendFeatureFlagsService);
    flags.setForTesting({ 'frontend.syncfusionGrid': true });
    const registry = TestBed.inject(CoglatasSyncfusionAdapterRegistry);

    await expect(registry.resolve('data-grid')).resolves.toBe('syncfusion');
    expect(factory.load).toHaveBeenCalledWith('data-grid');
  });
});
