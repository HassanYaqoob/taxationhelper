import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { errorText } from '../../core/api/api.service';
import { AuthService } from '../../core/auth/auth.service';

@Component({
  selector: 'k-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  template: `
<div class="auth-page">
  <div style="width: min(420px, 100%)">
    <div class="logo" style="justify-content: center; font-size: 24px; margin-bottom: 18px"><b>K</b>Kira</div>
    <form class="card pad stack" style="padding: 28px" (ngSubmit)="submit()">
      <div>
        <h1 style="font-size: 24px">Welcome back</h1>
        <p class="muted" style="margin: 6px 0 0">Your money and your company's books, in one place.</p>
      </div>
      @if (error()) { <div class="banner wa flush" role="alert">{{ error() }}</div> }
      <label class="field">Email<input class="input" type="email" name="email" [(ngModel)]="email" autocomplete="username" required></label>
      <label class="field">Password<input class="input" type="password" name="password" [(ngModel)]="password" autocomplete="current-password" required></label>
      <button type="submit" class="btn primary" style="min-height: 44px" [disabled]="busy()">{{ busy() ? 'Signing in…' : 'Sign in' }}</button>
      <p class="caption" style="margin: 0; text-align: center">New to Kira? <a routerLink="/register">Create an account</a></p>
    </form>
    <div class="card pad" style="margin-top: 14px">
      <h3>Demo logins</h3>
      <p class="caption" style="margin: 4px 0 10px">Available when the server runs with demo data turned on (Seed:Demo).</p>
      <div class="row wrap">
        <button type="button" class="btn sm" (click)="demo('hassan@demo.kira')">Owner: Hassan</button>
        <button type="button" class="btn sm" (click)="demo('sarah@demo.kira')">Tax agent: Sarah</button>
      </div>
    </div>
  </div>
</div>`
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  protected email = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected demo(email: string): void {
    this.email = email;
    this.password = 'Demo12345!';
    void this.submit();
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.login(this.email, this.password);
      await this.router.navigateByUrl(this.auth.homeUrl());
    } catch (e) {
      this.error.set(errorText(e));
    } finally {
      this.busy.set(false);
    }
  }
}
