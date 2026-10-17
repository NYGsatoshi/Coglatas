import { Component, inject, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { I18nService } from '../../core/i18n/i18n.service';
import { injectProjectsI18n } from '../../core/i18n/providers/projects-i18n';
import {
  normalizeProjectFeedbackError,
  projectFeedbackError,
  projectFeedbackField,
  projectFeedbackText,
  projectFeedbackValue,
  projectFeedbackView,
} from './project-feedback';

@Component({
  standalone: true,
  template: '<p>{{ view().message }}</p><span>{{ view().supplied }}</span>',
})
class FeedbackHost {
  readonly i18n = injectProjectsI18n();
  readonly raw = signal({
    ...projectFeedbackField(
      'message',
      projectFeedbackText('projectFeedback.projectCreate.enterAProjectName'),
    ),
    supplied: 'Enter a Project name.',
    selected: { id: 'opaque-id', title: 'User-authored 原文' },
  });
  view() {
    return projectFeedbackView(this.i18n, this.raw());
  }
}

describe('Project feedback provenance', () => {
  beforeEach(() => window.localStorage.setItem('coglatas.locale', 'ja'));
  afterEach(() => {
    window.localStorage.removeItem('coglatas.locale');
    TestBed.resetTestingModule();
  });

  it('switches mounted presentation without mutating the raw state or supplied text', () => {
    TestBed.configureTestingModule({ imports: [FeedbackHost] });
    const fixture = TestBed.createComponent(FeedbackHost);
    fixture.detectChanges();
    const raw = fixture.componentInstance.raw();
    expect(fixture.nativeElement.querySelector('p').textContent).toBe(
      'プロジェクト名を入力してください。',
    );
    expect(fixture.nativeElement.querySelector('span').textContent).toBe('Enter a Project name.');
    TestBed.inject(I18nService).setLocale('en');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('p').textContent).toBe('Enter a Project name.');
    expect(fixture.componentInstance.raw()).toBe(raw);
    expect(fixture.componentInstance.view().selected).toBe(raw.selected);
    expect(Object.getOwnPropertySymbols(fixture.componentInstance.view())).toHaveLength(0);
  });

  it('clears stale provenance when custom text replaces a field, including identical English text', () => {
    const i18n = TestBed.runInInjectionContext(injectProjectsI18n);
    const tagged = projectFeedbackField(
      'message',
      projectFeedbackText('projectFeedback.projectCreate.enterAProjectName'),
    );
    const replaced = { ...tagged, ...projectFeedbackField('message', 'Enter a Project name.') };
    expect(projectFeedbackView(i18n, replaced).message).toBe('Enter a Project name.');
    expect(projectFeedbackView(i18n, { ...tagged, message: 'Supplied 日本語' }).message).toBe(
      'Supplied 日本語',
    );
    expect(
      projectFeedbackView(i18n, { ...tagged, ...projectFeedbackField('message', undefined) })
        .message,
    ).toBeUndefined();
    expect(projectFeedbackValue(null as { message?: string } | null, 'message')).toBeUndefined();
  });

  it('localizes explicitly marked local diagnostics while retaining opaque server errors', () => {
    const i18n = TestBed.runInInjectionContext(injectProjectsI18n);
    const local = normalizeProjectFeedbackError(
      projectFeedbackError(
        projectFeedbackText('projectFeedback.projects.theParentProjectResponseIsInvalid'),
      ),
    );
    const remote = normalizeProjectFeedbackError({
      error: { message: 'The parent Project response is invalid.' },
    });
    expect(projectFeedbackView(i18n, local).message).toBe('親プロジェクトの応答が不正です。');
    expect(projectFeedbackView(i18n, remote).message).toBe(
      'The parent Project response is invalid.',
    );
  });
});
