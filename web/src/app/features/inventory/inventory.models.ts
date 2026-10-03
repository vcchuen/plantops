export type ReservationStatus = 'Active' | 'Consumed' | 'Released';

export interface PartListItem {
  id: string;
  partNumber: string;
  name: string;
  unit: string;
  binLocation: string;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
  reorderLevel: number;
  isLowStock: boolean;
}

export interface PartPage {
  items: PartListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PartReservation {
  id: string;
  workOrderId: string;
  workOrderNumber: string;
  quantity: number;
  status: ReservationStatus;
  reservedAt: string;
  reservedByName: string;
}

export type PartDetail = PartListItem & { reservations: PartReservation[] };

export interface ReservationItem {
  id: string;
  partId: string;
  partNumber: string;
  partName: string;
  unit: string;
  quantity: number;
  status: ReservationStatus;
  reservedAt: string;
  reservedByName: string;
}

export interface NewPart {
  partNumber: string;
  name: string;
  unit: string;
  binLocation: string;
  reorderLevel: number;
}

export interface NewReservation {
  partId: string;
  workOrderId: string;
  quantity: number;
}

export const RESERVATION_STATUS_LABELS: Record<ReservationStatus, string> = {
  Active: 'Reserved',
  Consumed: 'Consumed',
  Released: 'Released',
};
