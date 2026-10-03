import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpErrorResponse, httpResource } from '@angular/common/http';
import { FormField, form, min, required, submit, validate } from '@angular/forms/signals';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { SessionStore } from '../../core/auth/session.store';
import { describeError } from '../assets/assets.models';
import { InventoryCommands, describeInventoryError } from './inventory-commands';
import { PartDetail } from './inventory.models';

@Component({
  selector: 'app-part-detail-page',
  imports: [
    DatePipe,
    FormField,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
  ],
  templateUrl: './part-detail.page.html',
  styleUrl: './part-detail.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PartDetailPage {
  private readonly commands = inject(InventoryCommands);
  protected readonly session = inject(SessionStore);

  readonly id = input.required<string>();

  protected readonly part = httpResource<PartDetail>(
    () => `/api/inventory/parts/${encodeURIComponent(this.id())}`,
  );

  protected readonly notFound = computed(
    () => (this.part.error() as HttpErrorResponse | undefined)?.status === 404,
  );
  protected readonly errorMessage = computed(() =>
    this.part.error() && !this.notFound() ? describeError(this.part.error()) : null,
  );

  protected readonly model = signal<{ quantity: number | null }>({ quantity: null });
  protected readonly receiveForm = form(this.model, (p) => {
    required(p.quantity, { message: 'Quantity is required' });
    min(p.quantity, 1, { message: 'Quantity must be at least 1' });
    validate(p.quantity, ({ value }) =>
      value() !== null && !Number.isInteger(value())
        ? { kind: 'integer', message: 'Quantity must be a whole number' }
        : undefined,
    );
  });

  protected readonly receiveError = signal<string | null>(null);
  // Announced through an aria-live region so screen-reader users hear the outcome.
  protected readonly receiveNotice = signal('');

  protected onReceive(event: Event): void {
    event.preventDefault();
    void submit(this.receiveForm, async () => {
      this.receiveError.set(null);
      this.receiveNotice.set('');
      const quantity = this.model().quantity as number;
      try {
        await this.commands.receive(this.id(), quantity);
        this.receiveNotice.set(`Received ${quantity} into stock.`);
        this.receiveForm().reset({ quantity: null });
        this.part.reload();
      } catch (error) {
        this.receiveError.set(describeInventoryError(error));
      }
      return undefined;
    });
  }
}
