import { ChangeDetectionStrategy, Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { LucideCircle } from '@lucide/angular';
import { UploaderComponent, UploaderModule } from '@syncfusion/ej2-angular-inputs';
import { DialogComponent, DialogModule } from '@syncfusion/ej2-angular-popups';
import { firstValueFrom, map, of } from 'rxjs';

import { SignalrRealtimeTransport } from '../../../../core/realtime/signalr-realtime.transport';
import { AppDataGridComponent } from '../../../grid/app-data-grid/app-data-grid.component';
import { SyncfusionDataGridComponent } from './syncfusion-data-grid.component';
import { SYNCFUSION_GANTT_THEME_ASSETS, SyncfusionGanttComponent } from './syncfusion-gantt.component';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-angular22-third-party-compat-host',
  imports: [DialogModule, LucideCircle, UploaderModule],
  standalone: true,
  template: `
    <svg lucideCircle aria-hidden="true" data-testid="lucide-circle"></svg>
    <ejs-uploader [autoUpload]="false" [multiple]="true" />
    <ejs-dialog [showCloseIcon]="true" [visible]="false" />
  `,
})
// eslint-disable-next-line @typescript-eslint/no-extraneous-class -- Angular TestBed requires a template host type even when it has no class logic.
class Angular22ThirdPartyCompatibilityHostComponent {}

describe('Angular 22 third-party compatibility', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('instantiates the current Syncfusion Inputs and Popups Angular wrappers with Lucide', async () => {
    await TestBed.configureTestingModule({
      imports: [Angular22ThirdPartyCompatibilityHostComponent],
    }).compileComponents();

    const fixture = TestBed.createComponent(Angular22ThirdPartyCompatibilityHostComponent);
    fixture.detectChanges();
    await fixture.whenStable();

    {
      const dialog = fixture.debugElement.query(By.directive(DialogComponent)).injector.get(DialogComponent),
        icon = fixture.debugElement.query(By.directive(LucideCircle)).injector.get(LucideCircle),
        uploader = fixture.debugElement.query(By.directive(UploaderComponent)).injector.get(UploaderComponent);

      expect(uploader).toBeInstanceOf(UploaderComponent);
      expect(dialog).toBeInstanceOf(DialogComponent);
      expect(icon).toBeInstanceOf(LucideCircle);
    }
  });

  it('loads the production Gantt, Syncfusion Grid, and AG Grid Angular integration classes', () => {
    expect(SyncfusionGanttComponent).toBeDefined();
    expect(SyncfusionDataGridComponent).toBeDefined();
    expect(AppDataGridComponent).toBeDefined();
    expect(SYNCFUSION_GANTT_THEME_ASSETS).toContain('assets/vendor/syncfusion/material3.css');
  });

  it('loads the production SignalR transport and preserves RxJS execution in the jsdom test runtime', async () => {
    const inputValue = Number('21'),
      outputValue = await firstValueFrom(of(inputValue).pipe(map((value) => value + value)));

    expect(SignalrRealtimeTransport).toBeDefined();
    expect(outputValue).toBe(Number('42'));
    expect(window.document).toBe(document);
    expect(document.createElement('div')).toBeInstanceOf(HTMLElement);
  });

  it('loads the zone.js runtime used by the retained Storybook browser target', async () => {
    await import('zone.js');

    expect(Reflect.get(globalThis, 'Zone')).toBeDefined();
  });
});
