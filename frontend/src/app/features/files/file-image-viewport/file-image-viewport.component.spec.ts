import { ComponentFixture, TestBed } from '@angular/core/testing';

import { FileImageViewportComponent } from './file-image-viewport.component';

describe('FileImageViewportComponent', () => {
  let fixture: ComponentFixture<FileImageViewportComponent>;
  let host: HTMLElement;
  let viewport: HTMLElement;
  let image: HTMLImageElement;
  let width = 270;
  let height = 300;
  const disconnect = vi.fn();

  beforeEach(async () => {
    localStorage.setItem('coglatas.locale', 'en');
    width = 270; height = 300;
    disconnect.mockClear();
    vi.stubGlobal('ResizeObserver', class { observe() {} disconnect = disconnect; });
    await TestBed.configureTestingModule({ imports: [FileImageViewportComponent] }).compileComponents();
    fixture = TestBed.createComponent(FileImageViewportComponent);
    fixture.componentRef.setInput('source', 'blob:first-image');
    fixture.componentRef.setInput('alt', 'Authorized diagram');
    fixture.detectChanges();
    host = fixture.nativeElement as HTMLElement;
    viewport = host.querySelector('[data-testid="files-image-viewport"]')!;
    image = host.querySelector('img')!;
    Object.defineProperty(viewport, 'clientWidth', { get: () => width });
    Object.defineProperty(viewport, 'clientHeight', { get: () => height });
  });

  afterEach(() => vi.unstubAllGlobals());

  const load = (naturalWidth: number, naturalHeight: number): void => {
    Object.defineProperty(image, 'naturalWidth', { configurable: true, value: naturalWidth });
    Object.defineProperty(image, 'naturalHeight', { configurable: true, value: naturalHeight });
    image.dispatchEvent(new Event('load'));
    fixture.detectChanges();
  };
  const click = (id: string): void => {
    host.querySelector<HTMLButtonElement>(`[data-testid="files-image-${id}"]`)!.click();
    fixture.detectChanges();
  };

  it('waits for dimensions and fits a large image below 25 percent without cropping', () => {
    expect(host.querySelector('[data-testid="files-image-controls"]')).toBeNull();
    expect(image.style.visibility).toBe('hidden');
    load(4000, 2000);
    expect(image.style.width).toBe('270px');
    expect(image.style.height).toBe('135px');
    expect(host.querySelector('output')?.textContent).toBe('6.75% · 0°');
    expect(host.querySelector('[data-testid="files-image-fit"]')?.getAttribute('aria-pressed')).toBe('true');
  });

  it.each([[4000, 2000], [900, 1800]])('reserves actual rotated layout bounds for %s by %s pixels', (naturalWidth, naturalHeight) => {
    load(naturalWidth, naturalHeight);
    click('actual');
    expect(image.style.width).toBe(`${naturalWidth}px`);
    expect(image.style.height).toBe(`${naturalHeight}px`);
    const stage = host.querySelector<HTMLElement>('[data-testid="files-image-stage"]')!;
    for (const angle of [90, 180, 270, 0]) {
      click('rotate');
      expect(image.style.transform).toContain(`rotate(${angle}deg)`);
      expect(stage.style.width).toBe(`${angle % 180 ? naturalHeight : naturalWidth}px`);
      expect(stage.style.height).toBe(`${angle % 180 ? naturalWidth : naturalHeight}px`);
    }
  });

  it('zooms coherently from fit and bounds both zoom directions', () => {
    load(4000, 2000);
    click('zoom-in');
    expect(Number.parseFloat(image.style.width)).toBeCloseTo(337.5);
    for (let step = 0; step < 35; step++) { click('zoom-in'); }
    expect(host.querySelector<HTMLButtonElement>('[data-testid="files-image-zoom-in"]')!.disabled).toBe(true);
    expect(image.style.width).toBe('16000px');
    for (let step = 0; step < 45; step++) { click('zoom-out'); }
    expect(host.querySelector<HTMLButtonElement>('[data-testid="files-image-zoom-out"]')!.disabled).toBe(true);
    expect(Number.parseFloat(image.style.width)).toBeCloseTo(67.5);
    click('fit');
    expect(image.style.width).toBe('270px');
  });

  it('refits the rotated image on viewport resize and resets native scrolling', () => {
    load(4000, 2000);
    click('rotate');
    expect(image.style.width).toBe('300px');
    width = 180; height = 100;
    window.dispatchEvent(new Event('resize'));
    fixture.detectChanges();
    expect(image.style.width).toBe('100px');
    expect(image.style.height).toBe('50px');
    viewport.scrollTop = 80; viewport.scrollLeft = 90;
    click('actual');
    expect(viewport.scrollLeft).toBe(0);
    expect(viewport.scrollTop).toBe(0);
  });

  it('clears dimensions and transforms before a replacement URL can load, ignoring stale loads', () => {
    load(4000, 2000);
    click('actual'); click('rotate');
    fixture.componentRef.setInput('source', 'blob:replacement');
    fixture.detectChanges();
    expect(host.querySelector('[data-testid="files-image-controls"]')).toBeNull();
    expect(image.style.width).toBe('0px');
    expect(image.style.transform).toContain('rotate(0deg)');
    Object.defineProperty(image, 'currentSrc', { configurable: true, value: 'blob:first-image' });
    load(4000, 2000);
    expect(image.style.visibility).toBe('hidden');
    Object.defineProperty(image, 'currentSrc', { configurable: true, value: 'blob:replacement' });
    load(100, 200);
    expect(image.style.width).toBe('100px');
    expect(host.querySelector('[data-testid="files-image-fit"]')?.getAttribute('aria-pressed')).toBe('true');
  });

  it('fails closed on image decode failure and clears state on destruction', () => {
    load(4000, 2000); click('actual'); click('rotate');
    image.dispatchEvent(new Event('error'));
    fixture.detectChanges();
    expect(host.querySelector('[role="alert"]')?.textContent).toContain('Image preview unavailable');
    expect(host.querySelector('[data-testid="files-image-controls"]')).toBeNull();
    expect(image.style.visibility).toBe('hidden');
    fixture.destroy();
    expect(disconnect).toHaveBeenCalledOnce();
    expect(fixture.componentInstance.source).toBe('');
    expect(fixture.componentInstance.alt).toBe('');
  });
});
