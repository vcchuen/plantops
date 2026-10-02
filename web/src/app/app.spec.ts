import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { App } from './app';
import { routes } from './app.routes';

describe('App shell', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('renders the toolbar title, landmarks and the nav links', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('header')?.textContent).toContain('PlantOps');
    expect(el.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('System status');
    expect(el.querySelector('nav[aria-label="Primary"]')?.textContent).toContain('Assets');
    expect(el.querySelector('main#main-content')).not.toBeNull();
    expect(el.querySelector('.skip-link')).not.toBeNull();
  });
});
