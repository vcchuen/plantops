export interface HistoryEntry {
  eventType: string;
  actorName: string;
  occurredAt: string;
  payload: unknown;
}

const EVENT_LABELS: Record<string, string> = {
  AssetRegistered: 'Asset registered',
  AssetDetailsUpdated: 'Details updated',
  AssetRelocated: 'Relocated',
  AssetCriticalityChanged: 'Criticality changed',
  AssetDecommissioned: 'Decommissioned',
};

/** Unknown event types still read as a sentence ("WorkOrderApproved" -> "Work order approved"). */
export function eventLabel(eventType: string): string {
  const known = EVENT_LABELS[eventType];
  if (known) return known;
  const words = eventType.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

function humanizeKey(key: string): string {
  const words = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
  return words.charAt(0).toUpperCase() + words.slice(1);
}

/**
 * Payload shape differs per event and is stored as JSON, possibly as a string. Show scalar
 * facts only, and skip ids: a GUID tells an auditor nothing the name beside it does not.
 */
export function describePayload(payload: unknown): string | null {
  let value = payload;
  if (typeof payload === 'string') {
    try {
      value = JSON.parse(payload);
    } catch {
      return payload.trim() || null;
    }
  }
  if (typeof value !== 'object' || value === null) return null;
  const parts = Object.entries(value as Record<string, unknown>)
    .filter(
      ([key, v]) =>
        !/id$/i.test(key) &&
        v !== null &&
        v !== '' &&
        ['string', 'number', 'boolean'].includes(typeof v),
    )
    .map(([key, v]) => `${humanizeKey(key)}: ${v}`);
  return parts.length ? parts.join(' · ') : null;
}
