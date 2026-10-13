import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService, errorText, isoDate, todayIso } from '../../core/api/api.service';
import { Account, BalanceSheet, Company, GeneralLedger, Pnl, TrialBalance } from '../../core/api/models';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

type Kind = 'pl' | 'bs' | 'tb' | 'gl';

@Component({
  selector: 'k-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, RmPipe, DayPipe],
  template: `
<div class="page-head">
  <div><h1>Reports</h1><p>Built from every confirmed bank line and journal entry.</p></div>
  <div class="acts"><button type="button" class="btn" (click)="exportExcel()"><k-icon name="dl" [size]="16" />Export Excel</button></div>
</div>
<div class="tabs" role="tablist">
  @for (t of tabs; track t.kind) {
    <button type="button" role="tab" [class.on]="kind() === t.kind" [attr.aria-selected]="kind() === t.kind" (click)="kind.set(t.kind); load()">{{ t.label }}</button>
  }
</div>
<div class="row wrap mb16">
  @if (kind() === 'pl' || kind() === 'gl') { <label class="field inline">From<input class="input" type="date" [ngModel]="from()" (ngModelChange)="from.set($event); load()"></label> }
  <label class="field inline">{{ kind() === 'bs' || kind() === 'tb' ? 'As at' : 'To' }}<input class="input" type="date" [ngModel]="to()" (ngModelChange)="to.set($event); load()"></label>
  <button type="button" class="btn sm ghost" (click)="preset('fy')">This financial year</button>
  <button type="button" class="btn sm ghost" (click)="preset('lastMonth')">Last month</button>
  <button type="button" class="btn sm ghost" (click)="preset('lastFy')">Last financial year</button>
  @if (kind() === 'gl') {
    <select class="input" style="max-width: 300px" aria-label="Account" [ngModel]="accountId()" (ngModelChange)="accountId.set($event); load()">
      @for (a of accounts(); track a.id) { <option [value]="a.id">{{ a.code }} {{ a.name }}</option> }
    </select>
  }
</div>

<section class="card"><div class="tw">
  @switch (kind()) {
    @case ('pl') {
      @if (pl(); as p) {
        <table class="t"><tbody>
          <tr class="tot"><td>Income</td><td></td></tr>
          @for (r of p.income; track r.id) { <tr><td>{{ r.code }} {{ r.name }}</td><td class="n">{{ r.amount | rm }}</td></tr> }
          <tr><td><b>Total income</b></td><td class="n"><b>{{ p.totalIncome | rm }}</b></td></tr>
          <tr class="tot"><td>Expenses</td><td></td></tr>
          @for (r of p.expenses; track r.id) {
            <tr><td>{{ r.code }} {{ r.name }}
              @if (r.tax === 'HalfDeductible') { <span class="pill wa">50% deductible</span> }
              @if (r.tax === 'NonDeductible') { <span class="pill no">Not deductible</span> }</td><td class="n">{{ r.amount | rm }}</td></tr>
          }
          <tr><td><b>Total expenses</b></td><td class="n"><b>{{ p.totalExpenses | rm }}</b></td></tr>
          <tr class="tot"><td>{{ p.netProfit >= 0 ? 'Net profit' : 'Net loss' }}</td><td class="n" [class.pos]="p.netProfit >= 0" [class.neg]="p.netProfit < 0">{{ p.netProfit | rm }}</td></tr>
        </tbody></table>
      }
    }
    @case ('bs') {
      @if (bs(); as b) {
        <table class="t"><tbody>
          <tr class="tot"><td>Assets</td><td></td></tr>
          @for (r of b.assets; track $index) { <tr><td>{{ r.code }} {{ r.name }}</td><td class="n">{{ r.amount | rm }}</td></tr> }
          <tr><td><b>Total assets</b></td><td class="n"><b>{{ b.totalAssets | rm }}</b></td></tr>
          <tr class="tot"><td>Liabilities</td><td></td></tr>
          @for (r of b.liabilities; track $index) { <tr><td>{{ r.code }} {{ r.name }}</td><td class="n">{{ r.amount | rm }}</td></tr> }
          <tr><td><b>Total liabilities</b></td><td class="n"><b>{{ b.totalLiabilities | rm }}</b></td></tr>
          <tr class="tot"><td>Equity</td><td></td></tr>
          @for (r of b.equity; track $index) { <tr><td>{{ r.code }} {{ r.name }}</td><td class="n">{{ r.amount | rm }}</td></tr> }
          <tr><td><b>Total equity</b></td><td class="n"><b>{{ b.totalEquity | rm }}</b></td></tr>
          <tr class="tot"><td>Liabilities and equity</td><td class="n">{{ b.totalLiabilities + b.totalEquity | rm }}</td></tr>
        </tbody></table>
        <div class="caption" style="padding: 10px 14px">{{ b.balanced ? '✓ Balanced.' : 'Out of balance. Check recent manual entries.' }} As at {{ b.asAt | day }}.</div>
      }
    }
    @case ('tb') {
      @if (tb(); as t) {
        <table class="t">
          <thead><tr><th>Code</th><th>Account</th><th class="n">Debit</th><th class="n">Credit</th></tr></thead>
          <tbody>
            @for (r of t.rows; track r.code) { <tr><td class="caption">{{ r.code }}</td><td>{{ r.name }}</td><td class="n">{{ r.debit ? (r.debit | rm) : '' }}</td><td class="n">{{ r.credit ? (r.credit | rm) : '' }}</td></tr> }
            <tr class="tot"><td></td><td>Total</td><td class="n">{{ t.totalDebit | rm }}</td><td class="n">{{ t.totalCredit | rm }}</td></tr>
          </tbody>
        </table>
      }
    }
    @case ('gl') {
      @if (gl(); as g) {
        <table class="t">
          <thead><tr><th>Date</th><th>#</th><th>Description</th><th class="n">Debit</th><th class="n">Credit</th><th class="n">Balance</th></tr></thead>
          <tbody>
            <tr class="sub"><td></td><td></td><td>Opening balance</td><td></td><td></td><td class="n">{{ g.opening | rm }}</td></tr>
            @for (r of g.rows; track r.entryId) {
              <tr><td class="nowrap">{{ r.date | day }}</td><td class="caption">{{ r.number }}</td><td class="desc">{{ r.memo }}</td>
                <td class="n">{{ r.debit ? (r.debit | rm) : '' }}</td><td class="n">{{ r.credit ? (r.credit | rm) : '' }}</td><td class="n">{{ r.balance | rm }}</td></tr>
            }
            <tr class="tot"><td></td><td></td><td>Closing balance</td><td></td><td></td><td class="n">{{ g.closing | rm }}</td></tr>
          </tbody>
        </table>
      }
    }
  }
</div></section>`
})
export class ReportsPage {
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly route = inject(ActivatedRoute);

