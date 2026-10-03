import { TestBed } from '@angular/core/testing';
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { csrfInterceptor } from './csrf.interceptor';

describe('csrfInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([csrfInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('adds X-CSRF to unsafe /api requests', () => {
    for (const method of ['POST', 'PUT', 'PATCH', 'DELETE']) {
      client.request(method, '/api/x').subscribe();
      const req = http.expectOne('/api/x');
      expect(req.request.headers.get('X-CSRF')).toBe('1');
      req.flush(null);
    }
  });

  it('does not add it to GET', () => {
    client.get('/api/x').subscribe();
    const req = http.expectOne('/api/x');
    expect(req.request.headers.has('X-CSRF')).toBe(false);
    req.flush(null);
  });

  it('does not add it to absolute or non-/api URLs', () => {
    client.post('https://other.example/api/x', {}).subscribe();
    expect(http.expectOne('https://other.example/api/x').request.headers.has('X-CSRF')).toBe(false);

    client.post('/other', {}).subscribe();
    expect(http.expectOne('/other').request.headers.has('X-CSRF')).toBe(false);
  });
});
