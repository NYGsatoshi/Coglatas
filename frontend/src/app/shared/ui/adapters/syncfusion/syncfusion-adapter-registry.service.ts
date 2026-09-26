import { Injectable, InjectionToken, inject } from '@angular/core';

import { FrontendFeatureFlagsService } from '../../../../core/feature-flags/frontend-feature-flags.service';
import { CoglatasComplexAdapterName } from '../../contracts/coglatas-complex-adapter.contracts';

export type CoglatasAdapterImplementation = 'fallback' | 'syncfusion';

export interface CoglatasComplexAdapterFactory {
  load(adapter: CoglatasComplexAdapterName): Promise<CoglatasAdapterImplementation>;
}

const fallbackFactory: CoglatasComplexAdapterFactory = {
  load: async () => 'fallback'
};

// A future approved vendor implementation replaces this token from the adapter
// boundary. Feature code continues to consume Coglatas contracts only.
export const COGLATAS_COMPLEX_ADAPTER_FACTORY = new InjectionToken<CoglatasComplexAdapterFactory>(
  'COGLATAS_COMPLEX_ADAPTER_FACTORY',
  { factory: () => fallbackFactory }
);

@Injectable({ providedIn: 'root' })
export class CoglatasSyncfusionAdapterRegistry {
  private readonly factory = inject(COGLATAS_COMPLEX_ADAPTER_FACTORY);
  private readonly flags = inject(FrontendFeatureFlagsService);

  async resolve(adapter: CoglatasComplexAdapterName): Promise<CoglatasAdapterImplementation> {
    if (!this.isRolledOut(adapter)) {
      return 'fallback';
    }

    return this.factory.load(adapter);
  }

  private isRolledOut(adapter: CoglatasComplexAdapterName): boolean {
    switch (adapter) {
      case 'data-grid':
        return this.flags.syncfusionGridEnabled();
      case 'file-uploader':
        return this.flags.syncfusionUploaderEnabled();
      default:
        return false;
    }
  }
}
