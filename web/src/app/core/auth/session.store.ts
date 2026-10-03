import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { firstValueFrom } from 'rxjs';
import { BROWSER_LOCATION } from './browser-location';

export type Role = 'operator' | 'technician' | 'supervisor' | 'admin';

export interface SessionUser {
  name: string;
  email: string | null;
  roles: Role[];
}

export type SessionStatus = 'unknown' | 'anonymous' | 'authenticated';

interface SessionState {
  user: SessionUser | null;
  status: SessionStatus;
}

const initialState: SessionState = { user: null, status: 'unknown' };

export const SessionStore = signalStore(
  { providedIn: 'root' },
  withState(initialState),
  withComputed(({ user, status }) => ({
    isAuthenticated: computed(() => status() === 'authenticated'),
    displayName: computed(() => user()?.name ?? ''),
    roles: computed(() => user()?.roles ?? []),
    isSupervisorOrAdmin: computed(() => {
      const roles = user()?.roles ?? [];
      return roles.includes('supervisor') || roles.includes('admin');
    }),
  })),
  withMethods((store) => {
    const http = inject(HttpClient);
    const location = inject(BROWSER_LOCATION);
    // Shell and guard both call load() at startup; share one request.
    let inflight: Promise<void> | null = null;

    return {
      hasRole: (role: Role): boolean => store.roles().includes(role),

      // One-shot request/response, so firstValueFrom + async/await reads better than rxMethod
      // (which is for long-lived streams of triggers).
      load(): Promise<void> {
        if (store.status() !== 'unknown') return Promise.resolve();
        inflight ??= firstValueFrom(http.get<SessionUser>('/api/identity/me'))
          .then((user) => patchState(store, { user, status: 'authenticated' }))
          .catch((error: unknown) => {
            if (error instanceof HttpErrorResponse && error.status === 401) {
              patchState(store, { user: null, status: 'anonymous' });
            }
            // Any other failure (network, 5xx) stays 'unknown', not 'anonymous': we do not know
            // the user is signed out, and treating it as such would make the guard bounce the
            // browser to the IdP and back in a loop while the API is unhealthy.
          })
          .finally(() => (inflight = null));
        return inflight;
      },

      // Called by the 401 interceptor: the cookie expired, so what we believed is stale.
      expire(): void {
        patchState(store, { user: null, status: 'anonymous' });
      },

      login(returnUrl: string): void {
        location.assign('/api/identity/login?returnUrl=' + encodeURIComponent(returnUrl));
      },

      async logout(): Promise<void> {
        const { logoutUrl } = await firstValueFrom(
          http.post<{ logoutUrl: string }>('/api/identity/logout', null),
        );
        patchState(store, { user: null, status: 'anonymous' });
        location.assign(logoutUrl);
      },
    };
  }),
);
