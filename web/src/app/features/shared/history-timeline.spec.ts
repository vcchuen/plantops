import { TestBed } from '@angular/core/testing';
import { HistoryTimeline } from './history-timeline';
import { HistoryEntry, describePayload, eventLabel } from './history.models';

const entry = (over: Partial<HistoryEntry> = {}): HistoryEntry => ({
  eventType: 'WorkOrderApproved',
  actorName: 'Sam Supervisor',
  occurredAt: '2026-10-03T08:30:00Z',
  payload: null,
  ...over,
});

describe('HistoryTimeline', () => {
  function render(entries: HistoryEntry[]) {
    const fixture = TestBed.createComponent(HistoryTimeline);
    fixture.componentRef.setInput('entries', entries);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders an ordered list in the given order with label and actor', () => {
    const el = render([
      entry(),
      entry({ eventType: 'WorkOrderSubmitted', actorName: 'Olivia Operator' }),
    ]);
    const items = el.querySelectorAll('ol > li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Work order approved');
    expect(items[0].textContent).toContain('by Sam Supervisor');
    expect(items[1].textContent).toContain('Work order submitted');
    expect(items[0].querySelector('time')?.getAttribute('datetime')).toBe('2026-10-03T08:30:00Z');
  });

  it('shows payload facts and hides ids', () => {
    const el = render([entry({ payload: { reason: 'Duplicate', technicianId: 'abc' } })]);
    expect(el.textContent).toContain('Reason: Duplicate');
    expect(el.textContent).not.toContain('abc');
  });

  it('shows an empty message', () => {
    expect(render([]).textContent).toContain('No history yet');
  });
});

describe('history models', () => {
  it('labels known asset events and humanises unknown ones', () => {
    expect(eventLabel('AssetCriticalityChanged')).toBe('Criticality changed');
    expect(eventLabel('SomethingNewHappened')).toBe('Something new happened');
  });

  it('parses a JSON string payload and tolerates plain text and junk', () => {
    expect(describePayload('{"station":"S2"}')).toBe('Station: S2');
    expect(describePayload('moved by hand')).toBe('moved by hand');
    expect(describePayload(null)).toBeNull();
    expect(describePayload({ nested: { a: 1 } })).toBeNull();
  });
});
