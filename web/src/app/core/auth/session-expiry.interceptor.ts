import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { SessionStore } from './session.store';

export const sessionExpiryInterceptor: HttpInterceptorFn = (req, next) => {
  // Resolve lazily: SessionStore injects HttpClient, which injects this interceptor. Injecting the
  // store here eagerly would be a circular dependency. (inject() is also invalid inside catchError.)
  const injector = inject(Injector);

  return next(req).pipe(
    catchError((error: unknown) => {
      // /me answers 401 for anonymous visitors by design; reacting would redirect-loop.
      const isMe = req.url.startsWith('/api/identity/me');
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401 &&
        !isMe &&
        req.url.startsWith('/api/')
      ) {
        const store = injector.get(SessionStore);
        store.expire();
        store.login(injector.get(Router).url);
      }
      return throwError(() => error);
    }),
  );
};
