/**
 * The reasoning half of the OTA rollback guard (F-13, planning #681).
 *
 * An OTA channel holds exactly ONE descriptor per (platform, runtimeVersion): publishing
 * overwrites `{platform}/{runtimeVersion}/update.json`. Nothing downstream compares the new
 * descriptor with the one it replaced - not the API (it relays bytes), not the client (it
 * verifies the signature, which an old-but-genuinely-signed bundle passes), and not
 * `publish-ota.mjs`, which until now wrote the descriptor without reading the old one. So
 * re-running the publisher from an older checkout - a stale clone, a branch that predates the
 * last release - replaces newer JavaScript with older JavaScript on every phone that asks for
 * that runtime, and the only trace is a manifest whose signature is perfectly valid.
 *
 * This module answers two questions, both without side effects so they can be tested:
 *
 *   1. What commit does the descriptor that is served right now say it was built from?
 *      (the publisher now stamps `metadata.commit` and `metadata.committedAt` into the SIGNED
 *      manifest, so this is a claim nobody on the publishing path can rewrite afterwards)
 *   2. Is the commit about to be published older than that one?
 *
 * The script owns the side effects (git, rclone, refusing). The ancestry predicate is injected
 * because it is the one part that needs a repository; everything else is arithmetic on strings.
 */

export const VERDICTS = {
  /** Nothing is served for this runtime yet - a first publish, nothing to compare against. */
  NO_BASELINE: 'no-baseline',
  /** A descriptor is served but carries no commit - published before the stamp existed. */
  UNVERIFIABLE: 'unverifiable',
  /** The served commit is not in this repository, so ancestry cannot be decided here. */
  UNKNOWN_COMMIT: 'unknown-commit',
  /** The same commit is about to be published again - a re-export, not a rollback. */
  REPUBLISH: 'republish',
  /** The served commit is an ancestor of the new one: the fleet moves forward. */
  FORWARD: 'forward',
  /** The new commit is an ancestor of the served one: the fleet would move BACKWARD. */
  ROLLBACK: 'rollback',
  /** Neither commit contains the other - the served code is not part of this bundle. */
  DIVERGED: 'diverged',
};

/**
 * Reads the build stamp out of a served descriptor, without trusting its shape.
 *
 * Everything a stale or broken object store can return ends up as `commit: null`, which the
 * classifier reports as UNVERIFIABLE rather than as "no baseline" - the caller's message differs,
 * and an operator should be told which one happened.
 */
export const servedStampFrom = (descriptor) => {
  const manifestBase64 = descriptor?.manifestBase64;
  if (typeof manifestBase64 !== 'string' || manifestBase64.length === 0) {
    return { present: true, commit: null, committedAt: null };
  }

  let manifest;
  try {
    manifest = JSON.parse(Buffer.from(manifestBase64, 'base64').toString('utf8'));
  } catch {
    return { present: true, commit: null, committedAt: null };
  }

  const metadata = manifest?.metadata;
  if (metadata === null || typeof metadata !== 'object' || Array.isArray(metadata)) {
    return { present: true, commit: null, committedAt: null };
  }

  const commit = typeof metadata.commit === 'string' ? metadata.commit.trim() : '';
  const committedAt = typeof metadata.committedAt === 'string' ? metadata.committedAt.trim() : '';
  return { present: true, commit: commit || null, committedAt: committedAt || null };
};

/**
 * Decides what publishing `nextCommit` over `served` would do to the fleet.
 *
 * `isAncestor(a, b)` must answer "is a an ancestor of (or equal to) b", which is exactly
 * `git merge-base --is-ancestor a b`. Equality is handled before it so that a republish of the
 * same commit is never mistaken for a rollback.
 */
export const classifyPublish = ({ nextCommit, served, isAncestor, isKnownCommit }) => {
  if (!served?.present) return VERDICTS.NO_BASELINE;
  if (!served.commit) return VERDICTS.UNVERIFIABLE;
  if (!isKnownCommit(served.commit)) return VERDICTS.UNKNOWN_COMMIT;
  if (served.commit === nextCommit) return VERDICTS.REPUBLISH;
  if (isAncestor(served.commit, nextCommit)) return VERDICTS.FORWARD;
  if (isAncestor(nextCommit, served.commit)) return VERDICTS.ROLLBACK;
  return VERDICTS.DIVERGED;
};
