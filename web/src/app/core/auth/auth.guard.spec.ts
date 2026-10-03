import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Route, Router, UrlSegment, UrlTree } from '@angular/router';
import { authGuard } from './auth.guard';
import { BROWSER_LOCATION } from './browser-location';

describe('authGuard', () => {
  let http: HttpTestingController;
  let assign: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    assign = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_LOCATION, useValue: { assign } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const run = () => {
    const result = TestBed.runInInjectionContext(() =>
      authGuard({} as Route, [new UrlSegment('assets', {})], {} as never),
    );
    return result as Promise<boolean | UrlTree>;
  };

  it('lets an authenticated user through', async () => {
    const result = run();
    http.expectOne('/api/identity/me').flush({ name: 'Sam', email: null, roles: ['operator'] });
    expect(await result).toBe(true);
    expect(assign).not.toHaveBeenCalled();
  });

  it('sends an anonymous user to login and blocks the route', async () => {
    const result = run();
    http.expectOne('/api/identity/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(await result).toBe(false);
    expect(assign).toHaveBeenCalledWith('/api/identity/login?returnUrl=%2Fassets');
  });

  it('falls back to /status instead of looping when /me fails with a server error', async () => {
    const result = run();
    http.expectOne('/api/identity/me').flush('x', { status: 500, statusText: 'Server Error' });
    const outcome = await result;
    expect(outcome).toBeInstanceOf(UrlTree);
    expect(TestBed.inject(Router).serializeUrl(outcome as UrlTree)).toBe('/status');
    expect(assign).not.toHaveBeenCalled();
  });
});
