import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorText } from '../../core/api/api.service';
import { Receipt } from '../../core/api/models';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';
import { ReceiptImage } from './receipt-image';
import { ReceiptScanner, ScanResult } from './scan/receipt-scanner';

@Component({
  selector: 'k-receipts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon, RmPipe, DayPipe, ReceiptImage, ReceiptScanner],
  template: `
<div class="page-head">
  <div><h1>Receipts</h1><p>Scan a receipt: Kira crops and straightens it, joins long receipts taken in parts, and keeps a clean copy you can print. It also reads the details and checks for tax relief.</p></div>
  <div class="acts">
    <button type="button" class="btn" (click)="manual()"><k-icon name="plus" [size]="16" />Enter by hand</button>
    <button type="button" class="btn primary" [disabled]="busy()" (click)="scanning.set(true)"><k-icon name="camera" [size]="16" />{{ busy() ? 'Reading…' : 'Scan receipt' }}</button>
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
  } @empty { <div class="card pad empty" style="grid-column: 1 / -1">{{ attention() ? 'Nothing needs attention.' : 'No receipts yet. Tap Scan receipt to start.' }}</div> }
</div>
@if (scanning()) { <k-receipt-scanner (done)="onScanned($event)" (closed)="scanning.set(false)" /> }`
})
export class ReceiptsPage {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  protected readonly list = signal<Receipt[]>([]);
  protected readonly attention = signal(false);
  protected readonly busy = signal(false);
  protected readonly scanning = signal(false);
  protected readonly needing = computed(() => this.list().filter(r => r.status !== 'Saved' || r.paymentStatus === 'Unknown' || (!r.reliefCode && !!r.suggestedReliefCode)));
  protected readonly shown = computed(() => this.attention() ? this.needing() : this.list());

  constructor() { void this.load(); }

  private async load(): Promise<void> {
    try { this.list.set(await this.api.get<Receipt[]>('/personal/receipts')); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async onScanned(result: ScanResult): Promise<void> {
    this.scanning.set(false);
    const form = new FormData();
    if (result.kind === 'pdf') form.append('file', result.file);
    else {
      form.append('file', result.scan, 'receipt-scan.jpg');
      result.sections.forEach((s, i) => form.append('sections', s, `section-${i + 1}.jpg`));
    }
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
