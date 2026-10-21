import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorText } from '../../core/api/api.service';
import { PaymentAccount, PaymentStatus, PERSONAL_CATEGORIES, Receipt, ReliefLine, ReliefSummary } from '../../core/api/models';
import { Icon } from '../../core/ui/icon';
import { RmPipe, takeFile } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';
import { ReceiptImage } from './receipt-image';

interface Form { merchant: string; date: string; total: number; sst: number; category: string; }

@Component({
  selector: 'k-receipt-detail',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, RmPipe, ReceiptImage],
  template: `
<div class="page-head">
  <div><a class="caption" routerLink="/p/receipts">← Receipts</a><h1>{{ r()?.merchant || 'Receipt' }}</h1>
    @if (r(); as x) { <p>{{ x.status === 'Saved' ? 'Saved to your records.' : x.status === 'Error' ? 'Kira couldn\\'t read this receipt. Enter the details by hand.' : 'Check the details, then save.' }} {{ x.reason }}</p> }</div>
  @if (r(); as x) {
    <div class="acts">
      <button type="button" class="btn danger" (click)="remove(x)"><k-icon name="trash" [size]="16" />Delete</button>
      @if (x.status !== 'Saved') { <button type="button" class="btn primary" (click)="save()"><k-icon name="check" [size]="16" />Save receipt</button> }
    </div>
  }
</div>

@if (r(); as x) {
  <div class="grid receipt-layout">
    <section class="card" style="overflow: hidden; align-self: start">
      @if (x.hasImage) {
        <div class="scan-view"><k-receipt-image [url]="'/personal/receipts/' + x.id + '/image'" [alt]="'Scan of the receipt' + (x.merchant ? ' from ' + x.merchant : '')" /></div>
        <div class="row wrap" style="padding: 10px 12px; border-top: 1px solid var(--line)">
          <button type="button" class="btn sm" (click)="downloadPdf(x)"><k-icon name="dl" [size]="14" />Download PDF</button>
          <button type="button" class="btn sm" (click)="printPdf()"><k-icon name="doc" [size]="14" />Print</button>
          <button type="button" class="btn sm ghost" (click)="downloadImage(x)">Image</button>
        </div>
      } @else { <div class="empty">Entered by hand. No scan.</div> }
      @if (x.items.length) {
        <table class="t tight"><tbody>
          @for (i of x.items; track $index) { <tr><td class="desc">{{ i.description }}</td><td class="n">{{ i.amount | rm }}</td></tr> }
        </tbody></table>
      }
    </section>

    <div class="stack">
      <form class="card pad" (ngSubmit)="update()">
        <div class="ch"><h2>Details</h2>@if (x.confidence < 0.7 && x.status === 'Review') { <span class="pill wa">Kira wasn't sure</span> }</div>
        <div class="form-grid">
          <label class="field">Merchant<input class="input" name="merchant" [(ngModel)]="form.merchant"></label>
          <label class="field">Date<input class="input" type="date" name="date" [(ngModel)]="form.date"></label>
          <label class="field">Total (RM)<input class="input" type="number" step="0.01" name="total" [(ngModel)]="form.total"></label>
          <label class="field">SST (RM)<input class="input" type="number" step="0.01" name="sst" [(ngModel)]="form.sst"></label>
          <label class="field">Category
            <select class="input" name="category" [(ngModel)]="form.category">@for (c of categories; track c) { <option [value]="c">{{ c }}</option> }</select></label>
          <label class="field">Receipt no.<input class="input" disabled [value]="x.receiptNo || '—'"></label>
        </div>
        <div class="row" style="margin-top: 14px"><span class="grow caption">@if (x.statementMatched) { ✓ Matched to a line on your bank statement. }</span><button type="submit" class="btn">Update details</button></div>
      </form>

      <section class="card pad">
        <div class="ch"><h2>How it was paid</h2>
          @if (x.paymentSource === 'Screenshot') { <span class="pill in">From screenshot</span> }
          @else if (x.paymentSource === 'Receipt') { <span class="pill in">From the receipt</span> }
          @else if (x.paymentSource === 'Manual') { <span class="pill gr">Set by you</span> }</div>
        <div class="seg mb16">
          @for (o of payOptions; track o.status) {
            <button type="button" [class.on]="x.paymentStatus === o.status" (click)="pay(o.status)"><k-icon [name]="o.icon" [size]="16" />{{ o.label }}</button>
          }
        </div>
        @if (x.paymentStatus === 'Account' || choosing()) {
          <label class="field mb16">Paid from
            <select class="input" [ngModel]="x.paymentAccountId ?? ''" (ngModelChange)="pay('Account', $event)">
              <option value="" disabled>Choose an account…</option>
              @for (a of payable(); track a.id) { <option [value]="a.id">{{ a.name }}{{ a.last4 ? ' ••' + a.last4 : '' }}</option> }
            </select></label>
        }
        @if (x.paymentNote) { <p class="caption">{{ x.paymentNote }}</p> }
        @if (x.printedPayment) { <p class="caption">Printed on the receipt: {{ x.printedPayment }}</p> }

        <div class="row wrap" style="margin-top: 6px">
          <label class="btn filebtn" [class.disabled]="busy()"><k-icon name="img" [size]="16" />{{ busy() ? 'Reading…' : x.hasScreenshot ? 'Replace payment screenshot' : 'Upload payment screenshot' }}
            <input type="file" accept="image/*,application/pdf" [disabled]="busy()" (change)="screenshot($event)" aria-label="Upload payment screenshot"></label>
          <span class="caption grow">Kira reads the bank, card digits and amount, and picks the account.</span>
        </div>

        @if (x.screenshot; as s) {
          <div class="grid g2" style="margin-top: 14px">
            @if (x.hasScreenshot) { <div class="rthumb" style="border-radius: 10px"><k-receipt-image [url]="'/personal/receipts/' + x.id + '/screenshot'" alt="Payment screenshot" /></div> }
            <table class="t tight"><tbody>
              <tr><td class="caption">Bank / wallet</td><td>{{ s.institution || '—' }}</td></tr>
              <tr><td class="caption">Card / account</td><td>{{ s.last4 ? '••' + s.last4 : '—' }}</td></tr>
              <tr><td class="caption">Amount</td><td>{{ s.amount | rm }} @if (!s.amountMatches) { <span class="pill wa">Differs from receipt</span> }</td></tr>
              <tr><td class="caption">When</td><td>{{ s.when || '—' }}</td></tr>
              <tr><td class="caption">To</td><td>{{ s.recipient || '—' }}</td></tr>
              <tr><td class="caption">Status</td><td>{{ s.status || '—' }}</td></tr>
            </tbody></table>
          </div>
        }
      </section>

      <section class="card pad">
        <div class="ch"><h2>Tax relief</h2>@if (x.reliefCode) { <span class="pill ok">{{ reliefName(x.reliefCode) }}</span> }</div>
        @if (!x.reliefCode && x.suggestedReliefCode) {
          <div class="banner in"><k-icon name="spark" [size]="16" /><div class="grow">This might qualify for <b>{{ reliefName(x.suggestedReliefCode) }}</b>. {{ x.reliefReason }}</div></div>
          <div class="row mb16"><button type="button" class="btn sm primary" (click)="relief(x.suggestedReliefCode)">Claim it</button><button type="button" class="btn sm" (click)="dismissRelief()">Not relevant</button></div>
        }
        <label class="field">Relief category
          <select class="input" [ngModel]="x.reliefCode ?? ''" (ngModelChange)="relief($event || null)">
            <option value="">None</option>
            @for (l of reliefs(); track l.code) { <option [value]="l.code">{{ l.name }} (limit {{ l.limit | rm:'0' }})</option> }
          </select></label>
        <p class="caption">Keep the receipt for 7 years. LHDN can ask for it when you claim.</p>
      </section>
    </div>
  </div>
} @else { <div class="card pad skel" style="height: 300px"></div> }`
})
export class ReceiptDetailPage {
  readonly id = input.required<string>();
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly r = signal<Receipt | null>(null);
  protected readonly accounts = signal<PaymentAccount[]>([]);
  protected readonly reliefs = signal<ReliefLine[]>([]);
  protected readonly busy = signal(false);
  protected readonly choosing = signal(false);
  protected readonly payable = computed(() => this.accounts().filter(a => a.kind !== 'Cash'));
  protected readonly categories = PERSONAL_CATEGORIES;
  protected readonly payOptions: { status: PaymentStatus; label: string; icon: string }[] = [
    { status: 'Cash', label: 'Cash', icon: 'cash' }, { status: 'Account', label: 'Account', icon: 'card' }, { status: 'NotPaid', label: 'Not paid yet', icon: 'x' }
  ];
  protected form: Form = { merchant: '', date: '', total: 0, sst: 0, category: 'Other' };

