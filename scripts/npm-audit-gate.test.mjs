// The gate's own tests.
//
// The acceptance criterion for the JavaScript advisory gate is two-sided: it fails on a
// deliberately introduced advisory, and it passes on the current tree. The second half is proved
// by the CI job itself, which runs the gate over admin and mobile with the real advisory
// database. This file proves the first half, and the parts of the gate the real run cannot
// reach on a clean tree: a stale entry, an advisory below the level, a broken list.
//
// The reports are synthetic and written to a temporary directory: the gate reads them through
// --report-dir, so the test needs neither the network nor an install.

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const gate = join(repoRoot, "scripts", "npm-audit-gate.mjs");

// One advisory, in the shape npm's report uses: the advisory object carries the URL, the title
// and the affected range; the entry around it carries the severity and the package list.
function advisory({ id, severity, pkg, title = "an advisory", range = "<1.0.0" }) {
  return {
    [pkg]: {
      name: pkg,
      severity,
      isDirect: false,
      via: [
        {
          source: 1,
          name: pkg,
          dependency: pkg,
          title,
          url: `https://github.com/advisories/${id}`,
          severity,
          range,
        },
      ],
      effects: [],
      range,
      nodes: [`node_modules/${pkg}`],
      fixAvailable: false,
    },
  };
}

function report(advisories) {
  return {
    auditReportVersion: 2,
    vulnerabilities: advisories,
    metadata: { vulnerabilities: { info: 0, low: 0, moderate: 0, high: 0, critical: 0, total: 0 } },
  };
}

// A list of the shape the gate reads, carrying one accepted advisory. The tests that are about the
// gate's behaviour use a list they own: the shipped list changes whenever an advisory is fixed or
// withdrawn, and a test that names an entry of it breaks on that day without the gate changing.
function listWith(id, pkg) {
  return {
    level: "high",
    advisories: {
      [id]: {
        package: pkg,
        severity: "high",
        apps: ["mobile"],
        path: `${pkg} <- something in the tree`,
        fix: "None. Every published version is affected.",
        reason: "A reason long enough to say why the risk is accepted, and what the attack needs.",
      },
    },
  };
}

function withReportDir(files, run) {
  const dir = mkdtempSync(join(tmpdir(), "ff-npm-audit-"));
  try {
    for (const [name, content] of Object.entries(files)) {
      mkdirSync(dirname(join(dir, name)), { recursive: true });
      writeFileSync(join(dir, name), JSON.stringify(content), "utf8");
    }
    return run(dir);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

function runGate(dir, args = []) {
  return spawnSync(process.execPath, [gate, "--report-dir", dir, ...args], {
    cwd: repoRoot,
    encoding: "utf8",
  });
}

test("an advisory that is not on the list fails the gate", () => {
  withReportDir(
    { "mobile.json": report(advisory({ id: "GHSA-test1-test1-test1", severity: "high", pkg: "left-pad" })) },
    (dir) => {
      const result = runGate(dir, ["--allow-list", join(repoRoot, "scripts", "npm-audit-accepted-risk.json"), "mobile"]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /GHSA-test1-test1-test1/);
      assert.match(result.stderr, /not on the accepted-risk list/);
    }
  );
});

test("a critical advisory that is not on the list fails the gate", () => {
  withReportDir(
    { "mobile.json": report(advisory({ id: "GHSA-test2-test2-test2", severity: "critical", pkg: "left-pad" })) },
    (dir) => {
      const result = runGate(dir, ["--allow-list", join(repoRoot, "scripts", "npm-audit-accepted-risk.json"), "mobile"]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /critical/);
    }
  );
});

test("an advisory that is on the list passes, and the reason is printed", () => {
  withReportDir(
    {
      "mobile.json": report(
        advisory({
          id: "GHSA-test5-test5-test5",
          severity: "high",
          pkg: "braces",
          title: "braces vulnerable to stack-exhaustion denial of service through deeply nested patterns",
        })
      ),
    },
    (dir) => {
      const listFile = join(dir, "list.json");
      writeFileSync(listFile, JSON.stringify(listWith("GHSA-test5-test5-test5", "braces")), "utf8");

      const result = runGate(dir, ["--allow-list", listFile, "mobile"]);

      assert.equal(result.status, 0, `the gate should have passed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /accepted/);
      assert.match(result.stdout, /reason: /);
    }
  );
});

test("an advisory below the level is reported but does not fail the gate", () => {
  withReportDir(
    { "mobile.json": report(advisory({ id: "GHSA-test3-test3-test3", severity: "moderate", pkg: "left-pad" })) },
    (dir) => {
      const result = runGate(dir, ["--allow-list", join(repoRoot, "scripts", "npm-audit-accepted-risk.json"), "mobile"]);

      assert.equal(result.status, 0, `the gate should have passed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /below the level 'high', reported only/);
    }
  );
});

test("an entry on the list that no longer applies is a warning, not a failure", () => {
  withReportDir(
    { "mobile.json": report({}) },
    (dir) => {
      const listFile = join(dir, "list.json");
      writeFileSync(listFile, JSON.stringify(listWith("GHSA-test6-test6-test6", "braces")), "utf8");

      const result = runGate(dir, ["--allow-list", listFile, "mobile"]);

      assert.equal(result.status, 0, `the gate should have passed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /warning: GHSA-test6-test6-test6/);
    }
  );
});

test("a list entry without a reason is refused - an accepted advisory without a reason is a rumour", () => {
  withReportDir(
    { "mobile.json": report({}) },
    (dir) => {
      const listFile = join(dir, "list.json");
      writeFileSync(
        listFile,
        JSON.stringify({ level: "high", advisories: { "GHSA-test4-test4-test4": { package: "left-pad" } } }),
        "utf8"
      );

      const result = runGate(dir, ["--allow-list", listFile, "mobile"]);

      assert.equal(result.status, 2, `the gate should have refused to run: ${result.stdout}${result.stderr}`);
      assert.match(result.stderr, /no reason for GHSA-test4-test4-test4/);
    }
  );
});

test("the shipped list carries a decision for every advisory it accepts", () => {
  // The list is a decision, not a number: an advisory that npm's database names and the list
  // does not is a build failure, so every entry has to say what it is, where it comes from, what
  // the fix is (or that there is none), which app it applies to, and why it is accepted.
  const list = JSON.parse(readFileSync(join(repoRoot, "scripts", "npm-audit-accepted-risk.json"), "utf8"));

  const ids = Object.keys(list.advisories);
  assert.ok(ids.length > 0, "the list is empty - it should carry the tree's advisories");
  for (const id of ids) {
    const entry = list.advisories[id];
    assert.match(id, /^GHSA-[0-9A-Za-z-]+$/, `${id} is not an advisory id`);
    assert.ok(entry.package, `${id} names no package`);
    assert.ok(entry.path, `${id} has no path`);
    assert.ok(entry.fix, `${id} has no fix - or a statement that there is none`);
    assert.ok(entry.apps.length > 0, `${id} names no app`);
    assert.ok(entry.reason.length > 20, `${id} has no reason`);
  }
});
