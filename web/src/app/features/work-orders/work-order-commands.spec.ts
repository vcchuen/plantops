import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WorkOrderCommands, describeCommandError, isConflict } from './work-order-commands';

describe('WorkOrderCommands', () => {
  let commands: WorkOrderCommands;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    commands = TestBed.inject(WorkOrderCommands);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('POSTs the body with If-Match exactly as received and returns the new ETag', async () => {
    const result = commands.execute('wo 1', 'assign', '"AAAAAAAB"', { technicianId: 't1' });
    const req = http.expectOne('/api/work-orders/wo%201/assign');
    expect(req.request.method).toBe('POST');
    expect(req.request.headers.get('If-Match')).toBe('"AAAAAAAB"');
    expect(req.request.body).toEqual({ technicianId: 't1' });
    req.flush(null, { status: 204, statusText: 'No Content', headers: { ETag: '"AAAAAAAC"' } });

    expect(await result).toBe('"AAAAAAAC"');
  });

  it('sends an empty object for commands without input', async () => {
    const result = commands.execute('wo1', 'start', '"x"');
    const req = http.expectOne('/api/work-orders/wo1/start');
    expect(req.request.body).toEqual({});
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(await result).toBeNull();
  });

  it('creates and returns the detail body', async () => {
    const body = { assetId: 'a1', title: 't', description: '', priority: 'P3', assetDown: false };
    const result = commands.create(body as never);
    const req = http.expectOne('/api/work-orders');
    expect(req.request.body).toEqual(body);
    req.flush({ id: 'wo1' }, { status: 201, statusText: 'Created' });
    expect((await result).id).toBe('wo1');
  });

  it('posts a comment and returns it', async () => {
    const result = commands.addComment('wo 1', 'Waiting for parts');
    const req = http.expectOne('/api/work-orders/wo%201/comments');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ body: 'Waiting for parts' });
    req.flush({ id: 'c1', sequence: 1 }, { status: 201, statusText: 'Created' });
    expect((await result).sequence).toBe(1);
  });

  it('edits a comment with PUT', async () => {
    const result = commands.editComment('wo1', 'c1', 'Fixed');
    const req = http.expectOne('/api/work-orders/wo1/comments/c1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ body: 'Fixed' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await expect(result).resolves.toBeUndefined();
  });

  it('rejects with the HttpErrorResponse so callers can read the status', async () => {
    const result = commands.execute('wo1', 'close', '"x"');
    http
      .expectOne('/api/work-orders/wo1/close')
      .flush(null, { status: 412, statusText: 'Precondition Failed' });
    await expect(result).rejects.toSatisfy(isConflict);
  });
});

describe('describeCommandError', () => {
  const err = (status: number, error: unknown) => new HttpErrorResponse({ status, error });

  it('prefers the problem text', () => {
    expect(describeCommandError(err(400, { title: 'Bad', detail: 'Nope' }))).toBe('Bad: Nope');
  });

  it('words 403 as an action refusal, not a view refusal', () => {
    expect(describeCommandError(err(403, null))).toContain('not allowed');
  });

  it('explains 428', () => {
    expect(describeCommandError(err(428, null))).toContain('version');
  });

  it('falls back for non-problem bodies', () => {
    expect(describeCommandError(err(502, '<html>'))).toContain('Something went wrong');
  });
});
