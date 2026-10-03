import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { SessionStore } from '../../core/auth/session.store';
import { PartDetailPage } from './part-detail.page';
import { PartDetail } from './inventory.models';

const detail = (over: Partial<PartDetail> = {}): PartDetail => ({
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
  reservations: [
    {
      id: 'r1',
      workOrderId: 'w1',
      workOrderNumber: 'WO-000001',
      quantity: 2,
      status: 'Active',
      reservedAt: '2026-10-03T14:00:00Z',
      reservedByName: 'Tom Tech',
    },
  ],
  ...over,
});

describe('PartDetailPage', () => {
  let fixture: ComponentFixture<PartDetailPage>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const supervisor = signal(true);

  const url = '/api/inventory/parts/p1';
  const settle = () => new Promise<void>((resolve) => setTimeout(resolve));
  const submitBtn = () =>
    el.querySelector('form.receive button[type="submit"]') as HTMLButtonElement;
  const qty = () => el.querySelector('form.receive input') as HTMLInputElement;

  function type(value: string) {
    qty().value = value;
    qty().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  async function create(body: PartDetail = detail()) {
    fixture = TestBed.createComponent(PartDetailPage);
    el = fixture.nativeElement;
    fixture.componentRef.setInput('id', 'p1');
    fixture.detectChanges();
    http.expectOne(url).flush(body);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(() => {
    supervisor.set(true);
    TestBed.configureTestingModule({
      imports: [PartDetailPage],
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

  it('renders the stock figures and active reservations linking to work orders', async () => {
    await create();
    const text = el.querySelector('dl')?.textContent ?? '';
    expect(el.querySelector('h1')?.textContent).toContain('FDR-8MM-001');
    expect(text).toContain('A-01');
    expect(text).toContain('On hand10');
    expect(text).toContain('Reserved2');
    expect(text).toContain('Available8');
    expect(el.querySelector('a[href="/work-orders/w1"]')?.textContent).toContain('WO-000001');
    expect(el.querySelector('.banner')).toBeNull();
  });

  it('shows a low-stock banner', async () => {
    await create(detail({ isLowStock: true, quantityAvailable: 1 }));
    expect(el.querySelector('.banner')?.textContent).toContain('Low stock');
  });

  it('hides the receive form from non-managers', async () => {
    supervisor.set(false);
    await create();
    expect(el.querySelector('form.receive')).toBeNull();
  });

  it('starts with submit disabled and validates quantity', async () => {
    await create();
    expect(submitBtn().disabled).toBe(true);

    type('0');
    expect(submitBtn().disabled).toBe(true);
    type('1.5');
    expect(submitBtn().disabled).toBe(true);
    type('-3');
    expect(submitBtn().disabled).toBe(true);
    type('5');
    expect(submitBtn().disabled).toBe(false);
  });

  it('shows the quantity error after the field is touched', async () => {
    await create();
    qty().dispatchEvent(new Event('blur'));
    fixture.detectChanges();
    expect(el.querySelector('mat-error')?.textContent).toContain('Quantity is required');
  });

  it('does not post when submitted invalid', async () => {
    await create();
    el.querySelector('form.receive')?.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    http.expectNone(`${url}/receive`);
  });

  it('posts the quantity, announces it, clears the field and reloads the part', async () => {
    await create();
    type('5');
    el.querySelector('form.receive')?.dispatchEvent(new Event('submit'));

    const req = http.expectOne(`${url}/receive`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ quantity: 5 });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    http.expectOne(url).flush(detail({ quantityOnHand: 15, quantityAvailable: 13 }));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[role="status"][aria-live="polite"]')?.textContent).toContain(
      'Received 5',
    );
    expect(el.querySelector('dl')?.textContent).toContain('On hand15');
    expect(qty().value).toBe('');
  });

  it('shows the problem detail when receiving fails', async () => {
    await create();
    type('5');
    el.querySelector('form.receive')?.dispatchEvent(new Event('submit'));
    http
      .expectOne(`${url}/receive`)
      .flush(
        { title: 'Forbidden', detail: 'Supervisors only' },
        { status: 403, statusText: 'Forbidden' },
      );
    await settle();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Supervisors only');
  });

  it('shows not found on 404', async () => {
    fixture = TestBed.createComponent(PartDetailPage);
    el = fixture.nativeElement;
    fixture.componentRef.setInput('id', 'p1');
    fixture.detectChanges();
    http.expectOne(url).flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    expect(el.textContent).toContain('Part not found');
  });
});
