#!/usr/bin/env node
// Tests for the UPG/WOG fuel-price fetcher - pure Node, no dependencies, so they run anywhere a
// checkout does (node:test is in the standard library from Node 18 on).
//
// WHY THIS NEEDS A TEST
// The fetcher reads a JavaScript literal out of somebody else's HTML. That is the fragile half, and
// every shape below was taken from the real pages on 2026-10-17 rather than invented - UPG and WOG
// publish the blob inside `<script>` blocks, with float noise in the prices and `\u`-escaped
// Cyrillic. When the page changes, the fetcher must fail on ONE line naming the grade - not write a
// CSV of NaNs that nobody notices until a fuel stops pricing.
//
// Run: node --test scripts/*.test.mjs   (the glob, not `scripts/` - see the CI step)
import { readFileSync } from "node:fs";
import assert from "node:assert/strict";
import { test } from "node:test";

import {
  extractAveragePrices,
  extractNextPageFuels,
  parsePrices,
  reduxRowToRow,
  toRow,
} from "./fetch-upg-wog-prices.mjs";

/** The real assignment, trimmed to one grade plus the closing noise. */
const REAL_BLOB = `
    <script>
        function formatFuelPrice(value) {
            const price = Number(value);
            return Number.isFinite(price) ? price.toFixed(2) : '-';
        }

        let averagePrices = {"data":[{"Id":4,"Title":"\\u0413\\u0430\\u0437","IconUrl":"https:\\/\\/newmobi.upg.ua\\/img\\/mobile\\/fuel_icons\\/gas.png","Price":49.899999999999999},{"Id":8,"Title":"upg100","IconUrl":"https:\\/\\/newmobi.upg.ua\\/img\\/mobile\\/fuel_icons\\/upg100_flag_n.png","Price":99.900000000000006},{"Id":1,"Title":"upgDIESEL","IconUrl":"https:\\/\\/newmobi.upg.ua\\/img\\/mobile\\/fuel_icons\\/upgdiesel.png","Price":102.90000000000001},{"Id":5,"Title":"EURO DIESEL","IconUrl":"https:\\/\\/newmobi.upg.ua\\/img\\/mobile\\/fuel_icons\\/eurodiesel.png","Price":100.90000000000001},{"Id":9,"Title":"upg95","IconUrl":"https:\\/\\/newmobi.upg.ua\\/img\\/mobile\\/fuel_icons\\/upg95_flag_n.png","Price":91.900000000000006},{"Id":2,"Title":"A-95","IconUrl":"https:\\/\\/newmobi.upg.ua\\/img\\/mobile\\/fuel_icons\\/a95_flag_n.png","Price":88.90000000000001}]};
    </script>
`;

test("the blob is found by its assignment, not by a path", () => {
  // The marker is what survives a page redesign; the surrounding markup changes and this must not.
  const blob = extractAveragePrices(REAL_BLOB);
  assert.equal(typeof blob, "string");
  assert.equal(JSON.parse(blob).data.length, 6);
});

test("a page with no blob yields null, not an empty object", () => {
  // The caller turns null into an error naming the page shape. An empty object would read as "the
  // brand publishes no prices", which is a different claim and a dangerous one.
  assert.equal(extractAveragePrices("<html><body>nothing here</body></html>"), null);
});

test("every published grade of a real payload comes out with its own price", () => {
  const { rows, missing } = parsePrices(REAL_BLOB);

  assert.equal(missing, false);
  assert.deepEqual(rows, [
    ["Газ", "49.90"],
    ["upg100", "99.90"],
    ["upgDIESEL", "102.90"],
    ["EURO DIESEL", "100.90"],
    ["upg95", "91.90"],
    ["A-95", "88.90"],
  ]);
});

test("float noise is rounded here rather than downstream", () => {
  // The page publishes 49.899999999999999 for 49.90. Written raw, every reader of a price column
  // has to know to round it - and the one that does not shows a price off by a fraction.
  assert.deepEqual(toRow({ Title: "Газ", Price: 49.899999999999999 }), { row: ["Газ", "49.90"] });
});

test("a grade with no title is skipped with a reason", () => {
  assert.deepEqual(toRow({ Price: 50 }), { skip: { code: "<none>", reason: "no Title" } });
});

test("an unparseable or non-positive price is skipped, never written", () => {
  // A zero pump would become the ceiling for every package of that fuel and drive the customer price
  // to zero, which is why the OKKO importer refuses it at the door and this does too.
  assert.deepEqual(toRow({ Title: "upg95", Price: "abc" }).skip.reason, 'unparseable Price "abc"');
  assert.deepEqual(toRow({ Title: "upg95", Price: 0 }).skip.reason, "non-positive price 0");
  assert.deepEqual(toRow({ Title: "upg95", Price: -1 }).skip.reason, "non-positive price -1");
});

