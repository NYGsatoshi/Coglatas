import { Component, ElementRef, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { LocalDraftStore } from '../local-draft.store';

@Component({
  selector: 'app-drafts-page',
  standalone: true,
  imports: [FormsModule, RouterLink],
  templateUrl: './drafts-page.component.html',
  styleUrl: './drafts-page.component.scss'
})
export class DraftsPageComponent {
  readonly store = inject(LocalDraftStore);
  readonly confirmDiscard = signal(false);
  private readonly element = inject<ElementRef<HTMLElement>>(ElementRef);

  discard(): void {
    this.store.discard();
    this.confirmDiscard.set(false);
    this.focus('#local-draft-title');
  }

  addLink(id: string): void { this.store.addLink(id); this.focus('#draft-link-query'); }
  removeLink(id: string): void {
    this.store.removeLink(id);
    this.focus(this.store.view().linksPreviewEnabled ? '#draft-link-query' : '#draft-links-toggle');
  }

  private focus(selector: string): void { this.element.nativeElement.querySelector<HTMLElement>(selector)?.focus(); }
}
