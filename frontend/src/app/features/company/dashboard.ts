import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, errorText } from '../../core/api/api.service';
import { Dashboard, MONTHS } from '../../core/api/models';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe, percent } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'k-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon, RmPipe, DayPipe],
  template: `
@if (data(); as d) {
  <div class="page-head">
    <div><h1>{{ d.companyName }}</h1><p>Financial year {{ d.fyStart | day }} to {{ d.fyEnd | day }}</p></div>
    <div class="acts">
      <a class="btn" [routerLink]="ws.base() + '/reports'"><k-icon name="chart" [size]="16" />Reports</a>
      @if (ws.canEdit()) { <a class="btn primary" [routerLink]="ws.base() + '/banking'"><k-icon name="up" [size]="16" />Upload statement</a> }
    </div>
  </div>

  <div class="grid g4 mb16">
    <div class="card pad kpi"><div class="k">Revenue</div><div class="v">{{ d.revenue | rm:'0' }}</div><div class="s">Year to date</div></div>
    <div class="card pad kpi"><div class="k">Expenses</div><div class="v">{{ d.expenses | rm:'0' }}</div><div class="s">Year to date</div></div>
    <div class="card pad kpi"><div class="k">{{ d.netProfit >= 0 ? 'Net profit' : 'Net loss' }}</div>
      <div class="v" [class.pos]="d.netProfit >= 0" [class.neg]="d.netProfit < 0">{{ d.netProfit | rm:'0' }}</div><div class="s">{{ margin() }}</div></div>
    <div class="card pad kpi"><div class="k">Cash in bank</div><div class="v">{{ d.cash | rm:'0' }}</div><div class="s">Receivables {{ d.receivables | rm:'0' }}</div></div>
  </div>

  <div class="grid g21 mb16">
    <section class="card pad">
      <div class="ch"><h2>Revenue and expenses</h2>
        <div class="r caption">
          <span class="row" style="gap: 6px"><i class="legend-swatch" style="background: var(--accent)"></i>Revenue</span>
          <span class="row" style="gap: 6px"><i class="legend-swatch" style="background: var(--caption)"></i>Expenses</span>
        </div>
      </div>
      <div class="chart" role="img" aria-label="Monthly revenue and expenses">
        @for (m of bars(); track m.label) {
          <div class="m">
            <div class="bs">
              <i [style.height.px]="m.income" style="background: var(--accent)" [title]="m.label + ' revenue'"></i>
              <i [style.height.px]="m.expenses" style="background: var(--caption)" [title]="m.label + ' expenses'"></i>
            </div>
            <span class="caption">{{ m.label }}</span>
          </div>
        }
      </div>
    </section>
    <section class="card pad">
      <div class="ch"><h2>Bank statements</h2>@if (d.pendingLines) { <span class="r pill wa">{{ d.pendingLines }} to review</span> }</div>
      @for (b of d.banks; track b.id) {
        <div class="row" style="padding: 10px 0; border-top: 1px solid var(--line)">
          <span class="wsav company"><k-icon name="bank" [size]="16" /></span>
          <div class="grow">
            <div class="title">{{ b.name }}@if (b.last4) { <span class="caption"> ••{{ b.last4 }}</span> }</div>
            <div class="caption">{{ b.lastLineDate ? 'Statement up to ' + (b.lastLineDate | day) : 'No statement uploaded yet' }}</div>
          </div>
          <b class="nowrap">{{ b.balance | rm }}</b>
        </div>
      }
      @if (d.pendingLines && ws.canEdit()) { <a class="btn primary" style="margin-top: 12px; width: 100%" [routerLink]="ws.base() + '/banking'">Review {{ d.pendingLines }} lines</a> }
    </section>
  </div>

  <div class="grid g3">
    <section class="card">
      <div class="card-h"><h2>Needs your attention</h2></div>
      @for (a of d.attention; track a.title) {
        <a class="list-row click" [routerLink]="ws.base() + '/' + a.link">
          <span class="dot" [class.wa]="a.kind !== 'foreign'" [class.in]="a.kind === 'foreign'"></span>
          <div class="grow"><div class="title">{{ a.title }}</div><div class="caption">{{ a.detail }}</div></div><k-icon name="arrow" [size]="16" />
        </a>
      } @empty { <div class="empty"><b>All clear</b>Nothing needs you right now.</div> }
    </section>
    <section class="card">
      <div class="card-h"><h2>Coming up</h2><a class="r caption" [routerLink]="ws.base() + '/tax'">All deadlines</a></div>
      @for (x of d.deadlines; track x.name + x.due) {
        <div class="list-row">
          <span class="dot" [class]="'dot ' + urgency(x.daysLeft)"></span>
          <div class="grow"><div class="title">{{ x.name }}</div><div class="caption">Due {{ x.due | day }}</div></div>
          <span class="pill" [class]="'pill ' + urgencyPill(x.daysLeft)">{{ x.daysLeft === 0 ? 'Today' : x.daysLeft + ' days' }}</span>
        </div>
      } @empty { <div class="empty">No upcoming deadlines.</div> }
    </section>
    <section class="card pad">
      <div class="ch"><h2>e-Invoice</h2><span class="r pill" [class.ok]="!d.eInvoice.required" [class.wa]="d.eInvoice.required">{{ d.eInvoice.required ? 'Required' : 'Not required' }}</span></div>
      <p class="muted" style="margin: 0 0 10px; font-size: 13.5px">Revenue over the last 12 months compared with the exemption line.</p>
      <span class="progress"><i [style.width.%]="eInvoicePct()"></i></span>
      <div class="row caption" style="justify-content: space-between; margin-top: 6px"><span>{{ d.eInvoice.revenue12m | rm:'0' }}</span><span>{{ d.eInvoice.threshold | rm:'0' }}</span></div>
      <p class="caption" style="margin: 10px 0 0">{{ d.eInvoice.note }}</p>
    </section>
  </div>
} @else {
  <div class="stack">
    <div class="skel" style="width: 40%; height: 28px"></div>
    <div class="grid g4">@for (i of [1, 2, 3, 4]; track i) { <div class="card pad"><div class="skel"></div><div class="skel" style="margin-top: 10px; height: 24px"></div></div> }</div>
  </div>
}`
})
export class DashboardPage {
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly data = signal<Dashboard | null>(null);

  protected readonly bars = computed(() => {
    const months = this.data()?.months ?? [];
    const max = Math.max(1, ...months.map(m => Math.max(m.income, m.expenses)));
    return months.map(m => ({
      label: MONTHS[Number(m.month.slice(5, 7)) - 1],
      income: Math.round(Math.max(0, m.income) / max * 170),
      expenses: Math.round(Math.max(0, m.expenses) / max * 170)
    }));
  });
  protected readonly margin = computed(() => {
    const d = this.data();
    return d && d.revenue > 0 ? `${(d.netProfit / d.revenue * 100).toFixed(1)}% margin` : '';
  });
  protected readonly eInvoicePct = computed(() => {
    const e = this.data()?.eInvoice;
    return e ? percent(e.revenue12m, e.threshold) : 0;
  });

  constructor() { void this.load(); }

  protected urgency(days: number): string { return days <= 7 ? 'no' : days <= 30 ? 'wa' : 'ok'; }
  protected urgencyPill(days: number): string { return days <= 7 ? 'no' : days <= 30 ? 'wa' : 'gr'; }

  private async load(): Promise<void> {
    try { this.data.set(await this.api.get<Dashboard>(`${this.ws.api()}/reports/dashboard`)); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}
