import { ChangeDetectionStrategy, Component, Injectable, inject, signal } from '@angular/core';

export interface Toast { id: number; text: string; error: boolean; action?: string; run?: () => void; }

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly list = signal<Toast[]>([]);
  private next = 0;

  show(text: string, opts: { error?: boolean; action?: string; run?: () => void; ms?: number } = {}): void {
    const t: Toast = { id: ++this.next, text, error: !!opts.error, action: opts.action, run: opts.run };
    this.list.update(l => [...l.slice(-2), t]);
    setTimeout(() => this.dismiss(t.id), opts.ms ?? (opts.action ? 9000 : opts.error ? 7000 : 4000));
  }

  error(text: string): void { this.show(text, { error: true }); }
  dismiss(id: number): void { this.list.update(l => l.filter(t => t.id !== id)); }
}

@Component({
  selector: 'k-toasts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="toasts" role="status" aria-live="polite">
      @for (t of toasts.list(); track t.id) {
        <div class="toast" [class.err]="t.error">
          <span>{{ t.text }}</span>
          @if (t.action) { <button type="button" (click)="act(t)">{{ t.action }}</button> }
          <button type="button" class="x" aria-label="Dismiss" (click)="toasts.dismiss(t.id)">✕</button>
        </div>
      }
    </div>`
})
export class ToastHost {
  protected readonly toasts = inject(ToastService);

  protected act(t: Toast): void {
    if (t.run) t.run();
    this.toasts.dismiss(t.id);
  }
}