test("a repeated grade is reported rather than silently taking the last price", () => {
  // The page repeating a component is normal; a repeat that changes the price is the interesting one,
  // so both are said out loud instead of one of them winning by position.
  const twice = `<script>let averagePrices = {"data":[
    {"Title":"upg95","Price":91.90},
    {"Title":"upg95","Price":95.90}
  ]};</script>`;

  const { rows, skipped } = parsePrices(twice);

  assert.deepEqual(rows, [["upg95", "91.90"]]);
  assert.equal(skipped.length, 1);
  assert.match(skipped[0].reason, /duplicate/);
});

test("a blob whose object is broken fails loudly instead of yielding no rows", () => {
  // An empty CSV is indistinguishable from "the brand published nothing", and a fetcher that says
  // that when the page is merely broken is a fetcher that lies quietly.
  const broken = `<script>let averagePrices = {"data":[{"Title":"upg95",}]};</script>`;

  assert.equal(parsePrices(broken).missing, true);
});

// ---------------------------------------------------------------------------------------------
// WOG's current shape, captured 2026-10-18 from https://www.wog.ua/ua/fuels/.
//
// The page is a Next.js app and the blob is gone; prices live in the Redux state under
// __NEXT_DATA__ instead.
//
// The fixture is a FILE, not an inline literal, and that is not tidiness. The payload contains
// "\r\n" and Unicode escapes inside its JSON strings, which are correct JSON and catastrophic inside
// a JS string: the JS parser consumes the backslashes first, the value gains literal control
// characters, and JSON.parse then rejects what is a perfectly good page. An inline literal makes the
// fixture fail for a reason that has nothing to do with the parser under test.
//
// The objects are kept whole, marker field included. A trimmed fixture matches nothing, and the
// test then passes for the wrong reason.
//
// Two facts in it are why it is kept rather than described:
//
//   * "price" is in KOPECKS. 10350 is 103.50 UAH. Read as UAH the catalogue is a hundred times too
//     dear and nothing catches it - a higher price is never below cost, so the guard waves it through.
//   * The grade is "type" plus "brand", not one field. Neither half alone means anything; only
//     "ДП Mustang+" is the name the brand shows its customers.
const WOG_PAGE = `<html><body><script id="__NEXT_DATA__" type="application/json">${readFileSync(new URL("./fixtures/wog-next-page-state.json", import.meta.url), "utf8")}</script></body></html>`;

test("WOG's Next.js state is found and read", () => {
  const found = extractNextPageFuels(WOG_PAGE);
  assert.ok(found, "the __NEXT_DATA__ payload should be located and parsed");
  assert.equal(found.items.length, 7, "every published grade is on the page");
});

test("WOG's kopecks become UAH", () => {
  const byCode = Object.fromEntries(parsePrices(WOG_PAGE).rows);
  // Read straight off WOG's price list on 2026-10-18. If the /100 ever goes missing, these fail.
  assert.deepEqual(byCode["ДП Євро5"], "103.50");
  assert.deepEqual(byCode["ДП Mustang+"], "106.50");
  assert.deepEqual(byCode["95 Євро5-Е10"], "92.90");
  assert.deepEqual(byCode["95 Mustang Євро5-Е10"], "95.90");
  assert.deepEqual(byCode["100 Mustang Євро5-Е0"], "102.90");
  assert.deepEqual(byCode["ГАЗ"], "51.50");
  assert.deepEqual(byCode["AdBlue"], "61.90");
});

test("the grade is type plus brand when both are there, and either alone when only one is", () => {
  assert.deepEqual(reduxRowToRow({ type: "ДП", brand: "Mustang+", price: 10650 }).row, ["ДП Mustang+", "106.50"]);

  // WOG publishes two grades with no brand field at all - ГАЗ and AdBlue carry only `type` - so a
  // missing brand must not be an error. It is the ordinary shape for a grade with no sub-variant.
  assert.deepEqual(reduxRowToRow({ type: "ГАЗ", brand: undefined, price: 5150 }).row, ["ГАЗ", "51.50"]);
  assert.deepEqual(reduxRowToRow({ type: "ДП", brand: "", price: 10350 }).row, ["ДП", "103.50"]);
  assert.deepEqual(reduxRowToRow({ type: undefined, brand: "AdBlue", price: 6190 }).row, ["AdBlue", "61.90"]);

  // Neither half is not a grade, though. That is the only refusal here.
  assert.equal(reduxRowToRow({ type: "", brand: "", price: 1000 }).skip.reason, "no type and no brand");
});

test("a zero or missing price is refused, not imported as a NaN", () => {
  assert.equal(reduxRowToRow({ type: "ДП", brand: "Євро5", price: 0 }).skip.reason, "non-positive price 0");
  assert.equal(reduxRowToRow({ type: "ДП", brand: "Євро5" }).skip.reason, 'unparseable price "undefined"');
});

test("a page with neither shape still fails loudly", () => {
  const { rows, missing } = parsePrices("<html><body>nothing here</body></html>");
  assert.equal(rows.length, 0);
  assert.equal(missing, true, "an unrecognised page must not read as 'no prices published'");
});