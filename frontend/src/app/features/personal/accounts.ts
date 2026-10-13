import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorText } from '../../core/api/api.service';
import { KIND_LABEL, PaymentAccount, PaymentKind } from '../../core/api/models';
import { Icon } from '../../core/ui/icon';
import { RmPipe } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

interface Draft { id: string | null; name: string; kind: PaymentKind; institution: string; last4: string; openingBalance: number; }

@Component({
  selector: 'k-accounts',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, RmPipe],
  template: `
<div class="page-head">
  <div><h1>Accounts</h1><p>Optional. Add the bank accounts, cards and e-wallets you pay from, so Kira can tell which one paid each receipt from your payment screenshot.</p></div>
  <div class="acts"><button type="button" class="btn primary" (click)="open()"><k-icon name="plus" [size]="16" />Add account</button></div>
</div>
<section class="card">
  @for (a of list(); track a.id) {
    <div class="list-row">
      <k-icon [name]="icon(a.kind)" [size]="20" />
      <div class="grow"><div class="title">{{ a.name }}</div><div class="caption">{{ kinds[a.kind] }}{{ a.institution ? ' · ' + a.institution : '' }}{{ a.last4 ? ' · ••' + a.last4 : '' }}</div></div>
      <b>{{ a.balance | rm }}</b>
      <button type="button" class="btn sm ghost" (click)="open(a)">Edit</button>
      @if (a.kind !== 'Cash') { <button type="button" class="iconbtn" aria-label="Delete account" (click)="remove(a)"><k-icon name="trash" [size]="16" /></button> }
    </div>
  }
</section>
<p class="caption" style="margin-top: 12px">Kira matches a payment screenshot to an account by the last 4 digits first, then by the bank or wallet name. You can always change it by hand.</p>

@if (draft(); as d) {
  <div class="modal-wrap" (click)="draft.set(null)">
    <form class="modal" (click)="$event.stopPropagation()" (ngSubmit)="save(d)">
      <div class="mh"><h2>{{ d.id ? 'Edit account' : 'Add account' }}</h2></div>
      <div class="mb form-grid">
        <label class="field full">Type
          <div class="seg">@for (k of kindList; track k) { <button type="button" [class.on]="d.kind === k" (click)="d.kind = k">{{ kinds[k] }}</button> }</div></label>
        <label class="field">Name<input class="input" name="name" required [(ngModel)]="d.name" placeholder="e.g. Maybank savings"></label>
        <label class="field">Bank or provider<input class="input" name="inst" [(ngModel)]="d.institution" placeholder="e.g. Maybank, Touch 'n Go"></label>
        <label class="field">Last 4 digits<input class="input" name="last4" maxlength="4" inputmode="numeric" [(ngModel)]="d.last4"></label>
        <label class="field">Opening balance (RM)<input class="input" type="number" step="0.01" name="opening" [(ngModel)]="d.openingBalance"></label>
      </div>
      <div class="mf"><button type="button" class="btn" (click)="draft.set(null)">Cancel</button><button type="submit" class="btn primary">Save</button></div>
    </form>
  </div>
}`
})
export class AccountsPage {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly list = signal<PaymentAccount[]>([]);
  protected readonly draft = signal<Draft | null>(null);
  protected readonly kinds = KIND_LABEL;
  protected readonly kindList: PaymentKind[] = ['Bank', 'CreditCard', 'DebitCard', 'EWallet', 'Cash'];

  constructor() { void this.load(); }

  protected icon(k: PaymentKind): string { return k === 'Cash' ? 'cash' : k === 'EWallet' ? 'wallet' : k === 'Bank' ? 'bank' : 'card'; }

  private async load(): Promise<void> {
    try { this.list.set(await this.api.get<PaymentAccount[]>('/personal/accounts')); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected open(a?: PaymentAccount): void {
    this.draft.set(a ? { id: a.id, name: a.name, kind: a.kind, institution: a.institution, last4: a.last4, openingBalance: a.openingBalance }
      : { id: null, name: '', kind: 'Bank', institution: '', last4: '', openingBalance: 0 });
  }

  protected async save(d: Draft): Promise<void> {
    const body = { name: d.name, kind: d.kind, institution: d.institution, last4: d.last4, openingBalance: Number(d.openingBalance) || 0 };
    try {
      if (d.id) await this.api.put(`/personal/accounts/${d.id}`, body); else await this.api.post('/personal/accounts', body);
      this.draft.set(null);
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async remove(a: PaymentAccount): Promise<void> {
    if (!confirm(`Delete ${a.name}? Receipts paid from it will show as "account not set".`)) return;
    try { await this.api.delete(`/personal/accounts/${a.id}`); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}
