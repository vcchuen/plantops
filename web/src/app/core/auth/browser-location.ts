import { InjectionToken } from '@angular/core';

// Wrapper so tests can assert navigation: jsdom's window.location.assign logs "not implemented".
export const BROWSER_LOCATION = new InjectionToken<Pick<Location, 'assign'>>('BROWSER_LOCATION', {
  providedIn: 'root',
  factory: () => window.location,
});
