import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { SessionStore } from '../../core/auth/session.store';
import { describeError } from '../assets/assets.models';
import { isConflict } from '../work-orders/work-order-commands';
import { PRIORITY_LABELS } from '../work-orders/work-orders.models';
import { PmScheduleCommands, describePmError } from './pm-schedule-commands';
import { PmScheduleDetail, pmStatus } from './pm-schedules.models';

@Component({
  selector: 'app-pm-schedule-detail-page',
  imports: [DatePipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule],
  templateUrl: './pm-schedule-detail.page.html',
  styleUrl: './pm-schedule-detail.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PmScheduleDetailPage {
  private readonly commands = inject(PmScheduleCommands);
  private readonly snackBar = inject(MatSnackBar);
  protected readonly session = inject(SessionStore);

  readonly id = input.required<string>();

  protected readonly detail = httpResource<PmScheduleDetail>(
    () => `/api/pm-schedules/${encodeURIComponent(this.id())}`,
  );

  protected readonly busy = signal(false);
  protected readonly actionError = signal<string | null>(null);
  // Announced through a live region: a screen-reader user otherwise hears nothing after clicking.
  protected readonly notice = signal('');

  protected readonly notFound = computed(
    () => (this.detail.error() as HttpErrorResponse | undefined)?.status === 404,
  );
  protected readonly errorMessage = computed(() =>
    this.detail.error() && !this.notFound() ? describeError(this.detail.error()) : null,
  );

  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly status = pmStatus;

  protected async setActive(active: boolean): Promise<void> {
    // headers() holds the response headers of the last successful load: our If-Match.
    const etag = this.detail.headers()?.get('ETag');
    if (!etag) {
      this.actionError.set('The schedule version is unknown. Reload the page and try again.');
      return;
    }
    this.busy.set(true);
    this.actionError.set(null);
    this.notice.set('');
    try {
      await this.commands.setActive(this.id(), active, etag);
      this.notice.set(active ? 'Schedule activated.' : 'Schedule deactivated.');
    } catch (error) {
      if (isConflict(error)) {
        this.snackBar.open(
          'Someone else changed this schedule — showing the latest version.',
          'Dismiss',
          { duration: 8000 },
        );
      } else {
        this.actionError.set(describePmError(error));
      }
    } finally {
      // Reload after every outcome: success changed state and 412 means our copy is stale.
      this.detail.reload();
      this.busy.set(false);
    }
  }
}
