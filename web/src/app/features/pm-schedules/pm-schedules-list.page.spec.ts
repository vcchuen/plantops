import { ComponentFixture, TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { MatSelect } from '@angular/material/select';
import { By } from '@angular/platform-browser';
import { SessionStore } from '../../core/auth/session.store';
import { PmSchedulesListPage } from './pm-schedules-list.page';
import { PmScheduleListItem } from './pm-schedules.models';

const item = (over: Partial<PmScheduleListItem> = {}): PmScheduleListItem => ({
  id: 'pm1',
  assetId: 'a1',
  assetTag: 'SMT-001',
  assetName: 'Pick and place',
  title: 'Lubricate rails',
  intervalDays: 30,
  leadDays: 3,
  priority: 'P3',
  nextDueOn: '2026-10-20',
  isActive: true,
  ...over,
});

describe('PmSchedulesListPage', () => {
  let fixture: ComponentFixture<PmSchedulesListPage>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const supervisor = signal(false);

  // Inputs before the first detectChanges: httpResource sends its request from an effect.
  function create(inputs: Record<string, unknown> = {}) {
    fixture = TestBed.createComponent(PmSchedulesListPage);
    el = fixture.nativeElement;
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
  }

  const listReq = () => http.expectOne((r) => r.url === '/api/pm-schedules');
  const newLink = () => el.querySelector('a[href="/pm-schedules/new"]');

  beforeEach(() => {
    supervisor.set(false);
    TestBed.configureTestingModule({
      imports: [PmSchedulesListPage],
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

  it('sends no params when showing all schedules', () => {
    create();
    const req = listReq();
    expect(req.request.params.keys()).toEqual([]);
    req.flush([]);
  });

  it.each(['true', 'false'])('sends active=%s', (value) => {
    create({ active: value });
    const req = listReq();
    expect(req.request.params.get('active')).toBe(value);
    req.flush([]);
  });

  it('ignores an unknown active value rather than sending it', () => {
    create({ active: 'maybe' });
    const req = listReq();
    expect(req.request.params.has('active')).toBe(false);
    req.flush([]);
  });

  it('passes the asset filter through', () => {
    create({ assetId: 'a1' });
    const req = listReq();
    expect(req.request.params.get('assetId')).toBe('a1');
    req.flush([]);
  });

  it('renders the rows with interval, date, lead days and status as text', async () => {
    create();
    listReq().flush([item(), item({ id: 'pm2', title: 'Replace filter', isActive: false })]);
    await fixture.whenStable();

    expect(el.querySelectorAll('tr[mat-row]').length).toBe(2);
    const first = el.querySelector('tr[mat-row]')?.textContent ?? '';
    expect(first).toContain('SMT-001 — Pick and place');
    expect(first).toContain('Every 30 days');
    expect(first).toContain('Oct 20, 2026');
    expect(first).toContain('Active');
    expect(el.querySelector('a[href="/assets/a1"]')).not.toBeNull();
    expect(el.querySelector('a[href="/pm-schedules/pm1"]')?.textContent).toContain(
      'Lubricate rails',
    );
    expect(el.querySelectorAll('tr[mat-row]')[1].textContent).toContain('Inactive');
  });

  it('shows the empty state', async () => {
    create();
    listReq().flush([]);
    await fixture.whenStable();
    expect(el.textContent).toContain('No PM schedules match these filters');
  });

  it('shows a problem in an alert', async () => {
    create();
    listReq().flush({ title: 'Bad filter' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Bad filter');
  });

  it('writes the filter to the URL', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    create();
    listReq().flush([]);
    await fixture.whenStable();

    fixture.debugElement
      .query(By.directive(MatSelect))
      .componentInstance.selectionChange.emit({ value: 'false' });

    expect(navigate).toHaveBeenCalledWith([], {
      queryParams: { active: 'false' },
      queryParamsHandling: 'merge',
    });
  });

  it.each([
    [false, false],
    [true, true],
  ])('supervisor=%s sees the New schedule button: %s', (isSupervisor, visible) => {
    supervisor.set(isSupervisor);
    create();
    listReq().flush([]);
    expect(newLink() !== null).toBe(visible);
  });
});
