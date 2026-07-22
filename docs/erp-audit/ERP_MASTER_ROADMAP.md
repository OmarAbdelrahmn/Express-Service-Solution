# Sol Ultra ERP Master Roadmap

## Delivery rules for every phase

- Implement through the existing thin-controller / application-service / `Result<T>` / EF Core pattern, while splitting services by bounded context.
- Every schema change uses expand-migrate-contract, production-size migration rehearsal, backup, rollback/forward-correction plan and data reconciliation.
- Every financial workflow includes backend permission/object-scope tests, maker-checker, immutable audit, idempotency/concurrency tests and ledger/subledger reconciliation.
- Frontend pages are required for workflow completion even though the frontend repository is not currently in scope.
- No phase is production-enabled until its acceptance evidence is signed by engineering and accounting; tax/payroll legal points also require qualified Saudi review.

## Phase 0 — Financial containment and security

- **Objective/scope:** freeze unsafe accounting posting; preserve source files; rotate secrets; secure Hangfire/debug/import/document APIs; publish/freeze the accounting branch; dependency upgrade; build/CI baseline; endpoint-permission inventory. **Out:** new ERP features.
- **Changes:** secret provider, fallback authorization policy, explicit actor context, private attachment storage, import fingerprint table, audit baseline and CI/security/dependency checks. No migration of financial balances.
- **Workflow/permissions:** deny anonymous sensitive actions; separate temporary accounting preparer/reviewer/payment roles; disable self-approval and bank confirmation until Phase 1 controls.
- **Tests/acceptance:** clean secret scan, patched dependency, all sensitive endpoint auth tests, isolated solution build, existing 20 tests, duplicate-file quarantine and restore drill.
- **Migration/rollback/risks:** rotate/invalidate credentials with maintenance window; gateway rules can be rolled back individually. Main risk is interrupting existing operations, mitigated by route inventory and staged deny logs.
- **Complexity/order:** High; first and blocking.

## Phase 1 — Organization, ledger and posting foundation

- **Objective/scope:** create tenant/legal entity/branch/platform-account semantics, company-specific charts, fiscal calendars, currencies, dimensions, document state, posting engine, journals, periods and append-only audit. **Out:** AR/AP/payroll automation.
- **DB/entities:** `Tenant`, `LegalEntity`, `Branch`, `PlatformAccount`, `FiscalYear/Period`, `Currency/ExchangeRate`, `Account/PostingProfile`, `FinancialDocument`, `PostingBatch`, immutable `JournalEntry/Line`, dimensions, approval/audit/outbox.
- **Services/APIs/UI:** organization settings; chart/routing; manual journal prepare/approve/post/reverse; period close/reopen; trial balance/GL; approval inbox; audit viewer.
- **Accounting/controls:** database-balanced/idempotent posting, legal-entity sequences, immutable posted data, maker-checker, locked periods and explicit actor/entity context.
- **Tests/acceptance:** concurrent duplicate posting, invalid line constraints, reversal, multi-entity isolation, currency rounding, close/backdate and trial balance zero. Migrate only test/opening journal data.
- **Rollback/risks:** legacy stays read-only/unintegrated; disable posting feature flag and reverse open test batches. Complexity Very High; depends Phase 0.

## Phase 2 — Immutable platform import and reconciliation hub

- **Objective/scope:** exact Hunger/FTR, Keeta pay-per-order/segment/freelancer and Amazon payment/schedule adapters; raw evidence, identity/substitution resolution, control totals and approval. **Out:** automatic AR/payroll posting.
- **DB/entities:** import batch/sheet/raw row/cell, SHA-256/source reference, parser/schema version, normalized activities/transactions/summaries, platform worker accounts, resolution issues and reconciliation results.
- **Services/APIs/UI/jobs:** preview/commit/reconcile/resolve/approve/supersede/download; exception dashboard and durable parser jobs/outbox.
- **Controls:** re-upload idempotency, cross-tab authoritative metric rules, dated substitution, monthly ambiguous block, original-file retention, maker-checker.
- **Tests/acceptance:** four golden workbooks, 100% nonempty-cell lineage, exact source totals, duplicate bytes, corrected file, large 162k-row Keeta load, restart/resume and no journal on upload.
- **Migration/rollback/risks:** import historical files as non-posting evidence; legacy import endpoints remain read-only until cutover. Complexity Very High; depends Phase 1 identity/audit.

## Phase 3 — Sales, platform billing and accounts receivable

