// Flat ESLint config (ESLint 9). Expo's recommended base + TypeScript, with
// Prettier turning off all formatting-related rules so the two never fight.
// Run: `npm run lint` (report) / `npm run lint:fix` (autofix safe rules).
const expoConfig = require('eslint-config-expo/flat');
const prettierConfig = require('eslint-config-prettier');
const tsPlugin = require('@typescript-eslint/eslint-plugin');
const globals = require('globals');

module.exports = [
  ...expoConfig,
  prettierConfig,
  {
    // Never lint generated, native, or build output.
    ignores: [
      'node_modules/**',
      '.expo/**',
      'dist/**',
      'build/**',
      'web-build/**',
      'android/**',
      'ios/**',
      'scripts/**',
      'babel.config.cjs',
      'tailwind.config.cjs',
      'metro.config.js',
      'nativewind-env.d.ts',
      'expo-env.d.ts',
      '*.log',
    ],
  },
  {
    // Scope to TS files and register the plugin in the same object that uses its
    // rule — flat config does not share a plugin across config objects.
    files: ['**/*.{ts,tsx}'],
    plugins: { '@typescript-eslint': tsPlugin },
    rules: {
      // TypeScript already reports use-before-define / undefined; ESLint's core
      // version double-reports and misfires on type-only references.
      'no-unused-vars': 'off',
      // Surface dead locals/args as warnings (tsconfig has noUnusedLocals off, so
      // this is the only thing catching them). `_`-prefixed names are intentional.
      '@typescript-eslint/no-unused-vars': [
        'warn',
        {
          argsIgnorePattern: '^_',
          varsIgnorePattern: '^_',
          caughtErrorsIgnorePattern: '^_',
          ignoreRestSiblings: true,
        },
      ],
    },
  },
  {
    // eslint-plugin-react-hooks v6 (bundled by eslint-config-expo) ships the
    // React Compiler readiness rules as errors. They're valuable as a migration
    // signal but are not correctness bugs on a codebase that doesn't yet run the
    // compiler, so surface them as warnings. The classic correctness rules
    // (`rules-of-hooks`, `exhaustive-deps`) keep their default expo severities.
    rules: {
      'react-hooks/refs': 'warn',
      'react-hooks/set-state-in-effect': 'warn',
      'react-hooks/immutability': 'warn',
      'react-hooks/purity': 'warn',
    },
  },
  {
    // Jest globals (describe/it/expect/jest/beforeEach/...) for the test suite and
    // the shared setup file. Without this, `no-undef` fires on every test — the
    // project had no tests when this config was written. Scoped to test files so
    // app code can never accidentally reference a test global.
    files: ['**/*.test.{ts,tsx}', 'jest.setup.js'],
    languageOptions: {
      globals: { ...globals.jest, ...globals.node },
    },
  },
];
