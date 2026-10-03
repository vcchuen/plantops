import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { WorkOrdersQueuePage } from './work-orders-queue.page';
import { QueueItem } from './work-orders.models';

const item = (over: Partial<QueueItem> = {}): QueueItem => ({
  id: 'w1',
  number: 'WO-000001',
  title: 'Conveyor jam',
  assetTag: 'SMT-001',
  priority: 'P2',
  status: 'Assigned',
  dueAt: '2026-10-03T12:00:00Z',
  slaState: 'OnTrack',
  partsReserved: 2,
  ...over,
});

describe('WorkOrdersQueuePage', () => {
  let fixture: ComponentFixture<WorkOrdersQueuePage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  // Inputs before the first detectChanges: httpResource sends its request from an effect.
  function create(inputs: Record<string, unknown> = {}) {
    fixture = TestBed.createComponent(WorkOrdersQueuePage);
    el = fixture.nativeElement;
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  const queueReq = () => http.expectOne((r) => r.url === '/api/work-orders/queue');

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkOrdersQueuePage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the whole queue by default', () => {
    create();
    const req = queueReq();
    expect(req.request.params.has('dueToday')).toBe(false);
    req.flush([]);
  });

  it('asks for dueToday=true only when the param is "true"', () => {
    create({ dueToday: 'true' });
    const req = queueReq();
    expect(req.request.params.get('dueToday')).toBe('true');
    req.flush([]);
  });

  it('omits dueToday for any other value', () => {
    create({ dueToday: 'false' });
    const req = queueReq();
    expect(req.request.params.has('dueToday')).toBe(false);
    req.flush([]);
  });

  it('renders a card per work order with number, title, asset and reserved parts', async () => {
    create();
    queueReq().flush([
      item(),
      item({
        id: 'w2',
        number: 'WO-000002',
        title: 'Feeder jam',
        assetTag: 'SMT-002',
        partsReserved: 1,
      }),
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const cards = el.querySelectorAll('app-queue-card');
    expect(cards.length).toBe(2);
    expect(cards[0].textContent).toContain('WO-000001');
    expect(cards[0].textContent).toContain('Conveyor jam');
    expect(cards[0].textContent).toContain('SMT-001');
    expect(cards[0].textContent).toContain('2 parts reserved');
    expect(cards[1].textContent).toContain('1 part reserved');
  });

  it('colours the dot by urgency', async () => {
    create();
    queueReq().flush([
      item({ id: 'w1', slaState: 'Breached' }),
      item({ id: 'w2', slaState: 'AtRisk' }),
      item({ id: 'w3', slaState: 'OnTrack' }),
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    const dots = Array.from(el.querySelectorAll('.dot')).map((d) => d.className);
    expect(dots[0]).toContain('dot-red');
    expect(dots[1]).toContain('dot-amber');
    expect(dots[2]).toContain('dot-green');
  });

  it('opens the work order when a card is clicked', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    create();
    queueReq().flush([item()]);
    await fixture.whenStable();
    fixture.detectChanges();

    (el.querySelector('app-queue-card') as HTMLElement).click();

    expect(navigate).toHaveBeenCalledWith(['/work-orders', 'w1']);
  });

  it('shows an empty state, worded for the filter', async () => {
    create({ dueToday: 'true' });
    queueReq().flush([]);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('Nothing due today');
  });

  it('shows a problem in an alert', async () => {
    create();
    queueReq().flush({ title: 'Server error', detail: 'x' }, { status: 500, statusText: 'Err' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Server error');
  });

  it('has a labelled "Due today" toggle that reflects the URL param', async () => {
    create({ dueToday: 'true' });
    queueReq().flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    const toggle = el.querySelector('mat-slide-toggle') as HTMLElement;
    expect(toggle.textContent).toContain('Due today');
    expect(
      (toggle.querySelector('button[role="switch"]') as HTMLElement).getAttribute('aria-checked'),
    ).toBe('true');
  });

  it('toggling "Due today" sets the URL param', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    create();
    queueReq().flush([item()]);
    await fixture.whenStable();
    fixture.detectChanges();

    (el.querySelector('mat-slide-toggle button[role="switch"]') as HTMLButtonElement).click();

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { dueToday: 'true' },
      queryParamsHandling: 'merge',
    });
  });

  it('switching the toggle off clears the param', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    create({ dueToday: 'true' });
    queueReq().flush([item()]);
    await fixture.whenStable();
    fixture.detectChanges();

    (el.querySelector('mat-slide-toggle button[role="switch"]') as HTMLButtonElement).click();

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { dueToday: null },
      queryParamsHandling: 'merge',
    });
  });
});
