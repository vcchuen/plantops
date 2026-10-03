import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { SessionStore } from '../../core/auth/session.store';
import { describeError } from '../assets/assets.models';
import { PmScheduleListItem, pmStatus } from './pm-schedules.models';

@Component({
  selector: 'app-pm-schedules-list-page',
  imports: [
    DatePipe,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTableModule,
  ],
  templateUrl: './pm-schedules-list.page.html',
  styleUrl: './pm-schedules-list.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PmSchedulesListPage {
  private readonly router = inject(Router);
  protected readonly session = inject(SessionStore);

  // URL is the source of truth, as on the other lists.
  readonly active = input<string | undefined>();
  readonly assetId = input<string | undefined>();

  protected readonly schedules = httpResource<PmScheduleListItem[]>(() => {
    const params: Record<string, string> = {};
    // Anything but "true"/"false" means "all": the server would 400 on other values.
    if (this.active() === 'true' || this.active() === 'false') params['active'] = this.active()!;
    if (this.assetId()) params['assetId'] = this.assetId()!;
    return { url: '/api/pm-schedules', params };
  });

  protected readonly errorMessage = computed(() =>
    this.schedules.error() ? describeError(this.schedules.error()) : null,
  );

  protected readonly columns = ['asset', 'title', 'interval', 'nextDue', 'lead', 'status'];
  protected readonly status = pmStatus;

  protected onActive(value: string): void {
    this.router.navigate([], {
      queryParams: { active: value || null },
      queryParamsHandling: 'merge',
    });
  }
}
