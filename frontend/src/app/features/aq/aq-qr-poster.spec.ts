import { AQ_POSTER_HEIGHT, AQ_POSTER_WIDTH, aqCreatePoster, aqPngBlob, aqPosterPdfBytes, aqReadImage, aqReadLogo } from './aq-qr-poster';

describe('AQ printable poster serialization', () => {
  it('builds an A4 single-page PDF with correct binary stream length and xref offsets', () => {
    // Use arbitrary binary bytes to exercise offsets (rendering is covered by browser acceptance).
    const bytes = aqPosterPdfBytes(new Uint8Array([0, 255, 128, 10, 13]), 10, 20);
    const text = new TextDecoder('latin1').decode(bytes);
    expect(text.startsWith('%PDF-1.4')).toBe(true);
    expect(text).toContain('/MediaBox [0 0 595.276 841.89]');
    expect(text).toContain('/Count 1');
    expect(text).toContain('/Width 10 /Height 20');
    expect(text).toContain('/Filter /FlateDecode /Length 5');
    const xref = Number(/startxref\n(\d+)\n%%EOF/u.exec(text)![1]);
    expect(text.slice(xref, xref + 4)).toBe('xref');
    const entries = text.slice(xref).split('\n').slice(3, 8);
    entries.forEach((entry, index) => {
      const offset = Number(entry.slice(0, 10));
      expect(text.slice(offset, offset + 7)).toBe((index + 1) + ' 0 obj');
    });
  });

  it('returns a PNG Blob containing the exact decoded bytes for clipboard', () => {
    const blob = aqPngBlob('iVBORw0KGgo=');
    expect(blob.type).toBe('image/png');
    expect(blob.size).toBe(8);
  });
});

const pngHeader = [137, 80, 78, 71, 13, 10, 26, 10];
const jpegHeader = [255, 216, 255, 224, 0, 16, 0, 0];
const logoUrl = 'data:image/png;base64,iVBORw0KGgo=';
const qrUrl = 'data:image/png;base64,synthetic-qr';
const normalizedLogo = 'data:image/png;base64,normalized-logo';

function blobBytes(blob: Blob): Promise<Uint8Array> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer));
    reader.onerror = () => reject(new Error('Could not read test artifact'));
    reader.readAsArrayBuffer(blob);
  });
}

function imageFile(bytes: number[], type: string): File {
  return new File([new Uint8Array(bytes)], 'synthetic-logo', { type });
}