  constructor() {
    effect(() => { void this.load(this.id()); });
    this.api.get<PaymentAccount[]>('/personal/accounts').then(a => this.accounts.set(a)).catch(() => undefined);
  }

  private set(x: Receipt): void {
    this.r.set(x);
    this.form = { merchant: x.merchant, date: x.date, total: x.total, sst: x.sst, category: x.category };
    this.choosing.set(false);
  }

  private async load(id: string): Promise<void> {
    try {
      const x = await this.api.get<Receipt>(`/personal/receipts/${id}`);
      this.set(x);
      const summary = await this.api.get<ReliefSummary>('/personal/reliefs', { year: Number(x.date.slice(0, 4)) });
      this.reliefs.set(summary.lines);
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected reliefName(code: string): string { return this.reliefs().find(l => l.code === code)?.name ?? code; }

  private async run(p: Promise<Receipt>, message?: string): Promise<void> {
    try { this.set(await p); if (message) this.toasts.show(message); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected update(): Promise<void> {
    const f = this.form;
    return this.run(this.api.put<Receipt>(`/personal/receipts/${this.id()}`,
      { merchant: f.merchant, date: f.date, total: Number(f.total) || 0, sst: Number(f.sst) || 0, category: f.category }), 'Updated.');
  }

  protected pay(status: PaymentStatus, accountId?: string): void {
    if (status === 'Account' && !accountId) {
      const only = this.payable();
      if (only.length === 1) accountId = only[0].id;
      else { this.choosing.set(true); return; }
    }
    void this.run(this.api.put<Receipt>(`/personal/receipts/${this.id()}/payment`, { status, accountId: accountId ?? null }));
  }

  protected async screenshot(event: Event): Promise<void> {
    const file = takeFile(event);
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    this.busy.set(true);
    try {
      const x = await this.api.upload<Receipt>(`/personal/receipts/${this.id()}/screenshot`, form);
      this.set(x);
      this.toasts.show(x.paymentAccountName ? `Paid from ${x.paymentAccountName}.` : 'Read the screenshot but couldn\'t tell the account. Choose it below.');
      if (!x.paymentAccountName) this.choosing.set(true);
    } catch (e) { this.toasts.error(errorText(e)); }
    finally { this.busy.set(false); }
  }

  protected relief(code: string | null): Promise<void> { return this.run(this.api.put<Receipt>(`/personal/receipts/${this.id()}/relief`, { code })); }
  protected dismissRelief(): Promise<void> { return this.run(this.api.put<Receipt>(`/personal/receipts/${this.id()}/relief`, { code: null, dismiss: true })); }

  protected async save(): Promise<void> {
    await this.update();
    await this.run(this.api.post<Receipt>(`/personal/receipts/${this.id()}/save`), 'Saved to your records.');
  }

  protected downloadPdf(x: Receipt): void {
    this.api.download(`/personal/receipts/${x.id}/pdf`, undefined, `receipt-${x.date}.pdf`).catch(e => this.toasts.error(errorText(e)));
  }

  protected downloadImage(x: Receipt): void {
    this.api.download(`/personal/receipts/${x.id}/image`, undefined, `receipt-${x.date}.jpg`).catch(e => this.toasts.error(errorText(e)));
  }

  /** Opens the PDF in a new tab, ready to print. The tab is opened first so pop-up blockers allow it. */
  protected async printPdf(): Promise<void> {
    const tab = window.open('', '_blank');
    try {
      const url = await this.api.objectUrl(`/personal/receipts/${this.id()}/pdf`);
      if (tab) tab.location.href = url; else window.location.href = url;
      setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } catch (e) {
      tab?.close();
      this.toasts.error(errorText(e));
    }
  }

  protected async remove(x: Receipt): Promise<void> {
    if (!confirm(`Delete this receipt${x.merchant ? ' from ' + x.merchant : ''}?`)) return;
    try { await this.api.delete(`/personal/receipts/${x.id}`); await this.router.navigateByUrl('/p/receipts'); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}
