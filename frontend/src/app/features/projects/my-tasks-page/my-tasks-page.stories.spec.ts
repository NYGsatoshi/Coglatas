import { I18nService } from '../../../core/i18n/i18n.service';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationConfig } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { Decorator, StoryObj } from '@storybook/angular';

import { PROJECTS_UNAUTHORIZED_TASK_TITLE } from '../projects.mock';
import { MyTasksPageComponent } from './my-tasks-page.component';
import meta, { TasksDefault, TasksManyRowsBoundedPage } from './my-tasks-page.stories';

beforeEach(() => localStorage.setItem('coglatas.locale', 'en'));

describe('My Tasks Storybook fixtures', () => {
  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
    TestBed.resetTestingModule();
  });

  it.each([
    ['default', TasksDefault, 'Prepare sample kickoff checklist', 4],
    ['many rows', TasksManyRowsBoundedPage, 'Sample bounded task 128', 128]
  ] as const)('keeps the %s fixture rendered after session effects settle', async (_label, story, title, count) => {
    await TestBed.configureTestingModule({
      imports: [MyTasksPageComponent],
      providers: [
        ...storyApplicationConfig(story).providers,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
    const fixture = TestBed.createComponent(MyTasksPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const host = fixture.nativeElement as HTMLElement;
    expect(host.textContent).toContain(title);
    expect(host.textContent).not.toContain('Loading tasks');
    expect(host.textContent).not.toContain(PROJECTS_UNAUTHORIZED_TASK_TITLE);
    expect(host.querySelectorAll('[data-testid="my-tasks-semantic-list"] button')).toHaveLength(count);
    TestBed.inject(HttpTestingController).expectNone(() => true);
  });

  it('switches Japanese to English while preserving filter input and relationship selection without HTTP', async () => {
    await TestBed.configureTestingModule({
      imports: [MyTasksPageComponent],
      providers: [...storyApplicationConfig(TasksDefault).providers, provideHttpClient(), provideHttpClientTesting()]
    }).compileComponents();
    const i18n = TestBed.inject(I18nService);
    i18n.setLocale('ja');
    const fixture = TestBed.createComponent(MyTasksPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const name = root.querySelector<HTMLInputElement>('[data-testid="my-tasks-saved-filter-name"]')!;
    name.value = '未保存 filter draft';
    name.dispatchEvent(new Event('input'));
    fixture.componentInstance.setTab('reviews');
    fixture.detectChanges();
    expect(root.querySelector('h1')?.textContent).toContain('マイタスク');
    expect(root.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toContain('自分のレビュー');

    i18n.setLocale('en');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(root.querySelector('h1')?.textContent).toContain('My tasks');
    expect(root.querySelector('[role="tab"][aria-selected="true"]')?.textContent).toContain('Reviews');
    expect(root.querySelector('[data-testid="my-tasks-saved-filter-name"]')).toBe(name);
    expect(name.value).toBe('未保存 filter draft');
    TestBed.inject(HttpTestingController).expectNone(() => true);
  });

  it('keeps saved fixture filters out of browser storage', async () => {
    await TestBed.configureTestingModule({
      imports: [MyTasksPageComponent],
      providers: [
        ...storyApplicationConfig(TasksDefault).providers,
        provideHttpClient(),
        provideHttpClientTesting()
      ]
    }).compileComponents();
    const persist = vi.spyOn(Storage.prototype, 'setItem');
    const fixture = TestBed.createComponent(MyTasksPageComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const name = host.querySelector<HTMLInputElement>('[data-testid="my-tasks-saved-filter-name"]')!;
    name.value = 'Story-only filter';
    name.dispatchEvent(new Event('input'));
    name.closest('form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    fixture.detectChanges();

    expect(host.textContent).toContain('Story-only filter');
    expect(persist.mock.calls.every(([key]) => key === 'coglatas.locale')).toBe(true);
    persist.mockRestore();
  });
});

function storyApplicationConfig(story: StoryObj<MyTasksPageComponent>): ApplicationConfig {
  const decorators = [meta.decorators, story.decorators].flatMap((value) =>
    value ? (Array.isArray(value) ? value : [value]) : []) as Decorator[];
  const render = decorators.reduceRight<() => { applicationConfig?: ApplicationConfig }>(
    (next, decorate) => () => decorate(next as never, {} as never),
    () => ({})
  );
  return render().applicationConfig!;
}
