export type Criticality = 'A' | 'B' | 'C';
export type AssetStatus = 'InService' | 'Decommissioned';

export interface ProductionLine {
  id: string;
  code: string;
  name: string;
}

export interface AssetListItem {
  id: string;
  tag: string;
  name: string;
  lineId: string;
  lineName: string;
  station: string;
  criticality: Criticality;
  status: AssetStatus;
}

export interface AssetPage {
  items: AssetListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface AssetDetail {
  id: string;
  tag: string;
  name: string;
  manufacturer: string;
  model: string;
  serialNumber: string | null;
  lineId: string;
  lineCode: string;
  lineName: string;
  station: string;
  criticality: Criticality;
  status: AssetStatus;
  commissionedOn: string;
  decommissionedOn: string | null;
  decommissionReason: string | null;
}

export const CRITICALITY_LABELS: Record<Criticality, { short: string; description: string }> = {
  A: { short: 'Stops line', description: 'A: failure stops the line' },
  B: { short: 'Degrades line', description: 'B: failure degrades the line' },
  C: { short: 'Workaround', description: 'C: a workaround exists' },
};

export const STATUS_LABELS: Record<AssetStatus, string> = {
  InService: 'In service',
  Decommissioned: 'Decommissioned',
};

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
}

/** HttpErrorResponse.error is `any`; behind a proxy it may be an HTML page, so check the shape. */
export function isProblem(body: unknown): body is ProblemDetails {
  return (
    typeof body === 'object' &&
    body !== null &&
    (typeof (body as ProblemDetails).title === 'string' ||
      typeof (body as ProblemDetails).detail === 'string')
  );
}

export function describeError(error: unknown): string {
  const body = (error as { error?: unknown } | undefined)?.error;
  if (isProblem(body)) {
    return [body.title, body.detail].filter(Boolean).join(': ');
  }
  return 'Something went wrong while talking to the API. Please try again.';
}
