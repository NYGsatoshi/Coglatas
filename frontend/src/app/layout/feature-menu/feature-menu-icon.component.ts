import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { LucideDynamicIcon, type LucideIconData } from '@lucide/angular';

/*! Icon data from @lucide/angular 1.53.0.
 * ISC License
 *
 * Copyright (c) 2026 Lucide Icons and Contributors
 *
 * Permission to use, copy, modify, and/or distribute this software for any
 * purpose with or without fee is hereby granted, provided that the above
 * copyright notice and this permission notice appear in all copies.
 *
 * THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
 * WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
 * MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
 * ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
 * WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
 * ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
 * OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
 */
// One Lucide renderer avoids repeating the vendor Angular SVG template.
// Recheck these data-only definitions when upgrading the installed package.
const NAVIGATION_ICONS: Record<string, LucideIconData> = {
  'teams': {"name":"users-round","size":24,"node":[["path",{"d":"M18 21a8 8 0 0 0-16 0","key":"3ypg7q"}],["circle",{"cx":"10","cy":"8","r":"5","key":"o932ke"}],["path",{"d":"M22 20c0-3.37-2-6.5-4-8a5 5 0 0 0-.45-8.3","key":"10s06x"}]],"aliases":["users-2"]},
  'workspaces': {"name":"house","size":24,"node":[["path",{"d":"M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8","key":"5wwlr5"}],["path",{"d":"M3 10a2 2 0 0 1 .709-1.528l7-6a2 2 0 0 1 2.582 0l7 6A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z","key":"r6nss1"}]],"aliases":["home"]},
  'projects': {"name":"layout-grid","size":24,"node":[["rect",{"width":"7","height":"7","x":"3","y":"3","rx":"1","key":"1g98yp"}],["rect",{"width":"7","height":"7","x":"14","y":"3","rx":"1","key":"6d4xhi"}],["rect",{"width":"7","height":"7","x":"14","y":"14","rx":"1","key":"nxv5o0"}],["rect",{"width":"7","height":"7","x":"3","y":"14","rx":"1","key":"1bb6yr"}]]},
  'my-tasks': {"name":"list-checks","size":24,"node":[["path",{"d":"M13 5h8","key":"a7qcls"}],["path",{"d":"M13 12h8","key":"h98zly"}],["path",{"d":"M13 19h8","key":"c3s6r1"}],["path",{"d":"m3 17 2 2 4-4","key":"1jhpwq"}],["path",{"d":"m3 7 2 2 4-4","key":"1obspn"}]]},
  'messages': {"name":"messages-square","size":24,"node":[["path",{"d":"M16 10a2 2 0 0 1-2 2H6.828a2 2 0 0 0-1.414.586l-2.202 2.202A.71.71 0 0 1 2 14.286V4a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z","key":"1n2ejm"}],["path",{"d":"M20 9a2 2 0 0 1 2 2v10.286a.71.71 0 0 1-1.212.502l-2.202-2.202A2 2 0 0 0 17.172 19H10a2 2 0 0 1-2-2v-1","key":"1qfcsi"}]]},
  'announcements': {"name":"megaphone","size":24,"node":[["path",{"d":"M11 6a13 13 0 0 0 8.4-2.8A1 1 0 0 1 21 4v12a1 1 0 0 1-1.6.8A13 13 0 0 0 11 14H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2z","key":"q8bfy3"}],["path",{"d":"M6 14a12 12 0 0 0 2.4 7.2 2 2 0 0 0 3.2-2.4A8 8 0 0 1 10 14","key":"1853fq"}],["path",{"d":"M8 6v8","key":"15ugcq"}]]},
  'files': {"name":"files","size":24,"node":[["path",{"d":"M15 2h-4a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2h8a2 2 0 0 0 2-2V8","key":"14sh0y"}],["path",{"d":"M16.706 2.706A2.4 2.4 0 0 0 15 2v5a1 1 0 0 0 1 1h5a2.4 2.4 0 0 0-.706-1.706z","key":"1970lx"}],["path",{"d":"M5 7a2 2 0 0 0-2 2v11a2 2 0 0 0 2 2h8a2 2 0 0 0 1.732-1","key":"l4dndm"}]]},
  'account': {"name":"user-round","size":24,"node":[["circle",{"cx":"12","cy":"8","r":"5","key":"1hypcn"}],["path",{"d":"M20 21a8 8 0 0 0-16 0","key":"rfgkzh"}]],"aliases":["user-2"]},
  'audit': {"name":"clipboard-list","size":24,"node":[["rect",{"width":"8","height":"4","x":"8","y":"2","rx":"1","ry":"1","key":"tgr4d6"}],["path",{"d":"M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2","key":"116196"}],["path",{"d":"M12 11h4","key":"1jrz19"}],["path",{"d":"M12 16h4","key":"n85exb"}],["path",{"d":"M8 11h.01","key":"1dfujw"}],["path",{"d":"M8 16h.01","key":"18s6g9"}]]},
  'invites': {"name":"mail","size":24,"node":[["path",{"d":"m22 7-8.991 5.727a2 2 0 0 1-2.009 0L2 7","key":"132q7q"}],["rect",{"x":"2","y":"4","width":"20","height":"16","rx":"2","key":"izxlao"}]]},
  'fallback': {"name":"pin","size":24,"node":[["path",{"d":"M12 17v5","key":"bb1du9"}],["path",{"d":"M9 10.76a2 2 0 0 1-1.11 1.79l-1.78.9A2 2 0 0 0 5 15.24V16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-.76a2 2 0 0 0-1.11-1.79l-1.78-.9A2 2 0 0 1 15 10.76V7a1 1 0 0 1 1-1 2 2 0 0 0 0-4H8a2 2 0 0 0 0 4 1 1 0 0 1 1 1z","key":"1nkz8b"}]]}
};

@Component({
  selector: 'app-feature-menu-icon',
  standalone: true,
  imports: [LucideDynamicIcon],
  template: '<svg [lucideIcon]="icon" [size]="18" aria-hidden="true"></svg>',
  styles: [':host { display: inline-flex; align-items: center; justify-content: center; width: 18px; height: 18px; flex: 0 0 auto; }'],
  changeDetection: ChangeDetectionStrategy.OnPush
})
export class FeatureMenuIconComponent {
  @Input({ required: true }) destination!: string;

  get icon(): LucideIconData {
    if (this.destination === 'dm') { return NAVIGATION_ICONS['messages']; }
    return NAVIGATION_ICONS[this.destination === 'tasks' ? 'my-tasks' : this.destination] ?? NAVIGATION_ICONS['fallback'];
  }
}
