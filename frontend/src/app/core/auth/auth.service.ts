import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE, ApiService } from '../api/api.service';
import { AuthResponse, CompanySetup, Me } from '../api/models';

/**
 * Holds the short-lived access token in memory only. The long-lived refresh token lives in an HttpOnly cookie that
 * JavaScript can't read; calling /auth/refresh exchanges it for a new access token (and rotates it).
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private refreshing: Promise<boolean> | null = null;

  readonly accessToken = signal<string | null>(null);
  readonly me = signal<Me | null>(null);
  readonly signedIn = computed(() => this.me() !== null);

  async login(email: string, password: string): Promise<void> {
    this.accept(await this.api.post<AuthResponse>('/auth/login', { email, password }));
  }

  async register(name: string, email: string, password: string, personal: boolean, company: CompanySetup | null): Promise<void> {
    this.accept(await this.api.post<AuthResponse>('/auth/register', { name, email, password, personal, company }));
  }

  /** Called at start-up and when an access token expires. Concurrent callers share one request. */
  refresh(): Promise<boolean> {
    this.refreshing ??= firstValueFrom(this.http.post<AuthResponse>(`${API_BASE}/auth/refresh`, {}))
      .then(r => { this.accept(r); return true; })
      .catch(() => { this.clear(); return false; })
      .finally(() => { this.refreshing = null; });
    return this.refreshing;
  }

  /** Changes the password. The server signs out other devices and returns a new session for this one. */
  async changePassword(current: string, next: string): Promise<void> {
    this.accept(await this.api.post<AuthResponse>('/me/password', { current, new: next }));
  }

  async reloadMe(): Promise<void> {
    this.me.set(await this.api.get<Me>('/me'));
  }

  async logout(): Promise<void> {
    try { await firstValueFrom(this.http.post(`${API_BASE}/auth/logout`, {})); } catch { /* signing out locally is enough */ }
    this.clear();
    await this.router.navigateByUrl('/login');
  }

  /** Where to go after signing in. */
  homeUrl(): string {
    const ws = this.me()?.workspaces ?? [];
    const first = ws.find(w => w.kind === 'company' && w.role === 'Owner') ?? ws.find(w => w.kind === 'personal') ?? ws[0];
    if (!first) return '/welcome';
    return first.kind === 'personal' ? '/p/overview' : `/c/${first.id}/dashboard`;
  }

  clear(): void {
    this.accessToken.set(null);
    this.me.set(null);
  }

  private accept(r: AuthResponse): void {
    this.accessToken.set(r.accessToken);
    this.me.set(r.me);
  }
}
