import { TestBed } from '@angular/core/testing';

import { MessageNavigationStateService } from './message-navigation-state.service';

describe('MessageNavigationStateService', () => {
  let host: HTMLElement | null = null;
  let documentHost: HTMLElement | null = null;
  let focusTarget: HTMLElement | null = null;
  const originalScrollingElementDescriptor = Object.getOwnPropertyDescriptor(
    document,
    'scrollingElement',
  );
  const originalMatchMediaDescriptor = Object.getOwnPropertyDescriptor(window, 'matchMedia');

  afterEach(() => {
    host?.remove();
    documentHost?.remove();
    focusTarget?.remove();
    host = null;
    documentHost = null;
    focusTarget = null;
    if (originalScrollingElementDescriptor) {
      Object.defineProperty(document, 'scrollingElement', originalScrollingElementDescriptor);
    } else {
      Reflect.deleteProperty(document, 'scrollingElement');
    }
    if (originalMatchMediaDescriptor) {
      Object.defineProperty(window, 'matchMedia', originalMatchMediaDescriptor);
    } else {
      Reflect.deleteProperty(window, 'matchMedia');
    }
    sessionStorage.clear();
    vi.restoreAllMocks();
    TestBed.resetTestingModule();
  });

  function installScrollHost(initialTop = 0, scrollable = true) {
    host = document.createElement('main');
    host.id = 'app-shell-main-content';
    host.scrollTop = initialTop;
    Object.defineProperty(host, 'scrollHeight', {
      configurable: true,
      value: scrollable ? 1200 : 400,
    });
    Object.defineProperty(host, 'clientHeight', {
      configurable: true,
      value: 400,
    });
    const scrollTo = vi.fn((options: ScrollToOptions) => {
      host!.scrollTop = Number(options.top ?? 0);
    });
    Object.defineProperty(host, 'scrollTo', {
      configurable: true,
      value: scrollTo,
    });
    document.body.append(host);
    return { host, scrollTo };
  }

  function installDocumentScrollHost(initialTop = 0, scrollable = true) {
    documentHost = document.createElement('div');
    documentHost.scrollTop = initialTop;
    Object.defineProperty(documentHost, 'scrollHeight', {
      configurable: true,
      value: scrollable ? 1600 : 400,
    });
    Object.defineProperty(documentHost, 'clientHeight', {
      configurable: true,
      value: 400,
    });
    const scrollTo = vi.fn((options: ScrollToOptions) => {
      documentHost!.scrollTop = Number(options.top ?? 0);
    });
    Object.defineProperty(documentHost, 'scrollTo', {
      configurable: true,
      value: scrollTo,
    });
    Object.defineProperty(document, 'scrollingElement', {
      configurable: true,
      get: () => documentHost,
    });
    return { host: documentHost, scrollTo };
  }

  function runAnimationFramesImmediately() {
    return vi
      .spyOn(window, 'requestAnimationFrame')
      .mockImplementation((callback: FrameRequestCallback) => {
        callback(0);
        return 1;
      });
  }

  function setMobileHierarchy(matches: boolean) {
    Object.defineProperty(window, 'matchMedia', {
      configurable: true,
      value: (query: string) =>
        ({
          matches: query === '(max-width: 860px)' && matches,
          media: query,
          onchange: null,
          addListener: vi.fn(),
          removeListener: vi.fn(),
          addEventListener: vi.fn(),
          removeEventListener: vi.fn(),
          dispatchEvent: vi.fn(() => true),
        }) as MediaQueryList,
    });
  }

  function installConversationFocusTarget(conversationId: string) {
    focusTarget = document.createElement('a');
    focusTarget.dataset['conversationId'] = conversationId;
    focusTarget.tabIndex = 0;
    document.body.append(focusTarget);
    return focusTarget;
  }

  it('remembers a scrollable AppShell content position', () => {
    const { host: scrollHost } = installScrollHost(384);
    installDocumentScrollHost(0, false);
    const service = TestBed.inject(MessageNavigationStateService);

    service.rememberListScroll();

    expect(scrollHost.scrollTop).toBe(384);
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-y.v1')).toBe('384');
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-host.v1')).toBe('app');
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-restore-pending.v1')).toBe('1');
  });

  it('uses the document scroll root when AppShell content expands instead of overflowing', () => {
    installScrollHost(0, false);
    const { host: pageScroll } = installDocumentScrollHost(420);
    const service = TestBed.inject(MessageNavigationStateService);

    service.rememberListScroll();

    expect(pageScroll.scrollTop).toBe(420);
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-y.v1')).toBe('420');
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-host.v1')).toBe('document');
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-restore-pending.v1')).toBe('1');
  });

  it('restores a remembered AppShell position once after the list is rendered', () => {
    const { host: scrollHost, scrollTo } = installScrollHost(512);
    installDocumentScrollHost(0, false);
    const service = TestBed.inject(MessageNavigationStateService);
    service.rememberListScroll();
    scrollHost.scrollTop = 0;
    runAnimationFramesImmediately();

    service.restoreListScroll();

    expect(scrollTo).toHaveBeenCalledWith({ top: 512, left: 0, behavior: 'auto' });
    expect(scrollHost.scrollTop).toBe(512);
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-y.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-restore-pending.v1')).toBeNull();
  });

  it('restores a remembered document position once when it is the effective scroll root', () => {
    installScrollHost(0, false);
    const { host: pageScroll, scrollTo } = installDocumentScrollHost(480);
    const service = TestBed.inject(MessageNavigationStateService);
    service.rememberListScroll();
    pageScroll.scrollTop = 0;
    runAnimationFramesImmediately();

    service.restoreListScroll();

    expect(scrollTo).toHaveBeenCalledWith({ top: 480, left: 0, behavior: 'auto' });
    expect(pageScroll.scrollTop).toBe(480);
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-y.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-restore-pending.v1')).toBeNull();
  });

  it('waits for the remembered document root when both roots can overflow', () => {
    const { scrollTo: appScrollTo } = installScrollHost(0, true);
    const { host: pageScroll } = installDocumentScrollHost(480, true);
    const target = installConversationFocusTarget('conversation-a');
    const service = TestBed.inject(MessageNavigationStateService);
    service.rememberListScroll('conversation-a');
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-host.v1')).toBe('document');
    pageScroll.scrollTop = 0;
    let documentRootReady = false;
    Object.defineProperty(pageScroll, 'scrollHeight', {
      configurable: true,
      get: () => (documentRootReady ? 1600 : 400),
    });
    const pageScrollTo = vi.fn((options: ScrollToOptions) => {
      if (documentRootReady) {
        pageScroll.scrollTop = Number(options.top ?? 0);
      }
      documentRootReady = true;
    });
    Object.defineProperty(pageScroll, 'scrollTo', {
      configurable: true,
      value: pageScrollTo,
    });
    runAnimationFramesImmediately();

    service.restoreListScroll();

    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-host.v1')).toBeNull();
    expect(pageScrollTo).toHaveBeenCalledWith({ top: 480, left: 0, behavior: 'auto' });
    expect(appScrollTo).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(target);
  });

  it('does not restore an old position when there is no pending conversation return', () => {
    const { scrollTo } = installScrollHost();
    installDocumentScrollHost(0, false);
    const service = TestBed.inject(MessageNavigationStateService);
    sessionStorage.setItem('coglatas.messaging.list-scroll-y.v1', '256');
    runAnimationFramesImmediately();

    service.restoreListScroll();

    expect(scrollTo).not.toHaveBeenCalled();
  });

  it('clears unscoped list restoration state at a Workspace boundary', () => {
    installScrollHost(384);
    installDocumentScrollHost(0, false);
    const service = TestBed.inject(MessageNavigationStateService);
    service.rememberListScroll('conversation-a');

    service.clearForWorkspaceBoundary();

    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-y.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-host.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-restore-pending.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-focus-conversation.v1')).toBeNull();
  });

  it('drops invalid stored positions instead of attempting to scroll', () => {
    const { scrollTo } = installScrollHost();
    installDocumentScrollHost(0, false);
    const service = TestBed.inject(MessageNavigationStateService);
    sessionStorage.setItem('coglatas.messaging.list-scroll-y.v1', 'not-a-position');
    sessionStorage.setItem('coglatas.messaging.list-scroll-restore-pending.v1', '1');

    service.restoreListScroll();

    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-y.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-restore-pending.v1')).toBeNull();
    expect(scrollTo).not.toHaveBeenCalled();
  });

  it('fails closed when session storage rejects a navigation-state write', () => {
    installScrollHost(384);
    installDocumentScrollHost(0, false);
    const service = TestBed.inject(MessageNavigationStateService);
    const setItem = Storage.prototype.setItem;
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(function (key, value) {
      if (key === 'coglatas.messaging.list-scroll-host.v1') {
        throw new DOMException('Storage quota exceeded', 'QuotaExceededError');
      }
      setItem.call(this, key, value);
    });

    expect(() => service.rememberListScroll('conversation-a')).not.toThrow();

    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-y.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-host.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-restore-pending.v1')).toBeNull();
  });

  it('fails closed when session storage rejects navigation-state reads', () => {
    const { scrollTo } = installScrollHost();
    installDocumentScrollHost(0, false);
    const service = TestBed.inject(MessageNavigationStateService);
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new DOMException('Storage access denied', 'SecurityError');
    });
    runAnimationFramesImmediately();

    expect(() => service.restoreListScroll()).not.toThrow();
    expect(scrollTo).not.toHaveBeenCalled();
  });

  it('fails closed when session storage rejects navigation-state removal', () => {
    const { scrollTo } = installScrollHost(384);
    installDocumentScrollHost(0, false);
    const service = TestBed.inject(MessageNavigationStateService);
    sessionStorage.setItem('coglatas.messaging.list-scroll-y.v1', '768');
    sessionStorage.setItem('coglatas.messaging.list-scroll-host.v1', 'app');
    sessionStorage.setItem('coglatas.messaging.list-scroll-restore-pending.v1', '1');
    const removeItem = Storage.prototype.removeItem;
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(function (key) {
      if (key === 'coglatas.messaging.list-scroll-restore-pending.v1') {
        throw new DOMException('Storage access denied', 'SecurityError');
      }
      removeItem.call(this, key);
    });
    runAnimationFramesImmediately();

    expect(() => service.rememberListScroll()).not.toThrow();
    expect(() => service.restoreListScroll()).not.toThrow();

    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-y.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-host.v1')).toBeNull();
    expect(sessionStorage.getItem('coglatas.messaging.list-scroll-restore-pending.v1')).toBe('1');
    expect(scrollTo).not.toHaveBeenCalled();
  });

  it('starts conversation detail at the top of both possible scroll roots', () => {
    const { host: scrollHost, scrollTo: appScrollTo } = installScrollHost(320);
    const { host: pageScroll, scrollTo: pageScrollTo } = installDocumentScrollHost(240);
    focusTarget = document.createElement('a');
    focusTarget.id = 'messages-mobile-back-link';
    focusTarget.tabIndex = 0;
    document.body.append(focusTarget);
    const service = TestBed.inject(MessageNavigationStateService);
    setMobileHierarchy(true);
    runAnimationFramesImmediately();

    service.resetDetailScroll();

    expect(appScrollTo).toHaveBeenCalledWith({ top: 0, left: 0, behavior: 'auto' });
    expect(pageScrollTo).toHaveBeenCalledWith({ top: 0, left: 0, behavior: 'auto' });
    expect(scrollHost.scrollTop).toBe(0);
    expect(pageScroll.scrollTop).toBe(0);
    expect(document.activeElement).toBe(focusTarget);
  });

  it('focuses the mobile Back link after the detail view renders on a later frame', () => {
    installScrollHost(320);
    installDocumentScrollHost(240);
    const frames: FrameRequestCallback[] = [];
    vi.spyOn(window, 'requestAnimationFrame').mockImplementation((callback: FrameRequestCallback) => {
      frames.push(callback);
      return frames.length;
    });
    const service = TestBed.inject(MessageNavigationStateService);
    setMobileHierarchy(true);

    service.resetDetailScroll();

    expect(frames).toHaveLength(1);
    frames.shift()?.(0);
    expect(frames).toHaveLength(1);

    focusTarget = document.createElement('a');
    focusTarget.id = 'messages-mobile-back-link';
    focusTarget.tabIndex = 0;
    document.body.append(focusTarget);
    frames.shift()?.(16);

    expect(document.activeElement).toBe(focusTarget);
  });

  it('does not reset the shared page scroll when desktop split view changes route', () => {
    const { host: scrollHost, scrollTo: appScrollTo } = installScrollHost(320);
    const { host: pageScroll, scrollTo: pageScrollTo } = installDocumentScrollHost(240);
    const service = TestBed.inject(MessageNavigationStateService);
    setMobileHierarchy(false);
    runAnimationFramesImmediately();

    service.resetDetailScroll();

    expect(appScrollTo).not.toHaveBeenCalled();
    expect(pageScrollTo).not.toHaveBeenCalled();
    expect(scrollHost.scrollTop).toBe(320);
    expect(pageScroll.scrollTop).toBe(240);
  });
});
