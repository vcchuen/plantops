import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpClient, HttpErrorResponse, httpResource } from '@angular/common/http';
import {
  FormField,
  form,
  max,
  maxLength,
  min,
  required,
  submit,
  validate,
} from '@angular/forms/signals';
import { Router, RouterLink } from '@angular/router';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, debounceTime, distinctUntilChanged, of, Subject, switchMap } from 'rxjs';
import { AssetListItem, AssetPage, describeError } from '../assets/assets.models';
import { isConflict } from '../work-orders/work-order-commands';
import { PRIORITY_LABELS, Priority } from '../work-orders/work-orders.models';
import { PmScheduleCommands, describePmError } from './pm-schedule-commands';
import { PmScheduleDetail } from './pm-schedules.models';

interface PmModel {
  assetId: string;
  title: string;
  instructions: string;
  // number | null: an empty <input type="number"> is null, which `required` rejects.
  intervalDays: number | null;
  leadDays: number | null;
  priority: Priority;
  // Native date input value: 'YYYY-MM-DD', or '' when cleared.
  nextDueOn: string;
}

const integer = (label: string) => ({ value }: { value: () => number | null }) =>
  value() !== null && !Number.isInteger(value())
    ? { kind: 'integer', message: `${label} must be a whole number` }
    : undefined;

/** One component for /pm-schedules/new and /pm-schedules/:id/edit; the mode is "is there an id". */
@Component({
  selector: 'app-pm-schedule-form-page',
  imports: [
    FormField,
    RouterLink,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatSelectModule,
  ],
  templateUrl: './pm-schedule-form.page.html',
  styleUrl: './pm-schedule-form.page.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PmScheduleFormPage {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly commands = inject(PmScheduleCommands);
  private readonly snackBar = inject(MatSnackBar);

  readonly id = input<string | undefined>();
  protected readonly isEdit = computed(() => !!this.id());

  // An undefined URL keeps the resource idle, so create mode makes no request.
  protected readonly detail = httpResource<PmScheduleDetail>(() =>
    this.id() ? `/api/pm-schedules/${encodeURIComponent(this.id()!)}` : undefined,
  );
  protected readonly notFound = computed(
    () => (this.detail.error() as HttpErrorResponse | undefined)?.status === 404,
  );
  protected readonly loadError = computed(() =>
    this.detail.error() && !this.notFound() ? describeError(this.detail.error()) : null,
  );

  protected readonly model = signal<PmModel>({
    assetId: '',
    title: '',
    instructions: '',
    intervalDays: null,
    leadDays: 0,
    priority: 'P3',
    nextDueOn: '',
  });

  protected readonly scheduleForm = form(this.model, (p) => {
    required(p.assetId, { message: 'Choose an asset from the list' });
    required(p.title, { message: 'Title is required' });
    maxLength(p.title, 200, { message: 'Title must be at most 200 characters' });
    maxLength(p.instructions, 2000, { message: 'Instructions must be at most 2000 characters' });
    required(p.intervalDays, { message: 'Interval is required' });
    min(p.intervalDays, 1, { message: 'Interval must be at least 1 day' });
    max(p.intervalDays, 365, { message: 'Interval must be at most 365 days' });
    validate(p.intervalDays, integer('Interval'));
    required(p.leadDays, { message: 'Lead days is required' });
    min(p.leadDays, 0, { message: 'Lead days cannot be negative' });
    max(p.leadDays, 30, { message: 'Lead days must be at most 30' });
    validate(p.leadDays, integer('Lead days'));
    required(p.priority, { message: 'Priority is required' });
    required(p.nextDueOn, { message: 'Next due date is required' });
  });

  protected readonly priorities = Object.keys(PRIORITY_LABELS) as Priority[];
  protected readonly priorityLabels = PRIORITY_LABELS;

  protected readonly serverError = signal<string | null>(null);

  // Same picker approach as the raise-work-order page: the input shows search text, only the
  // picked asset's id enters the model, so it needs its own error-state rule.
  protected readonly assetText = signal('');
  protected readonly assetOptions = signal<AssetListItem[]>([]);
  protected readonly assetErrorMatcher: ErrorStateMatcher = {
    isErrorState: () =>
      this.scheduleForm.assetId().touched() && this.scheduleForm.assetId().invalid(),
  };

  private readonly assetSearch = new Subject<string>();

  constructor() {
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
      .subscribe((page) =>
        this.assetOptions.set((page?.items ?? []).filter((a) => a.status !== 'Decommissioned')),
      );

    // Runs on first load and again after a reload (e.g. a 412), so the form always shows the
    // server's latest version. untracked: writing the model must not re-trigger this effect.
    effect(() => {
      if (!this.detail.hasValue()) return;
      const d = this.detail.value();
      untracked(() => {
        this.assetText.set(`${d.assetTag} — ${d.assetName}`);
        this.model.set({
          assetId: d.assetId,
          title: d.title,
          instructions: d.instructions,
          intervalDays: d.intervalDays,
          leadDays: d.leadDays,
          priority: d.priority,
          nextDueOn: d.nextDueOn,
        });
      });
    });
  }

  protected assetLabel(asset: AssetListItem): string {
    return `${asset.tag} — ${asset.name}`;
  }

  protected onAssetInput(text: string): void {
    this.assetText.set(text);
    // Typing after a pick invalidates the pick.
    this.scheduleForm.assetId().value.set('');
    this.assetSearch.next(text);
  }

  protected onAssetFocus(): void {
    this.assetSearch.next(this.assetText());
  }

  protected pickAsset(asset: AssetListItem): void {
    this.assetText.set(this.assetLabel(asset));
    this.scheduleForm.assetId().value.set(asset.id);
  }

  protected onSubmit(event: Event): void {
    event.preventDefault();
    void submit(this.scheduleForm, async () => {
      this.serverError.set(null);
      const m = this.model();
      const body = {
        title: m.title.trim(),
        instructions: m.instructions.trim(),
        intervalDays: m.intervalDays as number,
        leadDays: m.leadDays as number,
        priority: m.priority,
        nextDueOn: m.nextDueOn,
      };
      try {
        const id = this.id();
        if (id) {
          const etag = this.detail.headers()?.get('ETag');
          if (!etag) {
            this.serverError.set('The schedule version is unknown. Reload the page and try again.');
            return undefined;
          }
          await this.commands.update(id, body, etag);
          await this.router.navigate(['/pm-schedules', id]);
        } else {
          const created = await this.commands.create({ assetId: m.assetId, ...body });
          await this.router.navigate(['/pm-schedules', created.id]);
        }
      } catch (error) {
        if (isConflict(error)) {
          this.snackBar.open(
            'Someone else changed this schedule — showing the latest version.',
            'Dismiss',
            { duration: 8000 },
          );
          this.detail.reload();
        } else {
          this.serverError.set(describePmError(error));
        }
      }
      // Problems are shown form-level, so no field keeps a stale server error.
      return undefined;
    });
  }
}
