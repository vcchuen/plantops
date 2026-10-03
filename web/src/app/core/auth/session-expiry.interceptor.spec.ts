import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { BROWSER_LOCATION } from './browser-location';
import { sessionExpiryInterceptor } from './session-expiry.interceptor';
import { SessionStore } from './session.store';

describe('sessionExpiryInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let assign: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    assign = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([sessionExpiryInterceptor])),
        provideHttpClientTesting(),
        { provide: BROWSER_LOCATION, useValue: { assign } },
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('401 on an API call marks the session anonymous and goes to login with the current URL', () => {
    vi.spyOn(TestBed.inject(Router), 'url', 'get').mockReturnValue('/assets?page=2');
    const errors: unknown[] = [];
    client.get('/api/assets').subscribe({ error: (e) => errors.push(e) });
    http.expectOne('/api/assets').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(assign).toHaveBeenCalledWith('/api/identity/login?returnUrl=%2Fassets%3Fpage%3D2');
    expect(TestBed.inject(SessionStore).status()).toBe('anonymous');
    expect(errors.length).toBe(1); // the caller still sees the error
  });

  it('401 on /api/identity/me does not navigate (no redirect loop)', () => {
    client.get('/api/identity/me').subscribe({ error: () => undefined });
    http.expectOne('/api/identity/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(assign).not.toHaveBeenCalled();
  });

  it('403 is left to the page', () => {
    client.get('/api/assets').subscribe({ error: () => undefined });
    http.expectOne('/api/assets').flush(null, { status: 403, statusText: 'Forbidden' });
    expect(assign).not.toHaveBeenCalled();
  });
});
