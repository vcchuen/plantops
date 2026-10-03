import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { httpResource } from '@angular/common/http';
import { FormField, form, maxLength, required, submit } from '@angular/forms/signals';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { describeError } from '../assets/assets.models';
import { WorkOrderCommands, describeCommandError } from './work-order-commands';
import { WorkOrderComment } from './work-orders.models';

interface CommentModel {
  body: string;
}

const MAX_LENGTH = 2000;

@Component({
  selector: 'app-work-order-comments',
  imports: [
    DatePipe,
    FormField,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
  templateUrl: './work-order-comments.html',
  styleUrl: './work-order-comments.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkOrderComments {
  private readonly commands = inject(WorkOrderCommands);
  private readonly sanitizer = inject(DomSanitizer);

  readonly workOrderId = input.required<string>();

  protected readonly comments = httpResource<WorkOrderComment[]>(
    () => `/api/work-orders/${encodeURIComponent(this.workOrderId())}/comments`,
  );
  protected readonly loadError = computed(() =>
    this.comments.error() ? describeError(this.comments.error()) : null,
  );

  // Spoken by the polite live region after a save, so the result is heard without moving focus.
  protected readonly announcement = signal('');

  protected readonly addModel = signal<CommentModel>({ body: '' });
  protected readonly addForm = form(this.addModel, (p) => {
    required(p.body, { message: 'Write a comment first' });
    maxLength(p.body, MAX_LENGTH, {
      message: `Comment must be at most ${MAX_LENGTH} characters`,
    });
  });
  protected readonly addError = signal<string | null>(null);

  protected readonly editingId = signal<string | null>(null);
  protected readonly editModel = signal<CommentModel>({ body: '' });
  protected readonly editForm = form(this.editModel, (p) => {
    required(p.body, { message: 'A comment cannot be empty' });
    maxLength(p.body, MAX_LENGTH, {
      message: `Comment must be at most ${MAX_LENGTH} characters`,
    });
  });
  protected readonly editError = signal<string | null>(null);

  // Keep the technician's line breaks.
  protected asHtml(body: string): SafeHtml {
    return this.sanitizer.bypassSecurityTrustHtml(body.replace(/\n/g, '<br>'));
  }

  protected onAdd(event: Event): void {
    event.preventDefault();
    void submit(this.addForm, async () => {
      this.addError.set(null);
      try {
        await this.commands.addComment(this.workOrderId(), this.addModel().body);
        this.addForm().reset({ body: '' });
        this.announcement.set('Comment added');
        this.comments.reload();
      } catch (error) {
        this.addError.set(describeCommandError(error));
      }
      return undefined;
    });
  }

  protected startEdit(comment: WorkOrderComment): void {
    this.editError.set(null);
    this.editForm().reset({ body: comment.body });
    this.editingId.set(comment.id);
  }

  protected cancelEdit(): void {
    this.editingId.set(null);
  }

  protected onSave(event: Event, comment: WorkOrderComment): void {
    event.preventDefault();
    void submit(this.editForm, async () => {
      this.editError.set(null);
      try {
        await this.commands.editComment(this.workOrderId(), comment.id, this.editModel().body);
        this.editingId.set(null);
        this.announcement.set('Comment updated');
        this.comments.reload();
      } catch (error) {
        this.editError.set(describeCommandError(error));
      }
      return undefined;
    });
  }
}
