import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { httpResource } from '@angular/common/http';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { PRIORITY_LABELS, Priority, Technician, WorkOrderAction } from './work-orders.models';

export type InputAction = Extract<
  WorkOrderAction,
  'approve' | 'reject' | 'assign' | 'complete' | 'cancel'
>;

export interface ActionDialogData {
  action: InputAction;
  currentPriority: Priority;
}

/** What the dialog closes with: the command body. Dismissing closes with undefined. */
export type ActionDialogResult = object;

const COPY: Record<InputAction, { title: string; confirm: string; label: string; max: number }> = {
  approve: { title: 'Approve work order', confirm: 'Approve', label: '', max: 0 },
  reject: {
    title: 'Reject work order',
    confirm: 'Reject',
    label: 'Reason for rejection',
    max: 500,
  },
  assign: { title: 'Assign technician', confirm: 'Assign', label: 'Technician', max: 0 },
  complete: { title: 'Complete work order', confirm: 'Complete', label: 'Resolution', max: 2000 },
  cancel: {
    title: 'Cancel work order',
    confirm: 'Cancel work order',
    label: 'Reason for cancelling',
    max: 500,
  },
};

@Component({
  selector: 'app-work-order-action-dialog',
  imports: [MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatSelectModule],
  template: `
    <h2 mat-dialog-title>{{ copy.title }}</h2>
    <mat-dialog-content>
      @switch (data.action) {
        @case ('approve') {
          <mat-form-field>
            <mat-label>Priority</mat-label>
            <mat-select [value]="value()" (selectionChange)="value.set($event.value)">
              <mat-option value="">Keep requested ({{ data.currentPriority }})</mat-option>
              @for (p of priorities; track p) {
                <mat-option [value]="p">{{ priorityLabels[p] }}</mat-option>
              }
            </mat-select>
          </mat-form-field>
        }
        @case ('assign') {
          <mat-form-field>
            <mat-label>Technician</mat-label>
            <mat-select [value]="value()" (selectionChange)="value.set($event.value)">
              @for (t of technicianList(); track t.id) {
                <mat-option [value]="t.id">{{ t.name }}</mat-option>
              }
            </mat-select>
            @if (technicians?.error()) {
              <mat-error>Could not load technicians</mat-error>
            }
          </mat-form-field>
        }
        @default {
          <mat-form-field>
            <mat-label>{{ copy.label }}</mat-label>
            <textarea
              matInput
              rows="4"
              [attr.maxlength]="copy.max"
              [value]="value()"
              (input)="value.set($any($event.target).value)"
            ></textarea>
            <mat-hint align="end">{{ value().length }} / {{ copy.max }}</mat-hint>
          </mat-form-field>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" mat-dialog-close>Back</button>
      <button mat-flat-button type="button" [disabled]="!canSubmit()" (click)="confirm()">
        {{ copy.confirm }}
      </button>
    </mat-dialog-actions>
  `,
  styles: `
    mat-form-field {
      width: 100%;
      min-width: 320px;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkOrderActionDialog {
  protected readonly data = inject<ActionDialogData>(MAT_DIALOG_DATA);
  private readonly ref =
    inject<MatDialogRef<WorkOrderActionDialog, ActionDialogResult>>(MatDialogRef);

  protected readonly copy = COPY[this.data.action];
  protected readonly priorities = Object.keys(PRIORITY_LABELS) as Priority[];
  protected readonly priorityLabels = PRIORITY_LABELS;

  // Only the assign dialog needs the directory, so only it makes the request.
  protected readonly technicians =
    this.data.action === 'assign'
      ? httpResource<Technician[]>(() => '/api/identity/users?role=technician')
      : undefined;
  protected readonly technicianList = computed(() =>
    this.technicians?.hasValue() ? this.technicians.value() : [],
  );

  protected readonly value = signal('');

  protected readonly canSubmit = computed(() => {
    const v = this.value().trim();
    if (this.data.action === 'approve') return true;
    return v.length > 0 && (this.copy.max === 0 || v.length <= this.copy.max);
  });

  protected confirm(): void {
    if (!this.canSubmit()) return;
    const v = this.value().trim();
    switch (this.data.action) {
      case 'approve':
        this.ref.close(v ? { priority: v } : {});
        break;
      case 'assign':
        this.ref.close({ technicianId: v });
        break;
      case 'complete':
        this.ref.close({ resolution: v });
        break;
      default:
        this.ref.close({ reason: v });
    }
  }
}
