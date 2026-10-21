import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, output, signal } from '@angular/core';
import { Icon } from '../../../core/ui/icon';
import { takeFile } from '../../../core/ui/pipes';
import { ToastService } from '../../../core/ui/toast.service';
import {
  Join, Point, Quad, ScanMode, detectReceipt, enhance, findOverlap, fullQuad, loadPhoto, rotate90, sections, stitch, toJpeg, warp
} from './scan-engine';

export type ScanResult = { kind: 'scan'; scan: Blob; sections: Blob[] } | { kind: 'pdf'; file: File };

type Step = 'pick' | 'adjust' | 'review';

/**
 * Scan a paper receipt: take or choose a photo, check the corners, add more parts for a long receipt,
 * then save one clean scan. Everything is processed on the device; only the finished scan is uploaded.
 */
@Component({
  selector: 'k-receipt-scanner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  styles: `
    .stage { position: relative; margin: 0 auto; width: fit-content; max-width: 100%; touch-action: none; user-select: none; }
    .stage img { display: block; max-width: 100%; max-height: 62vh; border-radius: 8px; }
    .stage svg { position: absolute; inset: 0; width: 100%; height: 100%; touch-action: none; }
    .preview { max-height: 62vh; overflow: auto; background: var(--sunk); border-radius: 10px; padding: 10px; text-align: center; }
    .preview img { width: 100%; max-width: 420px; box-shadow: var(--shadow); background: #fff; }
    .pick { display: grid; gap: 10px; }
    .pick .btn { justify-content: flex-start; min-height: 52px; }
    .tip { background: var(--info-soft); color: var(--info); border-radius: 10px; padding: 10px 12px; font-size: 13.5px; }
    .parts { display: grid; gap: 8px; }
    .join { display: grid; grid-template-columns: 1fr auto; gap: 6px; align-items: center; font-size: 13px; color: var(--muted); }
    .join input[type=range] { width: 100%; }
    .busy { position: absolute; inset: 0; display: grid; place-items: center; background: color-mix(in srgb, var(--surface) 75%, transparent); border-radius: inherit; font-weight: 600; z-index: 5; }
  `,
  template: `
<div class="modal-wrap" (click)="close()">
  <div class="modal wide" style="position: relative" (click)="$event.stopPropagation()" role="dialog" aria-label="Scan a receipt">
    @if (busy()) { <div class="busy">{{ busy() }}</div> }
    <div class="mh">
      <h2>{{ step() === 'pick' ? (parts().length ? 'Add part ' + (parts().length + 1) : 'Scan a receipt') : step() === 'adjust' ? 'Check the edges' : 'Your scan' }}</h2>
      <p>
        @switch (step()) {
          @case ('pick') { Photograph the receipt on a darker surface for the best result. }
          @case ('adjust') { Drag the four corners onto the receipt's corners, then use this part. }
          @case ('review') { This is the copy Kira keeps. Add the next part if the receipt continues. }
        }
      </p>
    </div>

    <div class="mb">
      @switch (step()) {
        @case ('pick') {
          <div class="pick">
            <label class="btn primary filebtn"><k-icon name="camera" [size]="18" />Take a photo
              <input type="file" accept="image/*" capture="environment" (change)="onPhoto($event)" aria-label="Take a photo"></label>
            <label class="btn filebtn"><k-icon name="img" [size]="18" />Choose a photo
              <input type="file" accept="image/*" (change)="onPhoto($event)" aria-label="Choose a photo"></label>
            @if (!parts().length) {
              <label class="btn filebtn"><k-icon name="doc" [size]="18" />Upload a PDF receipt (e-receipt or invoice)
                <input type="file" accept="application/pdf" (change)="onPdf($event)" aria-label="Upload a PDF receipt"></label>
              <div class="tip"><b>Long receipt?</b> Photograph it in parts, top to bottom, overlapping a few lines each time. After the first part you can add the next one, and Kira joins them into one receipt.</div>
            } @else {
              <div class="tip">Photograph the next section, starting a few lines above where part {{ parts().length }} ended.</div>
            }
          </div>
        }
        @case ('adjust') {
          <div class="stage">
            <img [src]="photoUrl()" alt="Your photo" draggable="false">
            <svg [attr.viewBox]="'0 0 ' + photoW() + ' ' + photoH()" preserveAspectRatio="none"
                 (pointermove)="drag($event)" (pointerup)="dragEnd()" (pointercancel)="dragEnd()" (pointerleave)="dragEnd()">
              <polygon [attr.points]="polygon()" fill="rgba(15,118,110,.12)" stroke="#0F766E" [attr.stroke-width]="handle() / 4" />
              @for (p of quad(); track $index) {
                <circle [attr.cx]="p.x" [attr.cy]="p.y" [attr.r]="handle()" fill="#fff" stroke="#0F766E" [attr.stroke-width]="handle() / 3"
                        style="cursor: grab" (pointerdown)="dragStart($event, $index)" />
              }
            </svg>
          </div>
          @if (!detected()) { <p class="caption" style="text-align: center">Couldn't find the receipt's edges automatically. Drag the corners to it.</p> }
        }
        @case ('review') {
          <div class="grid g65">
            <div class="preview">@if (previewUrl()) { <img [src]="previewUrl()" alt="Scanned receipt"> }</div>
            <div class="stack">
              <div class="seg" role="group" aria-label="Scan style">
                <button type="button" [class.on]="mode() === 'bw'" (click)="setMode('bw')">Black &amp; white</button>
                <button type="button" [class.on]="mode() === 'colour'" (click)="setMode('colour')">Colour</button>
              </div>
              <div class="parts">
                @for (p of parts(); track $index; let i = $index) {
                  <div class="row"><k-icon name="receipt" [size]="16" /><span class="grow">Part {{ i + 1 }}</span>
                    @if (parts().length > 1) { <button type="button" class="btn sm ghost" (click)="removePart(i)">Remove</button> }</div>
                  @if (i < joins().length) {
                    <div class="join">
                      <span>Overlap removed between part {{ i + 1 }} and {{ i + 2 }}: {{ joins()[i].overlap }} px {{ autoJoin()[i] === joins()[i].overlap ? '(auto)' : '' }}</span>
                      <button type="button" class="btn sm ghost" (click)="resetJoin(i)">Auto</button>
                      <input type="range" min="0" [attr.max]="maxOverlap(i)" [value]="joins()[i].overlap" style="grid-column: 1 / -1" (input)="setJoin(i, $any($event.target).valueAsNumber)"
                             [attr.aria-label]="'Overlap between part ' + (i + 1) + ' and ' + (i + 2)">
                    </div>
                  }
                }
              </div>
              <p class="caption">If a line appears twice or is missing where two parts meet, move the overlap slider.</p>
              <button type="button" class="btn" (click)="step.set('pick')"><k-icon name="plus" [size]="16" />Add next part (long receipt)</button>
            </div>
          </div>
        }
      }
    </div>

    <div class="mf">
      @switch (step()) {
        @case ('pick') {
          <button type="button" class="btn" (click)="parts().length ? step.set('review') : close()">{{ parts().length ? 'Back to scan' : 'Cancel' }}</button>
        }
        @case ('adjust') {
          <button type="button" class="btn ghost" (click)="rotate()">Rotate</button>
          <button type="button" class="btn ghost" (click)="resetCorners()">Whole photo</button>
          <span class="grow"></span>
          <button type="button" class="btn" (click)="cancelPart()">Retake</button>
          <button type="button" class="btn primary" (click)="usePart()">Use this part</button>
        }
        @case ('review') {
          <button type="button" class="btn" (click)="close()">Cancel</button>
          <button type="button" class="btn primary" [disabled]="!!busy()" (click)="save()"><k-icon name="check" [size]="16" />Save receipt</button>
        }
      }
    </div>
  </div>
</div>`
})
export class ReceiptScanner {
  readonly done = output<ScanResult>();
  readonly closed = output<void>();
  private readonly toasts = inject(ToastService);

  protected readonly step = signal<Step>('pick');
  protected readonly busy = signal('');
  protected readonly mode = signal<ScanMode>('bw');

  // Current photo being adjusted
  private photo: HTMLCanvasElement | null = null;
  protected readonly photoUrl = signal('');
  protected readonly photoW = signal(1);
  protected readonly photoH = signal(1);
  protected readonly quad = signal<Quad>(fullQuad(Object.assign(document.createElement('canvas'), { width: 1, height: 1 })));
  protected readonly detected = signal(true);
  protected readonly handle = computed(() => Math.max(this.photoW(), this.photoH()) * 0.018);
  protected readonly polygon = computed(() => this.quad().map(p => `${p.x},${p.y}`).join(' '));
  private dragging = -1;

  // Accepted parts and how they join
  private partCanvases: HTMLCanvasElement[] = [];
  protected readonly parts = signal<number[]>([]); // one entry per part; values unused, drives the template
  protected readonly joins = signal<Join[]>([]);
  protected readonly autoJoin = signal<number[]>([]);
  protected readonly previewUrl = signal('');
  private previewTimer?: ReturnType<typeof setTimeout>;

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      if (this.photoUrl()) URL.revokeObjectURL(this.photoUrl());
      if (this.previewUrl()) URL.revokeObjectURL(this.previewUrl());
      clearTimeout(this.previewTimer);
    });
  }

  /** Runs heavy work after the "busy" message has had a chance to paint. */
  private async work<T>(message: string, fn: () => Promise<T> | T): Promise<T | undefined> {
    this.busy.set(message);
    await new Promise(r => setTimeout(r, 30));
    try { return await fn(); }
    catch (e) { this.toasts.error(e instanceof Error ? e.message : 'Something went wrong while scanning.'); return undefined; }
    finally { this.busy.set(''); }
  }

  protected close(): void { this.closed.emit(); }

  protected onPdf(event: Event): void {
    const file = takeFile(event);
    if (file) this.done.emit({ kind: 'pdf', file });
  }

  protected async onPhoto(event: Event): Promise<void> {
    const file = takeFile(event);
    if (!file) return;
    await this.work('Finding the receipt…', async () => {
      this.photo = await loadPhoto(file);
      await this.showPhoto();
    });
  }

  private async showPhoto(): Promise<void> {
    const c = this.photo!;
    const q = detectReceipt(c);
    this.detected.set(!!q);
    this.quad.set(q ?? fullQuad(c));
    this.photoW.set(c.width);
    this.photoH.set(c.height);
    if (this.photoUrl()) URL.revokeObjectURL(this.photoUrl());
    this.photoUrl.set(URL.createObjectURL(await toJpeg(c, 0.8)));
    this.step.set('adjust');
  }

  protected async rotate(): Promise<void> {
    if (!this.photo) return;
    await this.work('Rotating…', async () => { this.photo = rotate90(this.photo!); await this.showPhoto(); });
  }

  protected resetCorners(): void { if (this.photo) this.quad.set(fullQuad(this.photo)); }

  protected cancelPart(): void {
    this.photo = null;
    this.step.set('pick');
  }

  protected dragStart(e: PointerEvent, i: number): void {
    this.dragging = i;
    (e.target as Element).setPointerCapture?.(e.pointerId);
    e.preventDefault();
  }

  protected drag(e: PointerEvent): void {
    if (this.dragging < 0) return;
    const svg = e.currentTarget as SVGSVGElement;
    const r = svg.getBoundingClientRect();
    const p: Point = {
      x: Math.min(this.photoW(), Math.max(0, (e.clientX - r.left) / r.width * this.photoW())),
      y: Math.min(this.photoH(), Math.max(0, (e.clientY - r.top) / r.height * this.photoH()))
    };
    const q = [...this.quad()] as Quad;
    q[this.dragging] = p;
    this.quad.set(q);
  }

  protected dragEnd(): void { this.dragging = -1; }

  protected async usePart(): Promise<void> {
    if (!this.photo) return;
    await this.work('Straightening…', () => {
      const part = warp(this.photo!, this.quad());
      const prev = this.partCanvases.at(-1);
      this.partCanvases.push(part);
      if (prev) {
        const ov = findOverlap(prev, part);
        this.joins.update(j => [...j, { overlap: ov }]);
        this.autoJoin.update(a => [...a, ov]);
      }
      this.parts.set(this.partCanvases.map((_, i) => i));
      this.photo = null;
      this.step.set('review');
    });
    this.renderPreview(0);
  }

  protected removePart(i: number): void {
    this.partCanvases.splice(i, 1);
    // Joins next to a removed part are recalculated.
    const joins: Join[] = [], auto: number[] = [];
    for (let k = 1; k < this.partCanvases.length; k++) {
      const ov = findOverlap(this.partCanvases[k - 1], this.partCanvases[k]);
      joins.push({ overlap: ov }); auto.push(ov);
    }
    this.joins.set(joins);
    this.autoJoin.set(auto);
    this.parts.set(this.partCanvases.map((_, k) => k));
    this.renderPreview(0);
  }

  protected maxOverlap(i: number): number {
    const a = this.partCanvases[i], b = this.partCanvases[i + 1];
    return a && b ? Math.round(Math.min(a.height, b.height) * 0.85) : 0;
  }

  protected setJoin(i: number, value: number): void {
    this.joins.update(j => j.map((x, k) => k === i ? { overlap: value } : x));
    this.renderPreview(250);
  }

  protected resetJoin(i: number): void { this.setJoin(i, this.autoJoin()[i]); }

  protected setMode(m: ScanMode): void {
    this.mode.set(m);
    this.renderPreview(0);
  }

  private finalCanvas(): HTMLCanvasElement {
    return enhance(stitch(this.partCanvases, this.joins()), this.mode());
  }

  private renderPreview(delay: number): void {
    clearTimeout(this.previewTimer);
    this.previewTimer = setTimeout(() => void this.work('Cleaning up…', async () => {
      const blob = await toJpeg(this.finalCanvas(), 0.8);
      if (this.previewUrl()) URL.revokeObjectURL(this.previewUrl());
      this.previewUrl.set(URL.createObjectURL(blob));
    }), delay);
  }

  protected async save(): Promise<void> {
    if (!this.partCanvases.length) return;
    const result = await this.work('Preparing your scan…', async () => {
      const c = this.finalCanvas();
      return { kind: 'scan' as const, scan: await toJpeg(c, 0.85), sections: await sections(c) };
    });
    if (result) this.done.emit(result);
  }
}
