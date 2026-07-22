# Accounting API endpoint reference

This reference describes the accounting routes currently implemented by the backend. The API is JSON unless noted as `multipart/form-data` or a file response.

## Authoritative implementation boundary

- The final accounting entity model is `Domain.Entities.AccountingCore`, `Domain.Entities.AccountingPlatform`, `Domain.Entities.FinancialOperations`, and `Domain.Entities.Organization`.
- The final API implementation is split across the organization, accounting-files, platform-imports, compensation, rider-payroll, ledger, financial-access, accounting-posting, accounting-storage, and financial-operations service slices.
- Do not reintroduce the retired monolithic `Domain.Entities.Accounting` or `Application.Service.Accounting` module. It duplicates DbSets, EF configurations, routes, and migrations from the authoritative implementation.
- `/api/Supplier`, `/api/Bill`, `/api/return`, `/api/Transfer`, `/api/SparePart`, and `/api/RiderAccessory` remain isolated legacy maintenance APIs. Their integer identifiers must never be passed to the modern GUID-based accounting endpoints.

## Common rules

- Base URL is the deployed API host.
- All routes below require a bearer JWT. Modern `/api/accounting` and `/api/ledger` routes require `Master` or `Accountant`, except the explicitly marked `Member` cash-delivery routes. `/api/organization-settings/current` accepts any authenticated actor and filters its response to that actor's accessible legal entities; `/api/financial-access` remains `Master` only.
- Successful commands return HTTP `200` with the response DTO. Successful no-value commands return an empty `200`, except routes explicitly documented as `204 No Content` (such as cash-access revocation).
- Expected failures are returned as `ProblemDetails` with `error.code`, `error.description`, and the feature-specific HTTP status.
- Financial commands that create or finalize accounting documents require the `Idempotency-Key` header. Reusing the key with a different request returns `409`.
- Dates are ISO dates (`YYYY-MM-DD`), decimals use JSON numbers, GUIDs are standard UUID strings, and `RowVersion` values are opaque base64 strings.
- A posted document is immutable. Correct it with a reversal route.
- Register/query routes use `PaginationRequest` (`PageNumber` defaults to `1`; `PageSize` defaults to `25` and is clamped to `1..100`) and return `PagedResponse<T>`. Read filters are legal-entity scoped and collections use a stable secondary key.
- Enum-typed response properties use the application's current JSON representation: integer values from the declared enum. The reused `FinancialOperationResponse.Status` compatibility field is a string and returns the enum name; the frontend therefore continues accepting both numeric enum fields and this existing string status field.

## 1. Private accounting files

Controller: `AccountingFilesController`

### `POST /api/accounting/files`

Uploads an evidence, bank, export, or other accounting file into encrypted private storage.

Request: `multipart/form-data`

| Field | Type | Required | Meaning |
|---|---|---:|---|
| `legalEntityId` | integer | yes | Owning Saudi legal entity. |
| `retainUntil` | datetime | no | Retention deadline; defaults to seven years. |
| `file` | file | yes | `.pdf`, `.png`, `.jpg`, `.jpeg`, `.xlsx`, `.csv`, or `.txt`; maximum 100 MB. |

The service validates the file signature, calculates SHA-256, encrypts the bytes, and deduplicates identical content.

Response: `AccountingFileResponse`

```json
{
  "id": "guid",
  "legalEntityId": 1,
  "originalFileName": "receipt.pdf",
  "contentType": "application/pdf",
  "length": 24576,
  "sha256": "64-hex-characters",
  "retainUntil": "2033-07-13T00:00:00Z",
  "createdAt": "2026-07-13T12:00:00Z"
}
```

### `GET /api/accounting/files/{fileId}`

Downloads an authenticated private file. The physical storage path is never returned. Response is the original file stream with its stored content type and safe original filename.

## 2. Direct platform imports

Controller: `PlatformImportsController`, base route `api/accounting`.

