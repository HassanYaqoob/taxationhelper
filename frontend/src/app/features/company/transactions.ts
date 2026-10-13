import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService, errorText, todayIso } from '../../core/api/api.service';
import { Account, EntryDetail, EntryNumber, EntrySummary } from '../../core/api/models';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe, takeFile } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

interface DraftLine { accountId: string; debit: number | null; credit: number | null; }
interface ManualDraft { date: string; memo: string; lines: DraftLine[]; }
interface PettyDraft { date: string; amount: number | null; accountId: string; memo: string; }

@Component({
  selector: 'k-transactions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, RmPipe, DayPipe],
  template: `
<div class="page-head">
  <div><h1>Transactions</h1><p>Every posted entry in the books. Entries are never edited or deleted; a mistake is corrected with a reversal.</p></div>
  @if (ws.canEdit()) {
    <div class="acts">
      <button type="button" class="btn" (click)="openPetty()"><k-icon name="cash" [size]="16" />Record petty cash spending</button>
      <button type="button" class="btn primary" (click)="openManual()"><k-icon name="plus" [size]="16" />Journal entry</button>
    </div>
  }
</div>

<form class="row wrap mb16" (ngSubmit)="load()">
  <input class="input" style="max-width: 300px" name="q" placeholder="Search description, amount or #number" aria-label="Search" [(ngModel)]="q">
  <label class="field inline">From<input class="input" type="date" name="from" [(ngModel)]="from"></label>
  <label class="field inline">To<input class="input" type="date" name="to" [(ngModel)]="to"></label>
  <button type="submit" class="btn">Search</button>
</form>

<section class="card"><div class="tw"><table class="t">
  <thead><tr><th>#</th><th>Date</th><th>Description</th><th>Accounts</th><th>Source</th><th class="n">Amount</th></tr></thead>
  <tbody>
    @for (e of list(); track e.id) {
      <tr class="click" (click)="open(e.id)">
        <td class="caption">{{ e.number }}</td>
        <td class="nowrap">{{ e.date | day }}</td>
        <td class="desc">{{ e.memo }}
          @if (e.reversed) { <span class="pill gr">Reversed</span> }
          @if (e.isReversal) { <span class="pill in">Reversal</span> }
          @if (e.documents) { <span class="pill gr"><k-icon name="doc" [size]="12" />{{ e.documents }}</span> }
        </td>
        <td class="caption">{{ e.accounts }}</td>
        <td><span class="pill gr">{{ e.source }}</span></td>
        <td class="n">{{ e.amount | rm }}</td>
      </tr>
    } @empty { <tr><td colspan="6"><div class="empty">No entries match.</div></td></tr> }
  </tbody>
</table></div></section>

@if (detail(); as e) {
  <div class="backdrop" (click)="detail.set(null)"></div>
  <aside class="drawer" role="dialog" aria-label="Journal entry">
    <div class="dh"><h2>Entry #{{ e.number }}</h2><button type="button" class="iconbtn" style="margin-left: auto" aria-label="Close" (click)="detail.set(null)"><k-icon name="x" /></button></div>
    <div class="db stack">
      <div><div class="caption">{{ e.date | day }} · {{ e.source }} · by {{ e.createdBy }}</div><div style="font-weight: 600; font-size: 16px">{{ e.memo }}</div></div>
      @if (e.reversedByNumber) { <div class="banner in flush">Reversed by entry #{{ e.reversedByNumber }}.</div> }
      @if (e.reversesNumber) { <div class="banner in flush">This reverses entry #{{ e.reversesNumber }}.</div> }
      <table class="t">
        <thead><tr><th>Account</th><th class="n">Debit</th><th class="n">Credit</th></tr></thead>
        <tbody>
          @for (l of e.lines; track $index) {
            <tr><td>{{ l.code }} {{ l.name }}</td><td class="n">{{ l.debit ? (l.debit | rm) : '' }}</td><td class="n">{{ l.credit ? (l.credit | rm) : '' }}</td></tr>
          }
        </tbody>
      </table>
      <div>
        <h3 style="margin-bottom: 8px">Documents</h3>
        @for (d of e.documents; track d.id) {
          <button type="button" class="btn ghost sm" (click)="download(d.id, d.fileName)"><k-icon name="doc" [size]="14" />{{ d.fileName }}</button>
        } @empty { <p class="caption" style="margin: 0 0 8px">No documents attached.</p> }
        @if (ws.canEdit()) { <label class="btn sm filebtn" style="margin-top: 6px"><k-icon name="up" [size]="14" />Attach a file<input type="file" (change)="attach($event, e)" aria-label="Attach a file"></label> }
      </div>
    </div>
    @if (ws.canEdit() && !e.reversedByNumber && !e.reversesNumber) {
      <div class="df"><button type="button" class="btn danger" (click)="reverse(e)">Reverse this entry</button></div>
    }
  </aside>
}

@if (manual(); as m) {
  <div class="modal-wrap" (click)="manual.set(null)">
    <form class="modal wide" (click)="$event.stopPropagation()" (ngSubmit)="saveManual(m)">
      <div class="mh"><h2>New journal entry</h2><p>For adjustments your accountant asks for. Debits must equal credits.</p></div>
      <div class="mb stack">
        <div class="form-grid">
          <label class="field">Date<input class="input" type="date" name="date" [(ngModel)]="m.date"></label>
          <label class="field">Description<input class="input" name="memo" [(ngModel)]="m.memo" placeholder="e.g. Depreciation for 2026"></label>
        </div>
        @for (l of m.lines; track $index; let i = $index) {
          <div class="row">
            <select class="input grow" [name]="'account' + i" [(ngModel)]="l.accountId" aria-label="Account">
              <option value="">Account…</option>
              @for (a of accounts(); track a.id) { <option [value]="a.id">{{ a.code }} {{ a.name }}</option> }
            </select>
            <input class="input" style="max-width: 130px" type="number" step="0.01" [name]="'debit' + i" [(ngModel)]="l.debit" placeholder="Debit" aria-label="Debit">
            <input class="input" style="max-width: 130px" type="number" step="0.01" [name]="'credit' + i" [(ngModel)]="l.credit" placeholder="Credit" aria-label="Credit">
            @if (m.lines.length > 2) { <button type="button" class="iconbtn" aria-label="Remove line" (click)="m.lines.splice(i, 1)"><k-icon name="x" [size]="16" /></button> }
          </div>
        }
        <div class="row">
          <button type="button" class="btn sm" (click)="m.lines.push({ accountId: '', debit: null, credit: null })"><k-icon name="plus" [size]="14" />Add line</button>
          <span class="grow"></span>
          <span [class.neg]="difference(m) !== 0" [class.pos]="difference(m) === 0 && totalDebit(m) > 0">Debits {{ totalDebit(m) | rm }} · Credits {{ totalCredit(m) | rm }}</span>
        </div>
      </div>
      <div class="mf">
        <button type="button" class="btn" (click)="manual.set(null)">Cancel</button>
        <button type="submit" class="btn primary" [disabled]="difference(m) !== 0 || totalDebit(m) === 0">Post entry</button>
      </div>
    </form>
  </div>
}

@if (petty(); as p) {
  <div class="modal-wrap" (click)="petty.set(null)">
    <form class="modal" (click)="$event.stopPropagation()" (ngSubmit)="savePetty(p)">
      <div class="mh"><h2>Petty cash spending</h2><p>What cash withdrawn from the bank was spent on. Attach the receipts to the entry afterwards.</p></div>
      <div class="mb form-grid">
        <label class="field">Date<input class="input" type="date" name="date" [(ngModel)]="p.date"></label>
        <label class="field">Amount (RM)<input class="input" type="number" step="0.01" name="amount" [(ngModel)]="p.amount"></label>
        <label class="field full">Spent on
          <select class="input" name="account" [(ngModel)]="p.accountId">
            <option value="">Choose…</option>
            @for (a of expenseAccounts(); track a.id) { <option [value]="a.id">{{ a.code }} {{ a.name }}</option> }
          </select></label>
        <label class="field full">Description<input class="input" name="memo" [(ngModel)]="p.memo" placeholder="e.g. Parking and tolls, client visit"></label>
      </div>
      <div class="mf"><button type="button" class="btn" (click)="petty.set(null)">Cancel</button><button type="submit" class="btn primary">Record</button></div>
    </form>
  </div>
}`
})
export class TransactionsPage {
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly route = inject(ActivatedRoute);

