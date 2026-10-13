import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ApiService, errorText } from '../../core/api/api.service';
import { SpendingReport } from '../../core/api/models';
import { Icon } from '../../core/ui/icon';
import { RmPipe, monthName } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'k-personal-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon, RmPipe],
  template: `
<div class="page-head">
  <div><h1>Reports</h1><p>Spending by category and month. Transfers between your own accounts are left out.</p></div>
  <div class="acts">
    <select class="input" aria-label="Year" [value]="year()" (change)="setYear(+$any($event.target).value)">
      @for (y of years; track y) { <option [value]="y">{{ y }}</option> }
    </select>
    <button type="button" class="btn" (click)="exportExcel()"><k-icon name="dl" [size]="16" />Export Excel</button>
  </div>
</div>

@if (data(); as d) {
  <section class="card pad mb16">
    <div class="ch"><h2>Monthly spending · {{ d.year }}</h2><span class="r caption">Total {{ grand() | rm:'0' }}</span></div>
    <div class="chart" role="img" aria-label="Spending per month">
      @for (m of monthTotals(); track m.label) {
        <div class="m"><div class="bs"><i [style.height.px]="m.height" style="background: var(--accent)" [title]="m.label + ': RM ' + m.value.toFixed(2)"></i></div><span class="caption">{{ m.label }}</span></div>
      }
    </div>
  </section>

  <section class="card"><div class="tw"><table class="t tight">
    <thead><tr><th>Category</th>@for (m of d.months; track m) { <th class="n">{{ short(m) }}</th> }<th class="n">Total</th></tr></thead>
    <tbody>
      @for (c of d.categories; track c; let i = $index) {
        <tr><td>{{ c }}</td>@for (v of d.values[i]; track $index) { <td class="n caption">{{ v ? (v | rm:'0') : '' }}</td> }<td class="n"><b>{{ rowTotal(i) | rm:'0' }}</b></td></tr>
      } @empty { <tr><td [attr.colspan]="d.months.length + 2"><div class="empty">No spending recorded for {{ d.year }}.</div></td></tr> }
    </tbody>
  </table></div></section>
} @else { <div class="card pad skel" style="height: 300px"></div> }`
})
export class PersonalReportsPage {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly data = signal<SpendingReport | null>(null);
  protected readonly year = signal(new Date().getFullYear());
  protected readonly years = [0, 1, 2].map(i => new Date().getFullYear() - i);

  private readonly totals = computed(() => {
    const d = this.data();
    return d ? d.months.map((_, mi) => d.values.reduce((s, row) => s + (row[mi] ?? 0), 0)) : [];
  });
  protected readonly grand = computed(() => this.totals().reduce((s, v) => s + v, 0));
  protected readonly monthTotals = computed(() => {
    const d = this.data();
    const max = Math.max(1, ...this.totals());
    return (d?.months ?? []).map((m, i) => ({ label: this.short(m), value: this.totals()[i], height: Math.round(this.totals()[i] / max * 170) }));
  });

  constructor() { void this.load(); }

  protected short(m: string): string { return monthName(m).slice(0, 3); }
  protected rowTotal(i: number): number { return (this.data()?.values[i] ?? []).reduce((s, v) => s + v, 0); }
  protected setYear(y: number): void { this.year.set(y); void this.load(); }

  private async load(): Promise<void> {
    try { this.data.set(await this.api.get<SpendingReport>('/personal/reports/spending', { year: this.year() })); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected exportExcel(): void {
    this.api.download('/personal/reports/spending/export', { year: this.year() }, `spending-${this.year()}.xlsx`).catch(e => this.toasts.error(errorText(e)));
  }
}