The accountant does not create a template, copy a fingerprint, activate a template, or re-upload/reprocess the file. Use exactly one of these four direct upload routes:

- `POST /api/accounting/platform-imports/amazon`
- `POST /api/accounting/platform-imports/hunger`
- `POST /api/accounting/platform-imports/keeta/pay-per-order`
- `POST /api/accounting/platform-imports/keeta/segments`

Each route accepts the same `multipart/form-data` shape: `legalEntityId`, `platformAccountId`, `externalReference`, `periodStart`, `periodEnd`, optional `sourceControlTotal`, and `file`. There is no `templateId`, `adapterKey`, `schemaFingerprint`, or `configurationJson` in an upload request. The route selects the built-in parser and the backend creates or reuses the active system template for the relevant fingerprint.

Response: `PlatformImportBatchResponse`:

```json
{
  "id": "guid",
  "legalEntityId": 1,
  "platformAccountId": 10,
  "storedFileId": "guid",
  "templateId": "guid",
  "adapterKey": "keeta-segments-v1",
  "externalReference": "KEETA-2026-05",
  "periodStart": "2026-05-01",
  "periodEnd": "2026-05-31",
  "parserVersion": "openxml-stream-v1",
  "schemaFingerprint": "64-hex-characters",
  "status": 3,
  "sourceControlTotal": 1122463.40,
  "normalizedControlTotal": 1122463.40,
  "sheetCount": 2,
  "rawRowCount": 154,
  "rawCellCount": 3977,
  "factCount": 1055,
  "openBlockingIssueCount": 0,
  "rowVersion": "base64"
}
```

Upload alone creates no payroll and no journal. The importer stores the original encrypted file, the relevant summary sheets and cells, normalized facts, control totals, and lineage. Amazon stores `Sheet1`; Hunger stores `WR` and `RLVL`; each Keeta route stores `تفاصيل الشركاء` and `تفاصيل سائق التوصيل`. The very large Keeta order-detail sheet is intentionally not expanded into database rows.

Template list/detail and maintenance actions remain available for support and audit visibility, but the accountant upload screen must not call template create/activate/reprocess during the normal four-route workflow.

### `GET /api/accounting/platform-imports/{id}`

Response: the complete `PlatformImportBatchResponse` and current numeric status. Values `1..8` represent `Received`, `Parsing`, `NeedsResolution`, `Reconciled`, `Approved`, `Rejected`, `Superseded`, and `Failed` respectively. Use `statusNameAr` for the Arabic UI label; keep `status` for workflow logic.

### `GET /api/accounting/platform-imports/{id}/issues`

Response: array of `PlatformImportIssueResponse` objects: `id`, severity (`Warning`/`Blocking`), status, issue code, message, resolution, and source raw-row ID. The Arabic display fields are `severityNameAr`, `statusNameAr`, `codeAr`, and `messageAr`; use these in the accountant UI. `code` remains the stable machine-readable value (for example `IDENTITY_MISSING`). When the linked row has a resolved rider, the response also includes `riderIqamaNo` and `riderNameAr`.

### `GET /api/accounting/platform-imports/{id}/facts` and `/rows`

Fact responses include the stable `metricCode` plus Arabic `metricNameAr`, `categoryNameAr`, and `workerCategoryAr`. Rider facts include `riderIqamaNo` and `riderNameAr`; raw-row responses expose the same rider fields when the row can be linked to exactly one rider. Company summary rows correctly return these rider fields as `null`.

### `POST /api/accounting/import-issues/{id}/resolve`

Request: `{ "resolution": "Mapped to rider 1000000001", "waive": false }`.

Response: resolved `PlatformImportIssueResponse`. A blocking issue may only be waived when the service permits an accountant reason.

### `POST /api/accounting/platform-imports/{id}/worker-remaps`

Request: `RemapPlatformWorkerRequest`:

