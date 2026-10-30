// The gate's own tests.
//
// The acceptance criterion is two-sided: the gate has to FAIL on a tree that disagrees with the
// table, and it has to PASS on the repository as it is. CI proves the second half by running the
// gate over the real tree right after this file. This file proves the first half, plus every rule
// the real tree cannot exercise: the four failure classes, the exclusions, declarations and the
// exit code for a broken table.
//
// Everything here runs against a tree built in a temporary directory - the gate takes `--repo`
// precisely so a test can hand it one - and never reads the repository's own counts, which move
// with every pull request.

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const gate = join(repoRoot, "scripts", "code-size-gate.mjs");

// `lines` non-blank lines, so a file's size in a test reads as the number the assertion talks about.
function body(lines, indent = "") {
  return Array.from({ length: lines }, (_, index) => `${indent}const line${index} = ${index};`).join("\n");
}

// A tree and a table, written together: `files` maps a repo-relative path to its content, `table`
// is the exceptions file. Both are laid out under one temporary directory and removed after.
function withTree(t, { files = {}, table = { exceptions: [] }, extraArgs = [] }, run) {
  const root = mkdtempSync(join(tmpdir(), "code-size-gate-"));

  try {
    for (const [path, content] of Object.entries(files)) {
      const full = join(root, path);
      mkdirSync(dirname(full), { recursive: true });
      writeFileSync(full, content);
    }

    const tablePath = join(root, "table.json");
    writeFileSync(tablePath, typeof table === "string" ? table : JSON.stringify(table));

    const result = spawnSync(
      process.execPath,
      [gate, "--repo", root, "--exceptions", tablePath, ...extraArgs],
      { encoding: "utf8" },
    );

    return run(result);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

const row = (path, lines, reason = "a recorded reason") => ({ path, lines, reason });

test("a tree that agrees with the table passes", (t) => {
  withTree(
    t,
    {
      files: {
        "backend/src/Big.cs": body(600),
        "admin/src/small.tsx": body(120),
      },
      table: { target: 500, exceptions: [row("backend/src/Big.cs", 600)] },
    },
    (result) => {
      assert.equal(result.status, 0, result.stderr);
      assert.match(result.stdout, /1 file\(s\) over the line, 1 recorded/);
      assert.match(result.stdout, /agrees with the exceptions table/);
    },
  );
});

test("a file over the target with no row is refused, and the message says what to do", (t) => {
  withTree(
    t,
    { files: { "mobile/src/New.tsx": body(501) } },
    (result) => {
      assert.equal(result.status, 1);
      assert.match(result.stderr, /501 {2}mobile\/src\/New\.tsx/);
      assert.match(result.stderr, /has no row in the exceptions file/);
      assert.match(result.stderr, /docs\/CODE_SIZE\.md/);
    },
  );
});

test("exactly at the target is not over it", (t) => {
  withTree(
    t,
    { files: { "scripts/exact.mjs": body(500) } },
    (result) => assert.equal(result.status, 0, result.stderr),
  );
});

test("blank lines do not count", (t) => {
  const spaced = `${body(499)}\n\n\n   \n\t\n`;
  withTree(t, { files: { "scripts/spaced.mjs": spaced } }, (result) => {
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /0 file\(s\) over the line/);
  });
});

test("a row whose count differs from the tree is refused, and names both numbers", (t) => {
  withTree(
    t,
    {
      files: { "admin/src/Grown.tsx": body(620) },
      table: { exceptions: [row("admin/src/Grown.tsx", 610)] },
    },
    (result) => {
      assert.equal(result.status, 1);
      assert.match(result.stderr, /620 {2}admin\/src\/Grown\.tsx/);
      assert.match(result.stderr, /records 610/);
      assert.match(result.stderr, /set the row to 620/);
    },
  );
});

test("a row for a file that fell below the target is refused as stale", (t) => {
  withTree(
    t,
    {
      files: { "admin/src/Split.tsx": body(300) },
      table: { exceptions: [row("admin/src/Split.tsx", 640)] },
    },
    (result) => {
      assert.equal(result.status, 1);
      assert.match(result.stderr, /admin\/src\/Split\.tsx/);
      assert.match(result.stderr, /at or below the target and still has a row/);
    },
  );
});

test("a row for a file that does not exist is refused as missing", (t) => {
  withTree(t, { table: { exceptions: [row("mobile/app/gone.tsx", 900)] } }, (result) => {
    assert.equal(result.status, 1);
    assert.match(result.stderr, /the file does not exist/);
  });
});

test("every class of disagreement is reported in one run", (t) => {
  withTree(
    t,
    {
      files: {
        "backend/src/Unrecorded.cs": body(540),
        "backend/src/Drifted.cs": body(560),
        "backend/src/Stale.cs": body(100),
      },
      table: {
        exceptions: [
          row("backend/src/Drifted.cs", 550),
          row("backend/src/Stale.cs", 560),
          row("backend/src/Missing.cs", 600),
        ],
      },
    },
    (result) => {
      assert.equal(result.status, 1);
      assert.match(result.stderr, /4 file\(s\) disagree/);
      assert.match(result.stderr, /Unrecorded\.cs/);
      assert.match(result.stderr, /Drifted\.cs/);
      assert.match(result.stderr, /Stale\.cs/);
      assert.match(result.stderr, /Missing\.cs/);
    },
  );
});

test("tests, declarations, EF-generated code and the migration bodies are not measured", (t) => {
  withTree(
    t,
    {
      files: {
        "admin/src/Widget.test.tsx": body(900),
        "admin/src/lib/api-types.d.ts": body(900),
        "backend/src/FuelFlow.API/Migrations/20260101_Init.Designer.cs": body(1400),
        "backend/src/FuelFlow.API/Migrations/20260101_Init.cs": body(1400),
        "backend/src/FuelFlow.API/Migrations/ApplicationDbContextModelSnapshot.cs": body(1400),
        "mobile/src/features/x/__tests__/helper.ts": body(900),
        "mobile/src/features/x/node_modules/pkg/index.js": body(900),
        "admin/src/kept.ts": body(600),
      },
      table: { exceptions: [row("admin/src/kept.ts", 600)] },
    },
    (result) => {
      assert.equal(result.status, 0, result.stderr);
      assert.match(result.stdout, /1 production files/);
    },
  );
});

test("a declared file is not measured, and a declaration that points at nothing is refused", (t) => {
  withTree(
    t,
    {
      files: { "admin/src/lib/api-types.ts": body(541) },
      table: { declarations: ["admin/src/lib/api-types.ts"], exceptions: [] },
    },
    (result) => {
      assert.equal(result.status, 0, result.stderr);
      assert.match(result.stdout, /0 production files/);
    },
  );

  withTree(
    t,
    { table: { declarations: ["admin/src/lib/api-types.ts"], exceptions: [] } },
    (result) => {
      assert.equal(result.status, 1);
      assert.match(result.stderr, /listed under "declarations", but the file does not exist/);
    },
  );
});

test("the target comes from the table and --target overrides it", (t) => {
  const files = { "backend/src/Mid.cs": body(450) };

  withTree(t, { files, table: { target: 400, exceptions: [] } }, (result) => {
    assert.equal(result.status, 1);
    assert.match(result.stdout, /target 400/);
  });

  withTree(t, { files, table: { exceptions: [] }, extraArgs: ["--target", "450"] }, (result) => {
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /target 450/);
  });
});

