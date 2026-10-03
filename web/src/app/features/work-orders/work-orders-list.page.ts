import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { describeError } from '../assets/assets.models';
import { SlaBadge } from './sla-badge';
import {
  PRIORITY_LABELS,
  Priority,
  STATUS_LABELS,
  WorkOrderPage,
  WorkOrderStatus,
} from './work-orders.models';

const positiveInt = (fallback: number) => (value: unknown) => {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : fallback;
};

@Component({
  selector: 'app-work-orders-list-page',
  imports: [
    DatePipe,
    RouterLink,
    SlaBadge,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTableModule,
  ],
  templateUrl: './work-orders-list.page.html',
  styleUrl: './work-orders-list.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkOrdersListPage {
  private readonly router = inject(Router);

  // URL is the source of truth, as on the assets list.
  readonly status = input<string | undefined>();
  readonly priority = input<string | undefined>();
  readonly mine = input<string | undefined>();
  readonly page = input(1, { transform: positiveInt(1) });
  readonly pageSize = input(25, { transform: positiveInt(25) });

  protected readonly onlyMine = computed(() => this.mine() === 'true');

  protected readonly orders = httpResource<WorkOrderPage>(() => {
    const all: Record<string, string | number | undefined> = {
      status: this.status(),
      priority: this.priority(),
      // Absent rather than mine=false: the server treats only "true" as a filter.
      mine: this.onlyMine() ? 'true' : undefined,
      page: this.page(),
      pageSize: this.pageSize(),
    };
    const params: Record<string, string | number> = {};
    for (const [key, value] of Object.entries(all)) {
      if (value !== undefined && value !== '') params[key] = value;
    }
    return { url: '/api/work-orders', params };
  });

  // Same reason as the assets list: a new request empties value(), so remember the last page.
  protected readonly current = linkedSignal<WorkOrderPage | undefined, WorkOrderPage | undefined>({
    source: () => (this.orders.hasValue() ? this.orders.value() : undefined),
    computation: (next, previous) => next ?? previous?.value,
  });

  protected readonly errorMessage = computed(() =>
    this.orders.error() ? describeError(this.orders.error()) : null,
  );

  protected readonly columns = [
    'number',
    'title',
    'asset',
    'priority',
    'status',
    'sla',
    'due',
    'assignee',
  ];
  protected readonly pageSizeOptions = [10, 25, 50, 100];
  protected readonly priorities = Object.keys(PRIORITY_LABELS) as Priority[];
  protected readonly priorityLabels = PRIORITY_LABELS;
  protected readonly statuses = Object.keys(STATUS_LABELS) as WorkOrderStatus[];
  protected readonly statusLabels = STATUS_LABELS;

  // Typed lookups: the table's `let w` row is untyped in templates.
  protected priorityLabel(p: Priority): string {
    return PRIORITY_LABELS[p];
  }

  protected statusLabel(s: WorkOrderStatus): string {
    return STATUS_LABELS[s];
  }

  protected onFilter(key: 'status' | 'priority', value: string): void {
    this.navigate({ [key]: value || null });
  }

  protected onMine(checked: boolean): void {
    this.navigate({ mine: checked ? 'true' : null });
  }

  protected onPage(event: PageEvent): void {
    this.router.navigate([], {
      queryParams: { page: event.pageIndex + 1, pageSize: event.pageSize },
      queryParamsHandling: 'merge',
    });
  }

  private navigate(filter: Record<string, string | null>): void {
    this.router.navigate([], {
      queryParams: { ...filter, page: 1 },
      queryParamsHandling: 'merge',
    });
  }
}
