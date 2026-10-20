import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Note: inject() only works before the first await, so both services are taken up front.

/** Allows the route when signed in (restoring the session from the refresh cookie if needed). */
export const authGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.signedIn() || await auth.refresh()) return true;
  return router.parseUrl('/login');
};

/** Sends signed-in users away from the login and register pages. */
export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (auth.signedIn() || await auth.refresh()) return router.parseUrl(auth.homeUrl());
  return true;
};