describe('AQ poster browser boundary and rendering', () => {
  const dimensions = new Map<string, [number, number]>();
  let decoded: string[];
  const context = {
    font: '',
    imageSmoothingEnabled: true,
    fillRect: vi.fn(),
    drawImage: vi.fn(),
    fillText: vi.fn(),
    measureText: vi.fn((value: string) => ({ width: Array.from(value).length *
      Number(context.font.match(/(\d+)px/u)?.[1] ?? 40) })),
    getImageData: vi.fn(),
  };

  beforeEach(() => {
    decoded = [];
    dimensions.clear();
    dimensions.set(qrUrl, [320, 320]);
    dimensions.set(logoUrl, [1600, 800]);
    dimensions.set(normalizedLogo, [1024, 512]);
    context.font = '';
    context.imageSmoothingEnabled = true;
    context.fillRect.mockClear();
    context.drawImage.mockClear();
    context.fillText.mockClear();
    context.measureText.mockClear();
    context.getImageData.mockReset();
    vi.stubGlobal('Image', class {
      naturalWidth = 0;
      naturalHeight = 0;
      onload: (() => void) | null = null;
      onerror: (() => void) | null = null;
      set src(value: string) {
        decoded.push(value);
        const size = dimensions.get(value);
        if (!size) { this.onerror?.(); return; }
        [this.naturalWidth, this.naturalHeight] = size;
        this.onload?.();
      }
    });
    vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as unknown as CanvasRenderingContext2D);
    vi.spyOn(HTMLCanvasElement.prototype, 'toDataURL').mockReturnValue(normalizedLogo);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('decodes an image and propagates a decode failure', async () => {
    const qr = await aqReadImage(qrUrl);
    expect([qr.naturalWidth, qr.naturalHeight]).toEqual([320, 320]);
    await expect(aqReadImage('invalid')).rejects.toThrow('Invalid AQ image');
  });

  it('rejects unsupported, oversized, and MIME/signature-mismatched logos before decoding', async () => {
    const oversized = new File([new Uint8Array(2 * 1024 * 1024 + 1)], 'large.png', { type: 'image/png' });
    await expect(aqReadLogo(oversized)).rejects.toThrow('Invalid logo format or size');
    await expect(aqReadLogo(imageFile(pngHeader, 'image/svg+xml'))).rejects.toThrow('Invalid logo format or size');
    await expect(aqReadLogo(imageFile(jpegHeader, 'image/png'))).rejects.toThrow('Invalid logo signature');
    await expect(aqReadLogo(imageFile(pngHeader, 'image/jpeg'))).rejects.toThrow('Invalid logo signature');
    expect(decoded).toEqual([]);
    expect(context.drawImage).not.toHaveBeenCalled();
  });

  it('normalizes PNG and JPEG logos with the aspect ratio intact and never enlarges a small logo', async () => {
    const jpegUrl = 'data:image/jpeg;base64,' + btoa(String.fromCharCode(...jpegHeader));
    dimensions.set(jpegUrl, [320, 640]);
    expect(await aqReadLogo(imageFile(pngHeader, 'image/png'))).toBe(normalizedLogo);
    expect(context.drawImage.mock.calls[0].slice(1)).toEqual([0, 0, 1024, 512]);
    expect(await aqReadLogo(imageFile(jpegHeader, 'image/jpeg'))).toBe(normalizedLogo);
    expect(context.drawImage.mock.calls[1].slice(1)).toEqual([0, 0, 320, 640]);
    expect(decoded).toEqual([logoUrl, jpegUrl]);
  });

  it('rejects failed decodes, invalid dimensions, and unavailable canvases', async () => {
    dimensions.delete(logoUrl);
    await expect(aqReadLogo(imageFile(pngHeader, 'image/png'))).rejects.toThrow('Invalid AQ image');
    for (const size of [[0, 32], [32, 0], [4097, 32], [32, 4097]] as [number, number][]) {
      dimensions.set(logoUrl, size);
      await expect(aqReadLogo(imageFile(pngHeader, 'image/png'))).rejects.toThrow('Logo dimensions exceed limits');
    }
    dimensions.set(logoUrl, [32, 32]);
    vi.mocked(HTMLCanvasElement.prototype.getContext).mockReturnValue(null);
    await expect(aqReadLogo(imageFile(pngHeader, 'image/png'))).rejects.toThrow('Canvas unavailable');
    expect(context.drawImage).not.toHaveBeenCalled();
  });

  it('rejects an empty, non-square or oversized QR and an unavailable poster canvas', async () => {
    for (const size of [[0, 0], [320, 321], [1151, 1151]] as [number, number][]) {
      dimensions.set(qrUrl, size);
      await expect(aqCreatePoster('Title', qrUrl, '')).rejects.toThrow('Invalid QR image');
    }
    dimensions.set(qrUrl, [320, 320]);
    vi.mocked(HTMLCanvasElement.prototype.getContext).mockReturnValue(null);
    await expect(aqCreatePoster('Title', qrUrl, '')).rejects.toThrow('Canvas unavailable');
    expect(context.drawImage).not.toHaveBeenCalled();
  });

  function preparePixels(): void {
    const pixels = new Uint8ClampedArray(AQ_POSTER_WIDTH * AQ_POSTER_HEIGHT * 4).fill(255);
    pixels.set([0, 23, 250, 7]);
    pixels.set([31, 17, 9, 0], pixels.length - 4);
    context.getImageData.mockReturnValue({ data: pixels });
  }

  it('renders an A4 poster without a logo and losslessly compresses the RGB pixels into the PDF', async () => {
    preparePixels();
    const pdf = await aqCreatePoster('発表  A', qrUrl, '');
    expect(pdf.type).toBe('application/pdf');
    expect(context.fillRect).toHaveBeenCalledWith(0, 0, AQ_POSTER_WIDTH, AQ_POSTER_HEIGHT);
    expect(context.fillText).toHaveBeenCalledWith('発表 A', AQ_POSTER_WIDTH / 2, 520);
    expect(context.drawImage).toHaveBeenCalledTimes(1);
    expect(context.drawImage.mock.calls[0].slice(1)).toEqual([347, 800, 960, 960]);
    expect(context.imageSmoothingEnabled).toBe(false);
    expect(context.getImageData).toHaveBeenCalledWith(0, 0, AQ_POSTER_WIDTH, AQ_POSTER_HEIGHT);
    const bytes = await blobBytes(pdf);
    const text = new TextDecoder('latin1').decode(bytes);
    expect(text).toContain('/MediaBox [0 0 595.276 841.89]');
    expect(text).toContain('/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode');
    const image = /\/Subtype \/Image[^\n]*\/Length (\d+) >>\nstream\n/u.exec(text);
    expect(image).not.toBeNull();
    const start = image!.index + image![0].length;
    const compressed = bytes.slice(start, start + Number(image![1]));
    const stream = new ReadableStream<Uint8Array>({
      start(controller) { controller.enqueue(compressed); controller.close(); },
    });
    const rgb = new Uint8Array(await new Response(stream.pipeThrough(new DecompressionStream('deflate'))).arrayBuffer());
    expect(rgb.length).toBe(AQ_POSTER_WIDTH * AQ_POSTER_HEIGHT * 3);
    expect(Array.from(rgb.slice(0, 6))).toEqual([0, 23, 250, 255, 255, 255]);
    expect(Array.from(rgb.slice(-3))).toEqual([31, 17, 9]);
  });

  it('keeps the optional logo and long wrapped title outside the integer-scaled QR area', async () => {
    preparePixels();
    const pdf = await aqCreatePoster('発表'.repeat(100), qrUrl, normalizedLogo);
    expect(pdf.type).toBe('application/pdf');
    expect(context.drawImage).toHaveBeenCalledTimes(2);
    expect(context.drawImage.mock.calls[0].slice(1)).toEqual([607, 90, 440, 220]);
    expect(context.drawImage.mock.calls[1].slice(1)).toEqual([347, 800, 960, 960]);
    const titles = context.fillText.mock.calls.slice(0, -2);
    expect(titles).toHaveLength(5);
    expect(titles.at(-1)?.[0]).toMatch(/…$/u);
    expect(titles.every((call) => call[2] > 310 && call[2] < 800)).toBe(true);
    expect(context.fillText.mock.calls.slice(-2).map(call => call[0])).toEqual([
      'アンケートにご回答ください', 'QRコードを読み取って回答ページを開いてください',
    ]);
  });
});
