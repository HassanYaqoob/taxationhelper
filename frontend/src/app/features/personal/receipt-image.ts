import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal } from '@angular/core';
import { ApiService } from '../../core/api/api.service';
import { Icon } from '../../core/ui/icon';

/** Shows a protected image (receipt or payment screenshot). Fetched with the access token, released when destroyed. */
@Component({
  selector: 'k-receipt-image',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `@if (src(); as s) { <img [src]="s" [alt]="alt()" loading="lazy"> } @else { <k-icon [name]="failed() ? 'doc' : 'img'" [size]="28" /> }`,
  host: { style: 'display: contents' }
})
export class ReceiptImage {
  readonly url = input.required<string>();
  readonly alt = input('Receipt');
  protected readonly src = signal<string | null>(null);
  protected readonly failed = signal(false);
  private readonly api = inject(ApiService);

  constructor() {
    effect(onCleanup => {
      const url = this.url();
      let current: string | null = null;
      let cancelled = false;
      this.api.objectUrl(url).then(s => { if (cancelled) URL.revokeObjectURL(s); else { current = s; this.src.set(s); } })
        .catch(() => this.failed.set(true));
      onCleanup(() => { cancelled = true; if (current) URL.revokeObjectURL(current); this.src.set(null); });
    });
    inject(DestroyRef).onDestroy(() => { const s = this.src(); if (s) URL.revokeObjectURL(s); });
  }
}
