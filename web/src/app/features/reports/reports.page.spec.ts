import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { MatSelect } from '@angular/material/select';
import { By } from '@angular/platform-browser';
import { defaultRange } from './reports.models';
import { ReportsPage } from './reports.page';

describe('ReportsPage', () => {
  let fixture: ComponentFixture<ReportsPage>;
  let http: HttpTestingController;
  let el: HTMLElement;
  // Same local computation as the page, so the expectation holds in any TZ.
  const defaults = defaultRange();

  function create(inputs: Record<string, unknown> = {}) {
    fixture = TestBed.createComponent(ReportsPage);
    el = fixture.nativeElement;
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  const req = (suffix: string) => http.expectOne((r) => r.url === `/api/reports/${suffix}`);
  const body = (groupBy: string, rows: object[]) => ({ from: 'x', to: 'y', groupBy, rows });

  function flushAll() {
    req('mttr').flush(
      body('line', [
        { key: 'l1', label: 'SMT Line 1', workOrders: 4, meanRepairMinutes: 135 },
        { key: 'l2', label: 'SMT Line 2', workOrders: 2, meanRepairMinutes: 45 },
      ]),
    );
    req('sla-compliance').flush(
      body('priority', [
        { key: 'P1', label: 'P1', completed: 10, metSla: 9, compliancePercent: 93.5 },
      ]),
    );
    req('downtime').flush(
      body('line', [{ key: 'l1', label: 'SMT Line 1', events: 3, downtimeMinutes: 60 }]),
    );
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ReportsPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('defaults to the last three months and the default groupings', () => {
    create();
    const m = req('mttr');
    expect(m.request.params.get('from')).toBe(defaults.from);
    expect(m.request.params.get('to')).toBe(defaults.to);
    expect(m.request.params.get('groupBy')).toBe('line');
    expect(req('sla-compliance').request.params.get('groupBy')).toBe('priority');
    expect(req('downtime').request.params.get('groupBy')).toBe('line');
  });

  it('reads the range and groupings from the URL and ignores unknown groupings', () => {
    create({
      from: '2026-01-01',
      to: '2026-02-01',
      mttrBy: 'asset',
      slaBy: 'month',
      downBy: 'bogus',
    });
    const m = req('mttr');
    expect(m.request.params.get('from')).toBe('2026-01-01');
    expect(m.request.params.get('to')).toBe('2026-02-01');
    expect(m.request.params.get('groupBy')).toBe('asset');
    expect(req('sla-compliance').request.params.get('groupBy')).toBe('month');
    expect(req('downtime').request.params.get('groupBy')).toBe('line');
  });

  it('writes a group-by change to the URL', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    create();
    flushAll();
    await fixture.whenStable();

    fixture.debugElement
      .queryAll(By.directive(MatSelect))[0]
      .componentInstance.selectionChange.emit({ value: 'month' });

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { mttrBy: 'month' },
      queryParamsHandling: 'merge',
    });
  });

  it('renders tables with formatted values and proportional bars', async () => {
    create();
    flushAll();
    await fixture.whenStable();
    fixture.detectChanges();

    const tables = el.querySelectorAll('table');
    expect(tables.length).toBe(3);
    const mttrRows = tables[0].querySelectorAll('tbody tr');
    expect(mttrRows[0].textContent).toContain('SMT Line 1');
    expect(mttrRows[0].textContent).toContain('2 h 15 min');
    expect(mttrRows[1].textContent).toContain('45 min');
    expect(tables[1].textContent).toContain('93.5 %');
    expect(tables[2].textContent).toContain('1 h');

    const bars = tables[0].querySelectorAll<HTMLElement>('.bar');
    expect(bars[0].style.inlineSize).toBe('100%');
    expect(parseFloat(bars[1].style.inlineSize)).toBeCloseTo((45 / 135) * 100, 5);
    expect(bars[0].closest('[aria-hidden="true"]')).not.toBeNull();
    expect(el.querySelectorAll('section h2').length).toBe(3);
  });

  it('shows the empty state', async () => {
    create();
    req('mttr').flush(body('line', []));
    req('sla-compliance').flush(body('priority', []));
    req('downtime').flush(body('line', []));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.textContent).toContain('No data for this date range');
  });

  it('shows a problem in an alert', async () => {
    create();
    req('mttr').flush({ title: 'Bad range' }, { status: 400, statusText: 'Bad Request' });
    req('sla-compliance').flush(body('priority', []));
    req('downtime').flush(body('line', []));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('section [role="alert"]')?.textContent).toContain('Bad range');
  });

  it('does not call the API and shows a hint when to <= from', () => {
    create({ from: '2026-05-01', to: '2026-05-01' });
    http.expectNone((r) => r.url.startsWith('/api/reports/'));
    expect(el.querySelector('p.hint')?.textContent).toContain('after the start date');
    const exp = el.querySelector('a.export')!;
    expect(exp.getAttribute('aria-disabled')).toBe('true');
    expect(exp.hasAttribute('href')).toBe(false);
  });

  it('builds the export link from the range', () => {
    create({ from: '2026-01-01', to: '2026-02-01' });
    flushAll();
    const a = el.querySelector('a.export')!;
    expect(a.getAttribute('href')).toBe('/api/reports/export.xlsx?from=2026-01-01&to=2026-02-01');
    expect(a.hasAttribute('download')).toBe(true);
  });
});
