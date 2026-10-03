export type WorkOrderStatus =
  | 'Submitted'
  | 'Approved'
  | 'Rejected'
  | 'Assigned'
  | 'InProgress'
  | 'Completed'
  | 'Closed'
  | 'Cancelled';
export type Priority = 'P1' | 'P2' | 'P3' | 'P4';
export type SlaState = 'OnTrack' | 'AtRisk' | 'Breached' | 'Met' | 'Missed';
export type WorkOrderAction =
  'approve' | 'reject' | 'assign' | 'start' | 'complete' | 'close' | 'cancel';

export interface PersonRef {
  id: string;
  name: string;
}

export interface WorkOrderListItem {
  id: string;
  number: string;
  title: string;
  assetId: string;
  assetTag: string;
  assetName: string;
  priority: Priority;
  status: WorkOrderStatus;
  // null for Rejected and Cancelled: no clock runs.
  slaState: SlaState | null;
  dueAt: string;
  assignedTo: PersonRef | null;
  submittedAt: string;
}

export interface WorkOrderPage {
  items: WorkOrderListItem[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface WorkOrderDetail {
  id: string;
  number: string;
  title: string;
  description: string;
  assetId: string;
  assetTag: string;
  assetName: string;
  priority: Priority;
  assetDown: boolean;
  status: WorkOrderStatus;
  // null for Rejected and Cancelled: no clock runs.
  slaState: SlaState | null;
  dueAt: string;
  submittedAt: string;
  approvedAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
  closedAt: string | null;
  reportedBy: PersonRef;
  approvedBy: PersonRef | null;
  assignedTo: PersonRef | null;
  resolution: string | null;
  rejectionReason: string | null;
  cancellationReason: string | null;
  allowedActions: WorkOrderAction[];
}

export interface NewWorkOrder {
  assetId: string;
  title: string;
  description: string;
  priority: Priority;
  assetDown: boolean;
}

export interface Technician {
  id: string;
  name: string;
  email: string;
  roles?: string[];
}

export const PRIORITY_LABELS: Record<Priority, string> = {
  P1: 'P1 · Critical (4h)',
  P2: 'P2 · High (8h)',
  P3: 'P3 · Medium (24h)',
  P4: 'P4 · Low (72h)',
};

export const STATUS_LABELS: Record<WorkOrderStatus, string> = {
  Submitted: 'Submitted',
  Approved: 'Approved',
  Rejected: 'Rejected',
  Assigned: 'Assigned',
  InProgress: 'In progress',
  Completed: 'Completed',
  Closed: 'Closed',
  Cancelled: 'Cancelled',
};

// Icon and text both carry the state; colour is only a third, redundant cue.
export const SLA_LABELS: Record<SlaState, { icon: string; text: string }> = {
  OnTrack: { icon: 'check_circle', text: 'On track' },
  AtRisk: { icon: 'warning', text: 'At risk' },
  Breached: { icon: 'error', text: 'Breached' },
  Met: { icon: 'task_alt', text: 'Met' },
  Missed: { icon: 'cancel', text: 'Missed' },
};

export const ACTION_LABELS: Record<WorkOrderAction, string> = {
  approve: 'Approve',
  reject: 'Reject',
  assign: 'Assign',
  start: 'Start work',
  complete: 'Complete',
  close: 'Close',
  cancel: 'Cancel work order',
};

/** Render order of the action bar. The server decides WHICH appear; this only fixes their order. */
export const ACTION_ORDER: WorkOrderAction[] = [
  'approve',
  'assign',
  'start',
  'complete',
  'close',
  'reject',
  'cancel',
];
