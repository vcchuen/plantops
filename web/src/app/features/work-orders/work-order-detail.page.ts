import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  input,
  signal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { firstValueFrom } from 'rxjs';
import { SessionStore } from '../../core/auth/session.store';
import { describeError } from '../assets/assets.models';
import { InventoryCommands, describeInventoryError } from '../inventory/inventory-commands';
import { RESERVATION_STATUS_LABELS, ReservationItem } from '../inventory/inventory.models';
import { ReservePartDialog, ReservePartDialogData } from '../inventory/reserve-part.dialog';
import { HistoryEntry } from '../shared/history.models';
import { HistoryTimeline } from '../shared/history-timeline';
import { isOpenSla, slaCountdown } from './sla';
import { SlaBadge } from './sla-badge';
import {
  ActionDialogData,
  ActionDialogResult,
  InputAction,
  WorkOrderActionDialog,
} from './work-order-action.dialog';
import { WorkOrderCommands, describeCommandError, isConflict } from './work-order-commands';
import { WorkOrderComments } from './work-order-comments';
import {
  ACTION_LABELS,
  ACTION_ORDER,
  PRIORITY_LABELS,
  STATUS_LABELS,
  SlaState,
  WorkOrderAction,
  WorkOrderDetail,
} from './work-orders.models';

const NEEDS_INPUT = new Set<WorkOrderAction>(['approve', 'reject', 'assign', 'complete', 'cancel']);

@Component({
  selector: 'app-work-order-detail-page',
  imports: [
    DatePipe,
    RouterLink,
    HistoryTimeline,
    SlaBadge,
    WorkOrderComments,
    MatButtonModule,
    MatIconModule,
    MatProgressBarModule,
  ],
  templateUrl: './work-order-detail.page.html',
  styleUrl: './work-order-detail.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkOrderDetailPage {
  private readonly commands = inject(WorkOrderCommands);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly inventory = inject(InventoryCommands);
  private readonly session = inject(SessionStore);

  readonly id = input.required<string>();

  private readonly encodedId = computed(() => encodeURIComponent(this.id()));
  protected readonly detail = httpResource<WorkOrderDetail>(
    () => `/api/work-orders/${this.encodedId()}`,
  );
  protected readonly history = httpResource<HistoryEntry[]>(
    () => `/api/work-orders/${this.encodedId()}/history`,
  );

  protected readonly reservations = httpResource<ReservationItem[]>(() => ({
    url: '/api/inventory/reservations',
    params: { workOrderId: this.id() },
  }));
  protected readonly reservationsError = computed(() =>
    this.reservations.error() ? describeError(this.reservations.error()) : null,
  );
  protected readonly partsError = signal<string | null>(null);

  // Hides the buttons only; the server enforces who may reserve or release. Mirrors its rule:
  // the order must be Assigned/InProgress and the user either works it (start/complete are
  // only offered to the assigned technician) or is a supervisor/admin.
  protected readonly canReserve = computed(() => {
    if (!this.detail.hasValue()) return false;
    const w = this.detail.value();
    if (w.status !== 'Assigned' && w.status !== 'InProgress') return false;
    return (
      w.allowedActions.includes('start') ||
      w.allowedActions.includes('complete') ||
      this.session.isSupervisorOrAdmin()
    );
  });

  protected readonly isFinished = computed(() => {
    const status = this.detail.hasValue() ? this.detail.value().status : null;
    return status === 'Completed' || status === 'Closed';
  });

  // One shared minute tick drives the countdown and the "breached" flip.
  protected readonly now = signal(Date.now());

  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);

  protected readonly notFound = computed(
    () => (this.detail.error() as HttpErrorResponse | undefined)?.status === 404,
  );
  protected readonly errorMessage = computed(() =>
    this.detail.error() && !this.notFound() ? describeError(this.detail.error()) : null,
  );
  protected readonly historyError = computed(() =>
    this.history.error() ? describeError(this.history.error()) : null,
  );

  // Rendering is driven only by what the server allows; the page holds no transition rules.
  protected readonly visibleActions = computed(() => {
    const allowed = this.detail.hasValue() ? this.detail.value().allowedActions : [];
    return ACTION_ORDER.filter((a) => allowed.includes(a));
  });

  protected readonly countdown = computed(() => {
    if (!this.detail.hasValue()) return null;
    const w = this.detail.value();
    return isOpenSla(w.slaState) ? slaCountdown(w.dueAt, this.now()) : null;
  });

  // The server computed slaState at load time; between reloads an open order can cross DueAt.
  protected readonly effectiveSla = computed((): SlaState | null => {
    if (!this.detail.hasValue()) return null;
    const w = this.detail.value();
    if (w.slaState === null) return null;
    const open = w.slaState === 'OnTrack' || w.slaState === 'AtRisk';
    return open && this.now() > Date.parse(w.dueAt) ? 'Breached' : w.slaState;
  });

  protected readonly actionLabels = ACTION_LABELS;
  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly statusLabels = STATUS_LABELS;
  protected readonly reservationStatusLabels = RESERVATION_STATUS_LABELS;

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 60_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected async reservePart(): Promise<void> {
    const reserved = await firstValueFrom(
      this.dialog
        .open<ReservePartDialog, ReservePartDialogData, boolean>(ReservePartDialog, {
          data: { workOrderId: this.id() },
        })
        .afterClosed(),
    );
    if (reserved) {
      this.partsError.set(null);
      this.reservations.reload();
    }
  }

  protected async release(reservation: ReservationItem): Promise<void> {
    this.partsError.set(null);
    try {
      await this.inventory.release(reservation.id);
    } catch (error) {
      this.partsError.set(describeInventoryError(error));
    } finally {
      this.reservations.reload();
    }
  }

  protected async run(action: WorkOrderAction): Promise<void> {
    if (!this.detail.hasValue()) return;
    // headers() holds the response headers of the last successful load: our If-Match.
    const etag = this.detail.headers()?.get('ETag');
    if (!etag) {
      this.actionError.set('The work order version is unknown. Reload the page and try again.');
      return;
    }

    let body: ActionDialogResult = {};
    if (NEEDS_INPUT.has(action)) {
      const result = await firstValueFrom(
        this.dialog
          .open<WorkOrderActionDialog, ActionDialogData, ActionDialogResult>(
            WorkOrderActionDialog,
            {
              data: {
                action: action as InputAction,
                currentPriority: this.detail.value().priority,
              },
            },
          )
          .afterClosed(),
      );
      if (!result) return;
      body = result;
    }

    this.busy.set(true);
    this.actionError.set(null);
    try {
      await this.commands.execute(this.id(), action, etag, body);
    } catch (error) {
      if (isConflict(error)) {
        this.snackBar.open(
          'Someone else changed this work order — showing the latest version.',
          'Dismiss',
          { duration: 8000 },
        );
      } else {
        this.actionError.set(describeCommandError(error));
      }
    } finally {
      // Reload after every outcome: success changed state, 412 means our copy is stale, and
      // after 403/400 the allowed actions may have changed too.
      this.detail.reload();
      this.history.reload();
      this.busy.set(false);
    }
  }
}