```json
{
  "externalWorkerId": "HUNGER-OLD-ID",
  "riderIqamaNo": 1000000001,
  "effectiveFrom": "2026-05-15",
  "effectiveTo": "2026-05-31",
  "reason": "Rider worked using another rider's platform ID"
}
```

Response: updated import batch. The mapping is effective-dated and preserves the original external ID.

### `POST /api/accounting/platform-facts/{id}/validity-override`

Request: `{ "isValid": true, "reason": "Keeta support confirmed eligibility by email" }`.

Response: updated `PlatformNormalizedFactResponse`. Missing/invalid Keeta validity blocks that payroll component until this route records a reasoned override.

### `POST /api/accounting/platform-imports/{id}/approve`

Request: `{ "comment": "Totals reconciled to invoice" }`.

Response: approved `PlatformImportBatchResponse`. Approval requires no blocking issues, no unresolved identity ambiguity, and reconciled control totals. It still creates no payroll or journal.

### `POST /api/accounting/platform-imports/{id}/reject`

Request: `{ "comment": "Wrong month supplied" }`.

Response: rejected `PlatformImportBatchResponse`.

### `GET /api/accounting/platform-imports/{id}/file`

Returns the authenticated encrypted source workbook stream for the import batch. The general private-file route is `GET /api/accounting/files/{fileId}`.

## 3. Compensation policies

Controller: `CompensationPoliciesController`, base route `api/accounting/compensation-policies`.

### `POST /api/accounting/compensation-policies`

Request: `CreateCompensationPolicyRequest` with entity/platform, worker category, code/name, effective dates, and a non-empty `rules` array. Each rule contains:

`code`, `name`, `template`, `componentType`, `metricCode`, optional condition metric/operator/value, lower/upper bounds, rate, below/above rates, fixed/base amounts, target component, priority, exclusive group, stacking mode, and rounding scale.

Safe templates are fixed amount, per-unit, threshold, tiered base-plus-excess, percentage, range, cap, floor, and eligibility condition. Scripts and arbitrary SQL expressions are not accepted.

Response: `CompensationPolicyResponse` with version, status, row version, effective dates, and `CompensationRuleResponse[]`.

### `GET /api/accounting/compensation-policies/{id}`

Response: the policy and its immutable rules.

### `POST /api/accounting/compensation-policies/{id}/activate`

Request: `{ "rowVersion": "base64" }`.

Response: activated policy. The service rejects stale row versions and overlapping active policies.

### `POST /api/accounting/compensation-policies/{id}/simulate`

Request: metrics dictionary, for example:

```json
{ "accepted_orders": 500, "delivered_orders": 500, "workdays": 26 }
```

Response: `CompensationSimulationResponse` with selected rules, quantities, rates, amounts, explanations, conflicts, total earnings, deductions, and net amount.

Example Hunger rule: under 500 orders uses `accepted_orders × 4`; at 500 or more, the tiered rule uses `2,000 + (accepted_orders - 500) × 6`. The same endpoint supports a fixed Keeta or Amazon rule.

## 4. Rider payroll and payments

Controller: `RiderPayrollController`, base route `api/accounting`.

### `POST /api/accounting/payroll-runs`

Request: `{ "legalEntityId": 1, "periodStart": "2026-05-01", "periodEnd": "2026-05-31", "currencyCode": "SAR" }`.

Response: `RiderPayrollRunResponse` with run ID/number, period, status (`Draft` initially), totals, row version, and lines.

### `GET /api/accounting/payroll-runs/{id}`

Response: full run, rider lines, platform components, automatic/manual sources, gross earnings, deductions, carry-forward, net pay, hold status, and accrual document ID.

### `POST /api/accounting/payroll-runs/{id}/calculate`

Request: `{ "rowVersion": "base64" }`.

Response: calculated `RiderPayrollRunResponse`. It consumes only approved, non-superseded facts and snapshots policy versions, source batches, rows, quantities, rates, and calculations.

### `POST /api/accounting/payroll-runs/{id}/adjustments`

