import { TestBed } from '@angular/core/testing';

import { CoglatasFileUploaderComponent } from './coglatas-file-uploader.component';

const renderUploader = async () => {
  await TestBed.configureTestingModule({ imports: [CoglatasFileUploaderComponent] }).compileComponents();
  const fixture = TestBed.createComponent(CoglatasFileUploaderComponent);
  fixture.detectChanges();
  return { fixture, component: fixture.componentInstance, host: fixture.nativeElement as HTMLElement };
};

const button = (host: HTMLElement, label: string): HTMLButtonElement => {
  const match = Array.from(host.querySelectorAll('button')).find(item => item.textContent?.trim() === label);
  if (!match) { throw new Error('Missing uploader button: ' + label); }
  return match;
};

describe('Coglatas file uploader presentation', () => {
  beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));
  afterEach(() => {
    localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('blocks selection and Retry while preserving in-flight Cancel', async () => {
    const { fixture, component, host } = await renderUploader();
    const selected = vi.fn(), cancelled = vi.fn(), retried = vi.fn();
    component.filesSelected.subscribe(selected);
    component.cancel.subscribe(cancelled);
    component.retry.subscribe(retried);
    fixture.componentRef.setInput('canUpload', false);
    fixture.componentRef.setInput('items', [
      { clientRequestId: 'active', fileName: 'active.txt', state: 'uploading' },
      { clientRequestId: 'failed', fileName: 'failed.txt', state: 'failed' },
    ]);
    fixture.detectChanges();
    expect(host.querySelector<HTMLInputElement>('input[type="file"]')?.disabled).toBe(true);
    expect(button(host, 'Choose files').disabled).toBe(true);
    expect(button(host, 'Retry').disabled).toBe(true);
    expect(button(host, 'Cancel').disabled).toBe(false);
    button(host, 'Retry').click();
    button(host, 'Cancel').click();
    const drop = new Event('drop', { cancelable: true });
    Object.defineProperty(drop, 'dataTransfer', { value: { files: [new File(['content'], 'blocked.txt')] } });
    component.handleDrop(drop as DragEvent);
    expect(selected).not.toHaveBeenCalled();
    expect(retried).not.toHaveBeenCalled();
    expect(cancelled).toHaveBeenCalledExactlyOnceWith('active');
  });

  it('preserves the existing global disabled control for selection, Cancel and Retry', async () => {
    const { fixture, component, host } = await renderUploader();
    const cancelled = vi.fn();
    component.cancel.subscribe(cancelled);
    fixture.componentRef.setInput('disabled', true);
    fixture.componentRef.setInput('items', [
      { clientRequestId: 'active', fileName: 'active.txt', state: 'pending' },
      { clientRequestId: 'failed', fileName: 'failed.txt', state: 'failed' },
    ]);
    fixture.detectChanges();
    expect(host.querySelector<HTMLInputElement>('input')?.disabled).toBe(true);
    expect(Array.from(host.querySelectorAll('button')).every(item => item.disabled)).toBe(true);
    button(host, 'Cancel').click();
    expect(cancelled).not.toHaveBeenCalled();
  });

  it('honors explicit per-item retry eligibility while preserving the existing default', async () => {
    const { fixture, component, host } = await renderUploader();
    const retried = vi.fn();
    component.retry.subscribe(retried);
    fixture.componentRef.setInput('items', [
      { clientRequestId: 'retained', fileName: 'retained.txt', state: 'failed', canRetry: true },
      { clientRequestId: 'cancelled', fileName: 'cancelled.txt', state: 'cancelled', canRetry: false },
      { clientRequestId: 'denied', fileName: 'denied.txt', state: 'failed', canRetry: false },
      { clientRequestId: 'legacy', fileName: 'legacy.txt', state: 'cancelled' },
    ]);
    fixture.detectChanges();
    const retries = host.querySelectorAll<HTMLButtonElement>('li button');
    expect(retries).toHaveLength(2);
    retries[0].click(); retries[1].click();
    expect(retried.mock.calls).toEqual([['retained'], ['legacy']]);
  });

  it('keeps selection single-file by default and permits selecting the same file again', async () => {
    const { component, host } = await renderUploader();
    const selected = vi.fn();
    component.filesSelected.subscribe(selected);
    const input = host.querySelector<HTMLInputElement>('input')!;
    const first = new File(['first'], 'first.txt'), second = new File(['second'], 'second.txt');
    expect(input.multiple).toBe(false);
    Object.defineProperty(input, 'files', { value: [first, second], configurable: true });
    input.dispatchEvent(new Event('change'));
    input.dispatchEvent(new Event('change'));
    expect(selected.mock.calls).toEqual([[[first]], [[first]]]);
    expect(input.value).toBe('');
  });

  it.each([undefined, 0, 37, 100])('presents accessible active progress with total percentage %s', async percent => {
    const { fixture, host } = await renderUploader();
    fixture.componentRef.setInput('uploading', true);
    fixture.componentRef.setInput('progressPercent', percent);
    fixture.detectChanges();
    const progress = host.querySelector<HTMLProgressElement>('progress')!;
    expect(progress.max).toBe(100);
    expect(progress.getAttribute('aria-label')).toBeTruthy();
    expect(progress.getAttribute('value')).toBe(percent === undefined ? null : String(percent));
    fixture.componentRef.setInput('uploading', false);
    fixture.detectChanges();
    expect(host.querySelector('progress')).toBeNull();
  });
});
