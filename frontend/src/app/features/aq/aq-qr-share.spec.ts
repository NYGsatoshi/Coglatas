import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { AqApi } from './aq-api';
import { AqQrShare, AqQrShareData, validAqQrShare } from './aq-qr-share';

const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aW5kAAAAASUVORK5CYII=';
const share = (code: string, title = '発表 A'): AqQrShareData => ({
  title,
  surveyUrl: 'https://example.test/app/aq/surveys/' + code.repeat(64),
  pngBase64: png,
});

describe('AqQrShare', () => {
  let fixture: ComponentFixture<AqQrShare>;
  const read = vi.fn();
  let clipboardDescriptor: PropertyDescriptor | undefined;

  beforeEach(() => {
    read.mockReset();
    clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    TestBed.configureTestingModule({ providers: [{ provide: AqApi, useValue: { read } }] });
  });

  afterEach(() => {
    if (clipboardDescriptor) Object.defineProperty(navigator, 'clipboard', clipboardDescriptor);
    else Reflect.deleteProperty(navigator, 'clipboard');
    vi.unstubAllGlobals();
  });

  async function mount(target = 'target-a') {
    fixture = TestBed.createComponent(AqQrShare);
    fixture.componentRef.setInput('campaignId', 'campaign');
    fixture.componentRef.setInput('targetId', target);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  function button(root: HTMLElement, text: string) {
    return Array.from(root.querySelectorAll('button')).find(element => element.textContent?.includes(text))!;
  }

  it('automatically loads the selected target and displays the server URL and matching PNG', async () => {
    const value = share('a');
    read.mockReturnValue(of(value));
    const root = await mount();
    expect(read).toHaveBeenCalledWith('survey-campaigns/campaign/qr-share', { targetId: 'target-a' });
    expect(root.querySelector<HTMLInputElement>('input[type=url]')?.value).toBe(value.surveyUrl);
    expect(root.querySelector('img.qr-preview')?.getAttribute('src')).toBe('data:image/png;base64,' + png);
    expect(root.textContent).toContain(value.title);
    expect(root.querySelector('dialog')?.textContent).toContain(value.title);
    expect(button(root, 'A4ポスター PDFを保存')).toBeTruthy();
  });

  it('clears the previous URL and ignores a late response on target change', async () => {
    const first = new Subject<AqQrShareData>();
    const second = new Subject<AqQrShareData>();
    read.mockReturnValueOnce(first).mockReturnValueOnce(second);
    const root = await mount();
    first.next(share('a'));
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('input[type=url]')?.value).toContain('a'.repeat(64));
    fixture.componentRef.setInput('targetId', 'target-b');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('input[type=url]')).toBeNull();
    first.next(share('a', 'stale'));
    second.next(share('b', '発表 B'));
    fixture.detectChanges();
    expect(root.querySelector<HTMLInputElement>('input[type=url]')?.value).toContain('b'.repeat(64));
    expect(root.textContent).not.toContain('stale');
  });

  it('removes the current QR on refresh when publication or permission is no longer available', async () => {
    read.mockReturnValueOnce(of(share('a'))).mockReturnValueOnce(throwError(() => new Error('not found')));
    const root = await mount();
    button(root, '共有情報を更新').click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.querySelector('input[type=url]')).toBeNull();
    expect(root.querySelector('img.qr-preview')).toBeNull();
    expect(root.querySelector('[role=alert]')).not.toBeNull();
  });

  it('copies the exact URL and reports clipboard denial without dropping the manual URL', async () => {
    const value = share('a');
    read.mockReturnValue(of(value));
    const writeText = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('denied'));
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText } });
    const root = await mount();
    button(root, '回答URLをコピー').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(writeText).toHaveBeenCalledWith(value.surveyUrl);
    expect(root.textContent).toContain('回答URLをコピーしました');
    button(root, '回答URLをコピー').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('手動でコピー');
    expect(root.querySelector<HTMLInputElement>('input[type=url]')?.value).toBe(value.surveyUrl);
  });

  it('copies a PNG clipboard item and reports denied image clipboard access', async () => {
    read.mockReturnValue(of(share('a')));
    class TestClipboardItem {
      constructor(readonly items: Record<string, Blob>) {}
    }
    vi.stubGlobal('ClipboardItem', TestClipboardItem);
    const write = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('denied'));
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { write } });
    const root = await mount();
    button(root, 'QR画像をコピー').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(write.mock.calls[0][0][0].items['image/png'].type).toBe('image/png');
    expect(root.textContent).toContain('QR画像をコピーしました');
    button(root, 'QR画像をコピー').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(root.textContent).toContain('QR画像 PNGの保存機能');
  });

  it('rejects unexpected URL schemes, paths and malformed image data', () => {
    const value = share('a');
    expect(validAqQrShare(value)).toBe(true);
    expect(validAqQrShare({ ...value, surveyUrl: 'javascript:alert(1)' })).toBe(false);
    expect(validAqQrShare({ ...value, surveyUrl: value.surveyUrl + '?unexpected=1' })).toBe(false);
    expect(validAqQrShare({ ...value, pngBase64: 'invalid' })).toBe(false);
  });
});