Request: `AddRiderPayrollAdjustmentRequest` with rider Iqama, positive/negative amount, reason, notes, and optional evidence file ID.

Response: updated payroll run.

### `POST /api/accounting/payroll-runs/{id}/approve`

Requires `Idempotency-Key` header. Body: posting profile, correlation ID, and optional row version. Response: approved/calculated run with accrual financial document ID.

### `POST /api/accounting/payroll-runs/{id}/reverse`

Requires `Idempotency-Key`. Body: reversal date, reason, correlation ID, optional row version. Response: reversed run. Financial items, installments, carry-forward, GL accrual, and payment state are restored atomically.

### `POST /api/accounting/rider-financial-item-types`

Request: entity ID, code/name, direction (`Earning`/`Deduction`), priority, and ledger account ID. Response: `RiderFinancialItemTypeResponse`.

### `POST /api/accounting/rider-financial-items`

Request: entity ID, rider, item type, reference, description, effective date, optional deduction start, amount, optional installment count/first installment date, and optional evidence file ID. Response: item with outstanding balance and installment schedule.

### `POST /api/accounting/payroll-runs/{id}/payment-batches`

Request: payment method (`Bank`, `Cash`, `Hold`, `Mixed`) and optional rider numbers or explicit allocations. Response: `RiderPaymentBatchResponse` with lines, amounts, IBAN snapshot, housing, and status.

### `POST /api/accounting/payment-batches/{id}/export`

Request: `{ "format": "xlsx" }` or `{ "format": "csv" }`.

Response: `AccountingFileResponse` for the private generated file. The export includes batch, rider, IBAN, housing, method, and amount and protects spreadsheet values from formula injection.

### `POST /api/accounting/payment-batches/{id}/confirm`

Requires `Idempotency-Key`. Body: settlement date, posting profile, correlation ID, optional line IDs, and notes. Response: updated payment batch. The settlement journal is posted only on confirmed settlement.

### `POST /api/accounting/payment-batches/{id}/reverse`

Requires `Idempotency-Key`. Body: reversal date, reason, and correlation ID. Response: reversed payment batch and reversal document references.

### `POST /api/accounting/cash-delivery-access`

Request: `{ "legalEntityId": 1, "userId": "identity-id", "housingId": 7 }`.

Response: `HousingCashAccessResponse`. Grants a user who already has the `Member` role access to cash confirmation for one housing.

### `GET /api/accounting/riders/{riderIqamaNo}/financial-profile?legalEntityId=1`

Response: rider name, IBAN, housing, platform summaries, financial items, payroll lines, outstanding deductions, and unpaid payroll.

### `POST /api/accounting/cash-deliveries/payment-batches/{batchId}/confirm` (housing-scoped controller)

Controller: `HousingCashDeliveriesController`. Requires the `Member` role and `Idempotency-Key`. Body contains settlement date, posting profile, correlation ID, selected line IDs, and notes. A Member can only view or confirm lines assigned to a housing granted to that user. Response is the updated batch.

## 5. Ledger, setup, periods, journals, and reports

Controller: `LedgerController`, base route `api/ledger`.

### Setup commands

