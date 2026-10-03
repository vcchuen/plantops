import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HistoryEntry, describePayload, eventLabel } from './history.models';

@Component({
  selector: 'app-history-timeline',
  imports: [DatePipe],
  template: `
    @if (entries().length === 0) {
      <p class="empty">No history yet.</p>
    } @else {
      <ol class="timeline">
        @for (entry of entries(); track $index) {
          <li>
            <time [attr.datetime]="entry.occurredAt">{{ entry.occurredAt | date: 'medium' }}</time>
            <span class="what">
              <strong>{{ label(entry) }}</strong> by {{ entry.actorName }}
            </span>
            @if (detail(entry); as text) {
              <span class="detail">{{ text }}</span>
            }
          </li>
        }
      </ol>
    }
  `,
  styles: `
    .timeline {
      list-style: none;
      margin: 0;
      padding: 0 0 0 16px;
      border-left: 2px solid var(--mat-sys-outline-variant);
    }
    li {
      display: flex;
      flex-direction: column;
      gap: 2px;
      padding: 0 0 16px 12px;
    }
    time,
    .detail {
      color: var(--mat-sys-on-surface-variant);
      font: var(--mat-sys-body-small);
    }
    .empty {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class HistoryTimeline {
  readonly entries = input.required<HistoryEntry[]>();

  protected label(entry: HistoryEntry): string {
    return eventLabel(entry.eventType);
  }

  protected detail(entry: HistoryEntry): string | null {
    return describePayload(entry.payload);
  }
}
