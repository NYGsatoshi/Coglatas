import { fileURLToPath } from 'node:url';
import js from '@eslint/js';
import angular from 'angular-eslint';
import globals from 'globals';
import tseslint from 'typescript-eslint';

const repoRoot = fileURLToPath(new URL('../../', import.meta.url));
const frontendRoot = fileURLToPath(new URL('../../frontend/', import.meta.url));
const repoScriptsProject = fileURLToPath(new URL('./tsconfig.repo-scripts.json', import.meta.url));

const ignores = [
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
  'coglatas-frontend/**',
  'frontend/src/app/features/artifacts/report-reader-page/report-reader-page.component.html',
  'frontend/src/app/features/artifacts/report-reader-page/report-reader-page.component.spec.ts',
  'frontend/src/app/features/artifacts/report-reader-page/report-reader-page.component.ts'
];

function warningValue(value) {
  if (Array.isArray(value)) {
    return ['warn', ...value.slice(1)];
  }
  if (value === 0 || value === 'off') {
    return value;
  }
  return 'warn';
}

function scope(configs, files, extra = {}) {
  return configs.flat().map((config) => ({
    ...config,
    ...extra,
    files,
    rules: Object.fromEntries(
      Object.entries(config.rules ?? {}).map(([name, value]) => [name, warningValue(value)])
    )
  }));
}

const javascriptFiles = ['**/*.{js,mjs,cjs}'];
const frontendTypeScriptFiles = ['frontend/**/*.ts'];
const repoTypeScriptFiles = ['tests/**/*.ts', '*.ts'];
const angularTemplateFiles = ['frontend/**/*.html'];

