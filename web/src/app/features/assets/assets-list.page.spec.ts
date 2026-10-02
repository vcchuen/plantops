import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { MatSelect } from '@angular/material/select';
import { By } from '@angular/platform-browser';
import { AssetsListPage } from './assets-list.page';
import { AssetListItem, AssetPage } from './assets.models';

const item = (over: Partial<AssetListItem> = {}): AssetListItem => ({
  id: 'a1',
  tag: 'SMT-001',
  name: 'Pick and place',
  lineId: 'l1',
  lineName: 'SMT Line 1',
  station: 'S1',
  criticality: 'A',
  status: 'InService',
  ...over,
});

const pageOf = (items: AssetListItem[], totalCount = items.length): AssetPage => ({
  items,
  page: 1,
  pageSize: 25,
  totalCount,
});

describe('AssetsListPage', () => {
  let fixture: ComponentFixture<AssetsListPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  // Inputs must be set before the first detectChanges: httpResource sends its request from an effect.
  function create(inputs: Record<string, unknown> = {}) {
    fixture = TestBed.createComponent(AssetsListPage);
    el = fixture.nativeElement;
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  const listReq = () => http.expectOne((r) => r.url === '/api/assets');

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AssetsListPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only non-empty filters plus paging defaults', () => {
    create({ lineId: 'l1', criticality: 'A', search: '' });
    const req = listReq();
    expect(req.request.params.keys().sort()).toEqual(['criticality', 'lineId', 'page', 'pageSize']);
    expect(req.request.params.get('lineId')).toBe('l1');
    expect(req.request.params.get('page')).toBe('1');
    expect(req.request.params.get('pageSize')).toBe('25');
    http.expectOne('/api/assets/lines').flush([]);
    req.flush(pageOf([]));
  });

  it('parses page and pageSize from strings, falling back on junk', () => {
    create({ page: '3', pageSize: 'abc' });
    const req = listReq();
    expect(req.request.params.get('page')).toBe('3');
    expect(req.request.params.get('pageSize')).toBe('25');
    http.expectOne('/api/assets/lines').flush([]);
    req.flush(pageOf([]));
  });

  it('renders rows with a link to the detail page', async () => {
    create();
    http.expectOne('/api/assets/lines').flush([]);
    listReq().flush(pageOf([item(), item({ id: 'a2', tag: 'SMT-002', criticality: 'C' })]));
    await fixture.whenStable();

    expect(el.querySelectorAll('tr[mat-row]').length).toBe(2);
    expect(el.querySelector('a[href="/assets/a1"]')?.textContent).toContain('SMT-001');
    expect(el.textContent).toContain('SMT Line 1');
  });

  it('shows the criticality as text, not only colour', async () => {
    create();
    http.expectOne('/api/assets/lines').flush([]);
    listReq().flush(pageOf([item()]));
    await fixture.whenStable();

    expect(el.querySelector('mat-chip')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
      'A · Stops line',
    );
  });

  it('shows the empty state', async () => {
    create({ search: 'zzz' });
    http.expectOne('/api/assets/lines').flush([]);
    listReq().flush(pageOf([]));
    await fixture.whenStable();

    expect(el.textContent).toContain('No assets match these filters');
    expect(el.querySelector('table')).toBeNull();
  });

  it('shows a problem title and detail in an alert', async () => {
    create();
    http.expectOne('/api/assets/lines').flush([]);
    listReq().flush(
      { title: 'Invalid filter', detail: 'pageSize must be at most 100' },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    const alert = el.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Invalid filter');
    expect(alert?.textContent).toContain('pageSize must be at most 100');
  });

  it('shows a generic alert when the error body is not a problem', async () => {
    create();
    http.expectOne('/api/assets/lines').flush([]);
    listReq().flush('<html>Bad gateway</html>', { status: 502, statusText: 'Bad Gateway' });
    await fixture.whenStable();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Something went wrong');
  });

  it('keeps the previous rows visible while new filters load', async () => {
    create();
    http.expectOne('/api/assets/lines').flush([]);
    listReq().flush(pageOf([item()]));
    await fixture.whenStable();

    fixture.componentRef.setInput('status', 'Decommissioned');
    fixture.detectChanges();
    const next = listReq();
    fixture.detectChanges();

    expect(el.querySelectorAll('tr[mat-row]').length).toBe(1);
    expect(el.querySelector('mat-progress-bar')).not.toBeNull();
    next.flush(pageOf([]));
  });

  it('navigates with page reset to 1 when a filter changes', async () => {
    const router = TestBed.inject(Router);
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    create({ page: '4' });
    http.expectOne('/api/assets/lines').flush([{ id: 'l1', code: 'SMT1', name: 'SMT Line 1' }]);
    listReq().flush(pageOf([item()], 200));
    await fixture.whenStable();

    const selects = fixture.debugElement.queryAll(By.directive(MatSelect));
    selects[0].componentInstance.selectionChange.emit({ value: 'l1' });

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { lineId: 'l1', page: 1 },
      queryParamsHandling: 'merge',
    });
  });

  it('debounces search before navigating', () => {
    vi.useFakeTimers();
    try {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      create();
      http.expectOne('/api/assets/lines').flush([]);
      listReq().flush(pageOf([]));

      const input = el.querySelector('input[type="search"]') as HTMLInputElement;
      for (const text of ['p', 'pl', 'plc']) {
        input.value = text;
        input.dispatchEvent(new Event('input'));
        vi.advanceTimersByTime(100);
      }
      expect(navigate).not.toHaveBeenCalled();
      vi.advanceTimersByTime(300);
      expect(navigate).toHaveBeenCalledTimes(1);
      expect(navigate).toHaveBeenCalledWith([], {
        queryParams: { search: 'plc', page: 1 },
        queryParamsHandling: 'merge',
      });
    } finally {
      vi.useRealTimers();
    }
  });
});
