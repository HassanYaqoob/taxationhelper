import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService, errorText, todayIso } from '../../core/api/api.service';
import { Compliance, Deadline, ReminderRepeat } from '../../core/api/models';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

interface ReminderDraft { name: string; dueDate: string; repeat: ReminderRepeat; notes: string; }

@Component({
  selector: 'k-tax',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, RmPipe, DayPipe],
  template: `
<div class="page-head">
  <div><h1>Tax &amp; compliance</h1><p>Estimates from your books under the current Malaysian rules. Your tax agent confirms the final figures.</p></div>
  <div class="acts"><button type="button" class="btn primary" (click)="downloadPackage()"><k-icon name="dl" [size]="16" />Accountant package (.xlsx)</button></div>
</div>

@if (data(); as d) {
  <div class="grid g2 mb16">
    <section class="card pad">
      <div class="ch"><h2>Estimated tax · YA {{ d.tax.yearOfAssessment }}</h2><span class="pill gr">{{ d.from | day }} – {{ d.to | day }}</span></div>
      <table class="t tight"><tbody>
        <tr><td>Income</td><td class="n">{{ d.tax.income | rm }}</td></tr>
        <tr><td>Deductible expenses</td><td class="n">−{{ d.tax.deductible | rm }}</td></tr>
        <tr><td>Entertainment (50% added back)</td><td class="n">+{{ d.tax.entertainmentAddBack | rm }}</td></tr>
        <tr><td>Not deductible (added back)</td><td class="n">{{ d.tax.nonDeductible | rm }}</td></tr>
        <tr><td>Capital allowance ({{ d.tax.capitalAllowancePct }}% of {{ d.tax.assetsBought | rm }})</td><td class="n">−{{ d.tax.capitalAllowance | rm }}</td></tr>
        <tr class="tot"><td>Chargeable income</td><td class="n">{{ d.tax.chargeable | rm }}</td></tr>
        @for (t of d.tax.tiers; track t.label) { <tr class="sub"><td>{{ t.label }}</td><td class="n">{{ t.amount | rm }}</td></tr> }
        <tr class="tot"><td>Estimated tax {{ d.tax.sme ? '(SME rates)' : '(standard rate)' }}</td><td class="n">{{ d.tax.tax | rm }}</td></tr>
        <tr><td>CP204 paid so far</td><td class="n">−{{ d.tax.cp204Paid | rm }}</td></tr>
        <tr class="tot"><td>{{ d.tax.balance >= 0 ? 'Balance to pay' : 'Overpaid' }}</td><td class="n" [class.neg]="d.tax.balance > 0">{{ d.tax.balance | rm }}</td></tr>
      </tbody></table>
      <p class="caption">Figures are annualised from the year so far. Expenses marked "not deductible" (fines, personal spending, donations without receipts) are added back.</p>
    </section>

    <section class="stack">
      <div class="card pad">
        <div class="ch"><h2>CP204 instalments</h2><span class="pill" [class.ok]="d.cp204.status === 'OnTrack'" [class.wa]="d.cp204.status !== 'OnTrack'">{{ statusLabel(d.cp204.status) }}</span></div>
        <div class="row wrap" style="gap: 24px">
          <div class="kpi"><span class="k">Estimate</span><span class="v">{{ d.cp204.estimate | rm:'0' }}</span></div>
          <div class="kpi"><span class="k">Projected tax</span><span class="v">{{ d.cp204.projectedTax | rm:'0' }}</span></div>
          <div class="kpi"><span class="k">Paid so far</span><span class="v">{{ d.cp204.paidSoFar | rm:'0' }}</span></div>
        </div>
        <p>{{ d.cp204.advice }}</p>
        @if (d.cp204.nextRevisionMonth) { <p class="caption">Next chance to revise: month {{ d.cp204.nextRevisionMonth }} of the basis period (by {{ d.cp204.nextRevisionBy | day }}), using form CP204A.</p> }
      </div>

      <div class="card pad">
        <div class="ch"><h2>e-Invoice</h2><span class="pill" [class.wa]="d.eInvoice.required" [class.ok]="!d.eInvoice.required">{{ d.eInvoice.required ? 'Required' : 'Exempt for now' }}</span></div>
        <p>Revenue in the last 12 months: <b>{{ d.eInvoice.revenue12m | rm }}</b> (exemption threshold {{ d.eInvoice.threshold | rm:'0' }}).</p>
        <p class="caption">{{ d.eInvoice.note }}</p>
      </div>

      <div class="card pad">
        <div class="ch"><h2>Audit exemption · FY {{ d.audit.year }}</h2><span class="pill" [class.ok]="d.audit.likelyQualifies" [class.wa]="!d.audit.likelyQualifies">{{ d.audit.likelyQualifies ? 'Likely exempt' : 'Audit likely needed' }}</span></div>
        <table class="t tight"><tbody>
          @for (c of d.audit.criteria; track c.name) {
            <tr><td>{{ c.name }}</td><td>{{ c.value }}</td><td class="caption">limit {{ c.limit }}</td><td>{{ c.within ? '✓' : '✗' }}</td></tr>
          }
        </tbody></table>
        <p class="caption">{{ d.audit.note }}</p>
      </div>
    </section>
  </div>

  <div class="grid g2 mb16">
    <section class="card pad">
      <div class="ch"><h2>Deadlines and reminders</h2>@if (ws.canEdit()) { <button type="button" class="btn sm" (click)="openReminder()"><k-icon name="plus" [size]="14" />Reminder</button> }</div>
      @for (x of d.deadlines; track x.name + x.due) {
        <div class="list-row tight">
          <span class="dot" [class.no]="x.daysLeft < 0" [class.wa]="x.daysLeft >= 0 && x.daysLeft <= 14" [class.ok]="x.daysLeft > 14"></span>
          <div class="grow"><div>{{ x.name }}</div><div class="caption">{{ x.due | day }} · {{ daysText(x) }}</div></div>
          @if (x.reminderId && ws.canEdit()) {
            <button type="button" class="btn sm ghost" (click)="done(x)">Done</button>
            <button type="button" class="iconbtn" aria-label="Delete reminder" (click)="remove(x)"><k-icon name="trash" [size]="16" /></button>
          }
        </div>
      } @empty { <div class="empty">No deadlines.</div> }
    </section>

    <section class="stack">
      <div class="card pad">
        <div class="ch"><h2>Director's account</h2></div>
        @if (d.dueFromDirector > 0) {
          <div class="banner wa flush">The director owes the company <b>{{ d.dueFromDirector | rm }}</b>. Under section 140B of the Income Tax Act 1967, LHDN can deem interest income on loans to directors. Repay it, or agree the treatment with your tax agent.</div>
        } @else if (d.dueFromDirector < 0) {
          <p>The company owes the director <b>{{ -d.dueFromDirector | rm }}</b>.</p>
        } @else { <p class="caption">Nothing owed either way.</p> }
      </div>

      <div class="card pad">
        <div class="ch"><h2>Payments to foreign parties</h2><a class="btn sm ghost" [routerLink]="['../banking']">Bank statements</a></div>
        <p class="caption">Payments to non-residents for services, royalties or interest may need withholding tax (Form CP37) within one month of paying or crediting.</p>
        @for (f of d.foreignPayees; track f.id) {
          <div class="list-row tight"><div class="grow"><div class="desc">{{ f.description }}</div><div class="caption">{{ f.date | day }} · {{ f.status }}</div></div><span>{{ f.amount | rm }}</span></div>
        } @empty { <div class="empty">None found.</div> }
      </div>

      <div class="card pad">
        <div class="ch"><h2>Cash</h2></div>
        <p>Petty cash balance: <b>{{ d.cash.pettyCashBalance | rm }}</b></p>
        @if (d.cash.pendingWithdrawals) { <p>{{ d.cash.pendingWithdrawals }} cash withdrawal(s) of {{ d.cash.pendingAmount | rm }} still need explaining.</p> }
      </div>
    </section>
  </div>
} @else { <div class="card pad skel" style="height: 300px"></div> }

@if (reminder(); as r) {
  <div class="modal-wrap" (click)="reminder.set(null)">
    <form class="modal" (click)="$event.stopPropagation()" (ngSubmit)="saveReminder(r)">
      <div class="mh"><h2>New reminder</h2><p>For example SST-02 returns, EPF/SOCSO payments or annual return to SSM.</p></div>
      <div class="mb form-grid">
        <label class="field full">Name<input class="input" name="name" required [(ngModel)]="r.name"></label>
        <label class="field">Due<input class="input" type="date" name="due" required [(ngModel)]="r.dueDate"></label>
        <label class="field">Repeat
          <select class="input" name="repeat" [(ngModel)]="r.repeat"><option value="Once">Once</option><option value="Monthly">Monthly</option><option value="Yearly">Yearly</option></select></label>
        <label class="field full">Notes<input class="input" name="notes" [(ngModel)]="r.notes"></label>
      </div>
      <div class="mf"><button type="button" class="btn" (click)="reminder.set(null)">Cancel</button><button type="submit" class="btn primary">Add</button></div>
    </form>
  </div>
}`
})
export class TaxPage {
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  protected readonly data = signal<Compliance | null>(null);
  protected readonly reminder = signal<ReminderDraft | null>(null);

