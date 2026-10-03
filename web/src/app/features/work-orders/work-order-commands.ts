import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { isProblem } from '../assets/assets.models';
import { NewWorkOrder, WorkOrderAction, WorkOrderDetail } from './work-orders.models';

@Injectable({ providedIn: 'root' })
export class WorkOrderCommands {
  private readonly http = inject(HttpClient);

  async create(body: NewWorkOrder): Promise<WorkOrderDetail> {
    const response = await firstValueFrom(
      this.http.post<WorkOrderDetail>('/api/work-orders', body, { observe: 'response' }),
    );
    if (!response.body) throw new Error('Create returned no body');
    return response.body;
  }

  /**
   * `etag` is sent back byte-for-byte, quotes included: the server compares it as an opaque
   * token (RFC 9110), so "re-formatting" it would turn every command into a 412.
   * Resolves with the new ETag so the caller could chain a second command without re-reading.
   */
  async execute(
    id: string,
    action: WorkOrderAction,
    etag: string,
    body: object = {},
  ): Promise<string | null> {
    const response = await firstValueFrom(
      this.http.post<void>(`/api/work-orders/${encodeURIComponent(id)}/${action}`, body, {
        headers: { 'If-Match': etag },
        observe: 'response',
      }),
    );
    return response.headers.get('ETag');
  }
}

export function isConflict(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 412;
}

/** describeError() words 403 as "you cannot view this", which is wrong for a command. */
export function describeCommandError(error: unknown): string {
  const status = (error as { status?: number } | undefined)?.status;
  const body = (error as { error?: unknown } | undefined)?.error;
  const problem = isProblem(body) ? [body.title, body.detail].filter(Boolean).join(': ') : '';
  if (status === 403) return problem || 'You are not allowed to do that on this work order.';
  if (status === 428) return 'The page lost track of the work order version. Reload and try again.';
  return problem || 'Something went wrong while talking to the API. Please try again.';
}