| Route | Request | Response |
|---|---|---|
| `POST /api/ledger/currencies` | `CreateCurrencyRequest`: code, name, decimal places | `CurrencyResponse` |
| `POST /api/ledger/exchange-rates` | entity, from/to currency, effective date, rate | `ExchangeRateResponse` |
| `POST /api/ledger/dimensions` | entity, code, name, required flag | `FinancialDimensionResponse` |
| `POST /api/ledger/dimension-values` | dimension ID, code, name | `FinancialDimensionValueResponse` |
| `POST /api/ledger/accounts` | entity, optional parent, code/name, account type, control/manual/cash flags | `AccountingAccountResponse` |
| `POST /api/ledger/posting-profiles` | entity, code/name, effective dates, event lines `{eventCode,debitAccountId,creditAccountId}` | `PostingProfileResponse` |
| `POST /api/ledger/fiscal-years` | entity, name, start/end, period array | `FiscalYearResponse` |
| `GET /api/ledger/fiscal-years/{id}` | route ID | `FiscalYearResponse` |
| `POST /api/ledger/fiscal-periods/{id}/soft-close` | reason, tax lock, payroll lock | `FiscalPeriodResponse` |
| `POST /api/ledger/fiscal-periods/{id}/close` | reason, tax lock, payroll lock | `FiscalPeriodResponse` |
| `POST /api/ledger/fiscal-periods/{id}/reopen` | reason, tax lock, payroll lock | `FiscalPeriodResponse` |
| `POST /api/ledger/recurring-journal-schedules` | entity, document metadata, frequency, dates, journal lines | `RecurringJournalScheduleResponse` |
| `POST /api/ledger/recurring-journal-schedules/generate?throughDate=YYYY-MM-DD` | query date | `FinancialDocumentResponse[]` |
| `GET /api/ledger/legal-entities/{id}/accounts` | entity ID | `AccountingAccountResponse[]` |

### Journals and documents

- `POST /api/ledger/manual-journals`: `CreateManualJournalRequest` contains entity, branch, transaction date, description, currency, exchange rate, idempotency key, and balanced journal lines. Response is `FinancialDocumentResponse`.
- `GET /api/ledger/documents/{documentId}`: returns document header and lines.
- `POST /api/ledger/documents/{documentId}/submit`: no body; returns the updated `FinancialDocumentResponse`.
- `POST /api/ledger/documents/{documentId}/approve`: `{ "comment": "..." }`; returns the updated `FinancialDocumentResponse`. The creator cannot approve their own manual journal.
- `POST /api/ledger/documents/{documentId}/post`: no body; returns the posted `JournalEntryResponse`.
- `POST /api/ledger/documents/{documentId}/reversals`: `ReverseJournalRequest` with reversal date, reason, and idempotency key; returns reversal document.
- `GET /api/ledger/legal-entities/{id}/approval-inbox`: returns `ApprovalInboxItemResponse[]`.

### Reports

| Route | Query | Response |
|---|---|---|
| `GET /api/ledger/legal-entities/{id}/trial-balance` | `fromDate`, `toDate` | opening, movement, closing debit/credit and balances by account |
| `GET /api/ledger/legal-entities/{id}/profit-and-loss` | `fromDate`, `toDate` | revenue/expense lines and net income |
| `GET /api/ledger/legal-entities/{id}/balance-sheet` | `asOfDate` | assets, liabilities, equity, current net position |
| `GET /api/ledger/legal-entities/{id}/cash-movement` | `fromDate`, `toDate` | configured cash-equivalent account IDs, inflows, outflows, net movement |
| `GET /api/ledger/legal-entities/{id}/dimensions/{dimensionId}/balances` | `fromDate`, `toDate` | balances by dimension value and account |
| `GET /api/ledger/legal-entities/{id}/audit-events` | `take` | immutable audit events with actor, payload, timestamp, and hash |

## 6. Bounded operational accounting

Controller: `OperationalAccountingController`, base route `api/accounting`. New bounded invoice/payment/movement/expense contracts omit control-account IDs; the service resolves them from the effective posting profile.

