# Supplier redemption reconciliation — operator runbook

This is the app-side operator guide for planning issue **#684** ("reconcile self-reported voucher
redemption with supplier evidence"). The lifecycle, the accounting treatment and the known gaps live
in the planning repo:
`docs/VOUCHER_LIFECYCLE.md`, `docs/RECONCILIATION.md`, `docs/PRICING_PROCUREMENT_ACCOUNTING.md`.

## Why there is a queue at all

A customer's "I used this voucher" is a **claim**, not a sale. Before this change the claim was
posted as a sale immediately, so the revenue, the margin, the stock and the journal entries all
depended on the customer telling the truth.

Now:

| Step | Voucher status | Journal | Money |
|---|---|---|---|
| The customer reports the use | `Assigned → PendingReconciliation` | **drafts only** (provisional) | not recognised |
| The supplier's evidence agrees | `PendingReconciliation → Used` | the drafts are posted | recognised |
| The operator rejects the claim | `PendingReconciliation → Assigned` | the drafts are discarded | not recognised |
| The customer withdraws the report | `PendingReconciliation → Assigned` | the drafts are discarded | not recognised |
| The operator reverses a confirmed sale | `Used → Assigned` | a reversing entry is posted today | undone, not erased |

A pending voucher is **not redeemable**: the mobile app shows "awaiting confirmation" beside a yellow
"used" stamp, hides the QR and refuses to renew it, and the wallet does not count its litres as free
stock. Both spent states wear the stamp, so the customer can tell a spent voucher from a live one at a
glance — and tell the two spent states apart by its colour:

| Stamp colour | Meaning | Can it be undone? |
|---|---|---|
| Yellow | the customer reported the use; the brand has not confirmed it | yes — the claim can be withdrawn or rejected |
| Blue | the brand confirmed the use | no — only an operator reversal, with a note |

An order wears the same stamp in its header once **every** voucher in it is spent: blue only when all
of them are confirmed, yellow while at least one is still a claim. The stamp is presentation only —
the API keeps reporting `PendingReconciliation` and `Used`, so an installed app keeps working.

## Where the queue lives

**Admin → Audit → "Redemptions awaiting evidence"** (`RedemptionReconciliationPanel`).

Three counters sit at the top of the panel:

| Counter | Meaning |
|---|---|
| Pending claims | every live claim waiting for a decision |
| Mismatches | claims whose statement line states different litres |
| Statement exceptions | statement lines that matched no live claim |

## The daily procedure

The supplier publishes a **per-pump redemption statement** (for OKKO, the nightly export from the
supplier's cabinet). There is **no supplier API** — see *Accepted risk* below — so the operator
imports that file by hand, once a day.

1. **Get yesterday's statement** from the supplier (the pump redemption export). Keep the file: it
   is the evidence behind every decision made from it.
2. **Convert the lines to JSON.** Every line needs an id of its own; the importer is idempotent on
   that id.

   ```json
   [
     {
       "externalId": "pump-2026-10-23-0001",
       "voucherNumber": "OKKO-0001",
       "liters": 50,
       "redeemedAtUtc": "2026-10-23T09:15:00Z",
       "note": "pump 7"
     }
   ]
   ```

   | Field | Required | Meaning |
   |---|---|---|
   | `externalId` | yes | the statement's own line id; the idempotency key |
   | `voucherNumber` | yes | the voucher the fuel was taken with |
   | `liters` | yes | the litres the statement says, positive |
   | `redeemedAtUtc` | no | when the fuel was taken, UTC; the import time is used when absent |
   | `note` | no | free text, kept with the line |

3. **Paste and import** in the panel. The panel reports how many lines were imported and how many
   were duplicates. Importing the same file twice changes nothing: the second import sees the
   `externalId` already stored and skips the line.
4. **Work the queue.** For every pending claim:
   - a **matched** line → **Confirm**. The drafts are posted and the sale is recognised.
   - a **mismatch** → the statement and the claim disagree about the litres. Check the pump line by
     hand: correct the claim's litres if the customer was right, otherwise **Reject**.
   - **no line at all** → the customer reported a use the supplier has not seen yet. Wait for the
     next statement; see *Claims that never get evidence*.
5. **Work the statement exceptions** — the lines that matched no live claim:
   - **Unknown voucher** — the number on the statement belongs to no voucher we hold. Find the right
     number (a typo in the export, or a voucher from another batch) or **Dismiss** the line.
   - **Not assigned** — the voucher exists but was never sold to a customer. Dismiss it once you
     know why (a test voucher, a wrong number).
   - **Already confirmed** — a late duplicate of a redemption that was already confirmed. Check the
     litres by hand, then dismiss the line.
6. **Close the day.** The counters should be back to zero, or every remaining claim should have a
   note saying why it is still waiting.

## Confirm, reject, reverse

| Action | What it does | Who does it |
|---|---|---|
| Confirm | posts the provisional sale; `PendingReconciliation → Used` | operator, in the panel |
| Reject | discards the drafts; `PendingReconciliation → Assigned` | operator, in the panel |
| Withdraw | discards the drafts; `PendingReconciliation → Assigned` | the customer, from "My codes" |
| Reverse | posts a reversing entry today; `Used → Assigned` | operator, deliberately (see below) |

**Reversal is not a button.** A reversal undoes a posted sale, so it is deliberately not one click
next to a pending claim. It is the same endpoint with a different action:

```http
POST /api/admin/redemptions/{voucherId}/decision
Content-Type: application/json

{ "action": "Reverse", "note": "Supplier statement says the fuel was never taken" }
```

A reversal only works on a **confirmed** redemption. The original entry keeps its date, its number
and its lines; the reversal is a second fact that undoes it, dated in today's open period. A
reversal without a note is refused.

## Claims that never get evidence

A claim with no statement line stays pending until somebody decides. That is the point: an
unconfirmed redemption is not revenue. But it is not free either — the voucher's litres are frozen
and the customer cannot use the voucher.

The operator's options, in order:

1. Wait — most claims are confirmed by the next statement (T+1).
2. Ask the customer for the pump receipt and confirm by hand, noting the receipt in the resolution
   note.
3. **Reject** the claim after a week without evidence. The voucher returns to `Assigned` and the
   customer can use it again.

## Accepted risk

- **There is no supplier API.** The evidence arrives as a file an operator imports by hand. A day
  that nobody imports leaves every claim of that day pending, and the day is settled late.
- **The evidence is a statement, not a signature.** The importer trusts the file the operator
  pasted; a wrong file confirms a wrong sale. The statement is kept as the operator's evidence.
- **A claim can sit pending for as long as it takes.** Nothing expires it automatically. The
  counters on the Audit tab are the reminder.
- **The customer sees "awaiting confirmation" instead of a QR.** For a customer who really did fuel
  up, the voucher is unusable until the operator answers. That is the deliberate trade: an
  unredeemable voucher is recoverable, a false sale is not.

## What is recorded

| Fact | Where |
|---|---|
| the claim | `voucher_redemptions` (`Reported → Confirmed / Rejected / Withdrawn / Reversed`) |
| the statement line | `redemption_statement_rows` (matched, mismatched, unknown, dismissed) |
| the status change | `voucher_events` (`StatusChanged`, with the reason) |
| the money | `journal_entries` — drafts while pending, posted on confirmation, reversed on reversal |

Every write is idempotent: a repeated import, a repeated confirm, a repeated reject and a repeated
reversal answer "already done" instead of writing twice.
