# HungerStation, Keeta and Amazon Sheet Integration Model

## Purpose

The source workbooks are operational evidence and company-billing inputs. They must not directly mutate rider balances, revenue, payroll or stock. The safe flow is:

`original file -> immutable import batch -> exact source adapter -> raw rows/cells -> normalized facts -> identity/substitution resolution -> reconciliation -> approval -> payroll/AR posting -> bank reconciliation`

## Source workbooks recovered from previous chats

| Platform/template | Previous workbook | Grain and important fields |
|---|---|---|
| Amazon | `EPSR- ANOW Monthly Payment Review for MAY'26.xlsx` | Rider/month row plus daily order columns: Amazon/platform ID, iqama, rider name, DSP, store, daily May orders, grand total, working days, days off, prorated amount, incentive shipments/amount, EID and EID OT. |
| Keeta pay per order | `...#2026-05#نظام الدفع بالطلب الفاتورة....xlsx` | Partner summary, rider summary and very large order/transaction detail: transaction type, work ID, amount detail, total due and face-verification time/result. |
| Keeta segment/freelancer | `...#2026-05#نظام الشرائح الفاتورة....xlsx` | Partner summary, rider summary with validity/reason, connection metrics, orders, distance and pricing, incentives, discounts, compensation, fees, adjustments, TGA and total due; detail has fee/ticket/violation/punishment/face fields. |
| Hunger/FTR | `khedmat_sareea_ftr_Invoice Issue May 2026.xlsx` | WR summary, RLVL rider totals and FTR cost sheet: completed orders, basic payment, rejected/declined penalties, no-show, missed days/weekends/EOM, distance payment, rider balance, IBAN/bank, hours/days and distance payable. |

The original files were not available in the current task. These structures come from the earlier direct workbook inspection and must be revalidated by golden-file tests before implementation changes.

## Canonical source model

### Import envelope

`PlatformImportBatch`

- `Id`, `LegalEntityId`, `PlatformAccountId`, `TemplateCode`, `ReportPeriodStart/End`.
- original filename, byte length, SHA-256, MIME signature, storage URI, encrypted-at-rest key, uploader and upload time.
- parser name/version, schema fingerprint, source timezone/currency, status and superseded-batch link.
- row/sheet/control totals supplied by source and totals calculated by parser.
- unique constraints on `(LegalEntityId, PlatformAccountId, SHA256)` and source-issued invoice/reference where available.

`PlatformImportSheet`, `PlatformImportRawRow`, `PlatformImportRawCell`

- exact sheet order/name/visibility, merged ranges, formula/value/displayed value, original header path, row/column numbers and source types.
- no update/delete after ingestion; correction creates a new version/superseding batch.

### Identity and effective dating

`PlatformWorkerAccount`

- platform, external driver ID, internal rider/worker, effective from/to, mapping status, evidence and approver.
- platform ID is never assumed globally unique and never resolved through current `CompanyId` alone.

`WorkAssignment` and `RiderSubstitution`

- original account holder, actual worker, platform, effective start/end timestamp, reason, approver and evidence.
- dated detail rows route to the actual worker effective on the service timestamp.
- monthly-only rows overlapping a substitution remain blocked until a dated allocation or accountant-approved split exists.

### Normalized facts

| Fact | Required fields |
|---|---|
| `PlatformDailyActivity` | platform worker, service date, accepted/rejected/cancelled/stacked orders, working/connection minutes, shift status, source row. |
| `PlatformShiftSlot` | scheduled/actual start/end, duration, qualification/status, source slot/order. |
| `PlatformOrderTransaction` | task/work/ticket ID, service time, transaction/fee type, amount, distance, status, face-verification and violation/punishment facts. |
| `PlatformRiderPeriodSummary` | rider/platform/period, orders, days/hours, validity/reason, base pay, incentives, distance pay, penalties, fees, VAT and company total. |
| `PlatformInvoiceControl` | invoice/reference, taxable amount, VAT, total due, currency, platform totals and reconciliation status. |
| `PlatformResolutionIssue` | issue code, severity, source location, proposed match, resolution, maker/approver and timestamps. |

Every fact keeps `ImportBatchId`, sheet/row/cell lineage, original platform ID, original rider and paid rider.

## Exact adapters

### Hunger/FTR adapter

- Treat each known tab as a separate contract; do not run one generic header scorer across all tabs.
- Map completed/accepted orders, base payment, distance, bonuses, declined/rejection/no-show/missed-day metrics, rider balance, bank details and company totals independently.
- Reconcile WR/RLVL/FTR cost totals without adding the same rider/month more than once.
- Salary policy is configurable and effective-dated. Initial user rule:
  - below 500 accepted orders: `accepted orders * SAR 3`;
  - 500 or more: `SAR 2,000 + (accepted orders - 500) * SAR 6`;
  - distance is company billing evidence and does not pay the rider unless a later rule explicitly enables it;
  - bonus rules are separately configurable, e.g. >=500 gives SAR 300 and >=600 gives SAR 500, with the highest eligible rule unless policy says cumulative.
