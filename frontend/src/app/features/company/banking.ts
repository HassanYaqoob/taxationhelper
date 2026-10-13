import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService, errorText } from '../../core/api/api.service';
import { Account, BankLine, BankLineStatus, BankingOverview, CountResult, Doc, EntryNumber, ImportResult, RecheckSummary, Rule, SetAccountResult } from '../../core/api/models';
import { WorkspaceService } from '../../core/auth/workspace.service';
import { Icon } from '../../core/ui/icon';
import { DayPipe, RmPipe, takeFile } from '../../core/ui/pipes';
import { ToastService } from '../../core/ui/toast.service';

type Tab = BankLineStatus | 'Rules';
const GROUPS: [string, string][] = [['Income', 'Income'], ['Expense', 'Expenses'], ['Asset', 'Assets'], ['Liability', 'Liabilities'], ['Equity', 'Equity']];

@Component({
  selector: 'k-banking',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, Icon, RmPipe, DayPipe],
  template: `
<div class="page-head">
  <div><h1>Bank statements</h1><p>Upload each month's statement. Kira reads every line, suggests the category from the description, and posts it to the books when you confirm.</p></div>
</div>

@if (ws.canEdit()) {
  <section class="card pad mb16">
    <div class="row wrap" style="gap: 12px">
      <span class="wsav company" style="width: 44px; height: 44px"><k-icon name="up" [size]="20" /></span>
      <div class="grow" style="min-width: 220px"><h2>Upload a statement</h2><div class="caption">PDF (password removed), CSV, Excel or a photo. Lines already imported are skipped.</div></div>
      <select class="input" style="max-width: 260px" [ngModel]="bankId()" (ngModelChange)="bankId.set($event)" aria-label="Bank account">
        @for (b of overview()?.banks ?? []; track b.id) { <option [value]="b.id">{{ b.name }}{{ b.last4 ? ' ••' + b.last4 : '' }}</option> }
      </select>
      <label class="btn primary filebtn" [class.disabled]="importing()">
        <k-icon name="up" [size]="16" />{{ importing() ? 'Reading statement…' : 'Choose file' }}
        <input type="file" accept=".pdf,.csv,.xlsx,.txt,.png,.jpg,.jpeg,.webp" [disabled]="importing()" (change)="upload($event)" aria-label="Statement file">
      </label>
    </div>
    @if (importing()) {
      <span class="progress" style="margin-top: 14px"><i class="skel" style="width: 100%; height: 8px"></i></span>
      <p class="caption" style="margin: 8px 0 0">The AI reads the whole statement. Long statements can take a minute.</p>
    }
    @if (overview() && !overview()!.aiEnabled) {
      <div class="banner wa flush" style="margin-top: 14px">AI is off on this server, so only CSV files with Date and Description columns can be read, and lines won't be categorised.</div>
    }
  </section>
}

<div class="tabs" role="tablist">
  @for (t of tabs; track t) { <button type="button" role="tab" [class.on]="tab() === t" [attr.aria-selected]="tab() === t" (click)="setTab(t)">{{ tabLabel(t) }}</button> }
</div>

@if (tab() !== 'Rules') {
  <div class="row wrap mb16">
    <input class="input" style="max-width: 280px" placeholder="Filter by description or amount" aria-label="Filter" [ngModel]="filter()" (ngModelChange)="filter.set($event)">
    <select class="input" style="max-width: 240px" aria-label="Bank account" [ngModel]="bankFilter()" (ngModelChange)="bankFilter.set($event); load()">
      <option value="">All bank accounts</option>
      @for (b of overview()?.banks ?? []; track b.id) { <option [value]="b.id">{{ b.name }}</option> }
    </select>
    <span class="grow"></span>
    @if (tab() === 'Pending' && ws.canEdit() && lines().length) {
      <button type="button" class="btn" [disabled]="busy()" (click)="recheck()"><k-icon name="spark" [size]="16" />Ask AI to re-check unsure</button>
      <button type="button" class="btn primary" [disabled]="busy() || !confident()" (click)="acceptConfident()"><k-icon name="check" [size]="16" />Confirm {{ confident() }} confident</button>
    }
  </div>

  <div class="grid" [class.side-panel]="!!selected()">
    <section class="card" style="overflow: hidden">
      <div class="tw"><table class="t">
        <thead><tr><th>Date</th><th>Bank line</th><th style="min-width: 250px">Category</th><th class="n">Amount</th>@if (editable()) { <th></th> }</tr></thead>
        <tbody>
          @for (l of shown(); track l.id) {
            <tr class="click" [class.flag]="tab() === 'Pending' && unsure(l)" [class.sel]="selected()?.id === l.id" (click)="select(l)">
              <td class="nowrap">{{ l.date | day }}</td>
              <td class="desc">
                <div class="title">{{ l.description }}</div>
                <div class="caption">{{ l.reason }}</div>
                <div class="row wrap" style="gap: 6px; margin-top: 4px">
                  @if (isCash(l)) { <span class="pill wa"><k-icon name="cash" [size]="12" />Cash withdrawal</span> }
                  @if (l.flags.includes('foreign_payee')) { <span class="pill in"><k-icon name="globe" [size]="12" />Foreign payee: check withholding tax</span> }
                  @if (l.documents) { <span class="pill gr"><k-icon name="doc" [size]="12" />{{ l.documents }}</span> }
                </div>
              </td>
              <td (click)="$event.stopPropagation()">
                @if (editable() && isCash(l)) {
                  <div class="stack" style="gap: 6px">
                    <button type="button" class="btn sm" (click)="explainCash(l, 'petty')"><k-icon name="cash" [size]="14" />Petty cash for business</button>
                    <button type="button" class="btn sm" (click)="explainCash(l, 'director')">Director drawings</button>
                  </div>
                } @else if (editable()) {
                  <select class="input" aria-label="Category" [ngModel]="l.accountId ?? ''" (ngModelChange)="setAccount(l, $event)">
                    <option value="" disabled>Choose a category…</option>
                    @for (g of groups(); track g.label) {
                      <optgroup [label]="g.label">
                        @for (a of g.accounts; track a.id) { @if (a.id !== l.bankAccountId) { <option [value]="a.id">{{ a.code }} {{ a.name }}</option> } }
                      </optgroup>
                    }
                  </select>
                  @if (l.accountId) { <span class="pill" style="margin-top: 5px" [class.ok]="l.confidence >= 0.8" [class.wa]="l.confidence < 0.8">{{ confidenceLabel(l) }}</span> }
                } @else { {{ l.accountName ?? '—' }} }
              </td>
              <td class="n" [class.pos]="l.direction === 'In'"><b>{{ (l.direction === 'In' ? l.amount : -l.amount) | rm:'sign' }}</b></td>
              @if (editable()) {
                <td class="n" (click)="$event.stopPropagation()">
                  @if (!isCash(l)) { <button type="button" class="btn sm primary" [disabled]="!l.accountId" (click)="accept(l)">Confirm</button> }
                </td>
              }
            </tr>
          } @empty {
            <tr><td colspan="5"><div class="empty">
              @if (tab() === 'Pending') { <b>Nothing to review</b>Upload your next statement when it arrives. } @else { No lines here. }
            </div></td></tr>
          }
        </tbody>
      </table></div>
    </section>

    @if (selected(); as l) {
      <aside class="card pad stack" style="align-self: start; position: sticky; top: 80px">
        <div class="row"><h2>Line details</h2><button type="button" class="iconbtn" style="margin-left: auto" aria-label="Close" (click)="selected.set(null)"><k-icon name="x" [size]="16" /></button></div>
        <div>
          <div class="caption">{{ l.date | day }} · {{ l.bankName }}</div>
          <div style="font-weight: 600">{{ l.description }}</div>
          <div style="font-size: 26px; font-weight: 600; letter-spacing: -0.02em" [class.pos]="l.direction === 'In'">{{ (l.direction === 'In' ? l.amount : -l.amount) | rm:'sign' }}</div>
        </div>
        @if (l.reason) { <div class="banner ok flush"><k-icon name="spark" [size]="16" /><span>{{ l.reason }}</span></div> }
        <div class="caption">From {{ l.sourceFile || 'statement' }}. Status: {{ l.status }}.</div>
        @if (editable()) { <label class="check"><input type="checkbox" [ngModel]="remember()" (ngModelChange)="remember.set($event)">Remember my choice for similar lines</label> }
        <div>
          <h3 style="margin-bottom: 8px">Supporting documents</h3>
          @for (doc of docs(); track doc.id) {
            <button type="button" class="btn ghost sm" style="justify-content: flex-start; width: 100%" (click)="download(doc)"><k-icon name="doc" [size]="14" />{{ doc.fileName }}</button>
          } @empty { <p class="caption" style="margin: 0 0 8px">Optional. Attach the bill, invoice or e-invoice when the description isn't enough.</p> }
          @if (ws.canEdit()) {
            <label class="btn sm filebtn"><k-icon name="up" [size]="14" />Attach a file<input type="file" (change)="attach($event, l)" aria-label="Attach a file"></label>
          }
        </div>
        @if (editable()) { <button type="button" class="btn sm danger" (click)="ignore(l)">Ignore this line</button> }
      </aside>
    }
  </div>
} @else {
  <section class="card">
    <div class="card-h"><h2>Your rules</h2><span class="r caption">Created when you choose a category. Applied to every future statement.</span></div>
    @for (r of rules(); track r.id) {
      <div class="list-row">
        <span class="pill gr">"{{ r.pattern }}"</span><k-icon name="arrow" [size]="14" /><span class="grow">{{ r.label }}</span>
        @if (ws.canEdit()) { <button type="button" class="btn sm ghost" aria-label="Delete rule" (click)="deleteRule(r)"><k-icon name="trash" [size]="14" /></button> }
      </div>
    } @empty { <div class="empty">No rules yet. Change a line's category and Kira will remember it.</div> }
  </section>
}`
})
export class BankingPage {
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly toasts = inject(ToastService);
  private readonly route = inject(ActivatedRoute);

