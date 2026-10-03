import { SlaState } from './work-orders.models';

/** Only open work orders have a live countdown; Met/Missed are settled facts. */
export function isOpenSla(state: SlaState): boolean {
  return state === 'OnTrack' || state === 'AtRisk' || state === 'Breached';
}

function formatMinutes(total: number): string {
  const days = Math.floor(total / 1440);
  const hours = Math.floor((total % 1440) / 60);
  const minutes = total % 60;
  // Two units at most: "2d 3h" is readable, "2d 3h 12m" is noise on a deadline that spans days.
  if (days > 0) return hours > 0 ? `${days}d ${hours}h` : `${days}d`;
  if (hours > 0) return minutes > 0 ? `${hours}h ${minutes}m` : `${hours}h`;
  return `${minutes}m`;
}

/** Pure on purpose: `now` is passed in so the caller's 60 s signal drives it and tests need no clock. */
export function slaCountdown(dueAt: string, now: number): string {
  const diff = Date.parse(dueAt) - now;
  // floor, so 59 s left reads "less than a minute" rather than rounding up to a minute we do not have.
  const minutes = Math.floor(Math.abs(diff) / 60_000);
  if (diff >= 0) {
    return minutes < 1 ? 'due in less than a minute' : `due in ${formatMinutes(minutes)}`;
  }
  return minutes < 1 ? 'overdue by less than a minute' : `overdue by ${formatMinutes(minutes)}`;
}
