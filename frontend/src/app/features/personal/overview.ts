import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, errorText } from '../../core/api/api.service';
import { KIND_LABEL, PersonalOverview } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe, monthName } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'k-overview',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon, RmPipe, DayPipe],
  template: `
@if (data(); as d) {
  <div class="page-head">
    <div><h1>Hi {{ firstName() }}</h1><p>Your money for {{ label(d.month) }}.</p></div>
    <div class="acts">
      @if (d.months.length > 1) {
        <select class="input" aria-label="Month" [value]="d.month" (change)="load($any($event.target).value)">
          @for (m of d.months; track m) { <option [value]="m">{{ label(m) }}</option> }
        </select>
      }
      <a class="btn primary" routerLink="/p/receipts"><k-icon name="camera" [size]="16" />Add receipt</a>
    </div>
  </div>

  <div class="grid g3 mb16">
    <div class="card pad kpi"><div class="k">Money in</div><div class="v pos">{{ d.moneyIn | rm:'0' }}</div><div class="s">Transfers between your accounts excluded</div></div>
    <div class="card pad kpi"><div class="k">Spending</div><div class="v">{{ d.spending | rm:'0' }}</div><div class="s">{{ d.byCategory.length }} categories</div></div>
    <div class="card pad kpi"><div class="k">Tax reliefs claimed</div><div class="v">{{ reliefTotal() | rm:'0' }}</div><div class="s">{{ d.filingNote }}</div></div>
  </div>

  <div class="grid g21 mb16">
    <section class="card pad">
      <div class="ch"><h2>Where the money went</h2><a class="r caption" routerLink="/p/reports">Full year</a></div>
      <div class="bars">
        @for (c of d.byCategory; track c.category) {
          <div class="b"><span>{{ c.category }}</span><span class="progress"><i [style.width.%]="share(c.amount)"></i></span><span class="n">{{ c.amount | rm }}</span></div>
        } @empty { <div class="empty">No spending recorded this month.</div> }
      </div>
    </section>
    <section class="card">
      <div class="card-h"><h2>Needs your attention</h2></div>
      @for (a of d.attention; track a.kind) {
        <a class="list-row click" [routerLink]="'/p/' + a.link"><k-icon name="alert" [size]="18" /><div class="grow"><div class="title">{{ a.title }}</div><div class="caption">{{ a.detail }}</div></div><k-icon name="chev" [size]="16" /></a>
      } @empty { <div class="empty">All caught up.</div> }
    </section>
  </div>

  <div class="grid g2">
    <section class="card">
      <div class="card-h"><h2>Accounts</h2><a class="r caption" routerLink="/p/accounts">Manage</a></div>
      @for (a of d.accounts; track a.id) {
        <div class="list-row"><k-icon [name]="a.kind === 'Cash' ? 'cash' : a.kind === 'EWallet' ? 'wallet' : a.kind === 'Bank' ? 'bank' : 'card'" [size]="18" />
          <div class="grow"><div>{{ a.name }}</div><div class="caption">{{ kinds[a.kind] }}{{ a.last4 ? ' · ••' + a.last4 : '' }}</div></div><b>{{ a.balance | rm }}</b></div>
      }
    </section>
    <section class="card">
      <div class="card-h"><h2>Recent receipts</h2><a class="r caption" routerLink="/p/receipts">All receipts</a></div>
      @for (r of d.recentReceipts; track r.id) {
        <a class="list-row click" [routerLink]="'/p/receipts/' + r.id"><k-icon name="receipt" [size]="18" />
          <div class="grow"><div>{{ r.merchant || 'Unnamed receipt' }}</div><div class="caption">{{ r.date | day }} · {{ r.category }}{{ r.paymentAccountName ? ' · ' + r.paymentAccountName : r.paymentStatus === 'Cash' ? ' · Cash' : '' }}</div></div>
          @if (r.status === 'Review') { <span class="pill wa">Check</span> }<b>{{ r.total | rm }}</b></a>
      } @empty { <div class="empty">No receipts yet.</div> }
    </section>
  </div>
} @else { <div class="card pad skel" style="height: 300px"></div> }`
})
export class OverviewPage {
  private readonly api = inject(ApiService);
  private readonly auth = inject(AuthService);
  private readonly toasts = inject(ToastService);
  protected readonly data = signal<PersonalOverview | null>(null);
  protected readonly kinds = KIND_LABEL;
  protected readonly firstName = computed(() => (this.auth.me()?.name ?? '').split(' ')[0]);
  protected readonly reliefTotal = computed(() => (this.data()?.reliefs ?? []).reduce((s, r) => s + r.claimed, 0));
  private readonly maxCategory = computed(() => Math.max(1, ...(this.data()?.byCategory ?? []).map(c => c.amount)));

  constructor() { void this.load(); }

  protected async load(month?: string): Promise<void> {
    try { this.data.set(await this.api.get<PersonalOverview>('/personal/overview', { month })); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected label(m: string): string { return monthName(m); }
  protected share(amount: number): number { return Math.round(amount / this.maxCategory() * 100); }
}
