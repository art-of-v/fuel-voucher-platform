#!/usr/bin/env node
/**
 * Ukrainian is "пальне". Every other form of that word in `src/` has to be a
 * real case of it, and the page may not say "пул" (the business vocabulary is
 * закуплення / запас компанії / залишок).
 *
 * Why a gate: the rule was written down twice — in the brief and in README.md —
 * and «пально» still shipped, in the FAQ answer a customer reads first. A rule
 * that lives only in prose is a rule nobody runs.
 *
 * The grammar check is a whitelist on purpose. "пальне" is an adjective that
 * became a noun, so a typo shows up only in the ending, and a spellchecker will
 * not flag a form it has never seen.
 */
import { readdirSync, readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const SRC = path.join(root, 'src');

/** Every case of "пальне" the copy is allowed to use. */
const FUEL_FORMS = [
  'пальне',
  'пальна',
  'пальної',
  'пальному',
  'пальним',
  'пального',
  'пальні',
  'пальній',
  'пальних',
  'пальну',
  'пальнім',
  'пальними',
  'пальною',
];

/** The word for the company's own fuel stock, in every case. */
const POOL_FORMS = [
  'пул',
  'пулу',
  'пула',
  'пулі',
  'пулом',
  'пули',
  'пулів',
  'пулам',
  'пулами',
  'пулах',
  'пуловий',
  'пулова',
  'пулове',
  'пулові',
  'пулового',
  'пуловому',
  'пуловим',
  'пулових',
  'пуловою',
];

const WORD = /[\p{L}]+/gu;
const FUEL = /пальн\p{L}*/giu;

const fuelAllowed = new Set(FUEL_FORMS);
const banned = new Map(POOL_FORMS.map((form) => [form, 'say закуплення, запас компанії or залишок']));

const walk = (dir) =>
  readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) return walk(full);
    return /\.(tsx?|md)$/.test(entry.name) ? [full] : [];
  });

const failures = [];
for (const file of walk(SRC)) {
  const source = readFileSync(file, 'utf8');
  source.split('\n').forEach((line, index) => {
    const where = `${path.relative(root, file)}:${index + 1}`;
    for (const match of line.matchAll(FUEL)) {
      if (!fuelAllowed.has(match[0].toLowerCase())) {
        failures.push(
          `${where}  «${match[0]}» - Ukrainian is «пальне»: ${FUEL_FORMS.slice(0, 6).join(', ')}…`,
        );
      }
    }
    for (const match of line.matchAll(WORD)) {
      // Whole words only: "пульс" is a fuel's name, not the word we ban.
      const why = banned.get(match[0].toLowerCase());
      if (why) failures.push(`${where}  «${match[0]}» - ${why}`);
    }
  });
}

if (failures.length) {
  console.error('[copy] Ukrainian grammar and vocabulary:');
  for (const failure of failures) console.error(`  ${failure}`);
  process.exit(1);
}

console.log('[copy] OK - «пальне» in a real case everywhere, no banned vocabulary.');