- **Objective/scope:** customers/platform contracts, rate cards, billing runs, invoice/credit note, receipt allocation, advances, aging/statements and revenue/VAT posting. **Out:** general POS/subscriptions unless prioritized.
- **DB/entities:** customer/platform account, contract/rate-card versions, billing facts, invoice/lines/tax, credit note, AR allocation, receipt/unapplied receipt, payment terms and numbering.
- **Services/APIs/UI:** billing preview/approval/post, invoice/credit, receipt/allocation/refund, aging/statement and billing reconciliation.
- **Accounting:** Dr AR / Cr revenue / Cr output VAT; receipt Dr bank / Cr AR; unapplied cash to customer advances; reversal/credit notes only.
- **Tests/acceptance:** rate-card edge dates, duplicate activity, partial/overpayment, VAT, credit note, closed period and invoice-to-GL reconciliation.
- **Migration/rollback/risks:** shadow invoices beside platform statements for two periods; feature-flag posting. Complexity High; depends Phases 1-2.

## Phase 4 — Rider payroll and contractor settlements

- **Objective/scope:** employment/contract classification, versioned payout policies, payroll runs, Hunger/Keeta/Amazon rules, components, loans/advances/deductions, payslips, final settlement and bank/cash payable creation. **Out:** full HR administration/GOSI automation until Phase 7 validation.
- **DB/entities:** worker contract, pay policy, eligibility/penalty policy, payroll run/result/line, receivable allocations, loan/advance schedules, payslip and settlement.
- **Services/APIs/UI:** payroll preview/exceptions, approve/post/reverse, component/rule approval, rider statement, loan allocation and payslip.
- **Accounting:** gross expense/allowances/bonus, deduction control accounts, net payable; contractor AP treatment separated; no net-only expense.
- **Tests/acceptance:** Hunger 0/499/500/501/600, Keeta validity, Amazon policy, substitutions, deductions/loan clearing, negative net, reversal and payroll-control reconciliation.
- **Migration/rollback/risks:** current salary records import as historical/nonposting or opening balances after reconciliation. Complexity Very High; depends Phases 1-2 and legal review.

## Phase 5 — Purchasing, AP and supplier control

- **Objective/scope:** requisition, RFQ, PO, receipt, supplier invoice/credit, three-way match, advances/payments, recurring bills, duplicate invoice control and aging. **Out:** inventory valuation until Phase 6.
- **DB/entities:** supplier legal/tax details, requisition/RFQ/quote/PO, goods receipt, purchase invoice/lines/tax, match exceptions, payable/allocation/payment.
- **Services/APIs/UI:** approval matrices, receive/match/post/pay/reverse, supplier statement/aging and duplicate detection.
- **Accounting:** service/expense/input VAT/inventory clearing vs AP; payment and advances; landed-cost clearing prepared.
- **Tests/acceptance:** quantity/price/tax tolerances, duplicate supplier invoice, partial receipt/invoice/payment, credit note, closed period and AP control reconciliation.
- **Migration/rollback/risks:** migrate operational suppliers/bills as legacy evidence; opening AP journals separately. Complexity High; depends Phase 1.

## Phase 6 — Inventory and warehouse accounting

- **Objective/scope:** catalog/UOM, warehouses/locations/bins, immutable movement ledger, reservations, counts, transfers, returns, weighted-average then optional FIFO, landed cost and COGS. **Out:** manufacturing beyond simple kits.
- **DB/entities:** item/UOM/barcode, warehouse/location/bin, inventory transaction/line, reservation, count, valuation layer, costing run and reconciliation.
- **Services/APIs/UI/jobs:** receive/issue/transfer/count/adjust/reserve, stock card/aging/valuation, costing recalculation job and exception queue.
- **Accounting:** inventory/GRNI/AP, COGS, variance, write-off and transfer dimensions; atomic stock+GL posting.
- **Tests/acceptance:** concurrent oversell, negative stock, backdate, returns, transfer cost, landed cost and inventory-to-control-account reconciliation.
- **Migration/rollback/risks:** physical count and valuation opening batch; keep legacy quantities frozen/read-only after cutover. Complexity Very High; depends Phases 1 and 5.

## Phase 7 — Expenses, HR and statutory payroll completion

- **Objective/scope:** expense requests/claims, petty cash/advances, employee org/contracts/attendance/leave, salary structures, GOSI/EOS/vacation accrual and confidentiality. **Out:** talent/recruiting.
- **DB/entities:** expense policy/claim/receipt/allocation, petty fund/custodian, employee contract/position/department, attendance result, leave/accrual, GOSI/EOS configuration.
- **Services/APIs/UI:** employee self-service, expense/advance settlement, attendance-to-payroll validation, accrual runs and confidential payroll reports.
- **Accounting:** expense/input VAT/payable; employee advances; salary/GOSI/EOS/vacation accrual and settlement.
- **Tests/acceptance:** self-approval, limits, duplicate receipt, advance settlement, leave/EOS scenarios, confidentiality and control-account reconciliation.
- **Migration/rollback/risks:** effective-dated contracts/leave balances with HR sign-off. Complexity Very High; depends Phases 1 and 4 plus legal review.

