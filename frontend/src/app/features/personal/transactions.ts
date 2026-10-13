import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, errorText, todayIso } from '../../core/api/api.service';
import { Direction, PaymentAccount, PERSONAL_CATEGORIES, PImportResult, PTx, SetAccountResult } from '../../core/api/models';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe, takeFile } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

interface Draft { accountId: string; date: string; description: string; direction: Direction; amount: number | null; category: string; }

@Component({
  selector: 'k-personal-transactions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, RmPipe, DayPipe],
  template: `
<div class="page-head">
  <div><h1>Transactions</h1><p>From your statements, receipts and what you add by hand. Transfers between your own accounts aren't counted as spending.</p></div>
  <div class="acts">
    <button type="button" class="btn" (click)="open()"><k-icon name="plus" [size]="16" />Add</button>
    <select class="input" style="max-width: 200px" aria-label="Statement account" [(ngModel)]="importAccount">
      <option value="">Statement for…</option>
      @for (a of accounts(); track a.id) { @if (a.kind !== 'Cash') { <option [value]="a.id">{{ a.name }}</option> } }
    </select>
    <label class="btn primary filebtn" [class.disabled]="busy() || !importAccount"><k-icon name="up" [size]="16" />{{ busy() ? 'Reading…' : 'Upload statement' }}
      <input type="file" accept=".pdf,.csv,.xlsx,image/*" [disabled]="busy() || !importAccount" (change)="upload($event)" aria-label="Upload statement"></label>
  </div>
</div>

<form class="row wrap mb16" (ngSubmit)="load()">
  <input class="input" style="max-width: 260px" name="q" placeholder="Search" aria-label="Search" [(ngModel)]="q">
  <select class="input" style="max-width: 200px" name="account" aria-label="Account" [(ngModel)]="accountId" (ngModelChange)="load()">
    <option value="">All accounts</option>
    @for (a of accounts(); track a.id) { <option [value]="a.id">{{ a.name }}</option> }
  </select>
  <input class="input" style="max-width: 170px" type="month" name="month" aria-label="Month" [(ngModel)]="month" (ngModelChange)="load()">
  <label class="check"><input type="checkbox" name="review" [(ngModel)]="review" (ngModelChange)="load()">Only ones to confirm</label>
</form>

<section class="card"><div class="tw"><table class="t">
  <thead><tr><th>Date</th><th>Description</th><th>Account</th><th>Category</th><th class="n">Amount</th><th></th></tr></thead>
  <tbody>
    @for (t of list(); track t.id) {
      <tr>
        <td class="nowrap">{{ t.date | day }}</td>
        <td class="desc">{{ t.description }}
          @if (t.receiptId) { <a class="pill ok" [routerLink]="['/p/receipts', t.receiptId]"><k-icon name="receipt" [size]="12" />Receipt</a> }
          @if (t.statementMatched) { <span class="pill gr">On statement</span> }
          @if (!t.reviewed) { <div class="caption">{{ t.reason }}</div> }</td>
        <td class="caption">{{ t.accountName || '—' }}</td>
        <td>
          <select class="input" style="min-width: 140px" [attr.aria-label]="'Category for ' + t.description" [ngModel]="t.category" (ngModelChange)="setCategory(t, $event)">
            @for (c of categories; track c) { <option [value]="c">{{ c }}</option> }
          </select>
        </td>
        <td class="n" [class.pos]="t.direction === 'In'">{{ (t.direction === 'In' ? t.amount : -t.amount) | rm:'sign' }}</td>
        <td class="right nowrap">
          @if (!t.reviewed) { <button type="button" class="btn sm" (click)="confirmTx(t)"><k-icon name="check" [size]="14" />OK</button> }
          @if (t.source === 'Manual') { <button type="button" class="iconbtn" aria-label="Delete" (click)="remove(t)"><k-icon name="trash" [size]="16" /></button> }
        </td>
      </tr>
    } @empty { <tr><td colspan="6"><div class="empty">No transactions.</div></td></tr> }
  </tbody>
</table></div></section>

@if (draft(); as d) {
  <div class="modal-wrap" (click)="draft.set(null)">
    <form class="modal" (click)="$event.stopPropagation()" (ngSubmit)="add(d)">
      <div class="mh"><h2>Add a transaction</h2></div>
      <div class="mb form-grid">
        <label class="field full"><div class="seg">
          <button type="button" [class.on]="d.direction === 'Out'" (click)="d.direction = 'Out'">Money out</button>
          <button type="button" [class.on]="d.direction === 'In'" (click)="d.direction = 'In'; d.category = 'Income'">Money in</button></div></label>
        <label class="field">Date<input class="input" type="date" name="date" [(ngModel)]="d.date"></label>
        <label class="field">Amount (RM)<input class="input" type="number" step="0.01" min="0" name="amount" required [(ngModel)]="d.amount"></label>
        <label class="field full">Description<input class="input" name="desc" [(ngModel)]="d.description"></label>
        <label class="field">Account
          <select class="input" name="acc" [(ngModel)]="d.accountId"><option value="">—</option>@for (a of accounts(); track a.id) { <option [value]="a.id">{{ a.name }}</option> }</select></label>
        <label class="field">Category
          <select class="input" name="cat" [(ngModel)]="d.category">@for (c of categories; track c) { <option [value]="c">{{ c }}</option> }</select></label>
      </div>
      <div class="mf"><button type="button" class="btn" (click)="draft.set(null)">Cancel</button><button type="submit" class="btn primary">Add</button></div>
    </form>
  </div>
}`
})
export class PersonalTransactionsPage {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly list = signal<PTx[]>([]);
  protected readonly accounts = signal<PaymentAccount[]>([]);
  protected readonly draft = signal<Draft | null>(null);
  protected readonly busy = signal(false);
  protected readonly categories = PERSONAL_CATEGORIES;
  protected q = '';
  protected accountId = '';
  protected month = '';
  protected review = false;
  protected importAccount = '';

