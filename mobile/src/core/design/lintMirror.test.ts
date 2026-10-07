import { spacing, radius, zIndex } from './layout';
import { typeScale } from './typography';

// The lint rule keeps its own copy of the scales rather than importing them, so that
// linting never executes app code. The cost of that choice is that the copy can drift,
// and it did: three type roles were missing and one value was wrong, so the rule told
// developers to add a token for a font size that already had one.
//
// Nothing caught it because nothing compared the copy with the original. This does.
const { SCALES } = require('../../../eslint-rules/use-design-tokens.js');

type ScaleName = 'spacing' | 'radius' | 'fontSize' | 'zIndex';

// `spacing` carries four role-named aliases that duplicate ladder steps (24, 16, 32,
// and the platform hairline). They are deliberately absent from the mirror: a value
// they share is already matched by the ladder step of the same number.
const NOT_LADDER_STEPS = ['containerPadding', 'cardGap', 'sectionGap', 'hairline'];

const SOURCES: Record<ScaleName, Record<string, number>> = {
  spacing: spacing as unknown as Record<string, number>,
  radius: radius as unknown as Record<string, number>,
  // A type role is not a bare number - its size is its `fontSize`.
  fontSize: Object.fromEntries(
    Object.entries(typeScale).map(([role, value]) => [
      role,
      (value as { fontSize: number }).fontSize,
    ]),
  ),
  zIndex: zIndex as unknown as Record<string, number>,
};

describe('the lint rule mirrors the real token scales', () => {
  const names = Object.keys(SOURCES) as ScaleName[];

  it.each(names)('%s lists every ladder step the source defines', (name) => {
    const mirrored = Object.keys(SCALES[name].values);
    const skipped = name === 'spacing' ? NOT_LADDER_STEPS : [];
    const missing = Object.keys(SOURCES[name])
      .filter((role) => !skipped.includes(role))
      .filter((role) => !mirrored.includes(role));
    // A role missing here is a role the rule will claim does not exist.
    expect(missing).toEqual([]);
  });

  it.each(names)('%s lists no role the source does not define', (name) => {
    const mirrored = Object.keys(SCALES[name].values);
    expect(mirrored.filter((role) => !(role in SOURCES[name]))).toEqual([]);
  });

  it.each(names)('%s agrees on every value', (name) => {
    const mirrored = SCALES[name].values as Record<string, number>;
    const actual = SOURCES[name];
    const wrong = Object.keys(mirrored)
      .filter((role) => role in actual && mirrored[role] !== actual[role])
      .map((role) => `${role}: rule says ${mirrored[role]}, source says ${actual[role]}`);
    expect(wrong).toEqual([]);
  });
});