  protected readonly list = signal<EntrySummary[]>([]);
  protected readonly detail = signal<EntryDetail | null>(null);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly manual = signal<ManualDraft | null>(null);
  protected readonly petty = signal<PettyDraft | null>(null);
  protected readonly expenseAccounts = computed(() => this.accounts().filter(a => a.type === 'Expense' || a.tax === 'Capital'));
  protected q = '';
  protected from = '';
  protected to = '';

  constructor() {
    void this.load();
    this.api.get<Account[]>(`${this.ws.api()}/accounts`).then(a => this.accounts.set(a)).catch(() => undefined);
    const focus = this.route.snapshot.queryParamMap.get('focus');
    if (focus) void this.open(focus);
  }

  protected totalDebit(m: ManualDraft): number { return m.lines.reduce((s, l) => s + (Number(l.debit) || 0), 0); }
  protected totalCredit(m: ManualDraft): number { return m.lines.reduce((s, l) => s + (Number(l.credit) || 0), 0); }
  protected difference(m: ManualDraft): number { return Math.round((this.totalDebit(m) - this.totalCredit(m)) * 100) / 100; }

  protected async load(): Promise<void> {
    try { this.list.set(await this.api.get<EntrySummary[]>(`${this.ws.api()}/journal`, { q: this.q, from: this.from, to: this.to })); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async open(id: string): Promise<void> {
    try { this.detail.set(await this.api.get<EntryDetail>(`${this.ws.api()}/journal/${id}`)); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async reverse(e: EntryDetail): Promise<void> {
    if (!confirm(`Reverse entry #${e.number}? A new entry with the opposite amounts will be posted today.`)) return;
    try {
      const r = await this.api.post<EntryNumber>(`${this.ws.api()}/journal/${e.id}/reverse`);
      this.toasts.show(`Reversal posted as entry #${r.number}. The original stays in the books.`);
      this.detail.set(null);
      await this.load();
    } catch (err) { this.toasts.error(errorText(err)); }
  }

  protected async attach(event: Event, e: EntryDetail): Promise<void> {
    const file = takeFile(event);
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    form.append('journalEntryId', e.id);
    try {
      await this.api.upload(`${this.ws.api()}/documents`, form);
      await this.open(e.id);
      await this.load();
    } catch (err) { this.toasts.error(errorText(err)); }
  }

  protected download(id: string, name: string): void {
    this.api.download(`${this.ws.api()}/documents/${id}/file`, undefined, name).catch(e => this.toasts.error(errorText(e)));
  }

  protected openManual(): void {
    this.manual.set({ date: todayIso(), memo: '', lines: [{ accountId: '', debit: null, credit: null }, { accountId: '', debit: null, credit: null }] });
  }

  protected async saveManual(m: ManualDraft): Promise<void> {
    const lines = m.lines.filter(l => l.accountId && (Number(l.debit) || Number(l.credit)))
      .map(l => ({ accountId: l.accountId, debit: Number(l.debit) || 0, credit: Number(l.credit) || 0 }));
    try {
      const r = await this.api.post<EntryNumber>(`${this.ws.api()}/journal`, { date: m.date, memo: m.memo, lines });
      this.toasts.show(`Entry #${r.number} posted.`);
      this.manual.set(null);
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected openPetty(): void { this.petty.set({ date: todayIso(), amount: null, accountId: '', memo: '' }); }

  protected async savePetty(p: PettyDraft): Promise<void> {
    if (!p.accountId || !p.amount) { this.toasts.error('Choose what it was spent on and enter the amount.'); return; }
    try {
      const r = await this.api.post<EntryNumber>(`${this.ws.api()}/journal/petty-cash`, p);
      this.toasts.show(`Recorded as entry #${r.number}.`);
      this.petty.set(null);
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }
}
