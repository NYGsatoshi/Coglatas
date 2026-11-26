/** A4 poster generation stays entirely in the browser; no logo or public URL is uploaded. */
export const AQ_POSTER_WIDTH = 1654;
export const AQ_POSTER_HEIGHT = 2339;

export function aqPngBlob(base64: string): Blob {
  const bytes = Uint8Array.from(atob(base64), character => character.charCodeAt(0));
  return new Blob([bytes], { type: 'image/png' });
}

export function aqReadImage(src: string): Promise<HTMLImageElement> {
  return new Promise((resolve, reject) => {
    const image = new Image();
    image.onload = () => resolve(image);
    image.onerror = () => reject(new Error('Invalid AQ image'));
    image.src = src;
  });
}

export async function aqReadLogo(file: File): Promise<string> {
  if (file.size > 2 * 1024 * 1024 || !['image/png', 'image/jpeg'].includes(file.type)) {
    throw new Error('Invalid logo format or size');
  }
  const header = new Uint8Array(await file.slice(0, 8).arrayBuffer());
  const png = [137, 80, 78, 71, 13, 10, 26, 10].every((byte, index) => header[index] === byte);
  const jpeg = header[0] === 255 && header[1] === 216 && header[2] === 255;
  if ((file.type === 'image/png' && !png) || (file.type === 'image/jpeg' && !jpeg)) {
    throw new Error('Invalid logo signature');
  }
  const url = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => typeof reader.result === 'string' ? resolve(reader.result) : reject(new Error('Invalid logo'));
    reader.onerror = () => reject(new Error('Logo read failed'));
    reader.readAsDataURL(file);
  });
  const image = await aqReadImage(url);
  if (!image.naturalWidth || !image.naturalHeight || image.naturalWidth > 4096 || image.naturalHeight > 4096) {
    throw new Error('Logo dimensions exceed limits');
  }
  const canvas = document.createElement('canvas');
  const scale = Math.min(1, 1024 / Math.max(image.naturalWidth, image.naturalHeight));
  canvas.width = Math.max(1, Math.round(image.naturalWidth * scale));
  canvas.height = Math.max(1, Math.round(image.naturalHeight * scale));
  const context = canvas.getContext('2d');
  if (!context) throw new Error('Canvas unavailable');
  context.drawImage(image, 0, 0, canvas.width, canvas.height);
  return canvas.toDataURL('image/png');
}

/** Serialize a single A4 page with a lossless RGB image. Offsets are byte-based. */
export function aqPosterPdfBytes(compressedRgb: Uint8Array, width: number, height: number): Uint8Array<ArrayBuffer> {
  const encoder = new TextEncoder();
  const chunks: Uint8Array[] = [];
  const offsets = [0];
  let length = 0;
  const append = (value: string | Uint8Array) => {
    const bytes = typeof value === 'string' ? encoder.encode(value) : value;
    chunks.push(bytes);
    length += bytes.length;
  };
  const object = (body: string, stream?: Uint8Array) => {
    const number = offsets.length;
    offsets.push(length);
    append(number + ' 0 obj\n' + body);
    if (stream) {
      append('\nstream\n');
      append(stream);
      append('\nendstream');
    }
    append('\nendobj\n');
  };
  append('%PDF-1.4\n');
  object('<< /Type /Catalog /Pages 2 0 R >>');
  object('<< /Type /Pages /Count 1 /Kids [3 0 R] >>');
  object('<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.276 841.89] /Resources << /XObject << /Poster 5 0 R >> >> /Contents 4 0 R >>');
  const drawing = encoder.encode('q\n595.276 0 0 841.89 0 0 cm\n/Poster Do\nQ\n');
  object('<< /Length ' + drawing.length + ' >>', drawing);
  object('<< /Type /XObject /Subtype /Image /Width ' + width + ' /Height ' + height +
    ' /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length ' + compressedRgb.length + ' >>', compressedRgb);
  const xref = length;
  append('xref\n0 ' + offsets.length + '\n0000000000 65535 f \n');
  offsets.slice(1).forEach(offset => append(String(offset).padStart(10, '0') + ' 00000 n \n'));
  append('trailer\n<< /Size ' + offsets.length + ' /Root 1 0 R >>\nstartxref\n' + xref + '\n%%EOF\n');
  const output = new Uint8Array(length);
  let position = 0;
  for (const chunk of chunks) {
    output.set(chunk, position);
    position += chunk.length;
  }
  return output;
}

