// Flat ESLint config (ESLint 9) for the admin SPA: the core recommended set, TypeScript's
// recommended set, the React hooks rules, and Prettier turning off every formatting rule so the
// two tools never fight over the same line.
//
// Run: `npm run lint` (report) / `npm run lint:fix` (autofix the safe rules).
//
// The admin had no lint gate at all before this (C-8): the mobile app was the template, and this
// config is the admin's answer to it. The rules that matter here are the ones that catch a bug
// rather than a style: `no-explicit-any` (the money screens were typed `any` end to end, which is
// how a field rename on the server went unnoticed) and the hooks rules (the admin reads the
// session out of a store and calls hooks from effects).
import js from "@eslint/js";
import globals from "globals";
import reactHooks from "eslint-plugin-react-hooks";
import prettier from "eslint-config-prettier";
import tseslint from "typescript-eslint";

export default tseslint.config(
  {
    // Never lint generated output, dependencies, or the tooling's own config.
    ignores: [
      "node_modules/**",
      "dist/**",
      "coverage/**",
      "scripts/**",
      "*.config.js",
      "*.config.ts",
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  reactHooks.configs.recommended,
  {
    // The admin is a browser app: `window`, `document`, `localStorage` and the timers are
    // ambient. The test files add the Node globals they need on top.
    files: ["src/**/*.{ts,tsx}"],
    languageOptions: {
      globals: { ...globals.browser },
    },
    plugins: { "@typescript-eslint": tseslint.plugin },
    rules: {
      // TypeScript already reports use-before-define and the undefined ones; ESLint's core
      // version double-reports them and misfires on type-only references.
      "no-unused-vars": "off",
      "@typescript-eslint/no-unused-vars": [
        "error",
        {
          argsIgnorePattern: "^_",
          varsIgnorePattern: "^_",
          caughtErrorsIgnorePattern: "^_",
          ignoreRestSiblings: true,
        },
      ],
      // The point of the gate. `any` in a request or a response is a hole in the contract with
      // the API, and it is how the voucher screen kept asking for `brands` and `fuelTypesByBrand`
      // - field names the server stopped sending - without a single compiler complaint.
      "@typescript-eslint/no-explicit-any": "error",
      // An empty catch is a decision; a catch that swallows an error without saying so is a bug
      // waiting to be reported as something else.
      "no-empty": ["error", { allowEmptyCatch: false }],
    },
  },
  {
    // The React Compiler rules that ship with eslint-plugin-react-hooks v6 are a migration
    // signal, not a correctness bug on a codebase that does not run the compiler - and this
    // admin does not run it. They are off rather than warnings because the gate treats a
    // warning as a failure (`--max-warnings=0`), and a rule that can never be satisfied is
    // worse than no rule: the next agent turns the whole gate off instead.
    //
    // The classic correctness rules - rules-of-hooks and exhaustive-deps - keep their default
    // severity, and they are the ones that catch a real bug.
    files: ["src/**/*.{ts,tsx}"],
    rules: {
      "react-hooks/refs": "off",
      "react-hooks/set-state-in-effect": "off",
      "react-hooks/immutability": "off",
      "react-hooks/purity": "off",
      "react-hooks/static-components": "off",
      "react-hooks/use-memo": "off",
      "react-hooks/component-hook-factories": "off",
      "react-hooks/preserve-manual-memoization": "off",
      "react-hooks/set-state-in-render": "off",
      "react-hooks/globals": "off",
      "react-hooks/incompatible-library": "off",
      "react-hooks/error-boundaries": "off",
      "react-hooks/unsupported-syntax": "off",
    },
  },
  {
    // The test suite and the setup file. Without the Node globals, `no-undef` fires on every
    // test - the project had no tests when this config was written. Scoped to test files so
    // app code can never accidentally reference a test global.
    files: ["src/**/*.test.{ts,tsx}", "src/test/**"],
    languageOptions: {
      globals: { ...globals.node, ...globals.browser },
    },
  },
  prettier,
);