- Rejection, no-show, Friday/weekend and missed-hour signals remain source facts. A versioned penalty policy decides `information only`, `review`, or `automatic deduction`; no parser may decide the deduction.

### Keeta adapters

- Separate pay-per-order, segment and freelancer schemas/endpoints/parser versions.
- Preserve `صالح/غير صالح`, reason, connection days/hours, peak hours, orders, distance, pricing, incentives, discounts, compensation, registration fee, adjustments, TGA and total due.
- Preserve detail-level transaction type, work/ticket/violation IDs, fee/violation type, punishment method and face verification.
- Validity is an explicit payroll gate. `غير صالح`, missing validity or contradictory tabs create a blocking issue; accountant override requires reason and independent approval.
- Company billing amount and rider payout are separate calculations. Distance, TGA and platform fees do not automatically become rider pay.

### Amazon adapter

- Unpivot daily date columns into one `PlatformDailyActivity` row per rider/date.
- Preserve platform ID, iqama, DSP, store, grand total, working/off days, prorated amount, incentive shipments/amount, EID and EID OT.
- Reconcile daily sum to grand total and payroll/company amount columns.
- Store schedule workbook facts separately from payment-review facts; join through platform worker/effective date, not sheet position.
- Payout remains an effective-dated rule (fixed salary, per-order, prorated, incentives/OT) because the workbook alone does not establish the employment contract.

## Reconciliation gates

An import cannot be approved until all applicable gates pass:

1. Hash/source-reference duplicate check.
2. Schema fingerprint and parser-version match.
3. Required sheet/header/control-total validation.
4. Rider identity and effective-date mapping.
5. Substitution allocation by service date.
6. Cross-tab duplicate and control-total reconciliation.
7. Platform invoice total = normalized bill total within configured rounding tolerance.
8. Payroll source facts aggregate once per rider/platform/period.
9. Validity and policy exceptions resolved by a maker and approved by a different user.
10. Source period is open and legal entity/currency/tax registration are explicit.

## Accounting outputs

The import batch creates no journal on upload. Approved downstream documents create journals atomically:

- Platform customer invoice/accrual: Dr platform AR; Cr delivery revenue; Cr output VAT where legally applicable.
- Rider employee payroll: Dr gross wages/allowances/bonus expense; Cr payroll liabilities and rider receivable control accounts for deductions; Cr net salary payable.
- Contractor rider bill: Dr contractor delivery cost; Cr contractor payable, with withholding/tax treatment configured separately.
- Company receipt: Dr bank; Cr platform AR.
- Salary payment: Dr salary payable; Cr bank/cash.
- Rejection/violation recovery: credit the exact rider receivable/control account, not salary expense.

All journal lines carry legal entity, branch, platform, rider/employee, housing, vehicle, project and cost-center dimensions where applicable.

## Required APIs

- `POST /api/accounting/platform-imports/{platform}/{template}/preview`
- `POST /api/accounting/platform-imports/{batchId}/commit`
- `GET /api/accounting/platform-imports/{batchId}` and `/issues`
- `POST /api/accounting/platform-imports/{batchId}/issues/{issueId}/resolve`
- `POST /api/accounting/platform-imports/{batchId}/reconcile`
- `POST /api/accounting/platform-imports/{batchId}/approve`
- `POST /api/accounting/platform-imports/{batchId}/supersede`
- `GET /api/accounting/platform-imports/{batchId}/source-file`
- `POST /api/payroll/runs/{runId}/build-from-platform-activity`
- `POST /api/ar/platform-billing-runs/{runId}/build-from-imports`

Preview is read-only. Commit is idempotent. Approve requires a different actor from uploader/resolver and posts only through the shared posting engine.

## Golden-file acceptance tests

- The four exact historical workbooks parse with expected sheet names, dimensions, row counts and hashes.
- Every non-empty source cell is retained with lineage.
- Amazon daily columns unpivot and reconcile to grand total.
- Keeta validity, reason, distance, fee, violation, punishment and face fields survive round-trip.
- Hunger/FTR duplicated summary tabs do not duplicate revenue or payroll.
- Re-uploading identical bytes returns the existing batch and creates no journal.
- Monthly Hunger rule tests cover 0, 499, 500, 501 and 600 orders plus bonus precedence.
- Keeta invalid/missing validity blocks payroll; override requires two users.
- Substitution tests cover before/start/inside/end/after dates and monthly ambiguous overlap.
- Rejected/no-show/missed-day metrics remain information-only until an approved policy applies.
- Raw totals, normalized totals, AR, payroll, journal, trial balance and bank settlement reconcile.
