# Query filters and the scopes that lift them

EF Core's global query filters are read policies: a soft-deleted row is invisible unless the query
says otherwise. `IgnoreQueryFilters()` with no argument says "lift every filter on every entity in
this query" — including a filter added later by someone who never saw the call site. That is how a
narrow admin read becomes a cross-cutting exception.

The model has two named filters:

| Key | What it hides | Configured by |
| --- | --- | --- |
| `SoftDelete` | rows with `is_deleted = true` | `OrderConfiguration`, `FuelVoucherConfiguration`, `UserConfiguration`, `BrandConfiguration`, `FuelTypeEntityConfiguration`, `BrandStationConfiguration`, `CompanyMemberConfiguration` |
| `ReleasedFulfilment` | fulfilments with `released_at_utc` set | `FulfillmentConfiguration` |

Only three scopes lift them, and each lifts only the filter it names:

- `.IncludingSoftDeleted()`
- `.IncludingReleasedFulfilments()`
- `.IncludingSoftDeletedAndReleasedFulfilments()`

The helpers live in `backend/src/FuelFlow.API/SharedKernel/Database/QueryFilterScopes.cs`. A raw
`IgnoreQueryFilters()` outside that file fails `QueryFilterBypassGuardTests`; so does a helper call
this table does not list, a count that does not match the code, and a row whose file no longer uses
the scope.

**Tenant isolation is not a query filter.** It is Postgres row-level security (`app.user_id` /
`app.is_system`), and `IgnoreQueryFilters` cannot lift it. That is pinned by
`TenantRowLevelSecurityIntegrationTests.Bypassing_the_model_filters_does_not_bypass_the_policies`.

## The inventory

The counts are exact; update them in the same change that adds or removes a call.