  protected readonly tabs: { kind: Kind; label: string }[] = [
    { kind: 'pl', label: 'Profit and loss' }, { kind: 'bs', label: 'Balance sheet' }, { kind: 'tb', label: 'Trial balance' }, { kind: 'gl', label: 'General ledger' }
  ];
  protected readonly kind = signal<Kind>('pl');
  protected readonly pl = signal<Pnl | null>(null);
  protected readonly bs = signal<BalanceSheet | null>(null);
  protected readonly tb = signal<TrialBalance | null>(null);
  protected readonly gl = signal<GeneralLedger | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly from = signal('');
  protected readonly to = signal(todayIso());
  protected readonly accountId = signal('');
  private fyStartMonth = 1;

  constructor() { void this.init(); }

  private async init(): Promise<void> {
    try {
      const [company, accounts] = await Promise.all([this.api.get<Company>(this.ws.api()), this.api.get<Account[]>(`${this.ws.api()}/accounts`)]);
      this.fyStartMonth = company.fyStartMonth;
      this.accounts.set(accounts);
      const focus = this.route.snapshot.queryParamMap.get('focus');
      const focused = accounts.find(a => a.id === focus);
      this.accountId.set(focused?.id ?? accounts.find(a => a.tax === 'Bank')?.id ?? accounts[0]?.id ?? '');
      if (focused) this.kind.set('gl');
      this.preset('fy');
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  private fyStart(d: Date): Date {
    const m = this.fyStartMonth - 1;
    return new Date(d.getMonth() >= m ? d.getFullYear() : d.getFullYear() - 1, m, 1);
  }

  protected preset(p: 'fy' | 'lastMonth' | 'lastFy'): void {
    const now = new Date();
    if (p === 'fy') { this.from.set(isoDate(this.fyStart(now))); this.to.set(isoDate(now)); }
    if (p === 'lastMonth') { this.from.set(isoDate(new Date(now.getFullYear(), now.getMonth() - 1, 1))); this.to.set(isoDate(new Date(now.getFullYear(), now.getMonth(), 0))); }
    if (p === 'lastFy') {
      const s = this.fyStart(now);
      this.from.set(isoDate(new Date(s.getFullYear() - 1, s.getMonth(), 1)));
      this.to.set(isoDate(new Date(s.getFullYear(), s.getMonth(), 0)));
    }
    void this.load();
  }

  protected async load(): Promise<void> {
    const c = this.ws.api();
    try {
      switch (this.kind()) {
        case 'pl': this.pl.set(await this.api.get<Pnl>(`${c}/reports/pl`, { from: this.from(), to: this.to() })); break;
        case 'bs': this.bs.set(await this.api.get<BalanceSheet>(`${c}/reports/bs`, { to: this.to() })); break;
        case 'tb': this.tb.set(await this.api.get<TrialBalance>(`${c}/reports/tb`, { to: this.to() })); break;
        case 'gl': if (this.accountId()) this.gl.set(await this.api.get<GeneralLedger>(`${c}/reports/gl`, { accountId: this.accountId(), from: this.from(), to: this.to() })); break;
      }
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected exportExcel(): void {
    const params = { from: this.from(), to: this.to(), accountId: this.kind() === 'gl' ? this.accountId() : null };
    this.api.download(`${this.ws.api()}/reports/export/${this.kind()}`, params, 'report.xlsx').catch(e => this.toasts.error(errorText(e)));
  }
}
