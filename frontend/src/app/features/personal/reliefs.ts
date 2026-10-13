import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService, errorText } from '../../core/api/api.service';
import { ReliefSummary } from '../../core/api/models';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe, percent } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

@Component({
  selector: 'k-reliefs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, Icon, RmPipe, DayPipe],
  template: `
<div class="page-head">
  <div><h1>Tax reliefs</h1><p>What you can claim on your Form BE from the receipts you've kept. Limits follow the rules set for each year of assessment.</p></div>
  <div class="acts">
    <select class="input" aria-label="Year" [value]="year()" (change)="setYear(+$any($event.target).value)">
      @for (y of years; track y) { <option [value]="y">YA {{ y }}</option> }
    </select>
  </div>
</div>

@if (data(); as d) {
  @if (d.filingNote) { <div class="banner in"><k-icon name="alert" [size]="16" />{{ d.filingNote }}</div> }

  @if (d.suggestions.length) {
    <section class="card mb16">
      <div class="card-h"><h2>Might qualify</h2><span class="r caption">Open a receipt to claim or dismiss</span></div>
      @for (s of d.suggestions; track s.receiptId) {
        <a class="list-row click" [routerLink]="['/p/receipts', s.receiptId]"><k-icon name="spark" [size]="18" />
          <div class="grow"><div>{{ s.merchant }} · <b>{{ s.name }}</b></div><div class="caption">{{ s.date | day }} · {{ s.reason }}</div></div>
          <b>{{ s.amount | rm }}</b><k-icon name="chev" [size]="16" /></a>
      }
    </section>
  }

  <div class="grid g3 mb16">
    <div class="card pad kpi"><div class="k">Claimed so far</div><div class="v">{{ total() | rm:'0' }}</div><div class="s">From saved receipts</div></div>
    <div class="card pad kpi"><div class="k">Categories used</div><div class="v">{{ used() }}</div><div class="s">of {{ d.lines.length }}</div></div>
    <div class="card pad kpi"><div class="k">Receipts linked</div><div class="v">{{ receipts() }}</div><div class="s">Keep them for 7 years</div></div>
  </div>

  <section class="card">
    @for (l of d.lines; track l.code) {
      <div class="list-row" style="align-items: flex-start">
        <div class="grow">
          <div class="title">{{ l.name }} @if (l.new) { <span class="pill in">New this year</span> }</div>
          <div class="caption">{{ l.hint }}</div>
          @if (l.limit > 0) { <span class="progress" style="margin-top: 8px; max-width: 420px"><i [style.width.%]="pct(l.claimed, l.limit)"></i></span> }
        </div>
        <div style="text-align: right">
          <b>{{ l.claimed | rm:'0' }}</b>
          <div class="caption">@if (l.limit > 0) { of {{ l.limit | rm:'0' }} } @else { no limit }</div>
          @if (l.receipts) { <div class="caption">{{ l.receipts }} receipt{{ l.receipts > 1 ? 's' : '' }}</div> }
        </div>
      </div>
    }
  </section>
  <p class="caption" style="margin-top: 12px">This is a guide, not tax advice. Automatic reliefs (individual RM9,000, EPF through payroll) are applied by LHDN and aren't tracked here.</p>
} @else { <div class="card pad skel" style="height: 300px"></div> }`
})
export class ReliefsPage {
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly data = signal<ReliefSummary | null>(null);
  protected readonly year = signal(new Date().getFullYear());
  protected readonly years = [0, 1, 2].map(i => new Date().getFullYear() - i);
  protected readonly total = computed(() => (this.data()?.lines ?? []).reduce((s, l) => s + l.claimed, 0));
  protected readonly used = computed(() => (this.data()?.lines ?? []).filter(l => l.claimed > 0).length);
  protected readonly receipts = computed(() => (this.data()?.lines ?? []).reduce((s, l) => s + l.receipts, 0));
  protected readonly pct = percent;

  constructor() { void this.load(); }

  protected setYear(y: number): void { this.year.set(y); void this.load(); }

  private async load(): Promise<void> {
    try { this.data.set(await this.api.get<ReliefSummary>('/personal/reliefs', { year: this.year() })); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}
