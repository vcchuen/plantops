import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormField, form, maxLength, min, required, submit, validate } from '@angular/forms/signals';
import { MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { InventoryCommands, describeInventoryError } from './inventory-commands';
import { PartDetail } from './inventory.models';

interface PartModel {
  partNumber: string;
  name: string;
  unit: string;
  binLocation: string;
  // number | null: an empty <input type="number"> is null, which `required` rejects.
  reorderLevel: number | null;
}

/** Closes with the created part, or undefined when dismissed. */
@Component({
  selector: 'app-part-new-dialog',
  imports: [
    FormField,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
  template: `
    <h2 mat-dialog-title>New part</h2>
    <form novalidate (submit)="onSubmit($event)">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>Part number</mat-label>
          <input matInput type="text" [formField]="partForm.partNumber" />
          @for (error of partForm.partNumber().errors(); track error.kind) {
            <mat-error>{{ error.message }}</mat-error>
          }
        </mat-form-field>
        <mat-form-field>
          <mat-label>Name</mat-label>
          <input matInput type="text" [formField]="partForm.name" />
          @for (error of partForm.name().errors(); track error.kind) {
            <mat-error>{{ error.message }}</mat-error>
          }
        </mat-form-field>
        <mat-form-field>
          <mat-label>Unit</mat-label>
          <input matInput type="text" [formField]="partForm.unit" />
          @for (error of partForm.unit().errors(); track error.kind) {
            <mat-error>{{ error.message }}</mat-error>
          }
        </mat-form-field>
        <mat-form-field>
          <mat-label>Bin location</mat-label>
          <input matInput type="text" [formField]="partForm.binLocation" />
          @for (error of partForm.binLocation().errors(); track error.kind) {
            <mat-error>{{ error.message }}</mat-error>
          }
        </mat-form-field>
        <mat-form-field>
          <mat-label>Reorder level</mat-label>
          <input matInput type="number" step="1" [formField]="partForm.reorderLevel" />
          @for (error of partForm.reorderLevel().errors(); track error.kind) {
            <mat-error>{{ error.message }}</mat-error>
          }
        </mat-form-field>

        @if (serverError(); as message) {
          <p class="error" role="alert">
            <mat-icon aria-hidden="true">error</mat-icon>
            {{ message }}
          </p>
        }
      </mat-dialog-content>
      <mat-dialog-actions align="end">
        <button mat-button type="button" mat-dialog-close>Cancel</button>
        <button
          mat-flat-button
          type="submit"
          [disabled]="partForm().invalid() || partForm().submitting()"
        >
          {{ partForm().submitting() ? 'Creating…' : 'Create part' }}
        </button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    mat-form-field {
      width: 100%;
      min-width: 320px;
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
export class PartNewDialog {
  private readonly ref = inject<MatDialogRef<PartNewDialog, PartDetail>>(MatDialogRef);
  private readonly commands = inject(InventoryCommands);

  protected readonly model = signal<PartModel>({
    partNumber: '',
    name: '',
    unit: 'pcs',
    binLocation: '',
    reorderLevel: 0,
  });

  protected readonly partForm = form(this.model, (p) => {
    required(p.partNumber, { message: 'Part number is required' });
    maxLength(p.partNumber, 40, { message: 'Part number must be at most 40 characters' });
    required(p.name, { message: 'Name is required' });
    maxLength(p.name, 100, { message: 'Name must be at most 100 characters' });
    required(p.unit, { message: 'Unit is required' });
    maxLength(p.unit, 10, { message: 'Unit must be at most 10 characters' });
    maxLength(p.binLocation, 20, { message: 'Bin location must be at most 20 characters' });
    required(p.reorderLevel, { message: 'Reorder level is required' });
    min(p.reorderLevel, 0, { message: 'Reorder level cannot be negative' });
    validate(p.reorderLevel, ({ value }) =>
      value() !== null && !Number.isInteger(value())
        ? { kind: 'integer', message: 'Reorder level must be a whole number' }
        : undefined,
    );
  });

  protected readonly serverError = signal<string | null>(null);

  protected onSubmit(event: Event): void {
    event.preventDefault();
    void submit(this.partForm, async () => {
      this.serverError.set(null);
      const m = this.model();
      try {
        const created = await this.commands.createPart({
          partNumber: m.partNumber.trim(),
          name: m.name.trim(),
          unit: m.unit.trim(),
          binLocation: m.binLocation.trim(),
          reorderLevel: m.reorderLevel ?? 0,
        });
        this.ref.close(created);
      } catch (error) {
        // 409 duplicate part number lands here; the dialog stays open so the user can fix it.
        this.serverError.set(describeInventoryError(error));
      }
      return undefined;
    });
  }
}
