import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { PartListItem } from './inventory.models';
import { ReservePartDialog } from './reserve-part.dialog';

const part: PartListItem = {
  id: 'p1',
  partNumber: 'FDR-8MM-001',
  name: 'Feeder 8mm',
  unit: 'pcs',
  binLocation: 'A-01',
  quantityOnHand: 10,
  quantityReserved: 8,
  quantityAvailable: 2,
  reorderLevel: 3,
  isLowStock: true,
};

describe('ReservePartDialog', () => {
  let fixture: ComponentFixture<ReservePartDialog>;
  let http: HttpTestingController;
  let el: HTMLElement;
  const close = vi.fn();

  const settle = () => new Promise<void>((resolve) => setTimeout(resolve));
  const submitBtn = () => el.querySelector('button[type="submit"]') as HTMLButtonElement;
  const qty = () => el.querySelector('input[type="number"]') as HTMLInputElement;

  function type(value: string) {
    qty().value = value;
    qty().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function pick() {
    (fixture.componentInstance as unknown as { pickPart(p: PartListItem): void }).pickPart(part);
    fixture.detectChanges();
  }

  const submit = () => el.querySelector('form')?.dispatchEvent(new Event('submit'));

  beforeEach(() => {
    close.mockReset();
    TestBed.configureTestingModule({
      imports: [ReservePartDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { workOrderId: 'w1' } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ReservePartDialog);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('starts with submit disabled', () => {
    expect(submitBtn().disabled).toBe(true);
  });

  it('requires a picked part and an integer quantity of at least 1', () => {
    type('2');
    expect(submitBtn().disabled).toBe(true); // no part picked

    pick();
    expect(submitBtn().disabled).toBe(false);
    type('0');
    expect(submitBtn().disabled).toBe(true);
    type('1.5');
    expect(submitBtn().disabled).toBe(true);
    type('1');
    expect(submitBtn().disabled).toBe(false);
  });

  it('searches parts after a debounce and labels options with availability', async () => {
    vi.useFakeTimers();
    try {
      const input = el.querySelector('input[role="combobox"]') as HTMLInputElement;
      input.value = 'fdr';
      input.dispatchEvent(new Event('input'));
      vi.advanceTimersByTime(300);
      const req = http.expectOne((r) => r.url === '/api/inventory/parts');
      expect(req.request.params.get('search')).toBe('fdr');
      req.flush({ items: [part], page: 1, pageSize: 10, totalCount: 1 });
    } finally {
      vi.useRealTimers();
    }
    expect(
      (fixture.componentInstance as unknown as { partLabel(p: PartListItem): string }).partLabel(
        part,
      ),
    ).toBe('FDR-8MM-001 — Feeder 8mm (2 available)');
  });

  it('keeps the dialog open and shows the problem detail on 409', async () => {
    pick();
    type('5');
    submit();

    const req = http.expectOne('/api/inventory/reservations');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ partId: 'p1', workOrderId: 'w1', quantity: 5 });
    req.flush(
      { title: 'Conflict', detail: 'Only 2 pcs of FDR-8MM-001 available', status: 409 },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    fixture.detectChanges();

    expect(close).not.toHaveBeenCalled();
    const alert = el.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain('Only 2 pcs of FDR-8MM-001 available');
    expect(alert?.textContent).not.toContain('Conflict');
    // Still usable: the user can lower the quantity and retry.
    expect(submitBtn().disabled).toBe(false);
  });

  it('closes with true on success', async () => {
    pick();
    type('2');
    submit();

    http
      .expectOne('/api/inventory/reservations')
      .flush({ id: 'r1' }, { status: 201, statusText: 'Created' });
    await settle();

    expect(close).toHaveBeenCalledWith(true);
  });

  it('clears a previous error on the next attempt', async () => {
    pick();
    type('5');
    submit();
    http
      .expectOne('/api/inventory/reservations')
      .flush({ title: 'Conflict', detail: 'Only 2 pcs' }, { status: 409, statusText: 'Conflict' });
    await settle();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')).not.toBeNull();

    type('2');
    submit();
    http.expectOne('/api/inventory/reservations').flush({}, { status: 201, statusText: 'Created' });
    await settle();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')).toBeNull();
  });
});
