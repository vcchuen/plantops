import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { WorkOrderDetailPage } from './work-order-detail.page';
import { signal } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';
import { SessionStore } from '../../core/auth/session.store';
import { ReservationItem } from '../inventory/inventory.models';
import { WorkOrderAction, WorkOrderDetail } from './work-orders.models';

const reservation = (over: Partial<ReservationItem> = {}): ReservationItem => ({
  id: 'r1',
  partId: 'p1',
  partNumber: 'FDR-8MM-001',
  partName: 'Feeder 8mm',
  unit: 'pcs',
  quantity: 2,
  status: 'Active',
  reservedAt: '2026-10-03T14:00:00Z',
  reservedByName: 'Tom Tech',
  ...over,
});

const detail = (over: Partial<WorkOrderDetail> = {}): WorkOrderDetail => ({
  id: 'w1',
  number: 'WO-000001',
  title: 'Conveyor jam',
  description: 'Belt keeps slipping',
  assetId: 'a1',
  assetTag: 'SMT-001',
  assetName: 'Pick and place',
  priority: 'P2',
  assetDown: true,
  status: 'Assigned',
  slaState: 'OnTrack',
  dueAt: '2026-10-03T20:00:00Z',
  submittedAt: '2026-10-03T12:00:00Z',
  approvedAt: null,
  startedAt: null,
  completedAt: null,
  closedAt: null,
  reportedBy: { id: 'u1', name: 'Olivia Operator' },
  approvedBy: null,
  assignedTo: { id: 't1', name: 'Tom Tech' },
  resolution: null,
  rejectionReason: null,
  cancellationReason: null,
  allowedActions: ['start', 'cancel'],
  source: 'Reactive',
  escalatedAt: null,
  pmScheduleId: null,
  pmDueOn: null,
  ...over,
});

const history = [
  {
    eventType: 'WorkOrderAssigned',
    actorName: 'Sam Supervisor',
    occurredAt: '2026-10-03T13:00:00Z',
    payload: { technicianId: 't1', technicianName: 'Tom Tech' },
  },
  {
    eventType: 'WorkOrderSubmitted',
    actorName: 'Olivia Operator',
    occurredAt: '2026-10-03T12:00:00Z',
    payload: null,
  },
];

