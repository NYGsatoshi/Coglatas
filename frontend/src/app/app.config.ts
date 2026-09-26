import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { authSessionInterceptor } from './core/auth/auth-session.interceptor';
import { COGLATAS_REALTIME_TRANSPORT } from './core/realtime/realtime-transport';
import { SignalrRealtimeTransport } from './core/realtime/signalr-realtime.transport';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withXhr(), withInterceptors([authSessionInterceptor])),
    provideRouter(routes),
    { provide: COGLATAS_REALTIME_TRANSPORT, useExisting: SignalrRealtimeTransport }
  ]
};
