// Flat ESLint config (ESLint 9). Expo's recommended base + TypeScript, with
// Prettier turning off all formatting-related rules so the two never fight.
// Run: `npm run lint` (report) / `npm run lint:fix` (autofix safe rules).
const expoConfig = require('eslint-config-expo/flat');
const prettierConfig = require('eslint-config-prettier');
const tsPlugin = require('@typescript-eslint/eslint-plugin');
const globals = require('globals');
const useDesignTokens = require('./eslint-rules/use-design-tokens');

/**
 * Screens that predate the `tokens/use-design-tokens` rule and are exempt from
 * it until they are cleaned up. Roughly 590 hard-coded values live across these
 * files; the rule reports per file, not per line, so without an exemption list
 * any edit to `my-codes.tsx` would bury the author under 100+ pre-existing
 * errors and the rule would simply be switched off.
 *
 * This is a migration list, not a permanent allowance. **Deleting a line here is
 * the whole migration step for that file** — the rule then applies to it. New
 * screens are never added to this list, so they are guarded from day one, which
 * is the point: the debt here is historical, and the way it stays historical is
 * if the next screen cannot repeat it.
 *
 * Ordered by size, largest first, so the worst offenders are easiest to find.
/**
 * No screen is exempt from `tokens/use-design-tokens` any more.
 *
 * There used to be a `GRANDFATHERED_SCREENS` list here, holding every screen that
 * predated the rule - around 590 hard-coded values across sixteen files. The rule
 * reports per file rather than per line, so with no exemption list any edit to a big
 * screen buried the author under 100+ pre-existing errors and the rule would simply
 * have been switched off.
 *
 * It was a migration list where deleting a line was the whole migration step for a
 * file. The list is deleted rather than left empty on purpose: an empty exemption is
 * not a safety net, it is an invitation. What keeps the debt historical is having
 * nowhere left to put it.
 */

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
    // Screens must not re-decide numbers the design system already owns. See
    // `eslint-rules/use-design-tokens.js` for why the rule names a specific
    // token rather than just objecting to the number.
    //
    // `src/components/` is deliberately not covered yet: it is the pre-`features/`
    // flat folder and carries values of its own, so widening the glob should be its
    // own change with its own count, not a footnote here.
    files: ['app/**/*.tsx'],
    plugins: { tokens: { rules: { 'use-design-tokens': useDesignTokens } } },
    rules: { 'tokens/use-design-tokens': 'error' },
  },
  {
    // Jest globals (describe/it/expect/jest/beforeEach,...) for the test suite and
    // the shared setup file. Without this, `no-undef` fires on every test —
    // the project had no tests when this config was written. Scoped to test files so
    // app code can never accidentally reference a test global.
    files: ['**/*.test.{ts,tsx}', 'jest.setup.js'],
    languageOptions: {
      globals: { ...globals.jest, ...globals.node },
    },
  },
];
