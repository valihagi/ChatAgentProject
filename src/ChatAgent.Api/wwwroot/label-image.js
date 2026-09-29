// Draws the complete label (product text, barcode at its natural size, key facts) into one PNG.
// The barcode comes from the TEC-IT API as an image; everything else is text we add here.

const PT = { title: 14, line: 10, fact: 8 }; // font sizes in points; converted with the image DPI
const FONT = 'system-ui, "Segoe UI", Arial, sans-serif';

/**
 * @param {{title: string, lines: string[], facts: [string, string][], barcodeUrl: string, dpi: number}} label
 * @returns {Promise<Blob>} PNG at `dpi`, so image viewers and printers use the physical size
 */
async function composeLabelPng({ title, lines, facts, barcodeUrl, dpi }) {
  const barcode = new Image();
  barcode.src = barcodeUrl;
  await barcode.decode();

  const px = (pt) => Math.round((pt * dpi) / 72);
  const margin = px(10); // ~3.5 mm white border
  const gap = px(4);
  const font = (pt, weight = '') => `${weight} ${px(pt)}px ${FONT}`.trim();

  const measure = document.createElement('canvas').getContext('2d');
  const width = (text, f) => ((measure.font = f), measure.measureText(text).width);

  const titleLines = wrap(title, font(PT.title, 'bold'), Math.max(barcode.width, dpi * 3), width);
  const labelColumn = Math.max(0, ...facts.map(([name]) => width(name, font(PT.fact))));
  const rowWidth = (value) => labelColumn + gap * 2 + width(value, font(PT.fact));
  const contentWidth = Math.ceil(Math.max(
    barcode.width,
    ...titleLines.map((l) => width(l, font(PT.title, 'bold'))),
    ...lines.map((l) => width(l, font(PT.line))),
    ...facts.map(([, value]) => rowWidth(value)),
  ));

  const rowHeight = (pt) => Math.round(px(pt) * 1.35);
  const height = margin * 2
    + titleLines.length * rowHeight(PT.title) + lines.length * rowHeight(PT.line)
    + gap + barcode.height + (facts.length ? gap : 0) + facts.length * rowHeight(PT.fact);

  const canvas = document.createElement('canvas');
  canvas.width = contentWidth + margin * 2;
  canvas.height = height;
  const ctx = canvas.getContext('2d');
  ctx.fillStyle = '#fff';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.strokeStyle = '#000';
  ctx.lineWidth = Math.max(2, Math.round(dpi / 150));
  ctx.strokeRect(ctx.lineWidth / 2, ctx.lineWidth / 2, canvas.width - ctx.lineWidth, canvas.height - ctx.lineWidth);

  ctx.fillStyle = '#111';
  ctx.textBaseline = 'top';
  let y = margin;
  const draw = (text, f, x = margin, color = '#111') => {
    ctx.font = f;
    ctx.fillStyle = color;
    ctx.fillText(text, x, y);
  };
  for (const line of titleLines) { draw(line, font(PT.title, 'bold')); y += rowHeight(PT.title); }
  for (const line of lines) { draw(line, font(PT.line)); y += rowHeight(PT.line); }

  y += gap;
  ctx.drawImage(barcode, margin, y);
  y += barcode.height + (facts.length ? gap : 0);

  for (const [name, value] of facts) {
    draw(name, font(PT.fact), margin, '#666');
    draw(value, font(PT.fact), margin + labelColumn + gap * 2);
    y += rowHeight(PT.fact);
  }

  const blob = await new Promise((resolve, reject) =>
    canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('PNG export failed'))), 'image/png'));
  return withDpi(blob, dpi);
}

/** Greedy word wrap; a single word longer than the line is left as it is. */
function wrap(text, fontSpec, maxWidth, width) {
  const lines = [];
  let current = '';
  for (const word of text.split(/\s+/).filter(Boolean)) {
    const candidate = current ? `${current} ${word}` : word;
    if (current && width(candidate, fontSpec) > maxWidth) { lines.push(current); current = word; }
    else current = candidate;
  }
  return current ? [...lines, current] : lines.length ? lines : [''];
}

/** Canvas PNGs carry no resolution. Insert a pHYs chunk (pixels per metre) right after IHDR. */
async function withDpi(blob, dpi) {
  const bytes = new Uint8Array(await blob.arrayBuffer());
  const ppm = Math.round(dpi / 0.0254);
  const chunk = new Uint8Array(21); // length(4) + type(4) + data(9) + crc(4)
  const view = new DataView(chunk.buffer);
  view.setUint32(0, 9);
  chunk.set([0x70, 0x48, 0x59, 0x73], 4); // "pHYs"
  view.setUint32(8, ppm);
  view.setUint32(12, ppm);
  chunk[16] = 1; // unit: metre
  view.setUint32(17, crc32(chunk.subarray(4, 17)));

  const ihdrEnd = 8 + 12 + 13; // signature + IHDR chunk (length, type, 13 data bytes, crc)
  return new Blob([bytes.subarray(0, ihdrEnd), chunk, bytes.subarray(ihdrEnd)], { type: 'image/png' });
}

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  return c >>> 0;
});

function crc32(data) {
  let c = 0xffffffff;
  for (const byte of data) c = CRC_TABLE[(c ^ byte) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}
