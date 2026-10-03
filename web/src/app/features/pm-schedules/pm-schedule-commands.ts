import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { describeCommandError } from '../work-orders/work-order-commands';
import { NewPmSchedule, PmScheduleBody, PmScheduleDetail } from './pm-schedules.models';

@Injectable({ providedIn: 'root' })
export class PmScheduleCommands {
  private readonly http = inject(HttpClient);

  async create(body: NewPmSchedule): Promise<PmScheduleDetail> {
    const response = await firstValueFrom(
      this.http.post<PmScheduleDetail>('/api/pm-schedules', body, { observe: 'response' }),
    );
    if (!response.body) throw new Error('Create returned no body');
    return response.body;
  }

  /** `etag` goes back byte-for-byte, quotes included; the server treats it as an opaque token. */
  async update(id: string, body: PmScheduleBody, etag: string): Promise<string | null> {
    const response = await firstValueFrom(
      this.http.put<void>(`/api/pm-schedules/${encodeURIComponent(id)}`, body, {
        headers: { 'If-Match': etag },
        observe: 'response',
      }),
    );
    return response.headers.get('ETag');
  }

  async setActive(id: string, active: boolean, etag: string): Promise<string | null> {
    const response = await firstValueFrom(
      this.http.post<void>(
        `/api/pm-schedules/${encodeURIComponent(id)}/${active ? 'activate' : 'deactivate'}`,
        {},
        { headers: { 'If-Match': etag }, observe: 'response' },
      ),
    );
    return response.headers.get('ETag');
  }
}

/** describeCommandError() words 403 and 428 in terms of work orders; PM schedules need their own. */
export function describePmError(error: unknown): string {
  const status = (error as { status?: number } | undefined)?.status;
  if (status === 403) return 'Only supervisors and admins can change PM schedules.';
  if (status === 428) return 'The page lost track of the schedule version. Reload and try again.';
  return describeCommandError(error);
}
