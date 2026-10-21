/**
 * Receipt scanning in the browser, with no libraries:
 *  - detectReceipt: finds the receipt's four corners in a photo (bright paper against a darker background)
 *  - warp: cuts the receipt out and straightens it (perspective correction)
 *  - stitch: joins the parts of a long receipt top to bottom, removing the overlap between photos
 *  - enhance: turns the result into a clean scan (black-and-white or colour)
 * Only the final scan leaves the device.
 */

export interface Point { x: number; y: number; }
export type Quad = [Point, Point, Point, Point]; // top-left, top-right, bottom-right, bottom-left
export type ScanMode = 'bw' | 'colour';

/** Largest side we keep from a photo, and the width of the final scan. Keeps memory use safe on phones. */
const MAX_PHOTO_SIDE = 2200;
const SCAN_WIDTH = 1100;
/** iOS Safari refuses canvases above about 16.7 million pixels. */
const MAX_CANVAS_PIXELS = 16_000_000;

export function canvas(w: number, h: number): HTMLCanvasElement {
  const c = document.createElement('canvas');
  c.width = Math.max(1, Math.round(w));
  c.height = Math.max(1, Math.round(h));
  return c;
}

function ctx2d(c: HTMLCanvasElement): CanvasRenderingContext2D {
  const x = c.getContext('2d', { willReadFrequently: true });
  if (!x) throw new Error('This browser can\'t process images.');
  return x;
}

/** Loads a photo, applying its camera orientation and shrinking it to a workable size. */
export async function loadPhoto(file: Blob): Promise<HTMLCanvasElement> {
  let bitmap: ImageBitmap | HTMLImageElement;
  try {
    bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  } catch {
    bitmap = await new Promise<HTMLImageElement>((resolve, reject) => {
      const img = new Image();
      img.onload = () => resolve(img);
      img.onerror = () => reject(new Error('That file isn\'t an image this browser can open.'));
      img.src = URL.createObjectURL(file);
    });
  }
  const w = 'naturalWidth' in bitmap ? bitmap.naturalWidth : bitmap.width;
  const h = 'naturalHeight' in bitmap ? bitmap.naturalHeight : bitmap.height;
  const scale = Math.min(1, MAX_PHOTO_SIDE / Math.max(w, h));
  const c = canvas(w * scale, h * scale);
  ctx2d(c).drawImage(bitmap, 0, 0, c.width, c.height);
  if ('close' in bitmap) bitmap.close();
  else URL.revokeObjectURL(bitmap.src);
  return c;
}

export function rotate90(src: HTMLCanvasElement): HTMLCanvasElement {
  const c = canvas(src.height, src.width);
  const x = ctx2d(c);
  x.translate(c.width, 0);
  x.rotate(Math.PI / 2);
  x.drawImage(src, 0, 0);
  return c;
}

/** The whole photo, slightly inset: the starting point when the receipt can't be found. */
export function fullQuad(c: HTMLCanvasElement): Quad {
  const ix = c.width * 0.03, iy = c.height * 0.03;
  return [{ x: ix, y: iy }, { x: c.width - ix, y: iy }, { x: c.width - ix, y: c.height - iy }, { x: ix, y: c.height - iy }];
}

/** Grey levels (0..255) of a canvas scaled to the given width. */
function greyAt(src: HTMLCanvasElement, width: number): { g: Float32Array; w: number; h: number } {
  const scale = width / src.width;
  const small = canvas(width, Math.max(1, src.height * scale));
  const x = ctx2d(small);
  x.drawImage(src, 0, 0, small.width, small.height);
  const d = x.getImageData(0, 0, small.width, small.height).data;
  const g = new Float32Array(small.width * small.height);
  for (let i = 0, j = 0; i < g.length; i++, j += 4) g[i] = 0.299 * d[j] + 0.587 * d[j + 1] + 0.114 * d[j + 2];
  return { g, w: small.width, h: small.height };
}

/** Otsu's method: the grey level that best separates paper from background. */
function otsu(g: Float32Array): number {
  const hist = new Array<number>(256).fill(0);
  for (const v of g) hist[v | 0]++;
  let sum = 0;
  for (let i = 0; i < 256; i++) sum += i * hist[i];
  let sumB = 0, wB = 0, best = 0, threshold = 128;
  for (let t = 0; t < 256; t++) {
    wB += hist[t];
    if (!wB) continue;
    const wF = g.length - wB;
    if (!wF) break;
    sumB += t * hist[t];
    const mB = sumB / wB, mF = (sum - sumB) / wF;
    const between = wB * wF * (mB - mF) ** 2;
    if (between > best) { best = between; threshold = t; }
  }
  return threshold;
}

