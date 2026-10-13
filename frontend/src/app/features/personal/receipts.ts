import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorText } from '../../core/api/api.service';
import { Receipt } from '../../core/api/models';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe, takeFile } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';
import { ReceiptImage } from './receipt-image';

@Component({
  selector: 'k-receipts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon, RmPipe, DayPipe, ReceiptImage],
  template: `
<div class="page-head">
  <div><h1>Receipts</h1><p>Snap or upload a receipt. Kira reads it, picks a category, and checks whether it qualifies for tax relief.</p></div>
  <div class="acts">
    <button type="button" class="btn" (click)="manual()"><k-icon name="plus" [size]="16" />Enter by hand</button>
    <label class="btn primary filebtn" [class.disabled]="busy()"><k-icon name="camera" [size]="16" />{{ busy() ? 'Reading…' : 'Upload receipt' }}
      <input type="file" accept="image/*,application/pdf" capture="environment" [disabled]="busy()" (change)="upload($event)" aria-label="Upload receipt"></label>
  </div>
</div>
<div class="tabs" role="tablist">
  <button type="button" role="tab" [class.on]="!attention()" (click)="attention.set(false)">All <span class="cnt">{{ list().length }}</span></button>
  <button type="button" role="tab" [class.on]="attention()" (click)="attention.set(true)">Needs attention <span class="cnt">{{ needing().length }}</span></button>
</div>
<div class="rgrid">
  @for (r of shown(); track r.id) {
    <a class="rcard" [routerLink]="['/p/receipts', r.id]">
      <div class="rthumb">@if (r.hasImage) { <k-receipt-image [url]="'/personal/receipts/' + r.id + '/image'" [alt]="r.merchant" /> } @else { <k-icon name="receipt" [size]="28" /> }</div>
      <div style="padding: 12px 14px">
        <div class="row"><b class="grow desc">{{ r.merchant || 'Unnamed receipt' }}</b><b>{{ r.total | rm }}</b></div>
        <div class="caption">{{ r.date | day }} · {{ r.category }}</div>
        <div class="row wrap" style="gap: 6px; margin-top: 8px">
          @if (r.status === 'Review') { <span class="pill wa">Check and save</span> }
          @if (r.status === 'Error') { <span class="pill no">Couldn't read</span> }
          @switch (r.paymentStatus) {
            @case ('Cash') { <span class="pill gr"><k-icon name="cash" [size]="12" />Cash</span> }
            @case ('Account') { <span class="pill gr"><k-icon name="card" [size]="12" />{{ r.paymentAccountName }}</span> }
            @case ('NotPaid') { <span class="pill gr">Not paid</span> }
            @default { <span class="pill wa">Payment?</span> }
          }
          @if (r.reliefCode) { <span class="pill ok"><k-icon name="heart" [size]="12" />Relief</span> }
          @else if (r.suggestedReliefCode) { <span class="pill in">Relief?</span> }
          @if (r.statementMatched) { <span class="pill ok"><k-icon name="check" [size]="12" />On statement</span> }
        </div>
      </div>
    </a>
  } @empty { <div class="card pad empty" style="grid-column: 1 / -1">{{ attention() ? 'Nothing needs attention.' : 'No receipts yet. Upload a photo or PDF to start.' }}</div> }
</div>`
})
export class ReceiptsPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  protected readonly list = signal<Receipt[]>([]);
  protected readonly attention = signal(false);
  protected readonly busy = signal(false);
  protected readonly needing = computed(() => this.list().filter(r => r.status !== 'Saved' || r.paymentStatus === 'Unknown' || (!r.reliefCode && !!r.suggestedReliefCode)));
  protected readonly shown = computed(() => this.attention() ? this.needing() : this.list());

  constructor() { void this.load(); }

  private async load(): Promise<void> {
    try { this.list.set(await this.api.get<Receipt[]>('/personal/receipts')); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async upload(event: Event): Promise<void> {
    const file = takeFile(event);
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    this.busy.set(true);
    try {
      const r = await this.api.upload<Receipt>('/personal/receipts', form);
      await this.router.navigate(['/p/receipts', r.id]);
    } catch (e) { this.toasts.error(errorText(e)); }
    finally { this.busy.set(false); }
  }

  protected async manual(): Promise<void> {
    try { const r = await this.api.post<Receipt>('/personal/receipts/manual'); await this.router.navigate(['/p/receipts', r.id]); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}
