import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { AssetListItem } from '../assets/assets.models';
import { WorkOrderNewPage } from './work-order-new.page';

const asset = (over: Partial<AssetListItem> = {}): AssetListItem => ({
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

describe('WorkOrderNewPage', () => {
  let fixture: ComponentFixture<WorkOrderNewPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const submitBtn = () => el.querySelector('button[type="submit"]') as HTMLButtonElement;
  const field = (selector: string) => el.querySelector(selector) as HTMLInputElement;

  function type(selector: string, value: string) {
    const input = field(selector);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function pick(a: AssetListItem) {
    // The overlay panel is awkward in jsdom; selecting is a one-line call on the page.
    (fixture.componentInstance as unknown as { pickAsset(a: AssetListItem): void }).pickAsset(a);
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkOrderNewPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WorkOrderNewPage);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('starts with submit disabled and shows no errors before interaction', () => {
    expect(submitBtn().disabled).toBe(true);
    expect(el.querySelector('mat-error')).toBeNull();
  });

  it('shows a required error only after the field is touched', () => {
    field('input[type="text"]:not([role="combobox"])').dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(el.querySelector('mat-error')?.textContent).toContain('Title is required');
  });

  it('blocks submit while the title is too long, then enables when valid', () => {
    pick(asset());
    type('input[type="text"]:not([role="combobox"])', 'x'.repeat(201));
    expect(submitBtn().disabled).toBe(true);

    type('input[type="text"]:not([role="combobox"])', 'Conveyor jam');
    expect(submitBtn().disabled).toBe(false);
  });

  it('does not post when the form is submitted invalid', () => {
    el.querySelector('form')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    http.expectNone('/api/work-orders');
  });

  it('posts the model, then navigates to the new work order on 201', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    pick(asset());
    type('input[type="text"]:not([role="combobox"])', 'Conveyor jam');
    type('textarea', 'Belt keeps slipping');
    const down = el.querySelector('mat-checkbox input') as HTMLInputElement;
    down.click();
    fixture.detectChanges();

    el.querySelector('form')?.dispatchEvent(new Event('submit'));
    const req = http.expectOne('/api/work-orders');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      assetId: 'a1',
      title: 'Conveyor jam',
      description: 'Belt keeps slipping',
      priority: 'P3',
      assetDown: true,
    });
    req.flush({ id: 'wo9' }, { status: 201, statusText: 'Created', headers: { ETag: '"x"' } });
    await fixture.whenStable();

    expect(navigate).toHaveBeenCalledWith(['/work-orders', 'wo9']);
  });

  it('shows a server 400 as a form-level alert and stays on the page', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    pick(asset());
    type('input[type="text"]:not([role="combobox"])', 'Conveyor jam');

    el.querySelector('form')?.dispatchEvent(new Event('submit'));
    http
      .expectOne('/api/work-orders')
      .flush(
        { title: 'Asset decommissioned', detail: 'SMT-001 is out of service' },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Asset decommissioned');
    expect(navigate).not.toHaveBeenCalled();
    expect(submitBtn().disabled).toBe(false);
  });

  it('searches in-service assets after a debounce and offers only non-decommissioned ones', () => {
    vi.useFakeTimers();
    try {
      field('input[role="combobox"]').dispatchEvent(new Event('focus'));
      type('input[role="combobox"]', 'smt');
      vi.advanceTimersByTime(299);
      http.expectNone((r) => r.url === '/api/assets');
      vi.advanceTimersByTime(1);

      const req = http.expectOne((r) => r.url === '/api/assets');
      expect(req.request.params.get('search')).toBe('smt');
      expect(req.request.params.get('pageSize')).toBe('10');
      req.flush({
        items: [asset(), asset({ id: 'a2', tag: 'OLD-1', status: 'Decommissioned' })],
        page: 1,
        pageSize: 10,
        totalCount: 2,
      });
      fixture.detectChanges();

      const options = (
        fixture.componentInstance as unknown as { assetOptions(): AssetListItem[] }
      ).assetOptions();
      expect(options.map((o) => o.tag)).toEqual(['SMT-001']);
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows the picked asset as "TAG — Name" and clears the pick when typing again', () => {
    pick(asset());
    expect(field('input[role="combobox"]').value).toBe('SMT-001 — Pick and place');
    type('input[type="text"]:not([role="combobox"])', 'Jam');
    expect(submitBtn().disabled).toBe(false);

    type('input[role="combobox"]', 'SMT-00');
    expect(submitBtn().disabled).toBe(true);
  });
});
