import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs';
import { ApiService, errorText } from '../core/api/api.service';
import { AskResponse, BankingOverview, SearchHit, Workspace } from '../core/api/models';
import { AuthService } from '../core/auth/auth.service';
import { WorkspaceService } from '../core/auth/workspace.service';
import { Icon } from '../core/ui/icon';
import { initials } from '../core/ui/pipes';
import { ToastService } from '../core/ui/toast.service';

interface NavItem { path: string; label: string; icon: string; count?: number; }

@Component({
  selector: 'k-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, FormsModule, Icon],
  templateUrl: './shell.html',
  host: { '(document:keydown)': 'onKey($event)' }
})
export class Shell {
  protected readonly auth = inject(AuthService);
  protected readonly ws = inject(WorkspaceService);
  private readonly api = inject(ApiService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly menuOpen = signal(false);
  protected readonly wsMenuOpen = signal(false);
  protected readonly userMenuOpen = signal(false);
  protected readonly searchOpen = signal(false);
  protected readonly askOpen = signal(false);
  protected readonly passwordOpen = signal(false);
  protected readonly searching = signal(false);
  protected readonly asking = signal(false);
  protected readonly hits = signal<SearchHit[]>([]);
  protected readonly answer = signal('');
  protected readonly pending = signal(0);
  protected readonly query = signal('');
  protected question = '';
  protected password = { current: '', new: '' };
  private searchTimer?: ReturnType<typeof setTimeout>;

  protected readonly current = this.ws.current;
  /** Recreates the page when switching workspace so it reloads its data. */
  protected readonly outletKey = computed(() => `${this.current()?.kind}:${this.current()?.id}`);
  protected readonly initials = initials;

  protected readonly nav = computed<NavItem[]>(() => {
    const w = this.current();
    if (!w) return [];
    if (w.kind === 'personal') return [
      { path: '/p/overview', label: 'Overview', icon: 'grid' }, { path: '/p/accounts', label: 'Accounts', icon: 'wallet' },
      { path: '/p/receipts', label: 'Receipts', icon: 'receipt' }, { path: '/p/transactions', label: 'Transactions', icon: 'list' },
      { path: '/p/reliefs', label: 'Tax reliefs', icon: 'heart' }, { path: '/p/reports', label: 'Reports', icon: 'chart' }
    ];
    const b = `/c/${w.id}`;
    const items: NavItem[] = [
      { path: `${b}/dashboard`, label: 'Dashboard', icon: 'grid' },
      { path: `${b}/banking`, label: 'Bank statements', icon: 'bank', count: this.pending() },
      { path: `${b}/transactions`, label: 'Transactions', icon: 'list' },
      { path: `${b}/reports`, label: 'Reports', icon: 'chart' },
      { path: `${b}/tax`, label: 'Tax & compliance', icon: 'pct' },
      { path: `${b}/documents`, label: 'Documents', icon: 'doc' },
      { path: `${b}/access`, label: 'Accountant access', icon: 'shield' },
      { path: `${b}/settings`, label: 'Settings', icon: 'gear' },
      { path: `${b}/activity`, label: 'Activity log', icon: 'history' }
    ];
    return w.role === 'Owner' ? items : items.filter(i => !i.path.endsWith('/settings') && !i.path.endsWith('/access'));
  });

  protected readonly searchEmptyText = computed(() =>
    this.query().trim().length < 2 ? 'Type at least 2 characters.' : this.searching() ? 'Searching…' : 'No matches in this workspace.');

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.route.paramMap.pipe(takeUntilDestroyed(destroyRef)).subscribe(p => this.pick(p.get('cid')));
    this.router.events.pipe(filter(e => e instanceof NavigationEnd), takeUntilDestroyed(destroyRef)).subscribe(() => {
      this.wsMenuOpen.set(false);
      this.userMenuOpen.set(false);
      this.menuOpen.set(false);
      void this.refreshCounts();
    });
    effect(() => { if (this.current()) void this.refreshCounts(); });
  }

  private pick(cid: string | null): void {
    const list = this.auth.me()?.workspaces ?? [];
    const w = cid ? list.find(x => x.kind === 'company' && x.id === cid) : list.find(x => x.kind === 'personal');
    if (!w) { void this.router.navigateByUrl(this.auth.homeUrl()); return; }
    this.ws.current.set(w);
  }

  protected wsLabel(w: Workspace | null): string {
    if (!w) return '';
    return w.kind === 'personal' ? 'Personal' : w.role === 'Owner' ? 'Company' : 'Tax agent access (read-only)';
  }

  protected switchTo(w: Workspace): void {
    this.wsMenuOpen.set(false);
    this.answer.set('');
    void this.router.navigateByUrl(w.kind === 'personal' ? '/p/overview' : `/c/${w.id}/dashboard`);
  }

  private async refreshCounts(): Promise<void> {
    const w = this.current();
    if (w?.kind !== 'company') { this.pending.set(0); return; }
    try { this.pending.set((await this.api.get<BankingOverview>(`/companies/${w.id}/banking`)).pending); }
    catch { /* the badge is optional */ }
  }

  protected onKey(e: KeyboardEvent): void {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); this.openSearch(); }
    if (e.key === 'Escape') { this.searchOpen.set(false); this.askOpen.set(false); this.passwordOpen.set(false); }
  }

  protected openSearch(): void {
    this.query.set('');
    this.hits.set([]);
    this.searchOpen.set(true);
  }

  protected onQuery(value: string): void {
    this.query.set(value);
    clearTimeout(this.searchTimer);
    if (value.trim().length < 2) { this.hits.set([]); return; }
    this.searching.set(true);
    this.searchTimer = setTimeout(async () => {
      const w = this.current();
      try {
        const url = w?.kind === 'company' ? `/companies/${w.id}/search` : '/personal/search';
        this.hits.set(await this.api.get<SearchHit[]>(url, { q: value.trim() }));
      } catch (e) { this.toasts.error(errorText(e)); }
      finally { this.searching.set(false); }
    }, 250);
  }

  protected go(h: SearchHit): void {
    this.searchOpen.set(false);
    const w = this.current();
    if (w?.kind === 'personal' && h.link === 'receipts' && h.id) { void this.router.navigateByUrl(`/p/receipts/${h.id}`); return; }
    const base = w?.kind === 'company' ? `/c/${w.id}` : '/p';
    void this.router.navigate([`${base}/${h.link}`], { queryParams: h.id ? { focus: h.id } : {} });
  }

  protected async ask(): Promise<void> {
    const w = this.current();
    this.asking.set(true);
    this.answer.set('');
    try {
      const url = w?.kind === 'company' ? `/companies/${w.id}/ask` : '/personal/ask';
      this.answer.set((await this.api.post<AskResponse>(url, { question: this.question })).answer);
    } catch (e) { this.toasts.error(errorText(e)); }
    finally { this.asking.set(false); }
  }

  protected async changePassword(): Promise<void> {
    try {
      await this.auth.changePassword(this.password.current, this.password.new);
      this.passwordOpen.set(false);
      this.password = { current: '', new: '' };
      this.toasts.show('Password changed. Other devices have been signed out.');
    } catch (e) { this.toasts.error(errorText(e)); }
  }
}
