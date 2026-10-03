import { inject } from '@angular/core';
import { CanMatchFn, Router, UrlSegment } from '@angular/router';
import { SessionStore } from './session.store';

// CanMatch, not CanActivate: it runs before the lazy chunk for the route is downloaded, so an
// anonymous visitor never fetches code for pages they cannot use.
export const authGuard: CanMatchFn = async (_route, segments: UrlSegment[]) => {
  const store = inject(SessionStore);
  const router = inject(Router);

  await store.load();
  if (store.isAuthenticated()) return true;

  if (store.status() === 'anonymous') {
    // Full target incl. query string (filters). The current URL is still the previous page here.
    const target =
      router.getCurrentNavigation()?.extractedUrl.toString() ??
      '/' + segments.map((s) => s.path).join('/');
    store.login(target);
    return false;
  }

  // Status still 'unknown': /me failed for a non-401 reason. Redirecting to login could loop,
  // so fall back to the public page.
  return router.parseUrl('/status');
};
