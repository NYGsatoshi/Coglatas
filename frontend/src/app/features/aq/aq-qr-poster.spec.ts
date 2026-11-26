import { aqPngBlob, aqPosterPdfBytes } from './aq-qr-poster';

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
