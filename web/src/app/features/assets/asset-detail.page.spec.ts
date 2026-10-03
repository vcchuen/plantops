import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { AssetDetailPage } from './asset-detail.page';
import { AssetDetail } from './assets.models';

const asset: AssetDetail = {
  id: 'a1',
  tag: 'SMT-001',
  name: 'Pick and place',
  manufacturer: 'Fuji',
  model: 'NXT III',
  serialNumber: null,
  lineId: 'l1',
  lineCode: 'SMT1',
  lineName: 'SMT Line 1',
  station: 'S1',
  criticality: 'A',
  status: 'InService',
  commissionedOn: '2021-03-15',
  decommissionedOn: null,
  decommissionReason: null,
};

describe('AssetDetailPage', () => {
  let fixture: ComponentFixture<AssetDetailPage>;
  let http: HttpTestingController;
  let el: HTMLElement;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [AssetDetailPage],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AssetDetailPage);
    el = fixture.nativeElement;
    fixture.componentRef.setInput('id', 'a1');
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  const history = [
    {
      eventType: 'AssetRelocated',
      actorName: 'Sam Supervisor',
      occurredAt: '2026-05-01T08:00:00Z',
      payload: { station: 'S2' },
    },
  ];

  it('renders the fields in a definition list', async () => {
    http.expectOne('/api/assets/a1').flush(asset);
    http.expectOne('/api/assets/a1/history').flush(history);
    await fixture.whenStable();

    expect(el.querySelector('h1')?.textContent).toContain('SMT-001');
    const text = el.querySelector('dl')?.textContent ?? '';
    expect(text).toContain('Fuji');
    expect(text).toContain('NXT III');
    expect(text).toContain('SMT Line 1 (SMT1)');
    expect(text).toContain('A · A: failure stops the line');
    expect(text).toContain('Mar 15, 2021');
    expect(el.querySelector('.banner')).toBeNull();
  });

  it('renders the history timeline', async () => {
    http.expectOne('/api/assets/a1').flush(asset);
    http.expectOne('/api/assets/a1/history').flush(history);
    await fixture.whenStable();

    const items = el.querySelectorAll('ol.timeline li');
    expect(items.length).toBe(1);
    expect(items[0].textContent).toContain('Relocated');
    expect(items[0].textContent).toContain('Sam Supervisor');
  });

  it('shows a banner with date and reason for a decommissioned asset', async () => {
    http.expectOne('/api/assets/a1').flush({
      ...asset,
      status: 'Decommissioned',
      decommissionedOn: '2024-06-01',
      decommissionReason: 'Replaced by NXT IV',
    });
    http.expectOne('/api/assets/a1/history').flush([]);
    await fixture.whenStable();

    const banner = el.querySelector('.banner')?.textContent ?? '';
    expect(banner).toContain('Decommissioned');
    expect(banner).toContain('Jun 1, 2024');
    expect(banner).toContain('Replaced by NXT IV');
  });

  it('shows "Asset not found" with a link back on 404', async () => {
    http
      .expectOne('/api/assets/a1')
      .flush({ title: 'Not Found', status: 404 }, { status: 404, statusText: 'Not Found' });
    http.expectOne('/api/assets/a1/history').flush([]);
    await fixture.whenStable();

    expect(el.textContent).toContain('Asset not found');
    expect(el.querySelector('[role="alert"]')).toBeNull();
    expect(el.querySelectorAll('a[href="/assets"]').length).toBeGreaterThan(0);
  });

  it('shows an alert for other errors', async () => {
    http.expectOne('/api/assets/a1').flush('boom', { status: 500, statusText: 'Server Error' });
    http.expectOne('/api/assets/a1/history').flush([]);
    await fixture.whenStable();

    expect(el.querySelector('[role="alert"]')).not.toBeNull();
  });
});
