import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { QueueItem } from './work-orders.models';

@Component({
  selector: 'app-queue-card',
  imports: [DatePipe],
  host: {
    class: 'queue-card',
    '(click)': 'opened.emit(item())',
  },
  template: `
    <div class="top">
      <span class="dot" [class]="dotClass()"></span>
      <strong class="number">{{ item().number }}</strong>
    </div>
    <div class="title">{{ item().title }}</div>
    <div class="asset">{{ item().assetTag }}</div>
    <div class="parts">{{ item().partsReserved }} {{ partsWord() }} reserved</div>
    <div class="due">Due {{ item().dueAt | date: 'short' }}</div>
  `,
  styles: `
    :host {
      display: block;
      padding: 16px;
      border-radius: 12px;
      border: 1px solid var(--mat-sys-outline-variant);
      background: var(--mat-sys-surface-container-low);
      cursor: pointer;
    }
    :host(:hover) {
      background: var(--mat-sys-surface-container);
    }
    .top {
      display: flex;
      align-items: center;
      gap: 8px;
    }
    .dot {
      width: 12px;
      height: 12px;
      border-radius: 50%;
    }
    .dot-red {
      background: #c62828;
    }
    .dot-amber {
      background: #f9a825;
    }
    .dot-green {
      background: #2e7d32;
    }
    .title {
      margin: 8px 0 4px;
      font: var(--mat-sys-title-medium);
    }
    .asset,
    .parts,
    .due {
      color: var(--mat-sys-on-surface-variant);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class QueueCard {
  readonly item = input.required<QueueItem>();
  readonly opened = output<QueueItem>();

  protected readonly partsWord = computed(() =>
    this.item().partsReserved === 1 ? 'part' : 'parts',
  );

  protected readonly dotClass = computed(() => {
    const { slaState, priority } = this.item();
    if (slaState === 'Breached') return 'dot-red';
    if (slaState === 'AtRisk' || priority === 'P1') return 'dot-amber';
    return 'dot-green';
  });
}
