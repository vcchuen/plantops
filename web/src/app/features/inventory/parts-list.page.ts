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
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { debounceTime, distinctUntilChanged, firstValueFrom, Subject } from 'rxjs';
import { SessionStore } from '../../core/auth/session.store';
import { describeError } from '../assets/assets.models';
import { PartNewDialog } from './part-new.dialog';
import { PartDetail, PartPage } from './inventory.models';

const positiveInt = (fallback: number) => (value: unknown) => {
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? n : fallback;
};

@Component({
  selector: 'app-parts-list-page',
  imports: [
    RouterLink,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatPaginatorModule,
    MatProgressBarModule,
    MatTableModule,
  ],
  templateUrl: './parts-list.page.html',
  styleUrl: './parts-list.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PartsListPage {
  private readonly router = inject(Router);
  private readonly dialog = inject(MatDialog);
  protected readonly session = inject(SessionStore);

  // The URL is the source of truth (see AssetsListPage).
  readonly search = input<string | undefined>();
  readonly lowStock = input<string | undefined>();
  readonly page = input(1, { transform: positiveInt(1) });
  readonly pageSize = input(25, { transform: positiveInt(25) });

  protected readonly lowStockOnly = computed(() => this.lowStock() === 'true');

  protected readonly parts = httpResource<PartPage>(() => {
    const params: Record<string, string | number | boolean> = {
      page: this.page(),
      pageSize: this.pageSize(),
    };
    const search = this.search()?.trim();
    if (search) params['search'] = search;
    if (this.lowStockOnly()) params['lowStock'] = true;
    return { url: '/api/inventory/parts', params };
  });

  // Keep the last good page visible while a changed filter is loading.
  protected readonly current = linkedSignal<PartPage | undefined, PartPage | undefined>({
    source: () => (this.parts.hasValue() ? this.parts.value() : undefined),
    computation: (next, previous) => next ?? previous?.value,
  });

  protected readonly errorMessage = computed(() =>
    this.parts.error() ? describeError(this.parts.error()) : null,
  );

  protected readonly columns = [
    'partNumber',
    'name',
    'bin',
    'onHand',
    'reserved',
    'available',
    'status',
  ];
  protected readonly pageSizeOptions = [10, 25, 50, 100];

  private readonly searchTyped = new Subject<string>();

  constructor() {
    this.searchTyped
      .pipe(debounceTime(300), distinctUntilChanged(), takeUntilDestroyed())
      .subscribe((value) => this.navigate({ search: value.trim() || null }));
  }

  protected onSearch(value: string): void {
    this.searchTyped.next(value);
  }

  protected onLowStock(checked: boolean): void {
    this.navigate({ lowStock: checked ? 'true' : null });
  }

  protected onPage(event: PageEvent): void {
    this.router.navigate([], {
      queryParams: { page: event.pageIndex + 1, pageSize: event.pageSize },
      queryParamsHandling: 'merge',
    });
  }

  protected async newPart(): Promise<void> {
    const created = await firstValueFrom(
      this.dialog.open<PartNewDialog, undefined, PartDetail>(PartNewDialog).afterClosed(),
    );
    if (created) await this.router.navigate(['/inventory', created.id]);
  }

  private navigate(filter: Record<string, string | null>): void {
    this.router.navigate([], {
      queryParams: { ...filter, page: 1 },
      queryParamsHandling: 'merge',
    });
  }
}
