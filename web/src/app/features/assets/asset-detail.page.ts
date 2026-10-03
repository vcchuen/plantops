import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { HistoryEntry } from '../shared/history.models';
import { HistoryTimeline } from '../shared/history-timeline';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import {
  AssetDetail,
  CRITICALITY_LABELS,
  MaintenanceEntry,
  STATUS_LABELS,
  describeError,
} from './assets.models';

@Component({
  selector: 'app-asset-detail-page',
  imports: [DatePipe, RouterLink, HistoryTimeline, MatIconModule, MatProgressBarModule],
  templateUrl: './asset-detail.page.html',
  styleUrl: './asset-detail.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AssetDetailPage {
  // Bound from the :id route param by withComponentInputBinding().
  readonly id = input.required<string>();

  // The id comes from the URL, so it is untrusted: encode it so a crafted link cannot
  // retarget the request to another API path (e.g. "..%2F..%2Fhealth").
  protected readonly asset = httpResource<AssetDetail>(
    () => `/api/assets/${encodeURIComponent(this.id())}`,
  );

  protected readonly history = httpResource<HistoryEntry[]>(
    () => `/api/assets/${encodeURIComponent(this.id())}/history`,
  );
  protected readonly historyError = computed(() =>
    this.history.error() ? describeError(this.history.error()) : null,
  );

  protected readonly maintenance = httpResource<MaintenanceEntry[]>(
    () => `/api/assets/${encodeURIComponent(this.id())}/maintenance`,
  );
  protected readonly maintenanceError = computed(() =>
    this.maintenance.error() ? describeError(this.maintenance.error()) : null,
  );

  protected readonly notFound = computed(
    () => (this.asset.error() as HttpErrorResponse | undefined)?.status === 404,
  );
  protected readonly errorMessage = computed(() =>
    this.asset.error() && !this.notFound() ? describeError(this.asset.error()) : null,
  );

  protected readonly criticalityLabels = CRITICALITY_LABELS;
  protected readonly statusLabels = STATUS_LABELS;
}
