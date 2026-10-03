import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { isProblem } from '../assets/assets.models';
import { describeCommandError } from '../work-orders/work-order-commands';
import { NewPart, NewReservation, PartDetail, ReservationItem } from './inventory.models';

@Injectable({ providedIn: 'root' })
export class InventoryCommands {
  private readonly http = inject(HttpClient);

  createPart(body: NewPart): Promise<PartDetail> {
    return firstValueFrom(this.http.post<PartDetail>('/api/inventory/parts', body));
  }

  async receive(partId: string, quantity: number): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(`/api/inventory/parts/${encodeURIComponent(partId)}/receive`, {
        quantity,
      }),
    );
  }

  reserve(body: NewReservation): Promise<ReservationItem> {
    return firstValueFrom(this.http.post<ReservationItem>('/api/inventory/reservations', body));
  }

  async release(reservationId: string): Promise<void> {
    await firstValueFrom(
      this.http.post<void>(
        `/api/inventory/reservations/${encodeURIComponent(reservationId)}/release`,
        {},
      ),
    );
  }
}

/**
 * 409 ("Only 2 pcs of X available") and 400 carry the useful sentence in `detail`; the title is
 * just the status text, so prefer the detail alone for those.
 */
export function describeInventoryError(error: unknown): string {
  const status = (error as { status?: number } | undefined)?.status;
  const body = (error as { error?: unknown } | undefined)?.error;
  if ((status === 409 || status === 400) && isProblem(body) && (body.detail || body.title)) {
    return (body.detail || body.title) as string;
  }
  return describeCommandError(error);
}
