import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { FormField, form, min, required, submit, validate } from '@angular/forms/signals';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { catchError, debounceTime, distinctUntilChanged, of, Subject, switchMap } from 'rxjs';
import { InventoryCommands, describeInventoryError } from './inventory-commands';
import { PartListItem, PartPage } from './inventory.models';

export interface ReservePartDialogData {
  workOrderId: string;
}

/** Closes with `true` after a successful reservation, undefined when dismissed. */
@Component({
  selector: 'app-reserve-part-dialog',
  imports: [
    FormField,
    MatDialogModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
  template: `
    <h2 mat-dialog-title>Reserve part</h2>
    <form novalidate (submit)="onSubmit($event)">
      <mat-dialog-content>
        <mat-form-field>
          <mat-label>Part</mat-label>
          <input
            matInput
            type="text"
            autocomplete="off"
            [matAutocomplete]="partAuto"
            [errorStateMatcher]="partErrorMatcher"
            [value]="partText()"
            (input)="onPartInput($any($event.target).value)"
            (focus)="onPartFocus()"
            (blur)="reserveForm.partId().markAsTouched()"
          />
          <mat-autocomplete #partAuto="matAutocomplete" (optionSelected)="pickPart($event.option.value)">
            @for (part of partOptions(); track part.id) {
              <mat-option [value]="part">{{ partLabel(part) }}</mat-option>
            }
          </mat-autocomplete>
          <mat-hint>Type a part number or name to search</mat-hint>
          @for (error of reserveForm.partId().errors(); track error.kind) {
            <mat-error>{{ error.message }}</mat-error>
          }
        </mat-form-field>

        <mat-form-field>
          <mat-label>Quantity</mat-label>
          <input matInput type="number" step="1" [formField]="reserveForm.quantity" />
          @for (error of reserveForm.quantity().errors(); track error.kind) {
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
          [disabled]="reserveForm().invalid() || reserveForm().submitting()"
        >
          {{ reserveForm().submitting() ? 'Reserving…' : 'Reserve' }}
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
export class ReservePartDialog {
  private readonly http = inject(HttpClient);
  private readonly commands = inject(InventoryCommands);
  private readonly data = inject<ReservePartDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ReservePartDialog, boolean>>(MatDialogRef);

  protected readonly model = signal<{ partId: string; quantity: number | null }>({
    partId: '',
    quantity: null,
  });
  protected readonly reserveForm = form(this.model, (p) => {
    required(p.partId, { message: 'Choose a part from the list' });
    required(p.quantity, { message: 'Quantity is required' });
    min(p.quantity, 1, { message: 'Quantity must be at least 1' });
    validate(p.quantity, ({ value }) =>
      value() !== null && !Number.isInteger(value())
        ? { kind: 'integer', message: 'Quantity must be a whole number' }
        : undefined,
    );
  });

  protected readonly serverError = signal<string | null>(null);

  // Same pattern as the asset picker: the input holds search text, only the picked id is modelled.
  protected readonly partText = signal('');
  protected readonly partOptions = signal<PartListItem[]>([]);
  protected readonly partErrorMatcher: ErrorStateMatcher = {
    isErrorState: () => this.reserveForm.partId().touched() && this.reserveForm.partId().invalid(),
  };

  private readonly partSearch = new Subject<string>();

  constructor() {
    this.partSearch
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((search) =>
          this.http
            .get<PartPage>('/api/inventory/parts', {
              params: { search: search.trim(), pageSize: 10 },
            })
            .pipe(catchError(() => of<PartPage | null>(null))),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((page) => this.partOptions.set(page?.items ?? []));
  }

  protected partLabel(part: PartListItem): string {
    return `${part.partNumber} — ${part.name} (${part.quantityAvailable} available)`;
  }

  protected onPartInput(text: string): void {
    this.partText.set(text);
    // Typing after a pick invalidates the pick.
    this.reserveForm.partId().value.set('');
    this.partSearch.next(text);
  }

  protected onPartFocus(): void {
    this.partSearch.next(this.partText());
  }

  protected pickPart(part: PartListItem): void {
    this.partText.set(this.partLabel(part));
    this.reserveForm.partId().value.set(part.id);
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    void submit(this.reserveForm, async () => {
      this.serverError.set(null);
      const m = this.model();
      try {
        await this.commands.reserve({
          partId: m.partId,
          workOrderId: this.data.workOrderId,
          quantity: m.quantity as number,
        });
        this.ref.close(true);
      } catch (error) {
        // 409 insufficient stock (or 400/403): keep the dialog open so the quantity can be fixed.
        this.serverError.set(describeInventoryError(error));
      }
      return undefined;
    });
  }
}
