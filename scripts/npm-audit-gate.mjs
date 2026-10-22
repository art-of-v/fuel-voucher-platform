#!/usr/bin/env node
// The JavaScript dependency gate.
//
// The .NET side of this repository has had an advisory gate since the Hangfire/Newtonsoft
// incident: .github/workflows/ci.yml runs `dotnet list package --vulnerable --include-transitive`
// across all five projects and fails the build on anything outside an explicit accepted-risk list
// (the five ImageSharp 2.1.13 advisories are the only exceptions, each with its reason in the
// workflow). The JavaScript apps - the admin SPA and the mobile app, the two that run on the
// operator's and the customer's devices - had no such gate at all: a package with a known
// advisory could be merged and nothing would say so.
//
// This script is that gate. It runs `npm audit --json` per app, collects the advisories the report
// names, and fails the build on any advisory at or above the level that is NOT on the
// accepted-risk list (scripts/npm-audit-accepted-risk.json). Below the level an advisory is
// reported but does not fail the build - the same shape as the NuGet gate: the list carries the
// decision, and every entry in it carries a reason.
//
// It reads the lockfile, not node_modules, so it needs no install: `npm audit` resolves the tree
// from package-lock.json. That keeps the job cheap and lets it run before anything is installed.
//
// Usage:
//   node scripts/npm-audit-gate.mjs [--level <severity>] [--allow-list <file>] [--report-dir <dir>] <app> [<app>...]
//
//   --level        the lowest severity that fails the build (default: the list's own "level")
//   --allow-list   the accepted-risk list (default: scripts/npm-audit-accepted-risk.json)
//   --report-dir   read <dir>/<app>.json instead of running npm audit - the test hook
//
// Exit 0 = every advisory at or above the level is on the list, 1 = one is not,
// 2 = the gate itself could not run (a broken list, an unreadable report, npm failing).

import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { basename, dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SEVERITIES = ["info", "low", "moderate", "high", "critical"];
const DEFAULT_LEVEL = "high";
const EXIT_BLOCKED = 1;
const EXIT_ERROR = 2;

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");

function rank(severity) {
  const index = SEVERITIES.indexOf(severity);
  return index === -1 ? SEVERITIES.length : index;
}

function parseArgs(argv) {
  const options = { level: null, allowList: null, reportDir: null, apps: [] };

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    if (arg === "--level") {
      index += 1;
      options.level = argv[index];
    } else if (arg === "--allow-list") {
      index += 1;
      options.allowList = argv[index];
    } else if (arg === "--report-dir") {
      index += 1;
      options.reportDir = argv[index];
    } else if (arg.startsWith("-")) {
      throw new Error(`unknown option: ${arg}`);
    } else {
      options.apps.push(arg);
    }
  }

  if (options.apps.length === 0) {
    throw new Error("no app given - pass the app directory to scan, for example: mobile");
  }
  if (options.level && !SEVERITIES.includes(options.level)) {
    throw new Error(`unknown severity: ${options.level}`);
  }

  options.allowList = resolve(options.allowList ?? join(repoRoot, "scripts", "npm-audit-accepted-risk.json"));
  return options;
}

function loadAcceptedRisks(path) {
  let parsed;
  try {
    parsed = JSON.parse(readFileSync(path, "utf8"));
  } catch (error) {
    throw new Error(`the accepted-risk list ${path} could not be read: ${error.message}`);
  }
  if (!parsed || typeof parsed.advisories !== "object" || parsed.advisories === null) {
    throw new Error(`the accepted-risk list ${path} has no "advisories" object`);
  }
  for (const [id, entry] of Object.entries(parsed.advisories)) {
    if (!entry || typeof entry.reason !== "string" || entry.reason.trim() === "") {
      throw new Error(`the accepted-risk list ${path} has no reason for ${id} - an accepted advisory without a reason is a rumour`);
    }
  }
  return parsed;
}

// `npm audit` exits 1 when it FINDS advisories, and still prints the report on stdout - so the
// exit code says nothing and the report is the answer. A non-zero exit with no report on stdout
// is a failure of npm itself (no lockfile, no network) and has to stop the gate rather than
// pass it silently.
function runAudit(appDir, reportDir) {
  if (reportDir) {
    const file = join(reportDir, `${basename(appDir)}.json`);
    return JSON.parse(readFileSync(file, "utf8"));
  }

  let stdout = "";
  try {
    stdout = execFileSync("npm", ["audit", "--json"], {
      cwd: appDir,
      encoding: "utf8",
      // `npm` is npm.cmd on Windows, and Windows cannot launch a .cmd file without a shell -
      // the same reason the CI runner is ubuntu and the developer laptop is not.
      shell: true,
      stdio: ["ignore", "pipe", "pipe"],
      maxBuffer: 128 * 1024 * 1024,
    });
  } catch (error) {
    stdout = typeof error.stdout === "string" ? error.stdout : "";
    if (stdout.trim() === "") {
      const detail = (typeof error.stderr === "string" ? error.stderr : "").trim() || error.message;
      throw new Error(`npm audit failed in ${appDir}: ${detail}`);
    }
  }

  try {
    return JSON.parse(stdout);
  } catch (error) {
    throw new Error(`npm audit in ${appDir} did not return a JSON report: ${error.message}`);
  }
}

