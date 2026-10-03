import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { Router } from '@angular/router';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ReportCard } from './report-card';
import {
  defaultRange,
  pickGroup,
  rangeProblem,
  ReportKind,
  validDateOrNull,
} from './reports.models';

@Component({
  selector: 'app-reports-page',
  imports: [MatFormFieldModule, MatInputModule, ReportCard],
  templateUrl: './reports.page.html',
  styleUrl: './reports.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportsPage {
  private readonly router = inject(Router);

  // The URL is the source of truth; withComponentInputBinding maps each query param to an input.
  readonly from = input<string | undefined>();
  readonly to = input<string | undefined>();
  readonly mttrBy = input<string | undefined>();
  readonly slaBy = input<string | undefined>();
  readonly downBy = input<string | undefined>();

  // Computed once per page instance: a long-open page keeps the day it was opened on.
  private readonly defaults = defaultRange();

  protected readonly fromDate = computed(() => validDateOrNull(this.from()) ?? this.defaults.from);
  protected readonly toDate = computed(() => validDateOrNull(this.to()) ?? this.defaults.to);
  protected readonly problem = computed(() => rangeProblem(this.fromDate(), this.toDate()));
  protected readonly valid = computed(() => this.problem() === null);

  protected readonly mttrGroup = computed(() => pickGroup(this.mttrBy(), 'mttr', 'line'));
  protected readonly slaGroup = computed(() => pickGroup(this.slaBy(), 'sla', 'priority'));
  protected readonly downGroup = computed(() => pickGroup(this.downBy(), 'downtime', 'line'));

  // Plain anchor: same origin, so the cookie goes along and the browser handles the download.
  protected readonly exportHref = computed(() =>
    this.valid()
      ? `/api/reports/export.xlsx?from=${encodeURIComponent(this.fromDate())}&to=${encodeURIComponent(this.toDate())}`
      : null,
  );

  protected setParam(name: 'from' | 'to' | 'mttrBy' | 'slaBy' | 'downBy', value: string): void {
    // Empty (cleared date input) removes the param, so the default applies again.
    this.router.navigate([], {
      queryParams: { [name]: value || null },
      queryParamsHandling: 'merge',
    });
  }

  protected setGroup(kind: ReportKind, value: string): void {
    this.setParam(kind === 'mttr' ? 'mttrBy' : kind === 'sla' ? 'slaBy' : 'downBy', value);
  }
}