/**
 * Finds the receipt: the largest bright region, then its four extreme corners.
 * Returns null when nothing receipt-like stands out (for example white paper on a white table).
 */
export function detectReceipt(src: HTMLCanvasElement): Quad | null {
  const W = 360;
  const { g, w, h } = greyAt(src, W);
  // Light blur so text inside the receipt doesn't break the paper region apart.
  const b = new Float32Array(g.length);
  for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
    let s = 0, n = 0;
    for (let dy = -2; dy <= 2; dy++) for (let dx = -2; dx <= 2; dx++) {
      const yy = y + dy, xx = x + dx;
      if (yy >= 0 && yy < h && xx >= 0 && xx < w) { s += g[yy * w + xx]; n++; }
    }
    b[y * w + x] = s / n;
  }
  const t = otsu(b);
  const bright = new Uint8Array(b.length);
  for (let i = 0; i < b.length; i++) bright[i] = b[i] > t ? 1 : 0;

  // Largest connected bright region (4-connected flood fill).
  const label = new Int32Array(b.length);
  let bestLabel = 0, bestSize = 0, next = 0;
  const stack: number[] = [];
  for (let i = 0; i < bright.length; i++) {
    if (!bright[i] || label[i]) continue;
    next++;
    let size = 0;
    stack.push(i); label[i] = next;
    while (stack.length) {
      const p = stack.pop()!;
      size++;
      const x = p % w, y = (p / w) | 0;
      const nb = [x > 0 ? p - 1 : -1, x < w - 1 ? p + 1 : -1, y > 0 ? p - w : -1, y < h - 1 ? p + w : -1];
      for (const q of nb) if (q >= 0 && bright[q] && !label[q]) { label[q] = next; stack.push(q); }
    }
    if (size > bestSize) { bestSize = size; bestLabel = next; }
  }
  const area = w * h;
  if (bestSize < area * 0.04 || bestSize > area * 0.97) return null;

  // Corners: extremes of x+y and x-y over the region.
  let tl = { x: 0, y: 0, v: Infinity }, br = { x: 0, y: 0, v: -Infinity }, tr = { x: 0, y: 0, v: -Infinity }, bl = { x: 0, y: 0, v: Infinity };
  for (let i = 0; i < label.length; i++) {
    if (label[i] !== bestLabel) continue;
    const x = i % w, y = (i / w) | 0;
    const s = x + y, d = x - y;
    if (s < tl.v) tl = { x, y, v: s };
    if (s > br.v) br = { x, y, v: s };
    if (d > tr.v) tr = { x, y, v: d };
    if (d < bl.v) bl = { x, y, v: d };
  }
  const k = src.width / w;
  const quad: Quad = [tl, tr, br, bl].map(p => ({ x: p.x * k, y: p.y * k })) as Quad;
  // Reject degenerate shapes (all corners bunched together).
  if (dist(quad[0], quad[1]) < src.width * 0.1 || dist(quad[0], quad[3]) < src.height * 0.1) return null;
  return quad;
}

function dist(a: Point, b: Point): number { return Math.hypot(a.x - b.x, a.y - b.y); }

/** Solves for the perspective transform that maps the output rectangle onto the quad. */
function homography(from: Point[], to: Point[]): number[] {
  const A: number[][] = [];
  for (let i = 0; i < 4; i++) {
    const { x, y } = from[i], { x: u, y: v } = to[i];
    A.push([x, y, 1, 0, 0, 0, -u * x, -u * y, u]);
    A.push([0, 0, 0, x, y, 1, -v * x, -v * y, v]);
  }
  // Gaussian elimination with partial pivoting on the 8x9 augmented matrix.
  for (let c = 0; c < 8; c++) {
    let p = c;
    for (let r = c + 1; r < 8; r++) if (Math.abs(A[r][c]) > Math.abs(A[p][c])) p = r;
    [A[c], A[p]] = [A[p], A[c]];
    const piv = A[c][c] || 1e-12;
    for (let j = c; j < 9; j++) A[c][j] /= piv;
    for (let r = 0; r < 8; r++) {
      if (r === c) continue;
      const f = A[r][c];
      if (f) for (let j = c; j < 9; j++) A[r][j] -= f * A[c][j];
    }
  }
  return [...A.map(r => r[8]), 1];
}

