import {
  barPercent,
  defaultRange,
  formatMinutes,
  formatPercent,
  rangeProblem,
} from './reports.models';

describe('formatMinutes', () => {
  it.each([
    [0, '0 min'],
    [59, '59 min'],
    [60, '1 h'],
    [61, '1 h 1 min'],
    [135, '2 h 15 min'],
    [1440, '24 h'],
  ])('%i minutes -> %s', (minutes, text) => {
    expect(formatMinutes(minutes)).toBe(text);
  });

  it('rounds fractional minutes', () => {
    expect(formatMinutes(59.6)).toBe('1 h');
  });
});

describe('formatPercent', () => {
  it('uses one decimal and a spaced sign', () => {
    expect(formatPercent(93.5)).toBe('93.5 %');
    expect(formatPercent(100)).toBe('100.0 %');
  });
});

describe('barPercent', () => {
  it('is relative to the max and safe when the max is 0', () => {
    expect(barPercent(50, 200)).toBe(25);
    expect(barPercent(0, 0)).toBe(0);
  });
});

describe('defaultRange', () => {
  it('ends today and starts three months earlier, clamping month overflow', () => {
    expect(defaultRange(new Date(2026, 9, 3))).toEqual({ from: '2026-07-03', to: '2026-10-03' });
    expect(defaultRange(new Date(2026, 4, 31))).toEqual({ from: '2026-02-28', to: '2026-05-31' });
  });
});

describe('rangeProblem', () => {
  it('flags to <= from and ranges over 366 days', () => {
    expect(rangeProblem('2026-01-01', '2026-01-01')).not.toBeNull();
    expect(rangeProblem('2026-02-01', '2026-01-01')).not.toBeNull();
    expect(rangeProblem('2025-01-01', '2026-01-03')).not.toBeNull();
    expect(rangeProblem('2025-01-01', '2026-01-02')).toBeNull();
  });
});
