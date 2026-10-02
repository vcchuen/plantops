import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  linkedSignal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { httpResource } from '@angular/common/http';
import { Router, RouterLink } from '@angular/router';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { debounceTime, distinctUntilChanged, Subject } from 'rxjs';
import {
  AssetPage,
  AssetStatus,
  CRITICALITY_LABELS,
  Criticality,
  ProductionLine,
  STATUS_LABELS,
  describeError,
} from './assets.models';

const positiveInt = (fallback: number) => (value: unknown) => {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : fallback;
};

@Component({
  selector: 'app-assets-list-page',
  imports: [
    RouterLink,
    MatChipsModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatSelectModule,
    MatTableModule,
    MatTooltipModule,
  ],
  templateUrl: './assets-list.page.html',
  styleUrl: './assets-list.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AssetsListPage {
  private readonly router = inject(Router);

  // The URL is the source of truth. withComponentInputBinding() maps each query param to
  // the input of the same name; a param that is absent arrives as undefined.
  readonly lineId = input<string | undefined>();
  readonly criticality = input<string | undefined>();
  readonly status = input<string | undefined>();
  readonly search = input<string | undefined>();
  readonly page = input(1, { transform: positiveInt(1) });
  readonly pageSize = input(25, { transform: positiveInt(25) });

  protected readonly assets = httpResource<AssetPage>(() => {
    const all: Record<string, string | number | undefined> = {
      lineId: this.lineId(),
      criticality: this.criticality(),
      status: this.status(),
      search: this.search()?.trim(),
      page: this.page(),
      pageSize: this.pageSize(),
    };
    const params: Record<string, string | number> = {};
    for (const [key, value] of Object.entries(all)) {
      if (value !== undefined && value !== '') params[key] = value;
    }
    return { url: '/api/assets', params };
  });

  protected readonly lines = httpResource<ProductionLine[]>(() => '/api/assets/lines');

  // A new request (changed params) puts httpResource into 'loading', where value() is
  // undefined; only a plain reload() keeps the old value. Remember the last good page so the
  // table does not flash empty on every filter change.
  protected readonly current = linkedSignal<AssetPage | undefined, AssetPage | undefined>({
    source: () => (this.assets.hasValue() ? this.assets.value() : undefined),
    computation: (next, previous) => next ?? previous?.value,
  });

  protected readonly errorMessage = computed(() =>
    this.assets.error() ? describeError(this.assets.error()) : null,
  );

  protected readonly columns = ['tag', 'name', 'line', 'station', 'criticality', 'status'];
  protected readonly pageSizeOptions = [10, 25, 50, 100];
  protected readonly criticalities = Object.keys(CRITICALITY_LABELS) as Criticality[];
  protected readonly criticalityLabels = CRITICALITY_LABELS;
  protected readonly statusLabels = STATUS_LABELS;

  private readonly searchTyped = new Subject<string>();

  constructor() {
    // The one place RxJS is used: typing is a stream of events over time, and debounce/
    // distinct have no clean signal equivalent. Everything else is signals.
    this.searchTyped
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((value) => this.navigate({ search: value.trim() || null }));
  }

  // Templates cannot use `as`, and the table's `let a` row is untyped, so typed lookups live here.
  protected critShort(c: Criticality): string {
    return CRITICALITY_LABELS[c].short;
  }

  protected critDescription(c: Criticality): string {
    return CRITICALITY_LABELS[c].description;
  }

  protected statusLabel(s: AssetStatus): string {
    return STATUS_LABELS[s];
  }

  protected onSearch(value: string): void {
    this.searchTyped.next(value);
  }

  protected onFilter(key: 'lineId' | 'criticality' | 'status', value: string): void {
    this.navigate({ [key]: value || null });
  }

  protected onPage(event: PageEvent): void {
    this.router.navigate([], {
      queryParams: { page: event.pageIndex + 1, pageSize: event.pageSize },
      queryParamsHandling: 'merge',
    });
  }

  // null removes a param under queryParamsHandling 'merge'. Any filter change goes back to page 1.
  private navigate(filter: Record<string, string | null>): void {
    this.router.navigate([], {
      queryParams: { ...filter, page: 1 },
      queryParamsHandling: 'merge',
    });
  }
}
