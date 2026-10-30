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
import { dirname, join, basename, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const gate = join(repoRoot, "scripts", "npm-audit-gate.mjs");

// One advisory, in the shape npm's report uses: the advisory object carries the URL, the title
// and the affected range; the entry around it carries the severity and the package list.
function advisory({ id, severity, pkg, title = "an advisory", range = "<1.0.0", nodes = [], fixAvailable = false }) {
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
      nodes,
      fixAvailable,
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

// A temporary APP directory, not just a report directory. The gate resolves the app argument, so
// an absolute path lets a test hand it a package-lock.json of its own - the "resolved version" half
// of the failure is a claim about this tree, and the repository's real lockfiles change.
function withApp({ report: reportBody, lock }, run) {
  const dir = mkdtempSync(join(tmpdir(), "ff-app-"));
  try {
    writeFileSync(join(dir, `${basename(dir)}.json`), JSON.stringify(reportBody), "utf8");
    if (lock) writeFileSync(join(dir, "package-lock.json"), JSON.stringify(lock), "utf8");
    return run(dir);
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
}

function lockfile(entries) {
  return { name: "app", lockfileVersion: 3, packages: { "": { name: "app" }, ...entries } };
}

const shippedList = join(repoRoot, "scripts", "npm-audit-accepted-risk.json");

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

// ---------------------------------------------------------------------------------------------
// Planning #733: a new advisory blocks every open pull request, so the failure has to carry the
// decision - what this tree resolves, what version clears it, and the one command that gets there.
// The operator who had to read the advisory page to learn that zod 4.4.3 -> 4.6.6 was the fix is
// the acceptance criterion for this block.
// ---------------------------------------------------------------------------------------------

test("a blocking advisory names the resolved version, the affected range and the fix", () => {
  withApp(
    {
      report: report(
        advisory({
          id: "GHSA-fix1-fix1-fix1",
          severity: "high",
          pkg: "zod",
          range: "<4.6.6",
          nodes: ["node_modules/zod"],
          fixAvailable: { name: "zod", version: "4.6.6", isSemVerMajor: false },
        })
      ),
      lock: lockfile({ "node_modules/zod": { version: "4.4.3" } }),
    },
    (dir) => {
      const result = runGate(dir, ["--allow-list", shippedList, dir]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /resolved 4\.4\.3 \(node_modules\/zod\), the advisory affects <4\.6\.6/);
      assert.match(result.stdout, /patched 4\.6\.6/);
      assert.match(result.stdout, /npm install zod@4\.6\.6/);
    }
  );
});

test("a transitive fix names the parent to bump, and says a major bump when it is one", () => {
  withApp(
    {
      report: report(
        advisory({
          id: "GHSA-fix2-fix2-fix2",
          severity: "high",
          pkg: "image-size",
          nodes: ["node_modules/image-size"],
          fixAvailable: { name: "expo", version: "54.0.0", isSemVerMajor: true },
        })
      ),
      lock: lockfile({ "node_modules/image-size": { version: "1.2.1" } }),
    },
    (dir) => {
      const result = runGate(dir, ["--allow-list", shippedList, dir]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.match(
        result.stdout,
        /the fix is expo@54\.0\.0, a major bump of the package that carries image-size/
      );
      assert.match(result.stdout, /npm install expo@54\.0\.0/);
    }
  );
});

test("the version comes from the node npm blames, not from the first copy in the tree", () => {
  // Two copies: a nested one the advisory is about, and a hoisted one that is not. Reading the
  // hoisted version would tell the operator to upgrade a package that is already patched.
  withApp(
    {
      report: report(
        advisory({
          id: "GHSA-fix3-fix3-fix3",
          severity: "high",
          pkg: "zod",
          nodes: ["node_modules/some-parent/node_modules/zod"],
          fixAvailable: { name: "zod", version: "4.6.6", isSemVerMajor: false },
        })
      ),
      lock: lockfile({
        "node_modules/zod": { version: "4.6.6" },
        "node_modules/some-parent/node_modules/zod": { version: "4.0.0" },
      }),
    },
    (dir) => {
      const result = runGate(dir, ["--allow-list", shippedList, dir]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /resolved 4\.0\.0 \(node_modules\/some-parent\/node_modules\/zod\)/);
    }
  );
});

test("a package the lockfile marks dev-only is named as build-time", () => {
  withApp(
    {
      report: report(
        advisory({
          id: "GHSA-fix4-fix4-fix4",
          severity: "high",
          pkg: "jest-worker",
          nodes: ["node_modules/jest-worker"],
        })
      ),
      lock: lockfile({ "node_modules/jest-worker": { version: "29.7.0", dev: true } }),
    },
    (dir) => {
      const result = runGate(dir, ["--allow-list", shippedList, dir]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /build-time only: the lockfile marks this package dev-only/);
    }
  );
});

test("a package that ships is not called build-time", () => {
  withApp(
    {
      report: report(
        advisory({
          id: "GHSA-fix5-fix5-fix5",
          severity: "high",
          pkg: "react-native-webview",
          nodes: ["node_modules/react-native-webview"],
        })
      ),
      lock: lockfile({ "node_modules/react-native-webview": { version: "13.0.0" } }),
    },
    (dir) => {
      const result = runGate(dir, ["--allow-list", shippedList, dir]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.doesNotMatch(result.stdout, /build-time only/);
    }
  );
});

test("an advisory with no published fix says so instead of offering a command", () => {
  withApp(
    {
      report: report(
        advisory({
          id: "GHSA-fix6-fix6-fix6",
          severity: "high",
          pkg: "node-forge",
          nodes: ["node_modules/node-forge"],
        })
      ),
      lock: lockfile({ "node_modules/node-forge": { version: "1.4.0" } }),
    },
    (dir) => {
      const result = runGate(dir, ["--allow-list", shippedList, dir]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /no published version clears this/);
      assert.match(result.stdout, /accepted-risk list, with the reason/);
      assert.doesNotMatch(result.stdout, /npm install/);
    }
  );
});

test("a package the lockfile does not name still fails, and the output says why it cannot help", () => {
  withApp(
    {
      report: report(
        advisory({ id: "GHSA-fix7-fix7-fix7", severity: "high", pkg: "left-pad", nodes: ["node_modules/left-pad"] })
      ),
      lock: lockfile({ "node_modules/zod": { version: "4.4.3" } }),
    },
    (dir) => {
      const result = runGate(dir, ["--allow-list", shippedList, dir]);

      assert.equal(result.status, 1, `the gate should have failed: ${result.stdout}${result.stderr}`);
      assert.match(result.stdout, /the lockfile does not name left-pad/);
    }
  );
});
