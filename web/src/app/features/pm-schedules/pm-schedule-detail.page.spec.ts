import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { SessionStore } from '../../core/auth/session.store';
import { PmScheduleDetailPage } from './pm-schedule-detail.page';
import { PmScheduleDetail } from './pm-schedules.models';

const detail = (over: Partial<PmScheduleDetail> = {}): PmScheduleDetail => ({
  id: 'pm1',
  assetId: 'a1',
  assetTag: 'SMT-001',
  assetName: 'Pick and place',
  title: 'Lubricate rails',
  instructions: 'Use grease type 2',
  intervalDays: 30,
  leadDays: 3,
  priority: 'P2',
  nextDueOn: '2026-10-20',
  isActive: true,
  ...over,
});

describe('PmScheduleDetailPage', () => {
  let fixture: ComponentFixture<PmScheduleDetailPage>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const supervisor = signal(false);
  const url = '/api/pm-schedules/pm1';

  function create() {
    fixture = TestBed.createComponent(PmScheduleDetailPage);
    el = fixture.nativeElement;
    fixture.componentRef.setInput('id', 'pm1');
    fixture.detectChanges();
  }

  async function load(body = detail(), etag = '"AAAAAAAB"') {
    create();
    http.expectOne(url).flush(body, { headers: { ETag: etag } });
    await fixture.whenStable();
    fixture.detectChanges();
  }

  // The command's finally-block starts a reload, so whenStable() would never settle before we
  // flush it. Yield one macrotask instead.
  const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

  const actionButtons = () =>
    Array.from(el.querySelectorAll('.actions button')).map((b) => b.textContent?.trim());

  beforeEach(() => {
    supervisor.set(false);
    TestBed.configureTestingModule({
      imports: [PmScheduleDetailPage],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SessionStore, useValue: { isSupervisorOrAdmin: supervisor } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('renders the summary with status as text', async () => {
    await load();
    expect(el.querySelector('h1')?.textContent).toContain('Lubricate rails');
    const text = el.querySelector('dl')?.textContent ?? '';
    expect(text).toContain('SMT-001 — Pick and place');
    expect(text).toContain('Every 30 days');
    expect(text).toContain('Oct 20, 2026');
    expect(text).toContain('Use grease type 2');
    expect(el.querySelector('.status')?.textContent).toContain('Active');
  });

  it('is read-only for non-supervisors', async () => {
    await load();
    expect(el.querySelector('.actions')).toBeNull();
    expect(el.querySelector('a[href="/pm-schedules/pm1/edit"]')).toBeNull();
  });

  it('offers Edit and Deactivate to supervisors on an active schedule', async () => {
    supervisor.set(true);
    await load();
    expect(el.querySelector('a[href="/pm-schedules/pm1/edit"]')?.textContent).toContain('Edit');
    expect(actionButtons()).toEqual(['Deactivate']);
  });

  it('offers Activate on an inactive schedule', async () => {
    supervisor.set(true);
    await load(detail({ isActive: false }));
    expect(actionButtons()).toEqual(['Activate']);
    expect(el.querySelector('.status')?.textContent).toContain('Inactive');
  });

  it('deactivates with If-Match from the GET ETag, then reloads', async () => {
    supervisor.set(true);
    await load(detail(), '"AAAAAAAB"');

    (el.querySelector('.actions button') as HTMLButtonElement).click();
    const req = http.expectOne(`${url}/deactivate`);
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"AAAAAAAB"');
    req.flush(null, { status: 204, statusText: 'No Content', headers: { ETag: '"AAAAAAAC"' } });
    await settle();

    http
      .expectOne(url)
      .flush(detail({ isActive: false }), { headers: { ETag: '"AAAAAAAC"' } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(actionButtons()).toEqual(['Activate']);
    expect(el.querySelector('[role="status"]')?.textContent).toContain('Schedule deactivated');
  });

  it('activates with If-Match', async () => {
    supervisor.set(true);
    await load(detail({ isActive: false }), '"BBBB"');

    (el.querySelector('.actions button') as HTMLButtonElement).click();
    const req = http.expectOne(`${url}/activate`);
    expect(req.request.headers.get('If-Match')).toBe('"BBBB"');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    http.expectOne(url).flush(detail(), { headers: { ETag: '"CCCC"' } });
  });

  it('on 412 shows a snackbar and reloads the latest version', async () => {
    const open = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    supervisor.set(true);
    await load();

    (el.querySelector('.actions button') as HTMLButtonElement).click();
    http
      .expectOne(`${url}/deactivate`)
      .flush(null, { status: 412, statusText: 'Precondition Failed' });
    await settle();

    expect(open).toHaveBeenCalledWith(
      'Someone else changed this schedule — showing the latest version.',
      'Dismiss',
      expect.anything(),
    );
    http.expectOne(url).flush(detail({ isActive: false }), { headers: { ETag: '"NEW"' } });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(actionButtons()).toEqual(['Activate']);
    expect(el.querySelector('.error[role="alert"]')).toBeNull();
  });

  it('on 403 shows an alert', async () => {
    supervisor.set(true);
    await load();

    (el.querySelector('.actions button') as HTMLButtonElement).click();
    http
      .expectOne(`${url}/deactivate`)
      .flush({ title: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });
    await settle();
    http.expectOne(url).flush(detail(), { headers: { ETag: '"AAAAAAAB"' } });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      'Only supervisors and admins',
    );
  });

  it('shows not found on 404', async () => {
    create();
    http.expectOne(url).flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    expect(el.textContent).toContain('PM schedule not found');
  });
});
