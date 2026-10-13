import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { API_BASE } from '../api/api.service';
import { AuthService } from './auth.service';

/** Adds the bearer token to API calls. On a 401 it refreshes the session once and retries; if that fails, it signs out. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  if (!req.url.startsWith(API_BASE)) return next(req);
  const auth = inject(AuthService);
  const router = inject(Router);
  const isAuthCall = req.url.startsWith(`${API_BASE}/auth/`);
  const withToken = (token: string | null) => token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

  return next(withToken(auth.accessToken())).pipe(
    catchError((err: unknown) => {
      if (!(err instanceof HttpErrorResponse) || err.status !== 401 || isAuthCall) return throwError(() => err);
      return from(auth.refresh()).pipe(switchMap(ok => {
        if (!ok) {
          void router.navigateByUrl('/login');
          return throwError(() => err);
        }
        return next(withToken(auth.accessToken()));
      }));
    })
  );
};