  constructor() { void this.load(); }

  protected async load(): Promise<void> {
    try { this.data.set(await this.api.get<Compliance>(`${this.ws.api()}/compliance`)); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected statusLabel(s: string): string {
    return s === 'OnTrack' ? 'On track' : s === 'TooLow' ? 'Estimate too low' : s === 'TooHigh' ? 'Estimate too high' : 'Not set';
  }

  protected daysText(x: Deadline): string {
    return x.daysLeft < 0 ? `${-x.daysLeft} days overdue` : x.daysLeft === 0 ? 'today' : `in ${x.daysLeft} days`;
  }

  protected downloadPackage(): void {
    this.api.download(`${this.ws.api()}/compliance/package`, undefined, 'accountant-package.xlsx').catch(e => this.toasts.error(errorText(e)));
  }

  protected openReminder(): void { this.reminder.set({ name: '', dueDate: todayIso(), repeat: 'Once', notes: '' }); }

  protected async saveReminder(r: ReminderDraft): Promise<void> {
    try { await this.api.post(`${this.ws.api()}/reminders`, r); this.reminder.set(null); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async done(x: Deadline): Promise<void> {
    try {
      const r = await this.api.post<{ next: string | null }>(`${this.ws.api()}/reminders/${x.reminderId}/done`);
      this.toasts.show(r.next ? `Done. Next one is due ${r.next}.` : 'Done.');
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async remove(x: Deadline): Promise<void> {
    if (!confirm(`Delete the reminder "${x.name}"?`)) return;
    try { await this.api.delete(`${this.ws.api()}/reminders/${x.reminderId}`); await this.load(); }
    catch (e) { this.toasts.error(errorText(e)); }
  }
}