| Route | Request | Response |
|---|---|---|
| `POST /api/accounting/receivables/customers` | entity, code, name, optional tax number | `MasterRecordResponse` |
| `POST /api/accounting/receivables/invoices` | customer, dates, currency, profile, lines `{description,quantity,unitPrice,taxCodeId}` | draft `FinancialOperationResponse` |
| `POST /api/accounting/receivables/invoices/{id}/issue` | idempotency header; optional command body | issued operation with financial document ID |
| `POST /api/accounting/receivables/receipts` | customer, receipt references, date, currency, amount, profile; idempotency header | receipt operation |
| `POST /api/accounting/receivables/receipts/{id}/allocations` | invoice ID and amount | success; allocation cannot exceed unmatched balances |
| `POST /api/accounting/receivables/platform-settlements` | source evidence, reference/date, gross, commission, net, profile; idempotency header | settlement operation and posted document ID |
| `POST /api/accounting/payables/suppliers` | entity, code, name, optional tax number | `MasterRecordResponse` |
| `POST /api/accounting/payables/invoices` | supplier, dates, currency, profile, lines `{description,quantity,unitPrice,taxCodeId}` | draft payable operation |
| `POST /api/accounting/payables/invoices/{id}/record` | idempotency header; optional command body | recorded payable and document ID |
| `POST /api/accounting/payables/payments` | supplier, payment references/date, amount, profile; idempotency header | payment operation |
| `POST /api/accounting/payables/payments/{id}/allocations` | supplier invoice ID and amount | success; bounded by unmatched balances |
| `POST /api/accounting/expenses/evidence` | entity/platform, evidence type/reference, `storedFileId`, metadata JSON | `SourceEvidenceResponse` |
| `POST /api/accounting/expenses/evidence/{id}/review` | accept flag and comment | reviewed evidence |
| `POST /api/accounting/expenses/claims` | employee, optional evidence, claim date/description/net/tax/profile; idempotency header | expense operation and document ID |
| `POST /api/accounting/inventory/items` | entity, SKU, name, unit | `MasterRecordResponse` |
| `POST /api/accounting/inventory/movements` | item, movement type/date/bins, quantity/unit cost/profile; idempotency header | inventory operation and document ID |
| `POST /api/accounting/treasury/bank-accounts` | entity, code/name/currency, configured ledger account | `MasterRecordResponse` |
| `POST /api/accounting/treasury/statement-lines` | bank account, external reference/date/amount/description | statement operation |
| `POST /api/accounting/treasury/statement-lines/{id}/reconcile` | financial document ID | reconciled statement operation |
| `POST /api/accounting/tax/codes` | entity, code/name, direction, rate, tax account, effective dates | `TaxCodeResponse` |
| `POST /api/accounting/tax/returns` | entity and period | `TaxReturnResponse` |
| `POST /api/accounting/tax/returns/{id}/submit` | submission reference | submitted `TaxReturnResponse` |
| `POST /api/accounting/assets` | entity, asset number/description/dates/cost/residual/life and configured accounts | `FixedAssetResponse` |
| `POST /api/accounting/budgets` | entity, name/dates, lines `{accountId,dimensionValueId,amount}` | `BudgetResponse` |

## 7. Financial access

Controller: `FinancialAccessController`. Routes are under `/api/financial-access` and require the `Master` role.

- `POST /api/financial-access`: request `{userId,legalEntityId,permissions}`; response `FinancialUserAccessResponse`.
- `GET /api/financial-access/legal-entities/{legalEntityId}`: response `FinancialUserAccessResponse[]`.
- `DELETE /api/financial-access/legal-entities/{legalEntityId}/users/{userId}`: revokes access; response is empty success.

`Accountant` is granted all financial permissions. `Member` cash-delivery access is restricted separately by housing assignment.

## 8. Register and detail queries

All routes in this section require `Master,Accountant` unless explicitly marked `Member`. Paged routes accept `PageNumber`, `PageSize`, applicable `Search`, `Status`, `FromDate`, `ToDate`, `SortBy`, and `SortDirection` values plus the listed feature filters.

### Organization, files, imports, and policies

