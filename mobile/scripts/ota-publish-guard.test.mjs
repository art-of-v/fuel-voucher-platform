// The publish guard's own tests (F-13, planning #681).
//
// The guard exists because a descriptor is the only thing an OTA channel can hold per runtime,
// and every publish overwrites it. The facts that matter are classification facts, so they are
// tested here against a fake ancestry graph; the last test reads the publisher itself and fails
// if the guard stops being wired to it - a rule that is only described is not a rule that runs.
//
// node:test is in the standard library, so this needs no npm install: CI runs the file from the
// mobile job, which already has the checkout and the pinned Node.

import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

import { classifyPublish, servedStampFrom, VERDICTS } from './ota-publish-guard.mjs';

const scriptsDir = dirname(fileURLToPath(import.meta.url));

// A descriptor shaped like the one the publisher writes: base64 of the exact signed bytes.
const descriptorFor = (manifest) => ({
  manifestBase64: Buffer.from(JSON.stringify(manifest), 'utf8').toString('base64'),
  signature: 'sig="irrelevant", keyid="main", alg="rsa-v1_5-sha256"',
});

// A commit graph by hand: MAIN < HOTFIX, MAIN < FEATURE, and REBUILT is unrelated to both.
const GRAPH = {
  [['main', 'hotfix'].join()]: true,
  [['main', 'feature'].join()]: true,
};
const isAncestor = (a, b) => GRAPH[[a, b].join()] === true || a === b;
const isKnownCommit = (commit) => ['main', 'hotfix', 'feature', 'rebuilt'].includes(commit);

const classify = (nextCommit, served) =>
  classifyPublish({ nextCommit, served, isAncestor, isKnownCommit });

test('a stamped manifest yields the commit and the commit time', () => {
  const served = servedStampFrom(
    descriptorFor({
      id: 'abc',
      metadata: { commit: 'deadbeef', committedAt: '2026-10-31T10:00:00+00:00' },
    })
  );

  assert.deepEqual(served, {
    present: true,
    commit: 'deadbeef',
    committedAt: '2026-10-31T10:00:00+00:00',
  });
});

test('a descriptor published before the stamp existed is understood as unverifiable, not absent', () => {
  for (const manifest of [
    { id: 'abc', metadata: {} },
    { id: 'abc' },
    { id: 'abc', metadata: { commit: '   ' } },
    { id: 'abc', metadata: 'a string, not an object' },
    { id: 'abc', metadata: { commit: 42 } },
  ]) {
    const served = servedStampFrom(descriptorFor(manifest));
    assert.equal(served.present, true);
    assert.equal(served.commit, null);
    assert.equal(classify('main', served), VERDICTS.UNVERIFIABLE);
  }
});

test('a descriptor that cannot be decoded is still a baseline we cannot compare against', () => {
  for (const descriptor of [
    {},
    { manifestBase64: '' },
    { manifestBase64: 'not-base64-json' },
    null,
  ]) {
    const served = servedStampFrom(descriptor);
    assert.equal(served.present, true);
    assert.equal(served.commit, null);
  }
});

test('no served descriptor at all is a first publish', () => {
  assert.equal(classify('main', { present: false }), VERDICTS.NO_BASELINE);
  assert.equal(classify('main', undefined), VERDICTS.NO_BASELINE);
});

test('a commit this checkout has never seen is not silently treated as a rollback', () => {
  const served = { present: true, commit: 'squashed-away', committedAt: null };
  assert.equal(classify('main', served), VERDICTS.UNKNOWN_COMMIT);
});

test('the same commit again is a republish, not a rollback', () => {
  const served = { present: true, commit: 'main', committedAt: null };
  assert.equal(classify('main', served), VERDICTS.REPUBLISH);
});

test('a descendant of the served commit moves the fleet forward', () => {
  const served = { present: true, commit: 'main', committedAt: null };
  assert.equal(classify('hotfix', served), VERDICTS.FORWARD);
  assert.equal(classify('feature', served), VERDICTS.FORWARD);
});

test('an ancestor of the served commit is a rollback, which is the whole point', () => {
  const served = { present: true, commit: 'hotfix', committedAt: null };
  assert.equal(classify('main', served), VERDICTS.ROLLBACK);
});

test('unrelated history is diverged - the served code is not in the new bundle', () => {
  const served = { present: true, commit: 'rebuilt', committedAt: null };
  assert.equal(classify('main', served), VERDICTS.DIVERGED);
});

test('the classification is a partition: no pair of commits lands in two verdicts at once', () => {
  const commits = ['main', 'hotfix', 'feature', 'rebuilt'];
  for (const next of commits) {
    for (const served of commits) {
      const verdict = classify(next, { present: true, commit: served, committedAt: null });
      assert.ok(Object.values(VERDICTS).includes(verdict), `${next} over ${served} -> ${verdict}`);
    }
  }

  // Both directions of one pair cannot both be forward.
  assert.equal(classify('main', { present: true, commit: 'hotfix' }), VERDICTS.ROLLBACK);
  assert.equal(classify('hotfix', { present: true, commit: 'main' }), VERDICTS.FORWARD);
});

test('the publisher stamps its commit and refuses a rollback unless told twice', () => {
  const source = readFileSync(join(scriptsDir, 'publish-ota.mjs'), 'utf8');

  assert.match(source, /from '\.\/ota-publish-guard\.mjs'/, 'the guard module is imported');
  assert.match(source, /classifyPublish\(/, 'the publisher asks the guard');
  assert.match(source, /VERDICTS\.ROLLBACK/, 'the rollback verdict is handled by name');
  assert.match(source, /--allow-downgrade/, 'the override exists and is named in the message');
  assert.match(source, /\bcommit: buildStamp\.commit\b/, 'the manifest is stamped with the commit');
  assert.match(source, /\bcommittedAt: buildStamp\.committedAt\b/, 'and with when it was committed');
});
