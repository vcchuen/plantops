import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { BROWSER_LOCATION } from './browser-location';
import { SessionStore } from './session.store';

describe('SessionStore', () => {
  let http: HttpTestingController;
  let assign: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    assign = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_LOCATION, useValue: { assign } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('load() authenticates with name and roles, and only asks once', async () => {
    const store = TestBed.inject(SessionStore);
    const first = store.load();
    const second = store.load();
    http
      .expectOne('/api/identity/me')
      .flush({ name: 'Sam Supervisor', email: null, roles: ['supervisor'] });
    await Promise.all([first, second]);
    await store.load(); // already known: no new request (verify() in afterEach)

    expect(store.status()).toBe('authenticated');
    expect(store.isAuthenticated()).toBe(true);
    expect(store.displayName()).toBe('Sam Supervisor');
    expect(store.roles()).toEqual(['supervisor']);
    expect(store.hasRole('supervisor')).toBe(true);
    expect(store.hasRole('admin')).toBe(false);
    expect(store.isSupervisorOrAdmin()).toBe(true);
  });

  it('isSupervisorOrAdmin is false for an operator', async () => {
    const store = TestBed.inject(SessionStore);
    const done = store.load();
    http.expectOne('/api/identity/me').flush({ name: 'Olive', email: null, roles: ['operator'] });
    await done;
    expect(store.isSupervisorOrAdmin()).toBe(false);
  });

  it('401 makes the session anonymous', async () => {
    const store = TestBed.inject(SessionStore);
    const done = store.load();
    http
      .expectOne('/api/identity/me')
      .flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });
    await done;

    expect(store.status()).toBe('anonymous');
    expect(store.isAuthenticated()).toBe(false);
  });

  it('a server error stays unknown and can be retried', async () => {
    const store = TestBed.inject(SessionStore);
    const done = store.load();
    http.expectOne('/api/identity/me').flush('boom', { status: 500, statusText: 'Server Error' });
    await done;
    expect(store.status()).toBe('unknown');

    const retry = store.load();
    http.expectOne('/api/identity/me').flush({ name: 'A', email: null, roles: [] });
    await retry;
    expect(store.status()).toBe('authenticated');
  });

  it('login() navigates with an encoded returnUrl', () => {
    TestBed.inject(SessionStore).login('/assets?lineId=l1&page=2');
    expect(assign).toHaveBeenCalledWith(
      '/api/identity/login?returnUrl=%2Fassets%3FlineId%3Dl1%26page%3D2',
    );
  });

  it('logout() posts with the CSRF header and navigates to the IdP logout URL', async () => {
    const store = TestBed.inject(SessionStore);
    const done = store.logout();
    const req = http.expectOne('/api/identity/logout');
    expect(req.request.method).toBe('POST');
    // The interceptor is not registered in this test, so the header is covered in csrf.interceptor.spec.
    req.flush({ logoutUrl: 'https://idp.example/logout?x=1' });
    await done;

    expect(assign).toHaveBeenCalledWith('https://idp.example/logout?x=1');
    expect(store.status()).toBe('anonymous');
  });
});
