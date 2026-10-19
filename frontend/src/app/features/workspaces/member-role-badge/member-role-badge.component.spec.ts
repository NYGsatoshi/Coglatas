import { TestBed } from '@angular/core/testing';

import { I18nService } from '../../../core/i18n/i18n.service';
import { WorkspaceMemberRole } from '../members.types';
import { MemberRoleBadgeComponent } from './member-role-badge.component';

describe('MemberRoleBadgeComponent canonical presentation', () => {
  afterEach(() => {
    TestBed.resetTestingModule();
    localStorage.removeItem('coglatas.locale');
  });

  it('renders all five roles distinctly in English and Japanese', async () => {
    await TestBed.configureTestingModule({ imports: [MemberRoleBadgeComponent] }).compileComponents();
    const fixture = TestBed.createComponent(MemberRoleBadgeComponent);
    const locale = TestBed.inject(I18nService);
    const roles: readonly WorkspaceMemberRole[] = ['Owner', 'Admin', 'Adviser', 'Member', 'ReadOnly'];
    const expected = {
      en: ['Owner', 'Admin', 'Adviser', 'Member', 'Read only'],
      ja: ['オーナー', '管理者', 'アドバイザー', 'メンバー', '閲覧のみ'],
    } as const;

    for (const language of ['en', 'ja'] as const) {
      locale.setLocale(language);
      for (const [index, role] of roles.entries()) {
        fixture.componentRef.setInput('role', role);
        fixture.detectChanges();
        expect(fixture.nativeElement.textContent.trim()).toBe(expected[language][index]);
      }
    }
  });

  it('shows unavailable when the grid provides no role instead of defaulting to Member', async () => {
    await TestBed.configureTestingModule({ imports: [MemberRoleBadgeComponent] }).compileComponents();
    const fixture = TestBed.createComponent(MemberRoleBadgeComponent);
    TestBed.inject(I18nService).setLocale('en');
    fixture.componentInstance.agInit({ value: 'Member' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent.trim()).toBe('Role unavailable');
  });
});
