#!/usr/bin/env node
// Tests for the station fetchers in scripts/ — pure Node, no dependencies, so they run anywhere a
// checkout does (node:test is in the standard library from Node 18 on).
//
// WHY THIS NEEDS A TEST
// The fetchers are the only write path into `station_nodes`, and they run by hand against a
// third-party site nobody here controls. The parsing is the fragile half: UPG rebuilds its map page
// from time to time, and the shapes below were all taken from real payloads rather than invented.
// When UPG's page changes, the fetcher must fail on ONE line naming the station — not import a
// few hundred rows with a mangled name that nobody notices until the map is already wrong.
//
// Run: node --test scripts/*.test.mjs   (the glob, not `scripts/` — see the CI step)
import assert from "node:assert/strict";
import { test } from "node:test";

import { toName } from "./fetch-upg-stations.mjs";

test("toName keeps UPG's site number and drops the noise", () => {
  // 494 of 500 live rows look like this; the number is the only part that differs between them.
  assert.equal(toName("АЗК №100 цілодобово"), "UPG №100");
  assert.equal(toName("АЗС №141 цілодобово"), "UPG №141");
  // UPG is not consistent about АЗК vs АЗС, and the inconsistency must not leak into the name.
  assert.equal(toName("АЗС №141 цілодобово"), toName("АЗК №141 цілодобово"));
});

test("toName drops the café-hours tail", () => {
  // Six sites carry their café's opening hours instead of «цілодобово». They are stale the moment
  // the café changes them and nothing re-imports them, so the tail is dropped, not kept.
  assert.equal(toName("АЗС №150 (🍽 07:00 – 21:00)"), "UPG №150");
  assert.equal(toName("АЗС №160 (🍽 07:00 – 21:00)"), "UPG №160");
  assert.equal(toName("АЗС №151 (07:00 – 20:00)"), "UPG №151");
});

test("toName normalises the spacing UPG actually emits", () => {
  // «АЗС№156» — no space at all — is a live row, not a hypothetical.
  assert.equal(toName("АЗС№156 цілодобово"), "UPG №156");
  assert.equal(toName("АЗК  №2  цілодобово"), "UPG №2");
  // Collapsed here as well, since the payload is not whitespace-normalised upstream.
  assert.equal(toName("АЗК №2   цілодобово"), "UPG №2");
});

test("toName returns empty when there is no site number", () => {
  // "" makes main() skip the row and print why, rather than importing a nameless pin — the
  // importer accepts an empty name and the admin list then shows a blank row with no trace.
  assert.equal(toName(""), "");
  assert.equal(toName("АЗК цілодобово"), "");
  assert.equal(toName("UPG №100"), "");
  // A number that is not at the front is not a site number — not "№100" with a prefix.
  assert.equal(toName("Станція UPG №100"), "");
  assert.equal(toName(undefined), "");
  assert.equal(toName(null), "");
  assert.equal(toName(123), "");
});