import { Priority } from '../work-orders/work-orders.models';

export interface PmScheduleListItem {
  id: string;
  assetId: string;
  assetTag: string;
  assetName: string;
  title: string;
  intervalDays: number;
  leadDays: number;
  priority: Priority;
  // Date-only 'YYYY-MM-DD': a calendar day in plant-local terms, not an instant.
  nextDueOn: string;
  isActive: boolean;
}

export interface PmScheduleDetail extends PmScheduleListItem {
  instructions: string;
}

export interface PmScheduleBody {
  title: string;
  instructions: string;
  intervalDays: number;
  leadDays: number;
  priority: Priority;
  nextDueOn: string;
}

export interface NewPmSchedule extends PmScheduleBody {
  assetId: string;
}

// Icon and text both carry the state; colour is never the only cue.
export const PM_STATUS = {
  active: { icon: 'check_circle', text: 'Active' },
  inactive: { icon: 'pause_circle', text: 'Inactive' },
} as const;

export function pmStatus(isActive: boolean): { icon: string; text: string } {
  return isActive ? PM_STATUS.active : PM_STATUS.inactive;
}
