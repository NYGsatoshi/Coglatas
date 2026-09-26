(() => {
  try {
    const stored = localStorage.getItem('coglatas.ui.theme.v1');
    const theme = stored === 'dark' || stored === 'light'
      ? stored
      : matchMedia('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
    document.documentElement.dataset.coglatasTheme = theme;
    document.documentElement.dataset.coglatasDensity = matchMedia('(max-width: 860px), (pointer: coarse)').matches ? 'comfortable' : 'compact';
  } catch {
    // The dark and compact defaults remain in the document markup.
  }
})();
