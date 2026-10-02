import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { StatusPage } from './status.page';
import { HealthReport } from './status.models';

const healthy: HealthReport = {
  status: 'Healthy',
  totalDurationMs: 12,
  checks: [{ name: 'sqlserver', status: 'Healthy', description: null, durationMs: 11 }],
};

const unhealthy: HealthReport = {
  status: 'Unhealthy',
  totalDurationMs: 30,
  checks: [
    { name: 'sqlserver', status: 'Unhealthy', description: 'Cannot connect', durationMs: 29 },
  ],
};

describe('StatusPage', () => {
  let fixture: ComponentFixture<StatusPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [StatusPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StatusPage);
    el = fixture.nativeElement;
    // httpResource sends its request from an effect, so run change detection once to fire it.
    // Do NOT await whenStable() here: the in-flight request counts as pending work, so it would never settle.
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('renders a healthy report on 200', async () => {
    http.expectOne('/health/ready').flush(healthy);
    await fixture.whenStable();

    expect(el.querySelector('.overall')?.textContent).toContain('Healthy');
    expect(el.querySelectorAll('tr[mat-row]').length).toBe(1);
    expect(el.textContent).toContain('sqlserver');
    expect(el.textContent).not.toContain('API unreachable');
  });

  it('renders the unhealthy report carried in a 503 body', async () => {
    http
      .expectOne('/health/ready')
      .flush(unhealthy, { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();

    expect(el.querySelector('.overall')?.textContent).toContain('Unhealthy');
    expect(el.textContent).toContain('Cannot connect');
    expect(el.textContent).not.toContain('API unreachable');
  });

  it('shows "API unreachable" on a network error', async () => {
    http.expectOne('/health/ready').error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(el.textContent).toContain('API unreachable');
    expect(el.querySelector('table')).toBeNull();
  });

  it('shows "API unreachable" when a 503 body is not a health report', async () => {
    http
      .expectOne('/health/ready')
      .flush('<html>Bad gateway</html>', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();

    expect(el.textContent).toContain('API unreachable');
  });
});
