import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService, errorText } from '../../core/api/api.service';
import { Me, Workspace } from '../../core/api/models';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/ui/toast.service';
import { CompanyForm, emptyCompany } from './company-form';

/** Sends a signed-in user to their first workspace. */
@Component({ selector: 'k-home', template: '' })
export class HomeRedirect implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  ngOnInit(): void { void this.router.navigateByUrl(this.auth.homeUrl(), { replaceUrl: true }); }
}

/** Add a company, or turn on the personal workspace. */
@Component({
  selector: 'k-welcome',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, CompanyForm],
  template: `
<div class="auth-page">
  <div class="stack" style="width: min(640px, 100%)">
    <div class="logo" style="padding: 0"><b>K</b>Kira</div>
    @if (!auth.me()?.usesPersonal) {
      <div class="card pad row wrap">
        <div class="grow"><h2>Personal workspace</h2><p class="caption" style="margin: 4px 0 0">Track your own accounts, receipts and tax reliefs. Private to you.</p></div>
        <button type="button" class="btn" (click)="enablePersonal()">Turn on</button>
      </div>
    }
    <form class="card pad stack" (ngSubmit)="create()">
      <div><h1 style="font-size: 22px">Add a company</h1><p class="muted" style="margin: 6px 0 0">You'll be the owner. You can give your tax agent access later.</p></div>
      <k-company-form [value]="company" />
      <div class="row">
        <span class="grow"></span>
        @if ((auth.me()?.workspaces ?? []).length) { <a class="btn" [routerLink]="auth.homeUrl()">Cancel</a> }
        <button type="submit" class="btn primary" [disabled]="busy()">{{ busy() ? 'Creating…' : 'Create company' }}</button>
      </div>
    </form>
  </div>
</div>`
})
export class WelcomePage {
  protected readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);
  protected readonly company = emptyCompany();
  protected readonly busy = signal(false);

  protected async enablePersonal(): Promise<void> {
    try {
      this.auth.me.set(await this.api.post<Me>('/me/personal'));
      await this.router.navigateByUrl('/p/overview');
    } catch (e) { this.toasts.error(errorText(e)); }
  }

  protected async create(): Promise<void> {
    if (!this.company.name.trim()) { this.toasts.error('Enter the company name.'); return; }
    this.busy.set(true);
    try {
      const w = await this.api.post<Workspace>('/me/companies', this.company);
      await this.auth.reloadMe();
      await this.router.navigateByUrl(`/c/${w.id}/dashboard`);
    } catch (e) { this.toasts.error(errorText(e)); }
    finally { this.busy.set(false); }
  }
}
