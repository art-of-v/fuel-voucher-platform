/**
 * The translation keys for the states the API reports.
 *
 * <p>
 * The API spells its states in PascalCase (`PendingPayment`, `DecodeIntegrityWarning`) and the
 * translation file keys them in camelCase (`order.status.pendingPayment`). These two functions
 * are the one place that knows the two spellings agree.
 */

/**
 * The key of an order's state in the translation file.
 *
 * <p>
 * It used to collapse the two pending states into one `pending` key - and it never did: the
 * comparison was against a lowercase literal while the value was camelCase, so the branch was
 * dead code. Collapsing them would have been a loss anyway: the translations carry
 * "Pending Payment" and "Pending Fulfillment" as two different things, and an operator reading
 * an order's money needs to know which of the two it is waiting for.
 */
export function orderStatusKey(status: string): string {
  return status.charAt(0).toLowerCase() + status.slice(1);
}

/** The key of a voucher's state in the translation file. */
export function voucherStatusKey(status: string): string {
  return status.charAt(0).toLowerCase() + status.slice(1);
}
