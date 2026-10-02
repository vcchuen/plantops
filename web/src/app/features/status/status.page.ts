import { ChangeDetectionStrategy, Component, computed } from '@angular/core';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { HealthReport, HealthStatus, isHealthReport } from './status.models';

const ICONS: Record<HealthStatus, string> = {
  Healthy: 'check_circle',
  Degraded: 'warning',
  Unhealthy: 'error',
};

@Component({
  selector: 'app-status-page',
  imports: [MatButtonModule, MatChipsModule, MatIconModule, MatProgressBarModule, MatTableModule],
  templateUrl: './status.page.html',
  styleUrl: './status.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatusPage {
  protected readonly health = httpResource<HealthReport>(() => '/health/ready');

  protected readonly columns = ['name', 'status', 'description', 'durationMs'];

  // The API answers 503 for an unhealthy system, but with a perfectly good report
  // in the body. httpResource treats any non-2xx as an error, so the report has to
  // be recovered from the error. (Reading value() while in error state would throw.)
  protected readonly report = computed<HealthReport | null>(() => {
    const error = this.health.error() as HttpErrorResponse | undefined;
    if (error) {
      return isHealthReport(error.error) ? error.error : null;
    }
    return this.health.hasValue() ? this.health.value() : null;
  });

  // Errored without a usable report: network failure (status 0), proxy error page, etc.
  protected readonly unreachable = computed(
    () => this.health.error() !== undefined && this.report() === null,
  );

  protected icon(status: HealthStatus): string {
    return ICONS[status];
  }
}