  constructor() {
    void this.load();
    this.api.get<PaymentAccount[]>('/personal/accounts').then(a => this.accounts.set(a)).catch(() => undefined);
  }

  protected async load(): Promise<void> {
    try { this.list.set(await this.api.get<PTx[]>('/personal/transactions', { q: this.q, accountId: this.accountId, month: this.month, review: this.review || null })); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async setCategory(t: PTx, category: string): Promise<void> {
    try {
      const r = await this.api.put<SetAccountResult>(`/personal/transactions/${t.id}`, { category, remember: true });
      if (r.similar > 0 && confirm(`${r.similar} other transaction(s) look like "${r.pattern}". Change them to ${category} too?`)) {
        const u = await this.api.post<{ updated: number }>(`/personal/transactions/${t.id}/apply-similar`);
        this.toasts.show(`${u.updated} updated.`);
      }
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async confirmTx(t: PTx): Promise<void> {
    try { await this.api.post(`/personal/transactions/${t.id}/confirm`); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async remove(t: PTx): Promise<void> {
    if (!confirm('Delete this transaction?')) return;
    try { await this.api.delete(`/personal/transactions/${t.id}`); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async upload(event: Event): Promise<void> {
    const file = takeFile(event);
    if (!file || !this.importAccount) return;
    const form = new FormData();
    form.append('accountId', this.importAccount);
    form.append('file', file);
    this.busy.set(true);
    try {
      const r = await this.api.upload<PImportResult>('/personal/transactions/import', form);
      this.toasts.show(r.message);
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
    finally { this.busy.set(false); }
  }

  protected open(): void { this.draft.set({ accountId: '', date: todayIso(), description: '', direction: 'Out', amount: null, category: 'Other' }); }

  protected async add(d: Draft): Promise<void> {
    if (!d.amount || d.amount <= 0) { this.toasts.error('Enter the amount.'); return; }
    try {
      await this.api.post('/personal/transactions', { ...d, accountId: d.accountId || null, amount: Number(d.amount) });
      this.draft.set(null);
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }
}