function titleLines(context: CanvasRenderingContext2D, title: string, maxWidth: number): string[] {
  const characters = Array.from(title.replace(/\s+/gu, ' ').trim());
  const lines: string[] = [];
  let line = '';
  for (const character of characters) {
    if (line && context.measureText(line + character).width > maxWidth) {
      lines.push(line);
      line = '';
    }
    line += character;
  }
  if (line) lines.push(line);
  return lines;
}

export async function aqCreatePoster(title: string, qrSrc: string, logoSrc: string): Promise<Blob> {
  const [qr, logo] = await Promise.all([aqReadImage(qrSrc), logoSrc ? aqReadImage(logoSrc) : Promise.resolve(null)]);
  if (!qr.naturalWidth || qr.naturalWidth !== qr.naturalHeight) throw new Error('Invalid QR image');
  const canvas = document.createElement('canvas');
  canvas.width = AQ_POSTER_WIDTH;
  canvas.height = AQ_POSTER_HEIGHT;
  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (!context) throw new Error('Canvas unavailable');

  const center = canvas.width / 2;
  context.fillStyle = '#ffffff';
  context.fillRect(0, 0, canvas.width, canvas.height);
  if (logo) {
    const scale = Math.min(850 / logo.naturalWidth, 220 / logo.naturalHeight);
    const width = logo.naturalWidth * scale;
    const height = logo.naturalHeight * scale;
    context.drawImage(logo, center - width / 2, 90 + (220 - height) / 2, width, height);
  }
  context.fillStyle = '#111111';
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  let fontSize = 68;
  context.font = 'bold ' + fontSize + 'px sans-serif';
  let lines = titleLines(context, title, canvas.width - 220);
  while (lines.length > 5 && fontSize > 38) {
    fontSize -= 2;
    context.font = 'bold ' + fontSize + 'px sans-serif';
    lines = titleLines(context, title, canvas.width - 220);
  }
  if (lines.length > 5) {
    lines = lines.slice(0, 5);
    let last = lines[4];
    while (last && context.measureText(last + '…').width > canvas.width - 220) last = Array.from(last).slice(0, -1).join('');
    lines[4] = last + '…';
  }
  lines.forEach((line, index) => context.fillText(line, center, 520 + (index - (lines.length - 1) / 2) * fontSize * 1.2));
  const qrSize = Math.min(1150, qr.naturalWidth * Math.max(1, Math.floor(1150 / qr.naturalWidth)));
  context.imageSmoothingEnabled = false;
  context.drawImage(qr, Math.floor(center - qrSize / 2), 800, qrSize, qrSize);
  context.font = 'bold 58px sans-serif';
  context.fillText('アンケートにご回答ください', center, 2110);
  context.font = '38px sans-serif';
  context.fillText('QRコードを読み取って回答ページを開いてください', center, 2200);

  // Preserve the server's black/white QR modules; do not introduce JPEG artifacts.
  const rgba = context.getImageData(0, 0, canvas.width, canvas.height).data;
  const rgb = new Uint8Array(canvas.width * canvas.height * 3);
  for (let i = 0, j = 0; i < rgba.length; i += 4, j += 3) {
    rgb[j] = rgba[i];
    rgb[j + 1] = rgba[i + 1];
    rgb[j + 2] = rgba[i + 2];
  }
  const compressed = new Uint8Array(await new Response(new Blob([rgb]).stream()
    .pipeThrough(new CompressionStream('deflate'))).arrayBuffer());
  return new Blob([aqPosterPdfBytes(compressed, canvas.width, canvas.height)], { type: 'application/pdf' });
}
