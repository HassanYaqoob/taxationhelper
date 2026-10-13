import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorText } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { Icon } from '../../core/ui/icon';
import { CompanyForm, emptyCompany } from './company-form';

type Use = 'personal' | 'company' | 'both';

@Component({
  selector: 'k-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, Icon, CompanyForm],
  template: `
<div class="auth-page">
  <form class="card pad stack" style="width: min(620px, 100%); padding: 28px" (ngSubmit)="next()">
    <div class="logo" style="padding: 0"><b>K</b>Kira</div>
    @switch (step()) {
      @case (1) {
        <div><h1 style="font-size: 24px">Create your account</h1><p class="muted" style="margin: 6px 0 0">Step 1 of {{ steps() }}</p></div>
        <label class="field">Your name<input class="input" name="name" [(ngModel)]="name" autocomplete="name" required></label>
        <label class="field">Email<input class="input" type="email" name="email" [(ngModel)]="email" autocomplete="email" required></label>
        <label class="field">Password<input class="input" type="password" name="password" [(ngModel)]="password" autocomplete="new-password" required>
          <span class="hint">At least 10 characters. A short sentence is easy to remember and hard to guess.</span></label>
      }
      @case (2) {
        <div><h1 style="font-size: 24px">What will you use Kira for?</h1><p class="muted" style="margin: 6px 0 0">Step 2 of {{ steps() }}. You can add the other later.</p></div>
        <div class="stack" style="gap: 10px" role="radiogroup" aria-label="Use">
          @for (o of options; track o.value) {
            <button type="button" role="radio" class="card list-row click" style="padding: 16px; border-top: 1px solid var(--line)"
              [attr.aria-checked]="use() === o.value" [style.borderColor]="use() === o.value ? 'var(--accent)' : ''" (click)="use.set(o.value)">
              <span class="wsav" [class.personal]="o.value !== 'company'" [class.company]="o.value === 'company'"><k-icon [name]="o.icon" [size]="16" /></span>
              <span class="grow"><b>{{ o.title }}</b><span class="caption" style="display: block">{{ o.sub }}</span></span>
              @if (use() === o.value) { <k-icon name="check" /> }
            </button>
          }
        </div>
      }
      @case (3) {
        <div><h1 style="font-size: 24px">Your company</h1><p class="muted" style="margin: 6px 0 0">Step 3 of 3. Sdn. Bhd. details and the bank account you'll upload statements for.</p></div>
        <k-company-form [value]="company" />
      }
    }
    @if (error()) { <div class="banner wa flush" role="alert">{{ error() }}</div> }
    <div class="row">
      @if (step() > 1) { <button type="button" class="btn" (click)="step.set(step() - 1)">Back</button> }
      <span class="grow"></span>
      <button type="submit" class="btn primary" [disabled]="busy()">{{ isLast() ? (busy() ? 'Creating…' : 'Create account') : 'Continue' }}</button>
    </div>
    <p class="caption" style="margin: 0">Already have an account? <a routerLink="/login">Sign in</a></p>
  </form>
</div>`
})
export class RegisterPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected readonly step = signal(1);
  protected readonly use = signal<Use>('both');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly steps = computed(() => this.use() === 'personal' ? 2 : 3);
  protected readonly isLast = computed(() => this.step() === 3 || (this.step() === 2 && this.use() === 'personal'));
  protected name = '';
  protected email = '';
  protected password = '';
  protected readonly company = emptyCompany();
  protected readonly options: { value: Use; title: string; sub: string; icon: string }[] = [
    { value: 'personal', title: 'Personal finances', sub: 'Accounts, receipts, spending and tax reliefs', icon: 'home' },
    { value: 'company', title: 'My company', sub: 'Upload monthly bank statements; Kira does the books and tax estimate', icon: 'bank' },
    { value: 'both', title: 'Both', sub: 'Kept completely separate, switch with one click', icon: 'grid' }
  ];

  protected async next(): Promise<void> {
    this.error.set('');
    if (this.step() === 1) {
      if (!this.name.trim() || !this.email.includes('@') || this.password.length < 10) {
        this.error.set('Fill in your name, a valid email and a password of at least 10 characters.');
        return;
      }
      this.step.set(2);
      return;
    }
    if (!this.isLast()) { this.step.set(3); return; }
    if (this.use() !== 'personal' && !this.company.name.trim()) { this.error.set('Enter the company name.'); return; }
    this.busy.set(true);
    try {
      await this.auth.register(this.name, this.email, this.password, this.use() !== 'company', this.use() === 'personal' ? null : this.company);
      await this.router.navigateByUrl(this.auth.homeUrl());
    } catch (e) {
      this.error.set(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }
}