describe('WorkOrderDetailPage', () => {
  let fixture: ComponentFixture<WorkOrderDetailPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const detailUrl = '/api/work-orders/w1';
  const historyUrl = '/api/work-orders/w1/history';
  const commentsUrl = '/api/work-orders/w1/comments';

  function create() {
    fixture = TestBed.createComponent(WorkOrderDetailPage);
    el = fixture.nativeElement;
    fixture.componentRef.setInput('id', 'w1');
    fixture.detectChanges();
    // The comments section has its own request; it is covered in work-order-comments.spec.ts.
    http.expectOne(commentsUrl).flush([]);
  }

  const reservationsReq = () =>
    http.expectOne(
      (r) => r.url === '/api/inventory/reservations' && r.params.get('workOrderId') === 'w1',
    );

  function load(
    body: WorkOrderDetail = detail(),
    etag = '"AAAAAAAB"',
    reservations: ReservationItem[] = [],
  ) {
    http.expectOne(detailUrl).flush(body, { headers: { ETag: etag } });
    http.expectOne(historyUrl).flush(history);
    reservationsReq().flush(reservations);
  }

  // The command's finally-block starts a reload, which counts as pending work, so whenStable()
  // would never settle before we flush it. Yield one macrotask instead.
  const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

  const buttons = () =>
    Array.from(el.querySelectorAll('.actions button')).map((b) => b.textContent?.trim());

  const supervisor = signal(false);
  const reserveButton = () =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Reserve part');

  beforeEach(() => {
    supervisor.set(false);
    TestBed.configureTestingModule({
      imports: [WorkOrderDetailPage],
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

  it('renders the summary, SLA badge and a countdown', async () => {
    create();
    load();
    await fixture.whenStable();

    expect(el.querySelector('h1')?.textContent).toContain('WO-000001');
    const text = el.querySelector('dl')?.textContent ?? '';
    expect(text).toContain('Olivia Operator');
    expect(text).toContain('Tom Tech');
    expect(text).toContain('P2 · High (8h)');
    expect(text).toContain('Machine down');
    expect(el.querySelector('app-sla-badge')?.textContent).toContain('On track');
    expect(el.querySelector('.countdown')?.textContent).toMatch(/due in|overdue by/);
  });

  it('shows the Reactive source with no PM link and no escalation banner', async () => {
    create();
    load();
    await fixture.whenStable();
    expect(el.querySelector('dl')?.textContent).toContain('Reactive');
    expect(el.querySelector('a[href^="/pm-schedules"]')).toBeNull();
    expect(el.querySelector('.escalated-banner')).toBeNull();
  });

  it('shows the PM due date and a link to the schedule for a preventive order', async () => {
    create();
    load(detail({ source: 'Preventive', pmScheduleId: 'pm1', pmDueOn: '2026-10-20' }));
    await fixture.whenStable();
    const text = el.querySelector('dl')?.textContent ?? '';
    expect(text).toContain('Preventive');
    expect(text).toContain('Oct 20, 2026');
    expect(el.querySelector('a[href="/pm-schedules/pm1"]')).not.toBeNull();
  });

  it('shows an Escalated banner as a status region when escalatedAt is set', async () => {
    create();
    load(detail({ escalatedAt: '2026-10-03T15:00:00Z' }));
    await fixture.whenStable();
    const banner = el.querySelector('.escalated-banner');
    expect(banner?.getAttribute('role')).toBe('status');
    expect(banner?.textContent).toContain('Escalated at');
  });

  it('renders history entries', async () => {
    create();
    load();
    await fixture.whenStable();

    const items = el.querySelectorAll('ol.timeline li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Assigned');
    expect(items[0].textContent).toContain('Sam Supervisor');
  });

  it('renders exactly the allowed actions and nothing else', async () => {
    create();
    load(detail({ allowedActions: ['start', 'cancel'] }));
    await fixture.whenStable();
    expect(buttons()).toEqual(['Start work', 'Cancel work order']);
  });

  it('renders no action bar when nothing is allowed', async () => {
    create();
    load(detail({ allowedActions: [] as WorkOrderAction[], status: 'Closed' }));
    await fixture.whenStable();
    expect(el.querySelector('.actions')).toBeNull();
  });

  it('sends If-Match equal to the ETag from the GET, then reloads detail and history', async () => {
    create();
    load(detail(), '"AAAAAAAB"');
    await fixture.whenStable();

    (el.querySelector('.actions button') as HTMLButtonElement).click();
    const req = http.expectOne('/api/work-orders/w1/start');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"AAAAAAAB"');
    req.flush(null, { status: 204, statusText: 'No Content', headers: { ETag: '"AAAAAAAC"' } });
    await settle();

    // The reload picks up the new state and the new ETag.
    http
      .expectOne(detailUrl)
      .flush(detail({ status: 'InProgress', allowedActions: ['complete'] }), {
        headers: { ETag: '"AAAAAAAC"' },
      });
    http.expectOne(historyUrl).flush(history);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(buttons()).toEqual(['Complete']);
  });

  it('on 412 shows a snackbar and reloads the latest version', async () => {
    const open = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    create();
    load();
    await fixture.whenStable();

    (el.querySelector('.actions button') as HTMLButtonElement).click();
    http
      .expectOne('/api/work-orders/w1/start')
      .flush(null, { status: 412, statusText: 'Precondition Failed' });
    await settle();

    expect(open).toHaveBeenCalledWith(
      'Someone else changed this work order — showing the latest version.',
      'Dismiss',
      expect.anything(),
    );
    http.expectOne(detailUrl).flush(detail({ status: 'Completed', allowedActions: ['close'] }), {
      headers: { ETag: '"NEW"' },
    });
    http.expectOne(historyUrl).flush(history);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(buttons()).toEqual(['Close']);
    expect(el.querySelector('.error[role="alert"]')).toBeNull();
  });

  it('on 403 shows the problem detail in an alert', async () => {
    create();
    load();
    await fixture.whenStable();

    (el.querySelector('.actions button') as HTMLButtonElement).click();
    http
      .expectOne('/api/work-orders/w1/start')
      .flush(
        { title: 'Forbidden', detail: 'Only the assigned technician can start work' },
        { status: 403, statusText: 'Forbidden' },
      );
    await settle();
    http.expectOne(detailUrl).flush(detail(), { headers: { ETag: '"AAAAAAAB"' } });
    http.expectOne(historyUrl).flush(history);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain(
      'Only the assigned technician can start work',
    );
  });

  it('shows not found on 404', async () => {
    create();
    http
      .expectOne(detailUrl)
      .flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    http.expectOne(historyUrl).flush(null, { status: 404, statusText: 'Not Found' });
    reservationsReq().flush([]);
    await fixture.whenStable();
    expect(el.textContent).toContain('Work order not found');
  });

  it('ticks the countdown every 60 s and flips an open order to Breached once overdue', async () => {
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'Date'] });
    try {
      vi.setSystemTime(new Date('2026-10-03T19:30:00Z'));
      create();
      load(detail({ dueAt: '2026-10-03T20:00:00Z' }));
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.querySelector('.countdown')?.textContent).toContain('due in 30m');

      vi.advanceTimersByTime(60_000);
      fixture.detectChanges();
      expect(el.querySelector('.countdown')?.textContent).toContain('due in 29m');

      vi.advanceTimersByTime(30 * 60_000);
      fixture.detectChanges();
      expect(el.querySelector('.countdown')?.textContent).toContain('overdue by 1m');
      expect(el.querySelector('app-sla-badge')?.textContent).toContain('Breached');
    } finally {
      vi.useRealTimers();
    }
  });

  it('renders a null SLA (rejected/cancelled) as "No SLA" with no countdown or flip', async () => {
    create();
    load(detail({ slaState: null, status: 'Rejected', allowedActions: [] }));
    await fixture.whenStable();
    expect(el.querySelector('app-sla-badge [aria-label="No SLA"]')).not.toBeNull();
    expect(el.querySelector('.countdown')).toBeNull();
  });

  describe('parts', () => {
    it('lists reservations with a status label', async () => {
      create();
      load(detail(), '"E"', [reservation(), reservation({ id: 'r2', status: 'Consumed' })]);
      await fixture.whenStable();
      fixture.detectChanges();

      const rows = el.querySelectorAll('table[aria-label="Reserved parts"] tbody tr');
      expect(rows.length).toBe(2);
      expect(rows[0].textContent).toContain('FDR-8MM-001');
      expect(rows[0].textContent).toContain('Reserved');
      expect(rows[1].textContent).toContain('Consumed');
    });

    it.each([
      ['assigned technician (start allowed)', 'Assigned', ['start', 'cancel'], false, true],
      ['working technician (complete allowed)', 'InProgress', ['complete'], false, true],
      ['unrelated user', 'Assigned', ['cancel'], false, false],
      ['supervisor on an assigned order', 'Assigned', ['cancel'], true, true],
      ['supervisor on a submitted order', 'Submitted', ['approve'], true, false],
      ['supervisor on a completed order', 'Completed', ['close'], true, false],
    ] as const)('reserve button: %s', async (_name, status, actions, isSupervisor, visible) => {
      supervisor.set(isSupervisor);
      create();
      load(detail({ status, allowedActions: [...actions] as WorkOrderAction[] }));
      await fixture.whenStable();
      fixture.detectChanges();
      expect(reserveButton() !== undefined).toBe(visible);
    });

    it('shows the stock note once the order is completed or closed', async () => {
      create();
      load(detail({ status: 'Completed', allowedActions: ['close'] }));
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.textContent).toContain('Stock is updated shortly after completion.');
    });

    it('reloads reservations after the reserve dialog reports success', async () => {
      create();
      load();
      await fixture.whenStable();
      fixture.detectChanges();
      const open = vi
        .spyOn(TestBed.inject(MatDialog), 'open')
        .mockReturnValue({ afterClosed: () => of(true) } as never);

      reserveButton()?.click();
      await settle();

      expect(open).toHaveBeenCalled();
      reservationsReq().flush([reservation()]);
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.querySelectorAll('table[aria-label="Reserved parts"] tbody tr').length).toBe(1);
    });

    it('does not reload when the dialog is dismissed', async () => {
      create();
      load();
      await fixture.whenStable();
      fixture.detectChanges();
      vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({
        afterClosed: () => of(undefined),
      } as never);

      reserveButton()?.click();
      await settle();
      http.expectNone((r) => r.url === '/api/inventory/reservations');
    });

    it('releases an active reservation then reloads the list', async () => {
      create();
      load(detail(), '"E"', [reservation(), reservation({ id: 'r2', status: 'Consumed' })]);
      await fixture.whenStable();
      fixture.detectChanges();

      const releaseButtons = Array.from(el.querySelectorAll('tbody button'));
      expect(releaseButtons.length).toBe(1); // only the Active one
      (releaseButtons[0] as HTMLButtonElement).click();

      const req = http.expectOne('/api/inventory/reservations/r1/release');
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      reservationsReq().flush([reservation({ status: 'Released' })]);
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.querySelector('table[aria-label="Reserved parts"]')?.textContent).toContain(
        'Released',
      );
      expect(el.querySelectorAll('tbody button').length).toBe(0);
    });

    it('shows the problem detail when release fails', async () => {
      create();
      load(detail(), '"E"', [reservation()]);
      await fixture.whenStable();
      fixture.detectChanges();

      (el.querySelector('tbody button') as HTMLButtonElement).click();
      http
        .expectOne('/api/inventory/reservations/r1/release')
        .flush(
          { title: 'Forbidden', detail: 'Not your work order' },
          { status: 403, statusText: 'Forbidden' },
        );
      await settle();
      reservationsReq().flush([reservation()]);
      await fixture.whenStable();
      fixture.detectChanges();

      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Not your work order');
    });
  });

  it('does not show a countdown for a settled SLA', async () => {
    create();
    load(detail({ slaState: 'Met', status: 'Closed', allowedActions: [] }));
    await fixture.whenStable();
    expect(el.querySelector('.countdown')).toBeNull();
    expect(el.querySelector('app-sla-badge')?.textContent).toContain('Met');
  });
});
