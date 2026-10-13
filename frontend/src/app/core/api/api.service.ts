import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpErrorResponse, HttpParams } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export const API_BASE = '/api/v1';

export class ApiError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

type Params = Record<string, string | number | boolean | null | undefined>;

function toParams(p?: Params): HttpParams {
  let hp = new HttpParams();
  for (const [k, v] of Object.entries(p ?? {})) if (v !== null && v !== undefined && v !== '') hp = hp.set(k, String(v));
  return hp;
}

/** Promise-based wrapper over HttpClient. Failures become ApiError with the server's user-facing message (RFC 9457 "detail"). */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);

  get<T>(url: string, params?: Params) { return this.run(firstValueFrom(this.http.get<T>(API_BASE + url, { params: toParams(params) }))); }
  post<T>(url: string, body: unknown = {}) { return this.run(firstValueFrom(this.http.post<T>(API_BASE + url, body))); }
  put<T>(url: string, body: unknown = {}) { return this.run(firstValueFrom(this.http.put<T>(API_BASE + url, body))); }
  delete<T = void>(url: string) { return this.run(firstValueFrom(this.http.delete<T>(API_BASE + url))); }
  upload<T>(url: string, form: FormData) { return this.run(firstValueFrom(this.http.post<T>(API_BASE + url, form))); }

  /** Fetches a protected file and returns an object URL for an <img>. Revoke it when done. */
  async objectUrl(url: string): Promise<string> {
    const blob = await this.run(firstValueFrom(this.http.get(API_BASE + url, { responseType: 'blob' })));
    return URL.createObjectURL(blob);
  }

  /** Downloads a protected file using the name the server suggests. */
  async download(url: string, params?: Params, fallbackName = 'download'): Promise<void> {
    const res = await this.run(firstValueFrom(this.http.get(API_BASE + url, { params: toParams(params), responseType: 'blob', observe: 'response' })));
    const disposition = res.headers.get('Content-Disposition') ?? '';
    const match = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(disposition);
    const name = match ? decodeURIComponent(match[1] ?? match[2] ?? fallbackName) : fallbackName;
    const href = URL.createObjectURL(res.body as Blob);
    const a = document.createElement('a');
    a.href = href; a.download = name;
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(href), 5000);
  }

  private async run<T>(p: Promise<T>): Promise<T> {
    try {
      return await p;
    } catch (e) {
      throw await toApiError(e);
    }
  }
}

async function toApiError(e: unknown): Promise<unknown> {
  if (!(e instanceof HttpErrorResponse)) return e;
  let body: unknown = e.error;
  if (body instanceof Blob) {
    try { body = JSON.parse(await body.text()); } catch { body = null; }
  }
  const problem = body as { detail?: string; title?: string } | null;
  const message = problem?.detail || problem?.title
    || (e.status === 0 ? 'Can\'t reach the server. Check your connection.' : e.status === 429 ? 'Too many requests. Wait a moment and try again.' : `Request failed (${e.status}).`);
  return new ApiError(message, e.status);
}

export function errorText(e: unknown): string {
  return e instanceof Error ? e.message : 'Something went wrong.';
}

export function todayIso(): string {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

export function isoDate(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
