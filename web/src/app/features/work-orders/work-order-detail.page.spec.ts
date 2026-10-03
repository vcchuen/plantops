import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { WorkOrderDetailPage } from './work-order-detail.page';
import { WorkOrderAction, WorkOrderDetail } from './work-orders.models';

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
  reportedByName: 'Olivia Operator',
  approvedByName: null,
  assigneeId: 't1',
  assigneeName: 'Tom Tech',
  resolution: null,
  rejectionReason: null,
  cancellationReason: null,
  allowedActions: ['start', 'cancel'],
  ...over,
});

const history = [
  {
    eventType: 'WorkOrderAssigned',
    actorName: 'Sam Supervisor',
    occurredAt: '2026-10-03T13:00:00Z',
    payload: { assigneeName: 'Tom Tech' },
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

  function create() {
    fixture = TestBed.createComponent(WorkOrderDetailPage);
    el = fixture.nativeElement;
    fixture.componentRef.setInput('id', 'w1');
    fixture.detectChanges();
  }

  function load(body: WorkOrderDetail = detail(), etag = '"AAAAAAAB"') {
    http.expectOne(detailUrl).flush(body, { headers: { ETag: etag } });
    http.expectOne(historyUrl).flush(history);
  }

  // The command's finally-block starts a reload, which counts as pending work, so whenStable()
  // would never settle before we flush it. Yield one macrotask instead.
  const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

  const buttons = () =>
    Array.from(el.querySelectorAll('.actions button')).map((b) => b.textContent?.trim());

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkOrderDetailPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
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

  it('renders history entries', async () => {
    create();
    load();
    await fixture.whenStable();

    const items = el.querySelectorAll('ol.timeline li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Work order assigned');
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

  it('does not show a countdown for a settled SLA', async () => {
    create();
    load(detail({ slaState: 'Met', status: 'Closed', allowedActions: [] }));
    await fixture.whenStable();
    expect(el.querySelector('.countdown')).toBeNull();
    expect(el.querySelector('app-sla-badge')?.textContent).toContain('Met');
  });
});