test("a broken table is the gate's own failure, exit 2, not a verdict", (t) => {
  const cases = [
    { table: "{ not json", because: /could not be read/ },
    { table: { rows: [] }, because: /has no "exceptions" array/ },
    { table: { exceptions: [{ path: "a.ts", lines: 600 }] }, because: /has no reason for a\.ts/ },
    { table: { exceptions: [{ path: "a.ts", lines: 600.5, reason: "x" }] }, because: /an integer "lines"/ },
    {
      table: { exceptions: [row("a.ts", 600), row("a.ts", 610)] },
      because: /lists a\.ts twice/,
    },
    {
      table: { declarations: ["a.ts", "a.ts"], exceptions: [] },
      because: /lists a\.ts twice in "declarations"/,
    },
  ];

  for (const { table, because } of cases) {
    withTree(t, { table }, (result) => {
      assert.equal(result.status, 2);
      assert.match(result.stderr, because);
    });
  }
});

test("an unknown option or a positional argument is exit 2, not a silent pass", (t) => {
  withTree(t, { extraArgs: ["--depth", "2"] }, (result) => {
    assert.equal(result.status, 2);
    assert.match(result.stderr, /unknown option: --depth/);
  });

  withTree(t, { extraArgs: ["admin/src"] }, (result) => {
    assert.equal(result.status, 2);
    assert.match(result.stderr, /takes no positional arguments/);
  });
});
