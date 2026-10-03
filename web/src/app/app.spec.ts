import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { routes } from './app.routes';
import { BROWSER_LOCATION } from './core/auth/browser-location';

describe('App shell', () => {
  let http: HttpTestingController;
  const assign = vi.fn();

  beforeEach(() => {
    assign.mockReset();
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: BROWSER_LOCATION, useValue: { assign } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function render(me: () => { body: object | null; status?: number }) {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const { body, status = 200 } = me();
    http
      .expectOne('/api/identity/me')
      .flush(body, { status, statusText: status === 200 ? 'OK' : 'Unauthorized' });
    // The store settles through a promise chain that whenStable() does not track.
    await new Promise((resolve) => setTimeout(resolve));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the toolbar title, landmarks and the nav links', async () => {
    const el = await render(() => ({ body: null, status: 401 }));

    expect(el.querySelector('header')?.textContent).toContain('PlantOps');
    expect(el.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('System status');
    expect(el.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Assets');
    expect(el.querySelector('main#main-content')).not.toBeNull();
    expect(el.querySelector('.skip-link')).not.toBeNull();
  });

  it('shows the name, roles and a Sign out button when authenticated', async () => {
    const el = await render(() => ({
      body: { name: 'Sam Supervisor', email: null, roles: ['supervisor'] },
    }));
    const header = el.querySelector('header')!;

    expect(header.textContent).toContain('Sam Supervisor · supervisor');
    const button = [...header.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('Sign out'),
    );
    expect(button).toBeDefined();
    expect(header.textContent).not.toContain('Sign in');
  });

  it.each([
    ['supervisor', true],
    ['admin', true],
    ['technician', false],
  ])('role %s sees the Reports nav link: %s', async (role, visible) => {
    const el = await render(() => ({ body: { name: 'U', email: null, roles: [role] } }));
    expect(el.querySelector('nav a[href="/reports"]') !== null).toBe(visible);
  });

  it('hides the Reports nav link when anonymous', async () => {
    const el = await render(() => ({ body: null, status: 401 }));
    expect(el.querySelector('nav a[href="/reports"]')).toBeNull();
  });

  it('shows Sign in when anonymous and starts login', async () => {
    const el = await render(() => ({ body: null, status: 401 }));
    const button = [...el.querySelectorAll('header button')].find((b) =>
      b.textContent?.includes('Sign in'),
    ) as HTMLButtonElement;

    expect(button).toBeDefined();
    expect(el.querySelector('header')?.textContent).not.toContain('Sign out');
    button.click();
    expect(assign).toHaveBeenCalledWith(expect.stringContaining('/api/identity/login?returnUrl='));
  });
});
