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

import { splitAddress, toName } from "./fetch-upg-stations.mjs";

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

// ---------------------------------------------------------------------------------------
// splitAddress
//
// The old parser left 56 of 500 stations with an empty city. It required the settlement to be a
// chunk of its own, and UPG writes it glued to its raion or to the street on 37 of those. These
// cases are the live payloads, verbatim, including the misspellings and the missing commas.
// ---------------------------------------------------------------------------------------

test("splitAddress finds a settlement that owns a chunk of its own", () => {
  assert.deepEqual(splitAddress("Запорізька обл., м. Запоріжжя, вул. Авраменка, 23"), {
    address: "вул. Авраменка, 23",
    city: "Запоріжжя",
  });
  assert.deepEqual(splitAddress("Рівненська обл, м.Роздільна вул. Європейська, буд. 56-Б"), {
    address: "вул. Європейська, буд. 56-Б",
    city: "Роздільна",
  });
});

test("splitAddress finds a settlement glued to its raion", () => {
  // No comma between «р-н.» and the settlement — the shape that defeated the old parser, which
  // only looked at the start of a chunk.
  assert.deepEqual(splitAddress("Ковельський р-н. с. Воля-Ковельська, вул. Волі, 38"), {
    address: "вул. Волі, 38",
    city: "Воля-Ковельська",
  });
  assert.deepEqual(splitAddress("Прилуцький р-н. селище Варва, вул. Космонавтів, 2-А"), {
    address: "вул. Космонавтів, 2-А",
    city: "Варва",
  });
  assert.deepEqual(splitAddress("Володимирський р-н. м. Нововолинськ, вул. Промислова, 2-А"), {
    address: "вул. Промислова, 2-А",
    city: "Нововолинськ",
  });
  assert.deepEqual(splitAddress("Черкаська обл. Уманський р-н. м. Жашків, вул. Перемоги, 7"), {
    address: "вул. Перемоги, 7",
    city: "Жашків",
  });
});

test("splitAddress keeps the street when the settlement shares its chunk", () => {
  // This is the regression that would have been invisible: the address used to become EMPTY,
  // importing a station with a city and no street at all.
  assert.deepEqual(splitAddress("м. Хмельницький вул. Прибузька 11\\3"), {
    address: "вул. Прибузька 11\\3",
    city: "Хмельницький",
  });
  assert.deepEqual(splitAddress("Тернопільська обл. Тернопільський р-н. м.Тернопіль вул.Бродівська 2\\вул.Гріга Є., 2"), {
    address: "вул.Бродівська 2\\вул.Гріга Є., 2",
    city: "Тернопіль",
  });
});

test("splitAddress does not read a street initial as a settlement", () => {
  // «С.» is a Cyrillic «с», so a naive marker match turns the city into «С» and the address into
  // «вул., 2а» — a station on С. Бандери with the street gone.
  assert.deepEqual(splitAddress("вул.С. Бандери, 2а"), { address: "вул.С. Бандери, 2а", city: "" });
  assert.deepEqual(splitAddress("вул. М. Грушевського, 2-А"), { address: "вул. М. Грушевського, 2-А", city: "" });
  assert.deepEqual(splitAddress("вул. С. Куприна, 132"), { address: "вул. С. Куприна, 132", city: "" });
});

test("splitAddress stops the settlement name at the street", () => {
  // Two-word names are kept whole...
  assert.deepEqual(splitAddress("Хмельницький р-н, сел.Стара синява вул. Лівобережна, 101"), {
    address: "вул. Лівобережна, 101",
    city: "Стара синява",
  });
  // ...but the word that names a street for a following street-type noun is not part of it.
  assert.deepEqual(splitAddress("с. Зарванці Хмельницьке шосе 14"), {
    address: "Хмельницьке шосе 14",
    city: "Зарванці",
  });
});

