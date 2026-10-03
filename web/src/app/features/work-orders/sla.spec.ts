import { isOpenSla, slaCountdown } from './sla';

const NOW = Date.parse('2026-10-03T12:00:00Z');
const at = (offsetMs: number) => new Date(NOW + offsetMs).toISOString();
const MIN = 60_000;
const HOUR = 60 * MIN;

describe('slaCountdown', () => {
  it.each([
    [0, 'due in less than a minute'],
    [59_999, 'due in less than a minute'],
    [MIN, 'due in 1m'],
    [59 * MIN, 'due in 59m'],
    [HOUR, 'due in 1h'],
    [3 * HOUR + 12 * MIN, 'due in 3h 12m'],
    [24 * HOUR, 'due in 1d'],
    [2 * 24 * HOUR + 3 * HOUR + 40 * MIN, 'due in 2d 3h'],
  ])('future offset %i ms -> %s', (offset, expected) => {
    expect(slaCountdown(at(offset), NOW)).toBe(expected);
  });

  it.each([
    [-1, 'overdue by less than a minute'],
    [-59_999, 'overdue by less than a minute'],
    [-MIN, 'overdue by 1m'],
    [-40 * MIN, 'overdue by 40m'],
    [-(HOUR + 5 * MIN), 'overdue by 1h 5m'],
    [-(25 * HOUR), 'overdue by 1d 1h'],
  ])('past offset %i ms -> %s', (offset, expected) => {
    expect(slaCountdown(at(offset), NOW)).toBe(expected);
  });
});

describe('isOpenSla', () => {
  it('is true only for states that still have a running clock', () => {
    expect(['OnTrack', 'AtRisk', 'Breached'].every((s) => isOpenSla(s as never))).toBe(true);
    expect(['Met', 'Missed'].some((s) => isOpenSla(s as never))).toBe(false);
  });
});
