import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { SLA_LABELS, SlaState } from './work-orders.models';

@Component({
  selector: 'app-sla-badge',
  imports: [MatIconModule],
  template: `
    @if (label(); as l) {
      <span class="badge" [class]="'sla-' + state()!.toLowerCase()">
        <mat-icon aria-hidden="true">{{ l.icon }}</mat-icon>
        <span class="visually-hidden">SLA: </span>{{ l.text }}
      </span>
    } @else {
      <span aria-label="No SLA" role="img">—</span>
    }
  `,
  styles: `
    .badge {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 2px 10px 2px 6px;
      border-radius: 999px;
      font: var(--mat-sys-label-medium);
      white-space: nowrap;
    }
    mat-icon {
      width: 18px;
      height: 18px;
      font-size: 18px;
    }
    .sla-ontrack,
    .sla-met {
      background: var(--mat-sys-secondary-container);
      color: var(--mat-sys-on-secondary-container);
    }
    .sla-atrisk {
      background: var(--mat-sys-tertiary-container);
      color: var(--mat-sys-on-tertiary-container);
    }
    .sla-breached,
    .sla-missed {
      background: var(--mat-sys-error-container);
      color: var(--mat-sys-on-error-container);
    }
    .visually-hidden {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SlaBadge {
  readonly state = input.required<SlaState | null>();
  protected readonly label = computed(() => {
    const s = this.state();
    return s ? SLA_LABELS[s] : null;
  });
}
