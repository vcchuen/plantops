export type ReportKind = 'mttr' | 'sla' | 'downtime';

export interface GroupOption {
  value: string;
  label: string;
}

export interface ReportResponse<TRow> {
  from: string;
  to: string;
  groupBy: string;
  rows: TRow[];
}

export interface MttrRow {
  key: string;
  label: string;
  workOrders: number;
  meanRepairMinutes: number;
}

export interface SlaRow {
  key: string;
  label: string;
  completed: number;
  metSla: number;
  compliancePercent: number;
}

export interface DowntimeRow {
  key: string;
  label: string;
  events: number;
  downtimeMinutes: number;
}

/** One shape for the shared card, whatever the endpoint returned. */
export interface DisplayRow {
  key: string;
  label: string;
  count: number;
  value: number;
  formatted: string;
}

export interface ReportConfig {
  kind: ReportKind;
  endpoint: string;
  countHeader: string;
  valueHeader: string;
  groups: GroupOption[];
  toRows: (rows: never[]) => DisplayRow[];
}

/** "2 h 15 min", "45 min", "3 h", "0 min". */
export function formatMinutes(minutes: number): string {
  const total = Math.max(0, Math.round(minutes));
  const h = Math.floor(total / 60);
  const m = total % 60;
  if (h === 0) return `${m} min`;
  return m === 0 ? `${h} h` : `${h} h ${m} min`;
}

export function formatPercent(value: number): string {
  return `${value.toFixed(1)} %`;
}

const mttrRows = (rows: MttrRow[]): DisplayRow[] =>
  rows.map((r) => ({
    key: r.key,
    label: r.label,
    count: r.workOrders,
    value: r.meanRepairMinutes,
    formatted: formatMinutes(r.meanRepairMinutes),
  }));

const slaRows = (rows: SlaRow[]): DisplayRow[] =>
  rows.map((r) => ({
    key: r.key,
    label: r.label,
    count: r.completed,
    value: r.compliancePercent,
    formatted: formatPercent(r.compliancePercent),
  }));

const downtimeRows = (rows: DowntimeRow[]): DisplayRow[] =>
  rows.map((r) => ({
    key: r.key,
    label: r.label,
    count: r.events,
    value: r.downtimeMinutes,
    formatted: formatMinutes(r.downtimeMinutes),
  }));

export const REPORTS: Record<ReportKind, ReportConfig> = {
  mttr: {
    kind: 'mttr',
    endpoint: '/api/reports/mttr',
    countHeader: 'Work orders',
    valueHeader: 'Mean time to repair',
    groups: [
      { value: 'line', label: 'Production line' },
      { value: 'asset', label: 'Asset' },
      { value: 'month', label: 'Month' },
    ],
    toRows: mttrRows as ReportConfig['toRows'],
  },
  sla: {
    kind: 'sla',
    endpoint: '/api/reports/sla-compliance',
    countHeader: 'Completed',
    valueHeader: 'SLA compliance',
    groups: [
      { value: 'priority', label: 'Priority' },
      { value: 'month', label: 'Month' },
    ],
    toRows: slaRows as ReportConfig['toRows'],
  },
  downtime: {
    kind: 'downtime',
    endpoint: '/api/reports/downtime',
    countHeader: 'Events',
    valueHeader: 'Downtime',
    groups: [
      { value: 'line', label: 'Production line' },
      { value: 'month', label: 'Month' },
    ],
    toRows: downtimeRows as ReportConfig['toRows'],
  },
};

/** Bar length relative to the largest row; 0 when everything is 0 (no divide-by-zero). */
export function barPercent(value: number, max: number): number {
  return max > 0 ? Math.min(100, (value / max) * 100) : 0;
}

const ISO_DATE = /^\d{4}-\d{2}-\d{2}$/;
const MAX_RANGE_DAYS = 366;

export function toIsoDate(d: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
}

/**
 * Default range: three months ending today, from the browser's LOCAL calendar date. The server
 * reads these strings as factory-local days (Asia/Kuala_Lumpur), so a user far from Penang may
 * see "today" differ by a day; acceptable for a default the user can edit.
 */
export function defaultRange(now: Date = new Date()): { from: string; to: string } {
  const from = new Date(now.getFullYear(), now.getMonth() - 3, now.getDate());
  // 31 May minus 3 months overflows into March; clamp to the last day of the target month.
  if (from.getDate() !== now.getDate()) from.setDate(0);
  return { from: toIsoDate(from), to: toIsoDate(now) };
}

export function validDateOrNull(value: string | undefined): string | null {
  return value && ISO_DATE.test(value) ? value : null;
}

/** Null when the range is acceptable; otherwise the hint to show. Mirrors the server's 400s. */
export function rangeProblem(from: string, to: string): string | null {
  if (to <= from) return 'The end date must be after the start date.';
  // Noon UTC on both sides keeps the day difference exact regardless of DST or timezone.
  const days = (Date.parse(`${to}T12:00:00Z`) - Date.parse(`${from}T12:00:00Z`)) / 86_400_000;
  return days > MAX_RANGE_DAYS ? `The range can be at most ${MAX_RANGE_DAYS} days.` : null;
}

export function pickGroup(value: string | undefined, kind: ReportKind, fallback: string): string {
  return REPORTS[kind].groups.some((g) => g.value === value) ? value! : fallback;
}
