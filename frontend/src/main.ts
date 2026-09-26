import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app';
import { CoglatasThemeService } from './app/core/theme/coglatas-theme.service';
import { FrontendFeatureFlagsService } from './app/core/feature-flags/frontend-feature-flags.service';

bootstrapApplication(AppComponent, appConfig)
  .then((application) => {
    application.injector.get(CoglatasThemeService);
    application.injector.get(FrontendFeatureFlagsService);
  })
  .catch((err) => console.error(err));