## Phase 8 — Treasury, bank/cash payments and reconciliation

- **Objective/scope:** bank/cash accounts, payment proposals, dual release, bank file/acknowledgment, statements, matching, charges/interest/transfers, reconciliation and cash positions. **Out:** advanced forecasting initially.
- **DB/entities:** treasury account, payment proposal/batch/release, bank statement/line, match, reconciliation, transfer, beneficiary and approval.
- **Services/APIs/UI/jobs:** payment prepare/release/confirm, statement import, matching suggestions, unmatched queue, reconciliation close and cash position.
- **Accounting:** clear AP/payroll/AR, bank fees/interest, transfers and reconciliation adjustments.
- **Tests/acceptance:** IBAN/file formats, maker/releaser separation, partial rejection, duplicate statement, matching, bounced/reversed payment and bank-to-GL zero difference.
- **Migration/rollback/risks:** opening bank reconciliation per account; parallel bank comparison. Complexity High; depends AR/AP/payroll.

## Phase 9 — Fixed assets

- **Objective/scope:** categories/register, acquisition/capitalization, components, books/methods, depreciation, impairment, transfer, disposal and custody. **Out:** complex lease accounting unless required.
- **DB/entities:** asset/category/component/book/schedule/run/transaction/location/custodian.
- **Services/APIs/UI/jobs:** capitalize, depreciation preview/post/reverse, transfer, impair/revalue/dispose and reports.
- **Accounting:** asset/AP or CIP, depreciation/accumulated depreciation, impairment and disposal gain/loss.
- **Tests/acceptance:** partial month, change estimate, component, disposal, reversal and asset-register-to-GL reconciliation.
- **Migration/rollback/risks:** asset count and opening cost/accumulated depreciation signed by accounting. Complexity Medium/High; depends Phases 1 and 5.

## Phase 10 — VAT and ZATCA e-invoicing

- **Objective/scope:** tax codes/rates/groups, input/output tax ledger, VAT return/reconciliation/lock and ZATCA invoice generation, onboarding, clearance/reporting, rejection/retry/archive. **Out:** compliance claim before external validation.
- **DB/entities:** tax code/rate/registration/transaction/return, e-invoice UUID/hash/stamp/status, solution unit/credential references and archived exchanges.
- **Services/APIs/UI/jobs:** tax configuration/return, invoice XML/QR, submit/status/retry and exception dashboard.
- **Accounting:** tax routing and adjustments/credit notes/reverse charge; return reconciles to control accounts.
- **Tests/acceptance:** official validation samples, Arabic/English fields, XML/hash/QR, retry/idempotency, credit note, tax lock and external adviser sign-off.
- **Migration/rollback/risks:** sandbox/onboarding then phased branches; never delete issued invoices. Complexity Very High; depends Phases 1, 3 and 5.

## Phase 11 — Budgets, projects and profitability

- **Objective/scope:** projects/jobs/contracts, cost/profit centers, budgets/forecasts/versions, approvals, commitments, allocations and budget-vs-actual. **Out:** advanced percentage-of-completion until needed.
- **DB/entities:** project/contract, budget/version/line/transfer, commitment, allocation rule and forecast scenario.
- **Services/APIs/UI/jobs:** budget workflow, commitment checks, allocation run, project cash/profit and variance reports.
- **Accounting:** journal dimensions and allocation journals; WIP/POC only after policy validation.
- **Tests/acceptance:** budget lock/transfer, PO commitments, payroll/inventory/project allocation and ledger-derived variance.
- **Migration/rollback/risks:** map legacy company/housing/vehicle dimensions carefully. Complexity High; depends core subledgers.

## Phase 12 — Enterprise reporting, consolidation and advanced integration

- **Objective/scope:** consolidated statements/intercompany/eliminations, semantic reporting, BI exports, APIs/webhooks, workflow automation, alerts, DR/scale hardening. **Out:** optional industry modules not justified by demand.
- **DB/services/UI/jobs:** consolidation groups/mappings/eliminations, read models/data warehouse, integration credentials/subscriptions, workflow definitions and monitoring.
- **Accounting:** entity trial-balance translation, due-to/from reconciliation, eliminations and consolidated close.
- **Tests/acceptance:** entity=consolidated reconciliation, FX translation, intercompany elimination, report performance, webhook signatures/replay and full DR exercise.
- **Migration/rollback/risks:** read-only reporting cutover first; retain entity ledgers as truth. Complexity Very High; depends stable Phases 1-11.

## Go-live sequence

1. Security containment.
2. Shadow ledger/import with no production posting.
3. Two fully reconciled closed periods for platform AR and rider payroll.
4. Controlled treasury release and bank reconciliation.
5. Purchasing/inventory cutover after count/AP reconciliation.
6. VAT/ZATCA only after external validation.
7. Remaining modules by business priority.