/** Cuts out the receipt and straightens it. The result is SCAN_WIDTH pixels wide (or less for small photos). */
export function warp(src: HTMLCanvasElement, q: Quad): HTMLCanvasElement {
  const rawW = (dist(q[0], q[1]) + dist(q[3], q[2])) / 2;
  const rawH = (dist(q[0], q[3]) + dist(q[1], q[2])) / 2;
  const scale = Math.min(SCAN_WIDTH, rawW * 1.4) / rawW;
  const W = Math.max(1, Math.round(rawW * scale)), H = Math.max(1, Math.round(rawH * scale));
  const m = homography([{ x: 0, y: 0 }, { x: W, y: 0 }, { x: W, y: H }, { x: 0, y: H }], q);
  const sd = ctx2d(src).getImageData(0, 0, src.width, src.height).data;
  const out = canvas(W, H);
  const o = ctx2d(out);
  const img = o.createImageData(W, H);
  const od = img.data, sw = src.width, sh = src.height;
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const den = m[6] * x + m[7] * y + m[8];
      const u = (m[0] * x + m[1] * y + m[2]) / den;
      const v = (m[3] * x + m[4] * y + m[5]) / den;
      const oi = (y * W + x) * 4;
      if (u < 0 || v < 0 || u >= sw - 1 || v >= sh - 1) { od[oi] = od[oi + 1] = od[oi + 2] = 255; od[oi + 3] = 255; continue; }
      const x0 = u | 0, y0 = v | 0, fx = u - x0, fy = v - y0;
      const i00 = (y0 * sw + x0) * 4, i10 = i00 + 4, i01 = i00 + sw * 4, i11 = i01 + 4;
      for (let c = 0; c < 3; c++) {
        const top = sd[i00 + c] + (sd[i10 + c] - sd[i00 + c]) * fx;
        const bot = sd[i01 + c] + (sd[i11 + c] - sd[i01 + c]) * fx;
        od[oi + c] = top + (bot - top) * fy;
      }
      od[oi + 3] = 255;
    }
  }
  o.putImageData(img, 0, 0);
  return out;
}

function resizeToWidth(src: HTMLCanvasElement, width: number): HTMLCanvasElement {
  if (src.width === width) return src;
  const c = canvas(width, src.height * width / src.width);
  const x = ctx2d(c);
  x.imageSmoothingQuality = 'high';
  x.drawImage(src, 0, 0, c.width, c.height);
  return c;
}

/**
 * How many pixels of `next`'s top repeat the bottom of `prev` (both the same width).
 * Compares a narrow grey version of each row; returns 0 when no convincing overlap is found.
 */
export function findOverlap(prev: HTMLCanvasElement, next: HTMLCanvasElement): number {
  const N = 120;
  const a = greyAt(prev, N), b = greyAt(next, N);
  const k = prev.width / N;
  const maxRows = Math.min(a.h, b.h);
  const minRows = Math.max(6, Math.round(maxRows * 0.05));
  const limit = Math.round(maxRows * 0.7);
  // Ignore overlaps made only of blank paper: they match anywhere.
  const rowVar = (g: Float32Array, w: number, y: number) => {
    let s = 0, s2 = 0;
    for (let x = 0; x < w; x++) { const v = g[y * w + x]; s += v; s2 += v * v; }
    const m = s / w; return s2 / w - m * m;
  };
  let best = 0, bestScore = Infinity;
  for (let ov = minRows; ov <= limit; ov++) {
    let diff = 0, n = 0, textRows = 0;
    for (let r = 0; r < ov; r += 1) {
      const ya = a.h - ov + r, yb = r;
      if (rowVar(a.g, a.w, ya) > 120) textRows++;
      for (let x = 2; x < N - 2; x += 2) { diff += Math.abs(a.g[ya * N + x] - b.g[yb * N + x]); n++; }
    }
    const score = diff / n;
    if (textRows >= 3 && score < bestScore) { bestScore = score; best = ov; }
  }
  return bestScore < 14 ? Math.round(best * k) : 0;
}

export interface Join { overlap: number; }

/** Joins parts top to bottom. `joins[i]` is the overlap removed between part i and part i+1. */
export function stitch(parts: HTMLCanvasElement[], joins: Join[]): HTMLCanvasElement {
  if (parts.length === 0) throw new Error('No parts to join.');
  const width = Math.min(...parts.map(p => p.width));
  const scaled = parts.map(p => resizeToWidth(p, width));
  let total = scaled[0].height;
  for (let i = 1; i < scaled.length; i++) total += Math.max(0, scaled[i].height - (joins[i - 1]?.overlap ?? 0));
  // Stay inside the browser's canvas size limit for very long receipts.
  const shrink = Math.min(1, Math.sqrt(MAX_CANVAS_PIXELS / (width * total)));
  const out = canvas(width * shrink, total * shrink);
  const x = ctx2d(out);
  x.fillStyle = '#fff';
  x.fillRect(0, 0, out.width, out.height);
  let y = 0;
  scaled.forEach((p, i) => {
    const cut = i === 0 ? 0 : Math.min(p.height - 1, joins[i - 1]?.overlap ?? 0);
    x.drawImage(p, 0, cut, p.width, p.height - cut, 0, y * shrink, out.width, (p.height - cut) * shrink);
    y += p.height - cut;
  });
  return out;
}