  protected readonly tabs: Tab[] = ['Pending', 'Posted', 'Ignored', 'Rules'];
  protected readonly tab = signal<Tab>('Pending');
  protected readonly overview = signal<BankingOverview | null>(null);
  protected readonly lines = signal<BankLine[]>([]);
  protected readonly accounts = signal<Account[]>([]);
  protected readonly rules = signal<Rule[]>([]);
  protected readonly selected = signal<BankLine | null>(null);
  protected readonly docs = signal<Doc[]>([]);
  protected readonly importing = signal(false);
  protected readonly busy = signal(false);
  protected readonly bankId = signal('');
  protected readonly bankFilter = signal('');
  protected readonly filter = signal('');
  protected readonly remember = signal(true);

  protected readonly editable = computed(() => this.tab() === 'Pending' && this.ws.canEdit());
  protected readonly groups = computed(() => GROUPS
    .map(([type, label]) => ({ label, accounts: this.accounts().filter(a => a.type === type) }))
    .filter(g => g.accounts.length));
  protected readonly confident = computed(() => this.lines().filter(l => l.accountId && l.confidence >= 0.9 && !this.isCash(l)).length);
  protected readonly shown = computed(() => {
    const f = this.filter().trim().toLowerCase();
    return f ? this.lines().filter(l => l.description.toLowerCase().includes(f) || l.amount.toFixed(2).includes(f)) : this.lines();
  });

