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
import assert from "node:assert/strict";
import { test } from "node:test";

import { extractAveragePrices, parsePrices, toRow } from "./fetch-upg-wog-prices.mjs";

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
