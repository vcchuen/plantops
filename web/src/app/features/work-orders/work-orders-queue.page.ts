import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
} from '@angular/core';
import { httpResource } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { describeError } from '../assets/assets.models';
import { QueueCard } from './queue-card';
import { QueueItem } from './work-orders.models';

@Component({
  selector: 'app-work-orders-queue-page',
  imports: [QueueCard, MatIconModule, MatProgressBarModule, MatSlideToggleModule],
  templateUrl: './work-orders-queue.page.html',
  styleUrl: './work-orders-queue.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkOrdersQueuePage {
  private readonly router = inject(Router);

  // URL is the source of truth, as on the list page.
  readonly dueToday = input<string | undefined>();

  protected readonly onlyToday = computed(() => this.dueToday() === 'true');

  protected readonly queue = httpResource<QueueItem[]>(() => ({
    url: '/api/work-orders/queue',
    // Absent rather than dueToday=false: the server treats only "true" as a filter.
    params: this.onlyToday() ? { dueToday: 'true' } : undefined,
  }));

  // A new request empties value(), so remember the last answer instead of flashing the empty state.
  protected readonly current = linkedSignal<QueueItem[] | undefined, QueueItem[] | undefined>({
    source: () => (this.queue.hasValue() ? this.queue.value() : undefined),
    computation: (next, previous) => next ?? previous?.value,
  });

  protected readonly errorMessage = computed(() =>
    this.queue.error() ? describeError(this.queue.error()) : null,
  );

  protected onToggle(checked: boolean): void {
    this.router.navigate([], {
      queryParams: { dueToday: checked ? 'true' : null },
      queryParamsHandling: 'merge',
    });
  }

  protected open(item: QueueItem): void {
    this.router.navigate(['/work-orders', item.id]);
  }
}
