import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService, errorText } from '../../core/api/api.service';
import { Account, AccountType, Company, MONTHS, TaxTreatment } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { Icon } from '../../core/ui/icon';
import { RmPipe } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

interface AccountDraft { code: string; name: string; type: AccountType; tax: TaxTreatment; hint: string; }

@Component({
  selector: 'k-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, RmPipe],
  template: `
<div class="page-head"><div><h1>Settings</h1><p>Company details, bank accounts and the chart of accounts.</p></div></div>

@if (company(); as c) {
  <form class="card pad mb16" (ngSubmit)="save(c)">
    <div class="ch"><h2>Company</h2></div>
    <div class="form-grid">
      <label class="field">Company name<input class="input" name="name" required [(ngModel)]="c.name"></label>
      <label class="field">SSM registration no.<input class="input" name="reg" [(ngModel)]="c.registrationNo" placeholder="202301012345 (1234567-A)"></label>
      <label class="field">Incorporated on<input class="input" type="date" name="inc" [(ngModel)]="c.incorporatedOn"></label>
      <label class="field">Financial year starts
        <select class="input" name="fy" [(ngModel)]="c.fyStartMonth">@for (m of months; track $index) { <option [ngValue]="$index + 1">{{ m }}</option> }</select></label>
      <label class="field">Industry<input class="input" name="industry" [(ngModel)]="c.industry"></label>
      <label class="field">Employees<input class="input" type="number" min="0" name="emp" [(ngModel)]="c.employeeCount"></label>
      <label class="field full">Address<input class="input" name="address" [(ngModel)]="c.address"></label>
      <label class="field">CP204 estimate for this year (RM)<input class="input" type="number" min="0" step="0.01" name="cp204" [(ngModel)]="c.cp204Estimate"></label>
      <label class="check full"><input type="checkbox" name="sme" [(ngModel)]="c.isSme">
        <span>SME for tax: paid-up capital RM2.5m or less, gross income RM50m or less, and not controlled by a large company. Gets the 15% and 17% tiers.</span></label>
    </div>
    <div class="row" style="margin-top: 14px"><span class="grow"></span><button type="submit" class="btn primary">Save</button></div>
  </form>
}

<div class="grid g2">
  <section class="card">
    <div class="card-h"><h2>Bank accounts</h2></div>
    @for (a of banks(); track a.id) {
      <div class="list-row"><k-icon name="bank" [size]="18" /><div class="grow"><div>{{ a.name }}</div><div class="caption">{{ a.code }}{{ a.last4 ? ' · ••' + a.last4 : '' }}</div></div><span>{{ a.balance | rm }}</span></div>
    }
    <form class="list-row" (ngSubmit)="addBank()">
      <input class="input grow" name="bankName" placeholder="e.g. Maybank current account" aria-label="Bank account name" [(ngModel)]="bankName">
      <input class="input" style="max-width: 90px" name="bankLast4" placeholder="Last 4" maxlength="4" aria-label="Last 4 digits" [(ngModel)]="bankLast4">
      <button type="submit" class="btn sm"><k-icon name="plus" [size]="14" />Add</button>
    </form>
  </section>

  <section class="card">
    <div class="card-h"><h2>Chart of accounts</h2><button type="button" class="btn sm" (click)="openAccount()"><k-icon name="plus" [size]="14" />Account</button></div>
    <div class="tw" style="max-height: 460px; overflow: auto"><table class="t tight">
      <thead><tr><th>Code</th><th>Name</th><th>Type</th><th>Tax</th></tr></thead>
      <tbody>
        @for (a of accounts(); track a.id) {
          <tr><td class="caption">{{ a.code }}</td><td>{{ a.name }}<div class="caption">{{ a.hint }}</div></td><td>{{ a.type }}</td><td><span class="pill gr">{{ taxLabel(a.tax) }}</span></td></tr>
        }
      </tbody>
    </table></div>
  </section>
</div>

@if (draft(); as d) {
  <div class="modal-wrap" (click)="draft.set(null)">
    <form class="modal" (click)="$event.stopPropagation()" (ngSubmit)="addAccount(d)">
      <div class="mh"><h2>New account</h2><p>The AI uses the hint to decide which bank lines belong here.</p></div>
      <div class="mb form-grid">
        <label class="field">Code<input class="input" name="code" required [(ngModel)]="d.code" placeholder="e.g. 6150"></label>
        <label class="field">Name<input class="input" name="name" required [(ngModel)]="d.name"></label>
        <label class="field">Type
          <select class="input" name="type" [(ngModel)]="d.type">@for (t of types; track t) { <option [value]="t">{{ t }}</option> }</select></label>
        <label class="field">Tax treatment
          <select class="input" name="tax" [(ngModel)]="d.tax">@for (t of taxes; track t) { <option [value]="t">{{ taxLabel(t) }}</option> }</select></label>
        <label class="field full">Hint for the AI<input class="input" name="hint" [(ngModel)]="d.hint" placeholder="e.g. Google Workspace, Microsoft 365, Canva"></label>
      </div>
      <div class="mf"><button type="button" class="btn" (click)="draft.set(null)">Cancel</button><button type="submit" class="btn primary">Add</button></div>
    </form>
  </div>
}`
})
export class SettingsPage {
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toasts = inject(ToastService);

  protected readonly months = MONTHS;
  protected readonly types: AccountType[] = ['Asset', 'Liability', 'Equity', 'Income', 'Expense'];
  protected readonly taxes: TaxTreatment[] = ['Standard', 'HalfDeductible', 'NonDeductible', 'Capital'];
  protected readonly company = signal<Company | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly banks = signal<Account[]>([]);
  protected readonly draft = signal<AccountDraft | null>(null);
  protected bankName = '';
  protected bankLast4 = '';

  constructor() { void this.load(); }

  protected taxLabel(t: TaxTreatment): string {
    return { Standard: 'Deductible', Bank: 'Bank', Capital: 'Capital allowance', HalfDeductible: '50% deductible', NonDeductible: 'Not deductible' }[t];
  }

  private async load(): Promise<void> {
    try {
      const [c, a] = await Promise.all([this.api.get<Company>(this.ws.api()), this.api.get<Account[]>(`${this.ws.api()}/accounts`)]);
      this.company.set({ ...c });
      this.accounts.set(a);
      this.banks.set(a.filter(x => x.tax === 'Bank'));
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async save(c: Company): Promise<void> {
    try {
      const saved = await this.api.put<Company>(this.ws.api(), {
        name: c.name, registrationNo: c.registrationNo, incorporatedOn: c.incorporatedOn || null, industry: c.industry, address: c.address,
        fyStartMonth: Number(c.fyStartMonth), isSme: c.isSme, cp204Estimate: Number(c.cp204Estimate) || 0, employeeCount: Number(c.employeeCount) || 0
      });
      this.company.set({ ...saved });
      await this.auth.reloadMe();
      this.toasts.show('Saved.');
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async addBank(): Promise<void> {
    if (!this.bankName.trim()) return;
    try {
      await this.api.post(`${this.ws.api()}/bank-accounts`, { name: this.bankName.trim(), last4: this.bankLast4.trim() || null });
      this.bankName = this.bankLast4 = '';
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected openAccount(): void { this.draft.set({ code: '', name: '', type: 'Expense', tax: 'Standard', hint: '' }); }

  protected async addAccount(d: AccountDraft): Promise<void> {
    try { await this.api.post(`${this.ws.api()}/accounts`, d); this.draft.set(null); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}
