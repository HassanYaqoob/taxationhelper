import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Allows the route when signed in (restoring the session from the refresh cookie if needed). */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  if (auth.signedIn() || await auth.refresh()) return true;
  return inject(Router).parseUrl('/login');
};

/** Sends signed-in users away from the login and register pages. */
export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  if (auth.signedIn() || await auth.refresh()) return inject(Router).parseUrl(auth.homeUrl());
  return true;
};