// npm's report lists two kinds of `via` entries: the advisory itself (an object with the URL,
// the title and the affected range) and the name of another vulnerable package (a string). Only
// the first is an advisory; the second is the propagation of one, and it is counted where it
// starts. Counting both would put the same advisory on the list once per package that carries it.
function collectAdvisories(report) {
  const advisories = new Map();

  for (const [name, entry] of Object.entries(report.vulnerabilities ?? {})) {
    for (const via of entry.via ?? []) {
      if (typeof via === "string") continue;

      const id = (via.url ?? "").match(/\/advisories\/(GHSA-[0-9A-Za-z-]+)/)?.[1];
      if (!id || advisories.has(id)) continue;

      advisories.set(id, {
        id,
        package: via.name ?? name,
        severity: via.severity ?? entry.severity ?? "moderate",
        title: via.title ?? "",
        range: via.range ?? "",
        url: via.url ?? "",
        direct: entry.isDirect === true,
      });
    }
  }

  return advisories;
}

function evaluate(appName, advisories, accepted, level) {
  const threshold = rank(level);
  const blocked = [];
  const acceptedHere = [];

  for (const advisory of advisories.values()) {
    const entry = accepted[advisory.id];
    if (!entry && rank(advisory.severity) >= threshold) {
      blocked.push(advisory);
    } else {
      acceptedHere.push({ advisory, entry });
    }
  }

  // An accepted-risk entry that no longer appears in the report is dead weight: the dependency
  // moved past the advisory and the reason it was written no longer says anything. It is a
  // warning, not a failure - the advisory database gains and loses entries, and a build must not
  // break because an upstream advisory was withdrawn.
  const stale = Object.entries(accepted)
    .filter(([id, entry]) => (entry.apps ?? []).includes(appName) && !advisories.has(id))
    .map(([id, entry]) => ({ id, entry }));

  return { blocked, acceptedHere, stale };
}

function describe(appName, result, level) {
  const lines = [];
  const blocking = result.blocked.length;
  const listed = result.acceptedHere.filter((item) => item.entry).length;
  const belowLevel = result.acceptedHere.filter((item) => !item.entry).length;

  lines.push(`== ${appName}`);
  if (result.acceptedHere.length === 0 && result.blocked.length === 0) {
    lines.push("   npm audit reported no advisories.");
  }

  for (const { advisory, entry } of result.acceptedHere) {
    const verdict = entry ? "accepted" : `below the level '${level}', reported only`;
    lines.push(
      `   ${advisory.severity.padEnd(8)} ${advisory.id}  ${advisory.package}  (${verdict})`
    );
    lines.push(`            ${advisory.title}`);
    if (entry) {
      lines.push(`            reason: ${entry.reason}`);
      if (entry.fix) lines.push(`            fix:   ${entry.fix}`);
    }
  }

  if (blocking > 0) {
    lines.push(
      `   ${blocking} advisory/advisories at or above the level '${level}' are NOT on the accepted-risk list:`
    );
    for (const advisory of result.blocked) {
      lines.push(`   ${advisory.severity.padEnd(8)} ${advisory.id}  ${advisory.package}`);
      lines.push(`            ${advisory.title}`);
      lines.push(`            ${advisory.url}`);
    }
  }

  for (const { id, entry } of result.stale) {
    lines.push(
      `   warning: ${id} (${entry.package}) is on the accepted-risk list for ${appName} but no longer appears in its report - remove the entry.`
    );
  }

  lines.push(
    `   ${blocking} blocking, ${listed} accepted, ${belowLevel} below the level.`
  );
  return lines.join("\n");
}

function main() {
  const options = parseArgs(process.argv.slice(2));
  const list = loadAcceptedRisks(options.allowList);
  const level = options.level ?? list.level ?? DEFAULT_LEVEL;
  const accepted = list.advisories ?? {};

  let blocked = 0;

  for (const app of options.apps) {
    const appDir = resolve(repoRoot, app);
    const report = runAudit(appDir, options.reportDir);
    const advisories = collectAdvisories(report);
    const result = evaluate(basename(appDir), advisories, accepted, level);

    console.log(describe(basename(appDir), result, level));
    blocked += result.blocked.length;
  }

  if (blocked > 0) {
    console.error(
      `\n${blocked} advisory/advisories at or above '${level}' are not on the accepted-risk list ` +
        `(${relative(repoRoot, options.allowList)}). Fix the dependency, or add the advisory to that list with the reason it is accepted.`
    );
    return EXIT_BLOCKED;
  }

  console.log(`\nNo unlisted advisory at or above '${level}'.`);
  return 0;
}

try {
  process.exit(main());
} catch (error) {
  console.error(`\nThe JavaScript advisory gate could not run: ${error.message}`);
  process.exit(EXIT_ERROR);
}