  constructor() { void this.init(); }

  private async init(): Promise<void> {
    try {
      const [overview, accounts] = await Promise.all([
        this.api.get<BankingOverview>(`${this.ws.api()}/banking`),
        this.api.get<Account[]>(`${this.ws.api()}/accounts`)
      ]);
      this.overview.set(overview);
      this.accounts.set(accounts);
      this.bankId.set(overview.banks[0]?.id ?? '');
      await this.load();
      const focus = this.route.snapshot.queryParamMap.get('focus');
      const hit = focus ? this.lines().find(l => l.id === focus) : undefined;
      if (hit) await this.select(hit);
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected tabLabel(t: Tab): string {
    if (t === 'Pending') return `To review${this.overview() ? ` (${this.overview()!.pending})` : ''}`;
    return t;
  }

  protected isCash(l: BankLine): boolean { return l.flags.includes('cash_withdrawal'); }
  protected unsure(l: BankLine): boolean { return !l.accountId || l.confidence < 0.7 || this.isCash(l); }
  protected confidenceLabel(l: BankLine): string { return l.confidence >= 1 ? 'Your choice' : `${Math.round(l.confidence * 100)}% sure`; }

  protected setTab(t: Tab): void {
    this.tab.set(t);
    this.selected.set(null);
    void (t === 'Rules' ? this.loadRules() : this.load());
  }

  protected async load(): Promise<void> {
    const t = this.tab();
    if (t === 'Rules') return;
    try {
      this.lines.set(await this.api.get<BankLine[]>(`${this.ws.api()}/banking/lines`, { status: t, bankAccountId: this.bankFilter() || null }));
      const s = this.selected();
      if (s) this.selected.set(this.lines().find(l => l.id === s.id) ?? null);
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  private async refresh(): Promise<void> {
    await this.load();
    try { this.overview.set(await this.api.get<BankingOverview>(`${this.ws.api()}/banking`)); } catch { /* counts are optional */ }
  }

  private async loadRules(): Promise<void> {
    try { this.rules.set(await this.api.get<Rule[]>(`${this.ws.api()}/banking/rules`)); }
    catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async upload(event: Event): Promise<void> {
    const file = takeFile(event);
    if (!file) return;
    if (!this.bankId()) { this.toasts.error('Add a bank account in Settings first.'); return; }
    const form = new FormData();
    form.append('file', file);
    form.append('bankAccountId', this.bankId());
    this.importing.set(true);
    try {
      const r = await this.api.upload<ImportResult>(`${this.ws.api()}/banking/import`, form);
      this.toasts.show(r.message);
      this.tab.set('Pending');
      await this.refresh();
    } catch (e) { this.toasts.error(errorText(e)); }
    finally { this.importing.set(false); }
  }

  protected async select(l: BankLine): Promise<void> {
    this.selected.set(l);
    try { this.docs.set(await this.api.get<Doc[]>(`${this.ws.api()}/documents`, { bankLineId: l.id })); }
    catch { this.docs.set([]); }
  }

  protected async setAccount(l: BankLine, accountId: string): Promise<void> {
    try {
      const r = await this.api.put<SetAccountResult>(`${this.ws.api()}/banking/lines/${l.id}`, { accountId, remember: this.remember() });
      const a = this.accounts().find(x => x.id === accountId);
      this.lines.update(ls => ls.map(x => x.id === l.id ? { ...x, accountId, accountName: a ? `${a.code} ${a.name}` : null, confidence: 1, reason: 'Chosen by you' } : x));
      if (r.similar > 0) this.toasts.show(`${r.similar} similar line${r.similar > 1 ? 's' : ''} found.`, { action: 'Apply to all', run: () => void this.applySimilar(l) });
      else if (r.pattern) this.toasts.show(`Saved. Kira will remember "${r.pattern}".`);
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  private async applySimilar(l: BankLine): Promise<void> {
    try {
      const r = await this.api.post<CountResult>(`${this.ws.api()}/banking/lines/${l.id}/apply-similar`);
      this.toasts.show(`Updated ${r.count}.`);
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async accept(l: BankLine): Promise<void> {
    try {
      const r = await this.api.post<EntryNumber>(`${this.ws.api()}/banking/lines/${l.id}/accept`);
      this.toasts.show(`Posted as entry #${r.number}.`);
      if (this.selected()?.id === l.id) this.selected.set(null);
      await this.refresh();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async explainCash(l: BankLine, choice: 'petty' | 'director'): Promise<void> {
    try {
      await this.api.post(`${this.ws.api()}/banking/lines/${l.id}/cash`, { choice });
      this.toasts.show(choice === 'petty' ? 'Moved to petty cash. Record what it was spent on under Transactions.' : 'Recorded as director drawings.');
      await this.refresh();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async ignore(l: BankLine): Promise<void> {
    if (!confirm('Ignore this line? It won\'t be posted to the books.')) return;
    try {
      await this.api.post(`${this.ws.api()}/banking/lines/${l.id}/ignore`);
      this.selected.set(null);
      await this.refresh();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async acceptConfident(): Promise<void> {
    this.busy.set(true);
    try {
      const r = await this.api.post<CountResult>(`${this.ws.api()}/banking/accept-confident`);
      this.toasts.show(`${r.count} lines posted.`);
      await this.refresh();
    } catch (e) { this.toasts.error(errorText(e)); }
    finally { this.busy.set(false); }
  }

  protected async recheck(): Promise<void> {
    this.busy.set(true);
    try {
      const r = await this.api.post<RecheckSummary>(`${this.ws.api()}/banking/recheck`);
      this.toasts.show(r.rechecked ? `Re-checked ${r.rechecked}. ${r.improved} are now confident.` : 'Nothing unsure to re-check.');
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
    finally { this.busy.set(false); }
  }

  protected async attach(event: Event, l: BankLine): Promise<void> {
    const file = takeFile(event);
    if (!file) return;
    const form = new FormData();
    form.append('file', file);
    form.append('bankLineId', l.id);
    try {
      await this.api.upload(`${this.ws.api()}/documents`, form);
      this.toasts.show('Attached.');
      await this.select(l);
      await this.load();
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected download(d: Doc): void {
    this.api.download(`${this.ws.api()}/documents/${d.id}/file`, undefined, d.fileName).catch(e => this.toasts.error(errorText(e)));
  }

  protected async deleteRule(r: Rule): Promise<void> {
    try {
      await this.api.delete(`${this.ws.api()}/banking/rules/${r.id}`);
      await this.loadRules();
    } catch (e) { this.toasts.error(errorText(e)); }
  }
}
