export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy';

export interface HealthCheckEntry {
  name: string;
  status: HealthStatus;
  description: string | null;
  durationMs: number;
}

export interface HealthReport {
  status: HealthStatus;
  totalDurationMs: number;
  checks: HealthCheckEntry[];
}

/**
 * Runtime guard for the 503 error body: HttpErrorResponse.error is typed `any`,
 * and behind a proxy it can just as well be an HTML/text gateway error page.
 */
export function isHealthReport(body: unknown): body is HealthReport {
  return (
    typeof body === 'object' &&
    body !== null &&
    typeof (body as HealthReport).status === 'string' &&
    Array.isArray((body as HealthReport).checks)
  );
}
