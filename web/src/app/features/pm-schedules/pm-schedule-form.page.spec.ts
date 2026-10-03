import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AssetListItem } from '../assets/assets.models';
import { PmScheduleFormPage } from './pm-schedule-form.page';
import { PmScheduleDetail } from './pm-schedules.models';

const asset: AssetListItem = {
  id: 'a1',
  tag: 'SMT-001',
  name: 'Pick and place',
  lineId: 'l1',
  lineName: 'SMT Line 1',
  station: 'S1',
  criticality: 'A',
  status: 'InService',
};

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

describe('PmScheduleFormPage', () => {
  let fixture: ComponentFixture<PmScheduleFormPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const submitBtn = () => el.querySelector('button[type="submit"]') as HTMLButtonElement;
  const field = (selector: string) => el.querySelector(selector) as HTMLInputElement;
  const title = 'input[type="text"]:not([role="combobox"])';
  const interval = 'input[type="number"]';
  const lead = 'mat-form-field:nth-of-type(5) input';
  const due = 'input[type="date"]';

  function type(selector: string, value: string) {
    const input = field(selector);
    input.value = value;
    input.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function errorsAfter(selector: string, value: string): string {
    type(selector, value);
    field(selector).dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    return Array.from(el.querySelectorAll('mat-error'))
      .map((e) => e.textContent)
      .join('|');
  }

  function pick(a: AssetListItem) {
    // The overlay panel is awkward in jsdom; selecting is a one-line call on the page.
    (fixture.componentInstance as unknown as { pickAsset(a: AssetListItem): void }).pickAsset(a);
    fixture.detectChanges();
  }

  function create(id?: string) {
    fixture = TestBed.createComponent(PmScheduleFormPage);
    el = fixture.nativeElement;
    if (id) fixture.componentRef.setInput('id', id);
    fixture.detectChanges();
  }

  function fillValid() {
    pick(asset);
    type(title, 'Lubricate rails');
    type(interval, '30');
    type(due, '2026-11-01');
  }

  const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

  async function loadEdit(body = detail(), etag = '"AAAAAAAB"') {
    create('pm1');
    http.expectOne('/api/pm-schedules/pm1').flush(body, { headers: { ETag: etag } });
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [PmScheduleFormPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('create mode', () => {
    beforeEach(() => create());

    it('makes no detail request, starts disabled and shows no errors', () => {
      expect(el.querySelector('h1')?.textContent).toContain('New PM schedule');
      expect(submitBtn().disabled).toBe(true);
      expect(el.querySelector('mat-error')).toBeNull();
    });

    it('requires a title once touched', () => {
      field(title).dispatchEvent(new Event('blur'));
      fixture.detectChanges();
      expect(el.querySelector('mat-error')?.textContent).toContain('Title is required');
    });

    it('rejects a title over 200 characters', () => {
      expect(errorsAfter(title, 'x'.repeat(201))).toContain('at most 200');
    });

    it('requires the interval', () => {
      field(interval).dispatchEvent(new Event('blur'));
      fixture.detectChanges();
      expect(el.textContent).toContain('Interval is required');
    });

    it.each([
      ['0', 'at least 1'],
      ['366', 'at most 365'],
      ['1.5', 'whole number'],
    ])('rejects interval %s', (value, message) => {
      expect(errorsAfter(interval, value)).toContain(message);
    });

    it.each([
      ['-1', 'cannot be negative'],
      ['31', 'at most 30'],
      ['2.5', 'whole number'],
    ])('rejects lead days %s', (value, message) => {
      expect(errorsAfter(lead, value)).toContain(message);
    });

    it('requires a next due date', () => {
      field(due).dispatchEvent(new Event('blur'));
      fixture.detectChanges();
      expect(el.textContent).toContain('Next due date is required');
    });

    it('enables submit only when everything is valid', () => {
      pick(asset);
      type(title, 'Lubricate rails');
      type(interval, '30');
      expect(submitBtn().disabled).toBe(true); // no date yet
      type(due, '2026-11-01');
      expect(submitBtn().disabled).toBe(false);
    });

    it('does not post when submitted invalid', () => {
      el.querySelector('form')?.dispatchEvent(new Event('submit'));
      fixture.detectChanges();
      http.expectNone('/api/pm-schedules');
    });

    it('posts the body, then navigates to the new schedule on 201', async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      fillValid();
      type('textarea', ' Check belts ');
      type(lead, '5');

      el.querySelector('form')?.dispatchEvent(new Event('submit'));
      const req = http.expectOne('/api/pm-schedules');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({
        assetId: 'a1',
        title: 'Lubricate rails',
        instructions: 'Check belts',
        intervalDays: 30,
        leadDays: 5,
        priority: 'P3',
        nextDueOn: '2026-11-01',
      });
      req.flush(detail({ id: 'pm9' }), {
        status: 201,
        statusText: 'Created',
        headers: { ETag: '"x"' },
      });
      await fixture.whenStable();

      expect(navigate).toHaveBeenCalledWith(['/pm-schedules', 'pm9']);
    });

    it('shows a server 400 as a form-level alert and stays on the page', async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      fillValid();
      el.querySelector('form')?.dispatchEvent(new Event('submit'));
      http
        .expectOne('/api/pm-schedules')
        .flush(
          { title: 'Asset decommissioned', detail: 'SMT-001 is out of service' },
          { status: 400, statusText: 'Bad Request' },
        );
      await fixture.whenStable();
      fixture.detectChanges();

      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Asset decommissioned');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('words a 403 in terms of supervisors', async () => {
      fillValid();
      el.querySelector('form')?.dispatchEvent(new Event('submit'));
      http
        .expectOne('/api/pm-schedules')
        .flush({ title: 'Forbidden' }, { status: 403, statusText: 'Forbidden' });
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.querySelector('[role="alert"]')?.textContent).toContain(
        'Only supervisors and admins',
      );
    });
  });

  describe('edit mode', () => {
    it('loads the schedule into the form and disables the asset field', async () => {
      await loadEdit();

      expect(el.querySelector('h1')?.textContent).toContain('Edit PM schedule');
      expect(field('input[role="combobox"]').value).toBe('SMT-001 — Pick and place');
      expect(field('input[role="combobox"]').disabled).toBe(true);
      expect(field(title).value).toBe('Lubricate rails');
      expect(field(interval).value).toBe('30');
      expect(field(due).value).toBe('2026-10-20');
      expect(submitBtn().disabled).toBe(false);
    });

    it('PUTs without assetId and with If-Match equal to the ETag from the GET', async () => {
      const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
      await loadEdit(detail(), '"AAAAAAAB"');
      type(title, 'Lubricate all rails');

      el.querySelector('form')?.dispatchEvent(new Event('submit'));
      const req = http.expectOne('/api/pm-schedules/pm1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.headers.get('If-Match')).toBe('"AAAAAAAB"');
      expect(req.request.body).toEqual({
        title: 'Lubricate all rails',
        instructions: 'Use grease type 2',
        intervalDays: 30,
        leadDays: 3,
        priority: 'P2',
        nextDueOn: '2026-10-20',
      });
      req.flush(null, { status: 204, statusText: 'No Content', headers: { ETag: '"AAAAAAAC"' } });
      await fixture.whenStable();

      expect(navigate).toHaveBeenCalledWith(['/pm-schedules', 'pm1']);
    });

    it('on 412 shows a snackbar, reloads and replaces the form with the latest version', async () => {
      const open = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
      await loadEdit();
      type(title, 'My stale edit');

      el.querySelector('form')?.dispatchEvent(new Event('submit'));
      http
        .expectOne('/api/pm-schedules/pm1')
        .flush(null, { status: 412, statusText: 'Precondition Failed' });
      await settle();

      expect(open).toHaveBeenCalledWith(
        'Someone else changed this schedule — showing the latest version.',
        'Dismiss',
        expect.anything(),
      );
      http
        .expectOne('/api/pm-schedules/pm1')
        .flush(detail({ title: 'Changed by someone else' }), { headers: { ETag: '"NEW"' } });
      await fixture.whenStable();
      fixture.detectChanges();

      expect(field(title).value).toBe('Changed by someone else');
      expect(el.querySelector('[role="alert"]')).toBeNull();
    });

    it('shows not found on 404', async () => {
      create('nope');
      http
        .expectOne('/api/pm-schedules/nope')
        .flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
      await fixture.whenStable();
      fixture.detectChanges();
      expect(el.textContent).toContain('No PM schedule with that id exists');
    });
  });
});