export default tseslint.config(
  { ignores },

  ...scope([js.configs.all], javascriptFiles, {
    languageOptions: {
      ecmaVersion: 'latest',
      sourceType: 'module',
      globals: globals.node
    }
  }),

  ...scope(
    [
      js.configs.all,
      tseslint.configs.all,
      tseslint.configs.strictTypeChecked,
      tseslint.configs.stylisticTypeChecked,
      angular.configs.tsAll
    ],
    frontendTypeScriptFiles
  ),
  {
    files: frontendTypeScriptFiles,
    processor: angular.processInlineTemplates,
    languageOptions: {
      parserOptions: {
        project: ['./tsconfig.eslint.json'],
        tsconfigRootDir: frontendRoot
      }
    },
    rules: {
      // angular-eslint 22 adds these rules to `tsAll`. Keep the Angular 21
      // lint-policy surface stable during the framework/toolchain migration.
      '@angular-eslint/inject-at-top': 'off',
      '@angular-eslint/prefer-service-decorator': 'off',
      // Angular 22's compatibility schematic adds explicit `changeDetection`
      // metadata. Core `sort-keys` requires that key before `selector`, so align
      // only the newly introduced key while preserving angular-eslint's order
      // for every other Component metadata property.
      '@angular-eslint/sort-keys-in-type-decorator': [
        'warn',
        {
          Component: [
            'changeDetection',
            'selector',
            'imports',
            'standalone',
            'templateUrl',
            'template',
            'styleUrl',
            'styleUrls',
            'styles',
            'providers',
            'encapsulation',
            'viewProviders',
            'host',
            'hostDirectives',
            'inputs',
            'outputs',
            'animations',
            'schemas',
            'exportAs',
            'queries',
            'preserveWhitespaces',
            'jit',
            'moduleId',
            'interpolation'
          ]
        }
      ]
    }
  },

  ...scope(
    [js.configs.all, tseslint.configs.all, tseslint.configs.strictTypeChecked, tseslint.configs.stylisticTypeChecked],
    repoTypeScriptFiles
  ),
  {
    files: repoTypeScriptFiles,
    languageOptions: {
      parserOptions: {
        project: [repoScriptsProject],
        tsconfigRootDir: repoRoot
      }
    }
  },

  ...scope([angular.configs.templateAll], angularTemplateFiles),
  {
    files: angularTemplateFiles,
    rules: {
      // angular-eslint 22 adds these rules to `templateAll`. Enabling new
      // policy belongs in a separate lint-policy change, not ANG22-04.
      '@angular-eslint/template/no-outerhtml': 'off',
      '@angular-eslint/template/require-switch-default': 'off'
    }
  },
  {
    files: [
      'playwright.functional.config.ts',
      'scripts/ci/build-functional-grep.mjs',
      'scripts/ci/functional-tags.mjs',
      'scripts/ci/run-functional-playwright.mjs',
      'tests/functional/**/*.{mjs,ts}'
    ],
    rules: {
      'capitalized-comments': 'off',
      'func-style': 'off',
      'max-params': 'off',
      'max-statements': 'off',
      'no-await-in-loop': 'off',
      'no-console': 'off',
      'no-continue': 'off',
      'no-inline-comments': 'off',
      'no-magic-numbers': 'off',
      'no-ternary': 'off',
      'no-undefined': 'off',
      'no-use-before-define': 'off',
      'one-var': 'off',
      'prefer-destructuring': 'off',
      'prefer-named-capture-group': 'off',
      'require-unicode-regexp': 'off',
      'sort-imports': 'off',
      'sort-keys': 'off'
    }
  },
  {
    files: [
      'playwright.config.ts',
      'scripts/ci/build-compat-critical-grep.mjs',
      'scripts/ci/compat-critical-contract.mjs',
      'scripts/ci/os-portability-contract.mjs',
      'scripts/ci/run-compat-critical.mjs',
      'scripts/ci/verify-compat-critical.mjs',
      'scripts/ci/verify-os-portability-results.mjs',
      'scripts/ci/write-os-portability-evidence.mjs',
      'tests/ci/os-portability-contract.node-test.mjs',
      'tests/ui/compat-critical-contract.node-test.mjs'
    ],
    rules: {
      'capitalized-comments': 'off',
      'complexity': 'off',
      'func-style': 'off',
      'init-declarations': 'off',
      'max-lines': 'off',
      'max-lines-per-function': 'off',
      'max-params': 'off',
      'max-statements': 'off',
      'no-await-in-loop': 'off',
      'no-console': 'off',
      'no-magic-numbers': 'off',
      'no-plusplus': 'off',
      'no-shadow': 'off',
      'no-template-curly-in-string': 'off',
      'no-ternary': 'off',
      'no-undefined': 'off',
      'no-use-before-define': 'off',
      'no-useless-escape': 'off',
      'one-var': 'off',
      'prefer-destructuring': 'off',
      'preserve-caught-error': 'off',
      'require-unicode-regexp': 'off',
      'sort-imports': 'off',
      'sort-keys': 'off'
    }
  },
  {
    files: ['playwright.functional.config.ts', 'tests/functional/**/*.ts'],
    rules: {
      '@typescript-eslint/array-type': 'off',
      '@typescript-eslint/max-params': 'off',
      '@typescript-eslint/no-magic-numbers': 'off',
      '@typescript-eslint/no-unnecessary-type-assertion': 'off',
      '@typescript-eslint/no-unsafe-argument': 'off',
      '@typescript-eslint/no-unsafe-assignment': 'off',
      '@typescript-eslint/no-unsafe-call': 'off',
      '@typescript-eslint/no-unsafe-member-access': 'off',
      '@typescript-eslint/no-unsafe-return': 'off',
      '@typescript-eslint/no-unsafe-type-assertion': 'off',
      '@typescript-eslint/no-use-before-define': 'off',
      '@typescript-eslint/prefer-readonly-parameter-types': 'off',
      '@typescript-eslint/promise-function-async': 'off',
      '@typescript-eslint/restrict-template-expressions': 'off',
      '@typescript-eslint/strict-boolean-expressions': 'off'
    }
  }
);
