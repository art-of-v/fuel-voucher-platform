# Held money — operator runbook

Money the platform holds that is not against a sale. This is the operator guide for the admin screen
**«Гроші, які ми тримаємо»** (`HeldPaymentsTab`), its two write paths, and the one thing it must never
do: delete the money. The accounting treatment is in the planning repo
(`docs/ACQUISITION_CHANNELS.md`); the statement side is in `docs/RECONCILIATION.md` there.

## What the queue is

`GET /api/admin/payments/unallocated` (policy `SettlementCode`) lists `payments` rows that are
`Succeeded`, have no `order_id`, and have not been dismissed. Three kinds of money land there:

| Where it came from | What the row looks like |
|---|---|
| A payment made outside checkout (a transfer, a Telegram declaration, a booking of an unapplied receipt) | may carry a customer, a settlement code and the message finance typed |
| Money attached to a customer who has no open order | has an owner, no order |
| A test capture, a rounding charge, a transfer from before the payer registered | often has neither owner nor order — the hardest case, and the reason the queue exists |

## The three answers

| The row is... | The operator | The endpoint |
|---|---|---|
| Somebody's, and they have an order waiting | names the order (or the customer, or both) | `POST /api/admin/payments/{id}/attach`, policy `MoneyMovement` |
| Somebody's, but they have no order yet | names the customer and leaves it — the checkout will find it when the order appears | same |
| Not going to be placed, ever | says why, and removes it from this list | `POST /api/admin/payments/{id}/dismiss`, policy `MoneyMovement` |

Attaching is the money path and its refusals are the feature: it will not place money on another
customer's order, on an order that is already settled, or a transfer that has already been drawn on.
The screens' refusal text is the server's sentence, not a generic failure.

## Dismissing: what happens to the row

`POST /api/admin/payments/{id}/dismiss` with `{ "reason": "…" }` sets `dismissed_at_utc`,
`dismissed_reason` and `dismissed_by_user_id` on the payment, and records an audit event
`PaymentDismissed` with the before/after values and the reason. The three columns are paired by the
constraint `ck_payments_dismissal_pair`, so a timestamp cannot exist without a reason.

The handler refuses:

| Request | The answer |
|---|---|
| No reason, or only spaces | `Say why this money will never be placed.` |
| A reason longer than 200 characters | the refusal names the column's own limit |
| A payment that is not `Succeeded` | `Only succeeded money can be dismissed. This payment is {Status}.` |
| A payment that is on an order | `This payment is on an order; the worklist does not hold it.` |
| The same press again | success, silently: a double click is not an error, and the first reason stands (`PaymentDismissed` is not written twice) |
| A request that carries no actor | `A dismissal names the person who made it.` — refused rather than recorded anonymously |
| An unknown id | 404 |

`GET /api/admin/payments/unallocated` filters `dismissed_at_utc IS NULL`, so the row leaves the list
and the sidebar count with it. Nothing else changes.

**Attaching still works on a dismissed payment.** A dismissal can be wrong — the owner may appear
later — and `POST …/attach` looks the row up by id without consulting the dismissal. The id to use is
in the `PaymentDismissed` audit row, which names the amount, the invoice and the reason. There is no
un-dismiss endpoint and no screen that lists dismissed rows: the money is not lost, it is out of the
queue, and the audit row is the handle to it.

## Deleting is not an option, and that is the point

The row, its `paid_at_utc`, its amount, its statement match and its journal entry are the evidence that
the acquirer took real money. Deleting the payment would:

- make the statement row point at no payment — the `UnappliedReceipts` check would report a received
  capture that the books do not hold, and `UnbookedCaptures` would stay red until somebody booked it
  again (`POST /api/admin/ledger/unapplied-receipts`);
- leave the journal's cash (account 311, «Поточні рахунки в національній валюті») higher than the
  payments explain, so the close would show a difference between the books and the bank that nobody
  can resolve without inventing an entry.

A dismissal answers a question about the **queue**. It does not answer one about the books, and it does
not touch them.

## Reading the dismissed rows

There is no screen for them; the table is the record:

```sql
select p.id, p.provider_invoice_id, p.amount_uah, p.paid_at_utc,
       p.dismissed_at_utc, p.dismissed_reason, p.dismissed_by_user_id
from payments p
where p.dismissed_at_utc is not null
order by p.dismissed_at_utc desc;
```

Read-only. Changing these columns by hand needs the same ruling as any other money row — and the
`PaymentDismissed` audit event is what a later question ("who closed this and why") is answered from.

## The 2026-11-07 cleanup

Two Live captures of **1.00 ₴** — invoices `261011AG8kie8RSeJXMv` (paid 2026-10-11 13:47:45Z) and
`261012CmgkPUHezCysiv` (paid 2026-10-12 10:12:23Z) — were test payments. They were booked on
2026-10-30 as unapplied receipts (journal entries **63** and **64**, `payment.received.unapplied`,
Dr 311 / Cr 681) and had sat on the worklist ever since, asking an operator to place money that was
never going to be placed. The 2026-11-07 decision is to dismiss both rows, with a reason saying they are test captures. The
dismissal is a row change on production and is run once the build carrying the endpoint is deployed;
until then the queue still shows them. The rows, the entries and the statement matches stay exactly
as they were — only the three dismissal columns are written.
