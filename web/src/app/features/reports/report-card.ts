import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { describeError } from '../assets/assets.models';
import { barPercent, DisplayRow, REPORTS, ReportKind, ReportResponse } from './reports.models';

@Component({
  selector: 'app-report-card',
  imports: [MatFormFieldModule, MatIconModule, MatProgressBarModule, MatSelectModule],
  template: `
    <section class="card" [attr.aria-labelledby]="headingId()">
      <div class="head">
        <h2 [id]="headingId()">{{ title() }}</h2>
        <mat-form-field subscriptSizing="dynamic">
          <mat-label>Group by</mat-label>
          <mat-select [value]="groupBy()" (selectionChange)="groupByChange.emit($event.value)">
            @for (g of config().groups; track g.value) {
              <mat-option [value]="g.value">{{ g.label }}</mat-option>
            }
          </mat-select>
        </mat-form-field>
      </div>

      @if (report.isLoading()) {
        <mat-progress-bar mode="indeterminate" [attr.aria-label]="'Loading ' + title()" />
      }

      @if (!valid()) {
        <p class="empty">Choose a valid date range to see this report.</p>
      } @else if (errorMessage(); as message) {
        <p class="error" role="alert">
          <mat-icon aria-hidden="true">error</mat-icon>
          {{ message }}
        </p>
      } @else if (report.hasValue()) {
        @if (rows().length === 0) {
          <p class="empty">No data for this date range</p>
        } @else {
          <table [attr.aria-label]="title()">
            <thead>
              <tr>
                <th scope="col">{{ groupLabel() }}</th>
                <th scope="col" class="num">{{ config().countHeader }}</th>
                <th scope="col" class="num">{{ config().valueHeader }}</th>
                <th scope="col" aria-hidden="true" class="bar-col"></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.key) {
                <tr>
                  <th scope="row">{{ row.label }}</th>
                  <td class="num">{{ row.count }}</td>
                  <td class="num">{{ row.formatted }}</td>
                  <td class="bar-col" aria-hidden="true">
                    <div class="bar" [style.inline-size.%]="width(row)"></div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
    }
    .card {
      padding: 16px;
      border-radius: 12px;
      background: var(--mat-sys-surface-container-low);
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0 16px;
    }
    h2 {
      margin: 0;
      font: var(--mat-sys-title-large);
    }
    table {
      inline-size: 100%;
      border-collapse: collapse;
    }
    th,
    td {
      padding: 8px;
      text-align: start;
      border-block-end: 1px solid var(--mat-sys-outline-variant);
    }
    .num {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }
    .bar-col {
      inline-size: 35%;
    }
    .bar {
      block-size: 12px;
      min-inline-size: 2px;
      border-radius: 6px;
      background: var(--mat-sys-primary);
    }
    .empty {
      padding: 24px 0;
      color: var(--mat-sys-on-surface-variant);
    }
    .error {
      display: flex;
      align-items: center;
      gap: 8px;
      color: var(--mat-sys-error);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportCard {
  readonly kind = input.required<ReportKind>();
  readonly title = input.required<string>();
  readonly from = input.required<string>();
  readonly to = input.required<string>();
  readonly groupBy = input.required<string>();
  readonly valid = input.required<boolean>();
  readonly groupByChange = output<string>();

  protected readonly config = computed(() => REPORTS[this.kind()]);
  protected readonly headingId = computed(() => `report-${this.kind()}-heading`);
  protected readonly groupLabel = computed(
    () => this.config().groups.find((g) => g.value === this.groupBy())?.label ?? 'Group',
  );

  // Returning undefined keeps the resource idle, so an invalid range never reaches the API.
  protected readonly report = httpResource<ReportResponse<never>>(() =>
    this.valid()
      ? {
          url: this.config().endpoint,
          params: { from: this.from(), to: this.to(), groupBy: this.groupBy() },
        }
      : undefined,
  );

  protected readonly errorMessage = computed(() =>
    this.report.error() ? describeError(this.report.error()) : null,
  );

  protected readonly rows = computed<DisplayRow[]>(() =>
    this.report.hasValue() ? this.config().toRows(this.report.value().rows) : [],
  );
  private readonly max = computed(() => Math.max(0, ...this.rows().map((r) => r.value)));

  protected width(row: DisplayRow): number {
    return barPercent(row.value, this.max());
  }
}
