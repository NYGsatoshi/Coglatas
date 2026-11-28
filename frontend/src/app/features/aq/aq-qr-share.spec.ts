import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject, throwError } from 'rxjs';
import { AqApi } from './aq-api';
import { AQ_QR_ARTIFACTS, AqQrShare, AqQrShareData, validAqQrShare } from './aq-qr-share';

const png = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aW5kAAAAASUVORK5CYII=';
const share = (code: string, title = '発表 A'): AqQrShareData => ({
  title,
  surveyUrl: 'https://example.test/app/aq/surveys/' + code.repeat(64),
  pngBase64: png,
});

describe('AqQrShare', () => {
  let fixture: ComponentFixture<AqQrShare>;
  const read = vi.fn();
  const readLogo = vi.fn<(file: File) => Promise<string>>();
  const createPoster = vi.fn<(title: string, qr: string, logo: string) => Promise<Blob>>();
  let clipboardDescriptor: PropertyDescriptor | undefined;

  beforeEach(() => {
    read.mockReset();
    readLogo.mockReset();
    createPoster.mockReset();
    clipboardDescriptor = Object.getOwnPropertyDescriptor(navigator, 'clipboard');
    TestBed.configureTestingModule({ providers: [
      { provide: AqApi, useValue: { read } },
      { provide: AQ_QR_ARTIFACTS, useValue: { readLogo, createPoster } },
    ] });
  });

  afterEach(() => {
    if (clipboardDescriptor) Object.defineProperty(navigator, 'clipboard', clipboardDescriptor);
    else Reflect.deleteProperty(navigator, 'clipboard');
    vi.restoreAllMocks();
    vi.useRealTimers();
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
  const logoSrc = 'data:image/png;base64,' + png;

  function selectLogo(root: HTMLElement, file?: File): HTMLInputElement {
    const input = root.querySelector<HTMLInputElement>('input[type=file]')!;
    Object.defineProperty(input, 'files', { configurable: true, value: file ? [file] : [] });
    input.dispatchEvent(new Event('change'));
    return input;
  }

  async function settle(): Promise<void> {
    await Promise.resolve();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('suspends requests and sharing while management is busy, retains a same-target logo, and clears it on a different target', async () => {
    read.mockReturnValueOnce(of(share('a'))).mockReturnValueOnce(of(share('b'))).mockReturnValueOnce(of(share('c')));
    readLogo.mockResolvedValue(logoSrc);
    const root = await mount();
    const logoFile = new File(['synthetic'], 'logo.png', { type: 'image/png' });
    selectLogo(root, logoFile);
    await settle();
    expect(readLogo).toHaveBeenCalledWith(logoFile);
    expect(root.querySelector('img.qr-logo')?.getAttribute('src')).toBe(logoSrc);
    fixture.componentRef.setInput('suspended', true);
    await settle();
    expect(root.querySelector('input[type=url]')).toBeNull();
    expect(button(root, '共有情報を更新').disabled).toBe(true);
    expect(read).toHaveBeenCalledTimes(1);
    fixture.componentRef.setInput('suspended', false);
    await settle();
    expect(root.querySelector<HTMLInputElement>('input[type=url]')?.value).toBe(share('b').surveyUrl);
    expect(root.querySelector('img.qr-logo')?.getAttribute('src')).toBe(logoSrc);
    fixture.componentRef.setInput('targetId', 'target-b');
    await settle();
    expect(root.querySelector('img.qr-logo')).toBeNull();
    expect(root.querySelector<HTMLInputElement>('input[type=url]')?.value).toBe(share('c').surveyUrl);
  });

  it('handles canceled and invalid logo selection, permits replacement and removal, and resets the file input', async () => {
    read.mockReturnValue(of(share('a')));
    readLogo.mockRejectedValueOnce(new Error('invalid logo')).mockResolvedValueOnce(logoSrc);
    const root = await mount();
    selectLogo(root);
    await settle();
    expect(readLogo).not.toHaveBeenCalled();
    const file = new File(['synthetic'], 'logo.png', { type: 'image/png' });
    selectLogo(root, file);
    await settle();
    expect(root.textContent).toContain('ロゴを読み込めませんでした');
    expect(root.querySelector('img.qr-logo')).toBeNull();
    const input = selectLogo(root, file);
    await settle();
    expect(input.value).toBe('');
    expect(root.querySelector('img.qr-logo')?.getAttribute('src')).toBe(logoSrc);
    expect(root.textContent).not.toContain('ロゴを読み込めませんでした');
    button(root, 'ロゴを削除').click();
    await settle();
    expect(root.querySelector('img.qr-logo')).toBeNull();
  });

  it('discards a pending logo after the target changes and does not block the new poster', async () => {
    read.mockReturnValueOnce(of(share('a'))).mockReturnValueOnce(of(share('b')));
    let finish!: (value: string) => void;
    readLogo.mockReturnValue(new Promise(resolve => { finish = resolve; }));
    const root = await mount();
    selectLogo(root, new File(['synthetic'], 'logo.png', { type: 'image/png' }));
    fixture.detectChanges();
    expect(button(root, 'A4ポスター PDFを保存').disabled).toBe(true);
    fixture.componentRef.setInput('targetId', 'target-b');
    await settle();
    finish(logoSrc);
    await settle();
    expect(root.querySelector('img.qr-logo')).toBeNull();
    expect(button(root, 'A4ポスター PDFを保存').disabled).toBe(false);
  });

  it('saves the current title, QR and logo in a PDF download and revokes the temporary URL', async () => {
    read.mockReturnValue(of(share('a')));
    readLogo.mockResolvedValue(logoSrc);
    const pdf = new Blob(['%PDF-1.4'], { type: 'application/pdf' });
    createPoster.mockResolvedValue(pdf);
    const root = await mount();
    selectLogo(root, new File(['synthetic'], 'logo.png', { type: 'image/png' }));
    await settle();
    const createUrl = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:aq-poster');
    const revokeUrl = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    const downloads: { url: string | null; name: string }[] = [];
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      downloads.push({ url: this.getAttribute('href'), name: this.download });
    });
    vi.useFakeTimers();
    button(root, 'A4ポスター PDFを保存').click();
    await Promise.resolve();
    fixture.detectChanges();
    expect(createPoster).toHaveBeenCalledWith(share('a').title, 'data:image/png;base64,' + png, logoSrc);
    expect(createUrl).toHaveBeenCalledWith(pdf);
    expect(downloads).toEqual([{ url: 'blob:aq-poster', name: 'presentation-qr-poster-a4.pdf' }]);
    expect(root.textContent).toContain('印刷用A4ポスターを保存しました');
    await vi.advanceTimersByTimeAsync(1000);
    expect(revokeUrl).toHaveBeenCalledWith('blob:aq-poster');
  });

  it('shows poster failure, prevents duplicate generation, and refuses obsolete output after a target change', async () => {
    read.mockReturnValueOnce(of(share('a'))).mockReturnValueOnce(of(share('b')));
    let finish!: (value: Blob) => void;
    createPoster.mockRejectedValueOnce(new Error('canvas unavailable'))
      .mockReturnValueOnce(new Promise(resolve => { finish = resolve; }));
    const root = await mount();
    const download = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => undefined);
    button(root, 'A4ポスター PDFを保存').click();
    await settle();
    expect(root.textContent).toContain('ポスターを作成できませんでした');
    const start = button(root, 'A4ポスター PDFを保存');
    start.click();
    fixture.detectChanges();
    expect(button(root, 'ポスターを作成中').disabled).toBe(true);
    start.click();
    expect(createPoster).toHaveBeenCalledTimes(2);
    fixture.componentRef.setInput('targetId', 'target-b');
    await settle();
    finish(new Blob(['obsolete'], { type: 'application/pdf' }));
    await settle();
    expect(download).not.toHaveBeenCalled();
    expect(root.textContent).not.toContain('印刷用A4ポスターを保存しました');
    expect(button(root, 'A4ポスター PDFを保存').disabled).toBe(false);
  });

  it('provides the projection modal even when fullscreen is denied and closes on Escape', async () => {
    read.mockReturnValue(of(share('a')));
    const root = await mount();
    const dialog = root.querySelector('dialog')!;
    const showModal = vi.fn(() => { dialog.open = true; });
    const close = vi.fn(() => { dialog.open = false; });
    const requestFullscreen = vi.fn().mockRejectedValue(new Error('denied'));
    Object.defineProperties(dialog, {
      showModal: { configurable: true, value: showModal },
      close: { configurable: true, value: close },
      requestFullscreen: { configurable: true, value: requestFullscreen },
    });
    button(root, '全画面QR表示').click();
    await settle();
    expect(showModal).toHaveBeenCalledOnce();
    expect(requestFullscreen).toHaveBeenCalledOnce();
    expect(dialog.open).toBe(true);
    button(root, '全画面QR表示').click();
    expect(showModal).toHaveBeenCalledOnce();
    const cancel = new Event('cancel', { cancelable: true });
    dialog.dispatchEvent(cancel);
    expect(cancel.defaultPrevented).toBe(true);
    expect(close).toHaveBeenCalledOnce();
    expect(dialog.open).toBe(false);
    expect(root.querySelector('img.qr-preview')).not.toBeNull();
  });

  it('reports modal opening failure without requesting fullscreen and cleans up an open modal on refresh', async () => {
    read.mockReturnValue(of(share('a')));
    const root = await mount();
    const dialog = root.querySelector('dialog')!;
    const showModal = vi.fn().mockImplementationOnce(() => { throw new Error('unavailable'); })
      .mockImplementationOnce(() => { dialog.open = true; });
    const close = vi.fn(() => { dialog.open = false; });
    const requestFullscreen = vi.fn().mockResolvedValue(undefined);
    Object.defineProperties(dialog, {
      showModal: { configurable: true, value: showModal },
      close: { configurable: true, value: close },
      requestFullscreen: { configurable: true, value: requestFullscreen },
    });
    button(root, '全画面QR表示').click();
    await settle();
    expect(root.textContent).toContain('投影表示を開始できませんでした');
    expect(requestFullscreen).not.toHaveBeenCalled();
    button(root, '全画面QR表示').click();
    await settle();
    expect(dialog.open).toBe(true);
    expect(requestFullscreen).toHaveBeenCalledOnce();
    button(root, '共有情報を更新').click();
    await settle();
    expect(close).toHaveBeenCalledOnce();
    expect(dialog.open).toBe(false);
  });

  it('rejects malformed share responses, unsafe origins and invalid image encodings', async () => {
    const value = share('a');
    for (const invalid of [
      { ...value, title: undefined },
      { ...value, surveyUrl: undefined },
      { ...value, pngBase64: undefined },
      { ...value, pngBase64: 'iVBORw0KGgo' },
      { ...value, pngBase64: 'iVBORw0KGgo!' },
      { ...value, pngBase64: 'iVBORw0KGgo' + 'A'.repeat(512 * 1024) },
      { ...value, surveyUrl: 'not-a-url' },
      { ...value, surveyUrl: value.surveyUrl.replace('https:', 'http:') },
      { ...value, surveyUrl: value.surveyUrl.replace('https://', 'https://user:secret@') },
      { ...value, surveyUrl: value.surveyUrl + '#fragment' },
      { ...value, surveyUrl: value.surveyUrl.replace('/surveys/', '/other/') },
    ]) {
      expect(validAqQrShare(invalid as AqQrShareData)).toBe(false);
    }
    expect(validAqQrShare({ ...value, surveyUrl: value.surveyUrl.replace('https://example.test', 'http://127.0.0.1:4200') })).toBe(true);
    read.mockReturnValue(of({ ...value, pngBase64: 'broken' }));
    const root = await mount();
    expect(root.querySelector('img.qr-preview')).toBeNull();
    expect(root.querySelector('[role=alert]')).not.toBeNull();
  });

});