| File | Scope | Sites | Why |
| --- | --- | --- | --- |
| `backend/src/FuelFlow.API/BackgroundJobs/DataRetentionService.cs` | `IncludingSoftDeleted` | 1 | The QR reference check must see a deleted voucher, or the purge removes a row a voucher still points at. |
| `backend/src/FuelFlow.API/BackgroundJobs/DeletedUnpaidOrderCleanupService.cs` | `IncludingSoftDeleted` | 1 | Every candidate is a deleted order by definition; the query-wide scope also lets the voucher and refund guards see deleted rows. |
| `backend/src/FuelFlow.API/BackgroundJobs/FulfillmentService.PurchaseFulfillment.cs` | `IncludingSoftDeleted` | 1 | The QA flag is read from the owner even when that account is deleted, matching the raw-SQL claim predicate. |
| `backend/src/FuelFlow.API/BackgroundJobs/FulfillmentService.Renewal.cs` | `IncludingSoftDeleted` | 1 | The QA flag is read from the owner even when that account is deleted, matching the raw-SQL claim predicate. |
| `backend/src/FuelFlow.API/BackgroundJobs/FulfillmentService.Renewal.cs` | `IncludingReleasedFulfilments` | 1 | A released delivery still counts as applied when deciding whether a renewal line is owed. |
| `backend/src/FuelFlow.API/BackgroundJobs/FulfillmentService.RenewalStock.cs` | `IncludingReleasedFulfilments` | 1 | A released delivery still counts as applied when deciding whether a renewal line is owed. |
| `backend/src/FuelFlow.API/BackgroundJobs/MonobankReconciliationService.cs` | `IncludingSoftDeleted` | 1 | A customer can delete an unpaid checkout while its invoice stays live and payable. |
| `backend/src/FuelFlow.API/Features/Acquiring/Reconciliation/GetStatementReconciliationQueryHandler.cs` | `IncludingSoftDeleted` | 3 | A deleted order must not hide the refund or the acquirer's row that belongs to it; the act records money. |
| `backend/src/FuelFlow.API/Features/Auth/RegisterDevice/RegisterDeviceCommandHandler.cs` | `IncludingSoftDeleted` | 1 | A soft-deleted owner must be distinguishable from a missing one, or device reclaim fails closed forever. |
| `backend/src/FuelFlow.API/Features/Monobank/ProcessWebhook/ProcessMonobankWebhookCommandHandler.WebhookProcessing.cs` | `IncludingSoftDeleted` | 1 | A paid invoice can outlive the customer's deletion of the order. |
| `backend/src/FuelFlow.API/Features/Orders/DeleteOrder/DeleteOrderCommandHandler.cs` | `IncludingSoftDeleted` | 1 | The foreign key counts deleted vouchers, so the guard has to count them too. |
| `backend/src/FuelFlow.API/Features/Orders/GetAdminOrderById/GetAdminOrderByIdQueryHandler.cs` | `IncludingSoftDeletedAndReleasedFulfilments` | 1 | The admin detail is the audit view: deleted orders and released deliveries are part of the history. |
| `backend/src/FuelFlow.API/Features/Orders/GetAdminOrders/GetAdminOrdersQueryHandler.cs` | `IncludingSoftDeletedAndReleasedFulfilments` | 1 | The admin order list includes deleted orders, and its fulfilment includes must keep released rows. |
| `backend/src/FuelFlow.API/Features/Orders/GetAdminPurchases/GetAdminPurchasesQueryHandler.cs` | `IncludingSoftDeletedAndReleasedFulfilments` | 1 | The purchases page is the same read model as the admin order list. |
| `backend/src/FuelFlow.API/Features/Orders/GetUserPurchases/GetUserPurchasesCommandHandler.cs` | `IncludingReleasedFulfilments` | 1 | A renewal whose voucher was later handed back is still part of the customer's timeline. |
| `backend/src/FuelFlow.API/Features/Orders/RefundOrder/RefundOrderCommandHandler.cs` | `IncludingSoftDeletedAndReleasedFulfilments` | 1 | A refund has to reach a deleted order and the released deliveries that came from it. |
| `backend/src/FuelFlow.API/Features/Vouchers/Exchange/ConfirmVoucherExchangeCommandHandler.cs` | `IncludingSoftDeleted` | 1 | The operator is told "this voucher is deleted" explicitly instead of "not found". |
| `backend/src/FuelFlow.API/Features/Vouchers/Exchange/GetVoucherExchangeBatchesQueryHandler.cs` | `IncludingSoftDeleted` | 1 | An exchange audit row must stay readable after the retired voucher is soft-deleted. |
| `backend/src/FuelFlow.API/Features/Vouchers/Exchange/GetVoucherExchangeBatchQueryHandler.cs` | `IncludingSoftDeleted` | 6 | Same audit, read by exchange batch id. |
| `backend/src/FuelFlow.API/Features/Vouchers/GetAdminVoucherById/GetAdminVoucherByIdQueryHandler.cs` | `IncludingSoftDeleted` | 1 | The admin detail is how an operator looks up an archived voucher. |
| `backend/src/FuelFlow.API/Features/Vouchers/GetAdminVoucherLifecycle/GetAdminVoucherLifecycleQueryHandler.cs` | `IncludingSoftDeleted` | 1 | The lifecycle of an archived voucher is exactly the history this screen exists for. |
| `backend/src/FuelFlow.API/Features/Vouchers/GetAdminVoucherLifecycle/GetAdminVoucherLifecycleQueryHandler.cs` | `IncludingReleasedFulfilments` | 3 | Released deliveries still moved the term and are part of the lineage. |
| `backend/src/FuelFlow.API/Features/Vouchers/GetAdminVouchers/GetAdminVouchersQueryHandler.cs` | `IncludingSoftDeleted` | 2 | The list and the unfiltered total include archived vouchers so they can be found and restored. |
| `backend/src/FuelFlow.API/Features/Vouchers/GetImportBatches/GetImportBatchVouchersQueryHandler.cs` | `IncludingSoftDeleted` | 1 | The batch's voucher tab shows what the import created, including rows later archived. |
| `backend/src/FuelFlow.API/Features/Vouchers/GetImportBatches/ImportBatchBulkActionQueryHandler.cs` | `IncludingSoftDeleted` | 1 | The action applies to the same set the batch tab shows; preserved from the pre-audit behaviour and called out in #689. |
| `backend/src/FuelFlow.API/Features/Vouchers/GetInventory/GetInventoryCommandHandler.cs` | `IncludingSoftDeleted` | 1 | Historical behaviour: the stock report counted archived rows before the audit. Preserved deliberately; changing it is a product decision, not a filter cleanup. |
| `backend/src/FuelFlow.API/Features/Vouchers/ParseInvoice/ParseInvoiceCommandHandler.cs` | `IncludingSoftDeleted` | 1 | The invoice roll-up covers every voucher the import created, archived or not. |
| `backend/src/FuelFlow.API/Features/Vouchers/PurchaseBatchCost/GetImportBatchCostTimelineQueryHandler.cs` | `IncludingSoftDeleted` | 5 | The costing timeline is an audit of exchanges, so the retired (and possibly archived) voucher must stay readable. |
| `backend/src/FuelFlow.API/Features/Vouchers/PurchaseBatchCost/GetImportBatchPnlQueryHandler.cs` | `IncludingSoftDeleted` | 1 | A deleted order still carries the reversed-status fact the P&L needs; the voucher side filters `!IsDeleted` explicitly. |
| `backend/src/FuelFlow.API/Features/Vouchers/PurchaseBatchCost/SetBatchCostCommandHandler.cs` | `IncludingSoftDeleted` | 2 | The pre-fill of the old cost and the supplier fallback read the batch's rows even after some were archived. |
| `backend/src/FuelFlow.API/Features/Vouchers/Renewal/Operator/ConfirmOperatorRenewalCommandHandler.cs` | `IncludingSoftDeleted` | 1 | A deleted voucher is reported as `not_found` on purpose rather than becoming invisible. |
| `backend/src/FuelFlow.API/Features/Vouchers/UnarchiveVoucher/UnarchiveVoucherCommandHandler.cs` | `IncludingSoftDeleted` | 2 | The read and the guarded update both address the row the filter hides. |
| `backend/src/FuelFlow.API/Middleware/SessionValidationMiddleware.cs` | `IncludingSoftDeleted` | 1 | A deleted account is rejected on purpose, not by accident; the middleware logs the difference. |

## Adding a scope

1. Prefer an explicit predicate (`!v.IsDeleted`) to a scope. If the query already says what it
   means, it does not need a bypass.
2. If the read genuinely needs hidden rows, use the narrowest helper — never
   `IgnoreQueryFilters()`.
3. Add the file, scope, count and reason to the table above in the same commit.
4. Run `dotnet test backend/tests/FuelFlow.UnitTests --filter QueryFilterBypassGuardTests`.
