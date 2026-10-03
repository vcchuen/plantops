import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkOrderComments } from './work-order-comments';
import { WorkOrderComment } from './work-orders.models';

const comment = (over: Partial<WorkOrderComment> = {}): WorkOrderComment => ({
  id: 'c1',
  sequence: 1,
  author: { id: 'tom', name: 'Tom Tech' },
  body: 'Replaced the feeder spring.',
  createdAt: '2026-10-03T09:00:00Z',
  editedAt: null,
  ...over,
});

describe('WorkOrderComments', () => {
  let fixture: ComponentFixture<WorkOrderComments>;
  let http: HttpTestingController;
  let el: HTMLElement;

  const url = '/api/work-orders/w1/comments';

  function create() {
    fixture = TestBed.createComponent(WorkOrderComments);
    el = fixture.nativeElement;
    fixture.componentRef.setInput('workOrderId', 'w1');
    fixture.detectChanges();
  }

  async function load(comments: WorkOrderComment[] = [comment()]) {
    http.expectOne(url).flush(comments);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  // A successful save starts a reload, which counts as pending work, so whenStable() would never settle
  // before we flush it. Yield one macrotask instead.
  const settle = () => new Promise<void>((resolve) => setTimeout(resolve));

  function type(textarea: HTMLTextAreaElement, value: string) {
    textarea.value = value;
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  const addForm = () => el.querySelector('form') as HTMLFormElement;
  const addBox = () => addForm().querySelector('textarea') as HTMLTextAreaElement;
  const addButton = () => addForm().querySelector('button[type="submit"]') as HTMLButtonElement;
  const status = () => el.querySelector('[role="status"]')?.textContent?.trim();
  const buttonLabelled = (text: string) =>
    Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === text);

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [WorkOrderComments],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('lists each comment with its author, time and body', async () => {
    create();
    await load([
      comment(),
      comment({ id: 'c2', sequence: 2, author: { id: 'sam', name: 'Sam Super' }, body: 'Thanks' }),
    ]);

    const items = el.querySelectorAll('ul.comments li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Tom Tech');
    expect(items[0].querySelector('time')?.getAttribute('datetime')).toBe('2026-10-03T09:00:00Z');
    expect(items[0].querySelector('.comment-body')?.textContent).toContain(
      'Replaced the feeder spring.',
    );
    expect(items[1].textContent).toContain('Sam Super');
  });

  it('keeps the line breaks of a multi-line comment', async () => {
    create();
    await load([comment({ body: 'Line one\nLine two' })]);

    const body = el.querySelector('.comment-body') as HTMLElement;
    expect(body.querySelectorAll('br').length).toBe(1);
    expect(body.textContent).toContain('Line one');
    expect(body.textContent).toContain('Line two');
  });

  it('marks an edited comment', async () => {
    create();
    await load([comment({ editedAt: '2026-10-03T10:00:00Z' }), comment({ id: 'c2', sequence: 2 })]);

    const items = el.querySelectorAll('ul.comments li');
    expect(items[0].textContent).toContain('(edited)');
    expect(items[1].textContent).not.toContain('(edited)');
  });

  it('shows an empty state', async () => {
    create();
    await load([]);
    expect(el.textContent).toContain('No comments yet');
  });

  it('shows a load problem in an alert', async () => {
    create();
    http.expectOne(url).flush({ title: 'Not Found' }, { status: 404, statusText: 'Not Found' });
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Not Found');
  });

  it('keeps "Add comment" disabled until something is typed', async () => {
    create();
    await load([]);
    expect(addButton().disabled).toBe(true);

    type(addBox(), 'Waiting for parts');
    expect(addButton().disabled).toBe(false);
  });

  it('posts the comment, clears the box, reloads the list and announces the result', async () => {
    create();
    await load([]);

    type(addBox(), 'Waiting for parts');
    addForm().dispatchEvent(new Event('submit'));
    const req = http.expectOne(url);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: 'Waiting for parts' });
    req.flush(comment({ body: 'Waiting for parts' }), { status: 201, statusText: 'Created' });
    await settle();

    http.expectOne(url).flush([comment({ body: 'Waiting for parts' })]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelectorAll('ul.comments li').length).toBe(1);
    expect(addBox().value).toBe('');
    expect(status()).toBe('Comment added');
  });

  it('shows the server problem when posting fails and keeps the text', async () => {
    create();
    await load([]);

    type(addBox(), 'Waiting for parts');
    addForm().dispatchEvent(new Event('submit'));
    http
      .expectOne(url)
      .flush(
        { title: 'Bad Request', detail: 'Comment is required.' },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Comment is required.');
    expect(addBox().value).toBe('Waiting for parts');
  });

  it('offers Edit on a comment and opens an editor holding its text', async () => {
    create();
    await load();

    buttonLabelled('Edit')?.click();
    fixture.detectChanges();

    const editor = el.querySelector('ul.comments textarea') as HTMLTextAreaElement;
    expect(editor.value).toBe('Replaced the feeder spring.');
    expect(el.querySelector('.comment-body')).toBeNull();
  });

  it('saves an edit with PUT, closes the editor, reloads and announces the result', async () => {
    create();
    await load();

    buttonLabelled('Edit')?.click();
    fixture.detectChanges();
    type(el.querySelector('ul.comments textarea') as HTMLTextAreaElement, 'Replaced the spring.');
    (el.querySelector('ul.comments form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const req = http.expectOne(`${url}/c1`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ body: 'Replaced the spring.' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await settle();

    http
      .expectOne(url)
      .flush([comment({ body: 'Replaced the spring.', editedAt: '2026-10-03T10:00:00Z' })]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('ul.comments textarea')).toBeNull();
    expect(el.querySelector('.comment-body')?.textContent).toContain('Replaced the spring.');
    expect(el.textContent).toContain('(edited)');
    expect(status()).toBe('Comment updated');
  });

  it('cancelling an edit discards it without a request', async () => {
    create();
    await load();

    buttonLabelled('Edit')?.click();
    fixture.detectChanges();
    type(el.querySelector('ul.comments textarea') as HTMLTextAreaElement, 'Something else');
    buttonLabelled('Cancel')?.click();
    fixture.detectChanges();

    expect(el.querySelector('.comment-body')?.textContent).toContain('Replaced the feeder spring.');
  });

  it('shows the server problem when an edit fails and keeps the editor open', async () => {
    create();
    await load();

    buttonLabelled('Edit')?.click();
    fixture.detectChanges();
    type(el.querySelector('ul.comments textarea') as HTMLTextAreaElement, 'Replaced the spring.');
    (el.querySelector('ul.comments form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    http
      .expectOne(`${url}/c1`)
      .flush(
        { title: 'Not Found', detail: 'Comment not found.' },
        { status: 404, statusText: 'Nf' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(el.querySelector('ul.comments [role="alert"]')?.textContent).toContain(
      'Comment not found.',
    );
    expect(el.querySelector('ul.comments textarea')).not.toBeNull();
  });
});