test("splitAddress accepts UPG's spelling of the settlement word", () => {
  // UPG writes the settlement word with U+0448, not the standard U+0449. Both are built from
  // code points here rather than typed as literal words on purpose: as literals the two spellings
  // are indistinguishable to the eye, and that is exactly how the typo went untested - a test that
  // silently used the standard spelling passed against a parser that only knew it, and the two
  // stations UPG really publishes that way kept an empty city.
  const SH = 0x448;
  const SHCH = 0x449;
  const settlementWord = (fifth) => String.fromCodePoint(0x441, 0x435, 0x43b, 0x438, fifth, 0x435);
  const upgTypo = settlementWord(SH);
  const standard = settlementWord(SHCH);

  assert.equal(splitAddress(`Берегівський район,${upgTypo} Вилок вул. Виноградівська, буд. 27`).city, "Вилок");
  assert.equal(splitAddress(`Тернопільський район, ${upgTypo} Залізці вул. Шевченка, буд. 125`).city, "Залізці");
  // The correct spelling has to keep working too - a parser fixed only for the typo is still broken.
  assert.equal(splitAddress(`район, ${standard} Залізці вул. Шевченка, 1`).city, "Залізці");
  // And the bare abbreviation «сел.» without a space before the name.
  assert.equal(splitAddress("Кременчуцький р-н., сел.Семенівка вул.Волі 73").city, "Семенівка");
});

test("splitAddress leaves councils and highways without a city", () => {
  // These 19 are honestly not towns: a council («с/рада») spans several villages and a territorial
  // community («ТГ») is a reform-era unit covering several of them. Writing «Глибочицької ТГ» into
  // a МІСТО column would be worse than an honest blank, and writing the genitive would read as
  // nonsense. A highway point has no settlement at all.
  for (const raw of [
    "Рівненська обл., Рокитнівський р-н, с/рада Кисорицька, автодорога Київ-Ковель, 254 км",
    "Кіровоградська обл., Голованівський р-н, автошлях Ульянівка-Миколаїв, 17 км + 650 м (праворуч)",
    "Житомирська обл, територія Глибочицької ТГ, а/д М-06 Київ-Чоп км 126+415 (ліворуч)",
    "Одеська обл., Біляївський р-н, Нерубайська с/р, Масив 27, зем. діл. 306-а, вздовж а/д М05 Київ-Одеса",
    "Київська обл., Києво-Святошинський р-н, а/д Т 10-27 Київське півкільце, 12+363-12+552 км (праворуч)",
  ]) {
    assert.equal(splitAddress(raw).city, "", `expected no city for: ${raw}`);
  }
});

test("splitAddress strips the raion whether or not it has a chunk to itself", () => {
  // Own chunk.
  assert.deepEqual(splitAddress("Івано-Франківська обл., Івано-Франківський р-н., с. Черніїв, вул. Надвірнянська, 67-В"), {
    address: "вул. Надвірнянська, 67-В",
    city: "Черніїв",
  });
  // Glued to the street, with no dot after «р-н». The old parser deleted the WHOLE chunk here,
  // losing the street: it came out as «1-А».
  assert.deepEqual(splitAddress("Запорізька обл., Хортицький р-н вул. Запорізького казацтва, 1-А"), {
    address: "вул. Запорізького казацтва, 1-А",
    city: "",
  });
});

test("splitAddress never invents a city that is not in the address", () => {
  // The single invariant that matters for a column people read: whatever we put in city must be
  // text UPG actually published. Checked across every shape the fetcher can meet.
  const shapes = [
    "Запорізька обл., м. Запоріжжя, вул. Авраменка, 23",
    "Ковельський р-н. с. Воля-Ковельська, вул. Волі, 38",
    "село Рукшин с/рада. Рукшинська урочище \"Чотири корчми\". 1",
    "Одеський р-н. Одеська обл.с.Барабой а/д Одеса-Білгород-Дністровський-Монаші 30 КМ",
    "с. Зарванці Хмельницьке шосе 14",
    "вул. Пирятинська, 96",
    "",
  ];
  const fold = (s) => s.toLowerCase().replace(/\s+/g, " ").trim();
  for (const raw of shapes) {
    const { city } = splitAddress(raw);
    if (!city) continue;
    assert.ok(fold(raw).includes(fold(city)), `city "${city}" is not in "${raw}"`);
  }
});

test("splitAddress survives empty and junk input", () => {
  assert.deepEqual(splitAddress(""), { address: "", city: "" });
  assert.deepEqual(splitAddress("   "), { address: "", city: "" });
  assert.deepEqual(splitAddress(undefined), { address: "", city: "" });
  assert.deepEqual(splitAddress(null), { address: "", city: "" });
  assert.deepEqual(splitAddress(42), { address: "", city: "" });
  // A bare marker with nothing after it must not yield an empty-string city.
  assert.equal(splitAddress("м.").city, "");
  assert.equal(splitAddress("с/рада Кисорицька").city, "");
});