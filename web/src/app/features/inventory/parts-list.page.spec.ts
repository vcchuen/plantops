import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { SessionStore } from '../../core/auth/session.store';
import { PartsListPage } from './parts-list.page';
import { PartListItem, PartPage } from './inventory.models';

const part = (over: Partial<PartListItem> = {}): PartListItem => ({
  id: 'p1',
  partNumber: 'FDR-8MM-001',
  name: 'Feeder 8mm',
  unit: 'pcs',
  binLocation: 'A-01',
  quantityOnHand: 10,
  quantityReserved: 2,
  quantityAvailable: 8,
  reorderLevel: 3,
  isLowStock: false,
  ...over,
});

const pageOf = (items: PartListItem[]): PartPage => ({
  items,
  page: 1,
  pageSize: 25,
  totalCount: items.length,
});

describe('PartsListPage', () => {
  let fixture: ComponentFixture<PartsListPage>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const supervisor = signal(false);

  function create(inputs: Record<string, unknown> = {}) {
    fixture = TestBed.createComponent(PartsListPage);
    el = fixture.nativeElement;
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  const listReq = () => http.expectOne((r) => r.url === '/api/inventory/parts');

  beforeEach(() => {
    supervisor.set(false);
    TestBed.configureTestingModule({
      imports: [PartsListPage],
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

  it('sends paging defaults only when no filter is set', () => {
    create();
    const req = listReq();
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    req.flush(pageOf([]));
  });

  it('sends search, lowStock and parsed paging from the URL inputs', () => {
    create({ search: ' feeder ', lowStock: 'true', page: '3', pageSize: 'abc' });
    const req = listReq();
    expect(req.request.params.get('search')).toBe('feeder');
    expect(req.request.params.get('lowStock')).toBe('true');
    expect(req.request.params.get('page')).toBe('3');
    expect(req.request.params.get('pageSize')).toBe('25');
    req.flush(pageOf([]));
  });

  it('does not send lowStock for any other value', () => {
    create({ lowStock: 'false' });
    const req = listReq();
    expect(req.request.params.has('lowStock')).toBe(false);
    req.flush(pageOf([]));
  });

  it('renders rows with a link to the part and the low stock status as icon plus text', async () => {
    create();
    listReq().flush(pageOf([part(), part({ id: 'p2', partNumber: 'NZL-001', isLowStock: true })]));
    await fixture.whenStable();

    expect(el.querySelectorAll('tr[mat-row]').length).toBe(2);
    expect(el.querySelector('a[href="/inventory/p1"]')?.textContent).toContain('FDR-8MM-001');
    const rows = el.querySelectorAll('tr[mat-row]');
    expect(rows[0].textContent).toContain('In stock');
    expect(rows[1].textContent).toContain('Low stock');
    expect(rows[1].querySelector('.low mat-icon')).not.toBeNull();
  });

  it('shows the empty state', async () => {
    create({ search: 'zzz' });
    listReq().flush(pageOf([]));
    await fixture.whenStable();
    expect(el.textContent).toContain('No parts match these filters');
  });

  it('shows an alert on error', async () => {
    create();
    listReq().flush(
      { title: 'Invalid filter', detail: 'bad' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Invalid filter');
  });

  it('navigates with page reset when low stock is toggled', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    create({ page: '4' });
    listReq().flush(pageOf([part()]));
    await fixture.whenStable();

    (el.querySelector('mat-checkbox input') as HTMLInputElement).click();
    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { lowStock: 'true', page: 1 },
      queryParamsHandling: 'merge',
    });
  });

  it('debounces search before navigating', () => {
    vi.useFakeTimers();
    try {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      create();
      listReq().flush(pageOf([]));

      const input = el.querySelector('input[type="search"]') as HTMLInputElement;
      for (const text of ['f', 'fe', 'fee']) {
        input.value = text;
        input.dispatchEvent(new Event('input'));
        vi.advanceTimersByTime(100);
      }
      expect(navigate).not.toHaveBeenCalled();
      vi.advanceTimersByTime(300);
      expect(navigate).toHaveBeenCalledTimes(1);
      expect(navigate).toHaveBeenCalledWith([], {
        queryParams: { search: 'fee', page: 1 },
        queryParamsHandling: 'merge',
      });
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows "New part" only to supervisors and admins', async () => {
    create();
    listReq().flush(pageOf([]));
    await fixture.whenStable();
    expect(el.textContent).not.toContain('New part');

    supervisor.set(true);
    fixture.detectChanges();
    expect(el.textContent).toContain('New part');
  });
});
