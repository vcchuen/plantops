import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient } from '@angular/common/http';
import { FormField, form, maxLength, required, submit } from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { catchError, debounceTime, distinctUntilChanged, of, Subject, switchMap } from 'rxjs';
import { AssetListItem, AssetPage, describeError } from '../assets/assets.models';
import { WorkOrderCommands } from './work-order-commands';
import { PRIORITY_LABELS, Priority } from './work-orders.models';

interface RaiseModel {
  assetId: string;
  title: string;
  description: string;
  priority: Priority;
  assetDown: boolean;
}

@Component({
  selector: 'app-work-order-new-page',
  imports: [
    FormField,
    RouterLink,
    MatAutocompleteModule,
    MatButtonModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: './work-order-new.page.html',
  styleUrl: './work-order-new.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WorkOrderNewPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly commands = inject(WorkOrderCommands);

  protected readonly model = signal<RaiseModel>({
    assetId: '',
    title: '',
    description: '',
    priority: 'P3',
    assetDown: false,
  });

  // Validation lives in the schema, not the template: the same rules then drive
  // errors, `invalid()` and the submit() gate.
  protected readonly raiseForm = form(this.model, (p) => {
    required(p.assetId, { message: 'Choose an asset from the list' });
    required(p.title, { message: 'Title is required' });
    maxLength(p.title, 200, { message: 'Title must be at most 200 characters' });
    maxLength(p.description, 2000, { message: 'Description must be at most 2000 characters' });
    required(p.priority, { message: 'Priority is required' });
  });

  protected readonly priorities = Object.keys(PRIORITY_LABELS) as Priority[];
  protected readonly priorityLabels = PRIORITY_LABELS;

  /** Form-level problem from the server (bad or decommissioned asset). Not tied to one field. */
  protected readonly serverError = signal<string | null>(null);

  // The autocomplete input shows search text; only the picked asset's id enters the model.
  // So it is not [formField]-bound, and needs its own error-state rule.
  protected readonly assetText = signal('');
  protected readonly assetOptions = signal<AssetListItem[]>([]);
  protected readonly assetErrorMatcher: ErrorStateMatcher = {
    isErrorState: () => this.raiseForm.assetId().touched() && this.raiseForm.assetId().invalid(),
  };

  private readonly assetSearch = new Subject<string>();

  constructor() {
    // RxJS again for the same reason as the assets search: debounce + switchMap (cancel the
    // stale request) over typing events has no tidy signal equivalent.
    this.assetSearch
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        switchMap((search) =>
          this.http
            .get<AssetPage>('/api/assets', {
              params: { search: search.trim(), pageSize: 10, status: 'InService' },
            })
            .pipe(catchError(() => of<AssetPage | null>(null))),
        ),
        takeUntilDestroyed(),
      )
      // Filtered client-side too: the picker must never offer a decommissioned asset
      // even if the server-side status filter is ignored or changes.
      .subscribe((page) =>
        this.assetOptions.set((page?.items ?? []).filter((a) => a.status !== 'Decommissioned')),
      );
  }

  protected assetLabel(asset: AssetListItem): string {
    return `${asset.tag} — ${asset.name}`;
  }

  protected onAssetInput(text: string): void {
    this.assetText.set(text);
    // Typing after a pick invalidates the pick.
    this.raiseForm.assetId().value.set('');
    this.assetSearch.next(text);
  }

  protected onAssetFocus(): void {
    this.assetSearch.next(this.assetText());
  }

  protected pickAsset(asset: AssetListItem): void {
    this.assetText.set(this.assetLabel(asset));
    this.raiseForm.assetId().value.set(asset.id);
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    void submit(this.raiseForm, async () => {
      this.serverError.set(null);
      try {
        const created = await this.commands.create(this.model());
        await this.router.navigate(['/work-orders', created.id]);
      } catch (error) {
        this.serverError.set(describeError(error));
      }
      // Server problems are shown form-level; returning nothing means "submission succeeded"
      // as far as signal forms is concerned, so no field gets a stale server error.
      return undefined;
    });
  }
}
