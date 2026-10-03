import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { MatSelect } from '@angular/material/select';
import { By } from '@angular/platform-browser';
import { WorkOrdersListPage } from './work-orders-list.page';
import { WorkOrderListItem, WorkOrderPage } from './work-orders.models';

const item = (over: Partial<WorkOrderListItem> = {}): WorkOrderListItem => ({
  id: 'w1',
  number: 'WO-000001',
  title: 'Conveyor jam',
  assetId: 'a1',
  assetTag: 'SMT-001',
  assetName: 'Pick and place',
  priority: 'P1',
  status: 'Assigned',
  slaState: 'AtRisk',
  dueAt: '2026-10-03T12:00:00Z',
  assignedTo: { id: 't1', name: 'Tom Tech' },
  submittedAt: '2026-10-03T08:00:00Z',
  ...over,
});

const pageOf = (items: WorkOrderListItem[], totalCount = items.length): WorkOrderPage => ({
  items,
  page: 1,
  pageSize: 25,
  totalCount,
});

describe('WorkOrdersListPage', () => {
  let fixture: ComponentFixture<WorkOrdersListPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  // Inputs before the first detectChanges: httpResource sends its request from an effect.
  function create(inputs: Record<string, unknown> = {}) {
    fixture = TestBed.createComponent(WorkOrdersListPage);
    el = fixture.nativeElement;
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  const listReq = () => http.expectOne((r) => r.url === '/api/work-orders');

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkOrdersListPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only non-empty filters plus paging defaults', () => {
    create({ status: 'Assigned', priority: '', mine: 'true' });
    const req = listReq();
    expect(req.request.params.keys().sort()).toEqual(['mine', 'page', 'pageSize', 'status']);
    expect(req.request.params.get('mine')).toBe('true');
    expect(req.request.params.get('pageSize')).toBe('25');
    req.flush(pageOf([]));
  });

  it('does not send mine when it is anything but "true"', () => {
    create({ mine: 'false', page: '2' });
    const req = listReq();
    expect(req.request.params.has('mine')).toBe(false);
    expect(req.request.params.get('page')).toBe('2');
    req.flush(pageOf([]));
  });

  it('renders the number as a link and the SLA badge as text', async () => {
    create();
    listReq().flush(
      pageOf([item(), item({ id: 'w2', number: 'WO-000002', slaState: 'Breached' })]),
    );
    await fixture.whenStable();

    expect(el.querySelectorAll('tr[mat-row]').length).toBe(2);
    expect(el.querySelector('a[href="/work-orders/w1"]')?.textContent).toContain('WO-000001');
    const badges = Array.from(el.querySelectorAll('app-sla-badge')).map((b) => b.textContent);
    expect(badges[0]).toContain('At risk');
    expect(badges[1]).toContain('Breached');
    expect(el.textContent).toContain('SMT-001 — Pick and place');
    expect(el.textContent).toContain('P1 · Critical (4h)');
  });

  it('links to the raise form', () => {
    create();
    expect(el.querySelector('a[href="/work-orders/new"]')?.textContent).toContain('Raise');
    listReq().flush(pageOf([]));
  });

  it('shows "Unassigned" when there is no assignee', async () => {
    create();
    listReq().flush(pageOf([item({ assignedTo: null, status: 'Submitted' })]));
    await fixture.whenStable();
    expect(el.textContent).toContain('Unassigned');
  });

  it('shows the empty state and an alert on error', async () => {
    create();
    listReq().flush(pageOf([]));
    await fixture.whenStable();
    expect(el.textContent).toContain('No work orders match these filters');
  });

  it('shows a problem in an alert', async () => {
    create();
    listReq().flush({ title: 'Bad filter', detail: 'x' }, { status: 400, statusText: 'Bad' });
    await fixture.whenStable();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Bad filter');
  });

  it('navigates with page reset to 1 when a filter changes', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    create({ page: '4' });
    listReq().flush(pageOf([item()], 200));
    await fixture.whenStable();

    const selects = fixture.debugElement.queryAll(By.directive(MatSelect));
    selects[1].componentInstance.selectionChange.emit({ value: 'P2' });

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { priority: 'P2', page: 1 },
      queryParamsHandling: 'merge',
    });
  });
});
