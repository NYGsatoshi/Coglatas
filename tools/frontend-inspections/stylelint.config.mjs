export default {
  extends: ['stylelint-config-standard-scss'],
  customSyntax: 'postcss-scss',
  defaultSeverity: 'warning',
  reportDescriptionlessDisables: true,
  reportInvalidScopeDisables: true,
  reportNeedlessDisables: true,
  reportUnscopedDisables: true,
  ignoreFiles: [
    '**/node_modules/**',
    '**/bin/**',
    '**/obj/**',
    '**/dist/**',
    '**/.angular/**',
    '**/coverage/**',
    '**/TestResults/**',
    '**/test-results/**',
    '**/playwright-report/**',
    '**/.playwright/**',
    '**/.qodana/**',
    '**/storybook-static/**',
    'src/Coglatas.Web/wwwroot/**',
    'coglatas-frontend/**'
  ]
};