/**
 * Makes the scan look scanned: evens out shadows and lighting (each pixel divided by the local paper brightness),
 * then boosts contrast. 'bw' gives crisp grey-scale; 'colour' keeps colours (logos, stamps) but whitens the paper.
 */
export function enhance(src: HTMLCanvasElement, mode: ScanMode): HTMLCanvasElement {
  const W = src.width, H = src.height;
  const out = canvas(W, H);
  const o = ctx2d(out);
  o.drawImage(src, 0, 0);
  const img = o.getImageData(0, 0, W, H);
  const d = img.data;

  // Paper brightness map: a coarse grid of the brightest values, smoothed and scaled back up.
  const cell = Math.max(8, Math.round(Math.min(W, H) / 40));
  const gw = Math.ceil(W / cell), gh = Math.ceil(H / cell);
  const grid = new Float32Array(gw * gh);
  for (let gy = 0; gy < gh; gy++) for (let gx = 0; gx < gw; gx++) {
    const vals: number[] = [];
    for (let y = gy * cell; y < Math.min(H, (gy + 1) * cell); y += 2) for (let x = gx * cell; x < Math.min(W, (gx + 1) * cell); x += 2) {
      const i = (y * W + x) * 4;
      vals.push(0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2]);
    }
    vals.sort((a, b) => a - b);
    grid[gy * gw + gx] = vals[Math.floor(vals.length * 0.9)] ?? 255; // 90th percentile ≈ paper, ignoring text
  }
  const sm = new Float32Array(grid.length);
  for (let gy = 0; gy < gh; gy++) for (let gx = 0; gx < gw; gx++) {
    let s = 0, n = 0;
    for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) {
      const yy = gy + dy, xx = gx + dx;
      if (yy >= 0 && yy < gh && xx >= 0 && xx < gw) { s += grid[yy * gw + xx]; n++; }
    }
    sm[gy * gw + gx] = Math.max(40, s / n);
  }
  const paper = (x: number, y: number) => {
    const fx = Math.min(gw - 1.001, Math.max(0, x / cell - 0.5)), fy = Math.min(gh - 1.001, Math.max(0, y / cell - 0.5));
    const x0 = fx | 0, y0 = fy | 0, tx = fx - x0, ty = fy - y0;
    const x1 = Math.min(gw - 1, x0 + 1), y1 = Math.min(gh - 1, y0 + 1);
    const a = sm[y0 * gw + x0] + (sm[y0 * gw + x1] - sm[y0 * gw + x0]) * tx;
    const b = sm[y1 * gw + x0] + (sm[y1 * gw + x1] - sm[y1 * gw + x0]) * tx;
    return a + (b - a) * ty;
  };
  // Contrast curve on the normalised value (1 = paper): text gets darker, paper goes white.
  const curve = (r: number) => {
    const t = Math.min(1, Math.max(0, (r - 0.35) / 0.55));
    return 255 * Math.pow(t, mode === 'bw' ? 1.6 : 1.25);
  };
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
    const i = (y * W + x) * 4;
    const p = paper(x, y);
    if (mode === 'bw') {
      const v = curve((0.299 * d[i] + 0.587 * d[i + 1] + 0.114 * d[i + 2]) / p);
      d[i] = d[i + 1] = d[i + 2] = v;
    } else {
      d[i] = curve(d[i] / p); d[i + 1] = curve(d[i + 1] / p); d[i + 2] = curve(d[i + 2] / p);
    }
  }
  o.putImageData(img, 0, 0);
  return out;
}

export function toJpeg(c: HTMLCanvasElement, quality = 0.85): Promise<Blob> {
  return new Promise((resolve, reject) => c.toBlob(b => b ? resolve(b) : reject(new Error('Couldn\'t save the scan.')), 'image/jpeg', quality));
}

/**
 * For the AI: the scan cut into sections that are each easy to read (AI vision shrinks very tall images,
 * which would make small receipt text unreadable). Sections overlap slightly so no line is lost at a cut.
 */
export async function sections(scan: HTMLCanvasElement): Promise<Blob[]> {
  const sectionH = Math.round(scan.width * 1.4);
  if (scan.height <= sectionH * 1.15) return [];
  const overlap = Math.round(sectionH * 0.08);
  const blobs: Blob[] = [];
  for (let y = 0; y < scan.height && blobs.length < 12; y += sectionH - overlap) {
    const h = Math.min(sectionH, scan.height - y);
    const c = canvas(scan.width, h);
    ctx2d(c).drawImage(scan, 0, y, scan.width, h, 0, 0, scan.width, h);
    blobs.push(await toJpeg(c, 0.85));
    if (y + h >= scan.height) break;
  }
  return blobs;
}