| Route | Filters/body | Response |
|---|---|---|
| `GET /api/organization-settings/current` | authenticated actor | `OrganizationResponse` restricted to accessible legal entities |
| `GET /api/accounting/files` | `legalEntityId`, `contentType`, date filters, pagination | `PagedResponse<AccountingFileResponse>` |
| `GET /api/accounting/platform-templates` | `legalEntityId`, `platformAccountId`, `status`, `search`, pagination | `PagedResponse<PlatformImportTemplateResponse>` |
| `GET /api/accounting/platform-templates/{id}` | template ID | complete template including `configurationJson` |
| `POST /api/accounting/platform-imports/amazon` | direct multipart upload, no template fields | `PlatformImportBatchResponse` with `adapterKey=amazon-anow-v1` |
| `POST /api/accounting/platform-imports/hunger` | direct multipart upload, no template fields | `PlatformImportBatchResponse` with `adapterKey=hunger-ftr-v1` |
| `POST /api/accounting/platform-imports/keeta/pay-per-order` | direct multipart upload, no template fields | `PlatformImportBatchResponse` with `adapterKey=keeta-pay-per-order-v1` |
| `POST /api/accounting/platform-imports/keeta/segments` | direct multipart upload, no template fields | `PlatformImportBatchResponse` with `adapterKey=keeta-segments-v1` |
| `GET /api/accounting/platform-imports` | entity/platform/status/date/search, pagination | `PagedResponse<PlatformImportBatchResponse>` |
| `GET /api/accounting/platform-imports/{id}/facts` | category/metric/resolved filters, pagination | paged normalized facts |
| `GET /api/accounting/platform-imports/{id}/rows` | `sheetId`, `search`, pagination | paged raw rows with cells |
| `POST /api/accounting/platform-imports/{id}/reprocess` | `{templateId,rowVersion?}` | updated `PlatformImportBatchResponse` |
| `POST /api/accounting/platform-templates/{id}/retire` | `{comment?}` | retired `PlatformImportTemplateResponse` |
| `POST /api/accounting/platform-imports/{id}/supersede` | `{replacementBatchId,reason,rowVersion?}` | superseded `PlatformImportBatchResponse` |
| `GET /api/accounting/compensation-policies` | entity/platform/category/status/date/search, pagination | `PagedResponse<CompensationPolicyResponse>` |
| `POST /api/accounting/compensation-policies/{id}/versions` | `{effectiveFrom,effectiveTo?}` | cloned draft `CompensationPolicyResponse` |
| `POST /api/accounting/compensation-policies/{id}/retire` | `{rowVersion?,comment?}` | retired `CompensationPolicyResponse` |

The four direct routes automatically certify the relevant workbook shape and create/reuse the internal active template. The create/activate/reprocess template workflow is maintenance-only and is not called by the accountant upload screen. Import download resolves the batch's `StoredFileId`; callers do not pass a stored-file ID to the batch route.

### Payroll, payments, and cash delivery

| Route | Filters/body | Response |
|---|---|---|
| `GET /api/accounting/payroll-runs` | entity/status/date/search, pagination | `PagedResponse<RiderPayrollRunResponse>` |
| `GET /api/accounting/rider-financial-item-types` | entity/direction/active/search, pagination | `PagedResponse<RiderFinancialItemTypeResponse>` |
| `GET /api/accounting/rider-financial-items` | entity/rider/status/type/date/search, pagination | `PagedResponse<RiderFinancialItemResponse>` |
| `GET /api/accounting/rider-financial-items/{id}` | item ID | `RiderFinancialItemResponse` |
| `GET /api/accounting/payment-batches` | entity/run/method/status/date/search, pagination | `PagedResponse<RiderPaymentBatchResponse>` |
| `GET /api/accounting/payment-batches/{id}` | batch ID | `RiderPaymentBatchResponse` |
| `POST /api/accounting/payment-batches/{id}/lines/{lineId}/reject` | `{reason}` | updated `RiderPaymentBatchResponse` |
| `GET /api/accounting/cash-delivery-access` | entity/user/housing/active, pagination | `PagedResponse<HousingCashAccessResponse>` |
| `DELETE /api/accounting/cash-delivery-access/{id}` | access ID | `204 No Content` |
| `GET /api/accounting/cash-deliveries/inbox` | **Member only**; optional entity/status, pagination | assigned-housing `PagedResponse<RiderPaymentBatchResponse>` |
| `GET /api/accounting/cash-deliveries/payment-batches/{id}` | **Member only** | assigned-housing `RiderPaymentBatchResponse` |

