import { HttpInterceptorFn } from '@angular/common/http';

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

export const csrfInterceptor: HttpInterceptorFn = (req, next) => {
  // Relative /api only. An absolute URL could be a third-party host; the header would tell it
  // nothing useful and would force a CORS preflight on calls we do not own.
  if (UNSAFE_METHODS.has(req.method) && req.url.startsWith('/api/')) {
    req = req.clone({ setHeaders: { 'X-CSRF': '1' } });
  }
  return next(req);
};