The existing `POST .../calculate` action remains the only payroll calculation action. Payment batches intentionally have no row-version field.

### Ledger registers

| Route | Response |
|---|---|
| `GET /api/ledger/currencies` | filtered `CurrencyResponse[]` |
| `GET /api/ledger/legal-entities/{id}/exchange-rates` | `PagedResponse<ExchangeRateResponse>` |
| `GET /api/ledger/legal-entities/{id}/dimensions` | filtered `FinancialDimensionResponse[]` |
| `GET /api/ledger/dimensions/{id}/values` | filtered `FinancialDimensionValueResponse[]` |
| `GET /api/ledger/legal-entities/{id}/posting-profiles` | `PagedResponse<PostingProfileResponse>` |
| `GET /api/ledger/posting-profiles/{id}` | complete `PostingProfileResponse` |
| `GET /api/ledger/legal-entities/{id}/fiscal-years` | `PagedResponse<FiscalYearResponse>` |
| `GET /api/ledger/legal-entities/{id}/recurring-journal-schedules` | `PagedResponse<RecurringJournalScheduleResponse>` |
| `GET /api/ledger/recurring-journal-schedules/{id}` | complete schedule with lines |
| `GET /api/ledger/legal-entities/{id}/documents` | `PagedResponse<FinancialDocumentResponse>` |
| `GET /api/ledger/legal-entities/{id}/journal-entries` | `PagedResponse<JournalEntryResponse>` |

### Operational accounting registers

The modern `/api/accounting` controller exposes paged list and same-resource detail GETs for receivable customers/invoices/receipts/platform settlements; payable suppliers/invoices/payments; expense evidence/claims; inventory items/movements; bank accounts/statement lines; tax codes/returns; fixed assets; and budgets. Details use the same existing response records and include persisted lines, allocations, open/unapplied amounts, or included transactions where applicable. `GET /api/accounting/inventory/stock-balances` returns current quantity and value by item and bin.

The accounting dashboard is composed client-side from profit-and-loss, balance-sheet, cash-movement, approval-inbox, audit-event, import, and payroll calls. There is no accounting dashboard persistence or aggregate response model.

## 9. Legacy compatibility and maintenance APIs

Controller: `FinancialOperationsController`, base route `/api/financial-operations`. It remains for one compatibility release behind the `Accounting:LegacyFinancialOperationsEnabled` feature flag. It exposes older equivalents for evidence, platform settlements, customers/invoices/receipts, employee payroll contracts, legacy payroll runs, suppliers/invoices/payments, inventory, expenses, banks, tax, assets, and budgets.

Legacy request DTOs include direct account IDs for backward compatibility. New integrations must use `/api/accounting/...`, where control accounts are resolved from posting profiles. The legacy evidence route no longer accepts a physical path; its compatibility `storageLocator` value must be a private stored-file GUID.

The maintenance APIs `/api/Supplier`, `/api/Bill`, `/api/return`, `/api/Transfer`, `/api/SparePart`, and `/api/RiderAccessory` remain separate. Their integer IDs are never accepted by modern GUID/legal-entity `SupplierAccount`, `SupplierInvoice`, or `InventoryItem` endpoints; the modern entities are authoritative for financial accounting.

## Accounting flow summary

`Upload → parse and preserve evidence → resolve issues and substitutions → reconcile → approve import → create policy/version → simulate → calculate payroll → adjust → approve → prepare payment → export → confirm settlement → post GL → report → close period`.

The import approval step never posts payroll by itself. Every final posting stores source IDs, policy versions, currencies, rates, dimensions, audit event, correlation ID, and outbox message in the accounting transaction.
