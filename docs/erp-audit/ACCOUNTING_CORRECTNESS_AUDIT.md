# Accounting Correctness Audit

## Verdict

The accounting branch is a useful prototype, not a reliable ledger. The 20 tests demonstrate selected happy-path workflow behavior, but they do not cover the confirmed P0 cases below. No financial posting should be enabled until A-001 through A-010 are corrected and reconciled in shadow periods.

## Findings

### A-001 — Duplicate source bills can be posted more than once

- **Severity:** P0 / confirmed.
- **Affected:** `AccountingImportService.ImportCompanyBillAsync`, `CompanyBillImport`, `CompanyBillImportConfigration`, `CompanyReceivable`, `JournalEntry`.
- **Evidence:** import records filename but no content hash, source invoice ID or idempotency key; the index on period/company/template is non-unique. Each upload receives a new import ID and therefore bypasses source-based journal duplicate checks.
- **Financial impact:** duplicate AR, revenue, VAT and rider earnings.
- **Failure scenario:** the accountant retries the same May Hunger file after a timeout; both imports are approved and both post.
- **Correction:** retain original bytes, SHA-256 and provider reference; unique legal-entity/platform/hash and provider-reference constraints; idempotent preview/commit/approve; supersede instead of reimport.
- **Required tests:** identical bytes, renamed identical file, concurrent retry, same provider invoice with changed bytes, legitimate corrected/superseding bill.

### A-002 — Multi-tab workbooks can double-count riders and invoice totals

- **Severity:** P0 / confirmed.
- **Affected:** `ParseWorksheet`, `AddRiderSummary`, `CalculateImportTotals`; `CompanyBillRiderSummaries`, `CompanyReceivables`.
- **Evidence:** every worksheet is parsed with the same summary heuristic and totals sum every resulting summary. The recovered Keeta and FTR workbooks contain partner, rider, cost and detail tabs with overlapping totals.
- **Financial impact:** duplicated revenue, VAT, receivables, payroll and bonuses.
- **Failure scenario:** the same rider total appears in RLVL and FTR cost sheets; both become salary sources and invoice total lines.
- **Correction:** exact template/tab contracts, canonical source keys, cross-tab reconciliation, one authoritative source per metric, invoice control-total gate.
- **Required tests:** all four golden workbooks, duplicate rider across tabs, detail+summary agreement/disagreement, hidden/renamed sheet and repeated headers.

### A-003 — Salary generation consumes pending and reversed imports

- **Severity:** P0 / confirmed.
- **Affected:** `AccountingSalaryService.EnsureEarningsFromSummariesAsync`, `GenerateMonthlySalariesAsync`; `RiderEarnings`.
- **Evidence:** the query filters year/month/company and `PaidRiderId`; it does not require `CompanyBillImport.Status == Posted`. The later earnings query excludes only cancelled/reversed earnings, not pending-review earnings.
- **Financial impact:** salary can be created from unapproved or reversed company data.
- **Failure scenario:** an unresolved import is uploaded; a rider mapping exists; salary generation creates an earning before import approval.
- **Correction:** earnings are created only by atomic import posting or from posted facts; reversal reverses/deactivates dependent earnings; database source-state constraint or posting ledger.
- **Required tests:** pending, unresolved, posted, reversed and superseded imports; salary regeneration after reversal.

### A-004 — Monthly salary formulas are applied per summary row, not per rider/platform/period

- **Severity:** P0 / confirmed.
- **Affected:** `EnsureEarningsFromSummariesAsync`, `CalculateSalaryAmount`, `RiderEarning`.
- **Evidence:** one earning is calculated for each `CompanyBillRiderSummary`. Hunger's 500-order rule is a monthly aggregate rule, but summaries may be split by tab/import or duplicated.
- **Financial impact:** large under/overpayment around thresholds and duplicated base amounts.
- **Failure scenario:** 300 orders in one source row and 250 in another are each paid below-target instead of one 550-order salary; duplicated tabs can pay the base twice.
- **Correction:** aggregate deduplicated facts by legal entity/platform/paid rider/contract/period before applying a single effective-dated rule; store calculation trace.
- **Required tests:** split rows, multiple files, substitution splits, exactly 499/500/501/600 and duplicate tabs.

### A-005 — Keeta validity is stored but not a payroll gate

- **Severity:** P0 / confirmed.
- **Affected:** `AddRiderSummary`, `EnsureEarningsFromSummariesAsync`, `GenerateMonthlySalariesAsync`.
- **Evidence:** `ValidityStatus`/`ValidityReason` are populated but never checked when creating or approving rider earnings/salaries.
- **Financial impact:** `غير صالح` riders may be paid automatically.
- **Failure scenario:** Keeta total due is parsed for an invalid rider and fallback salary uses that net amount.
- **Correction:** effective-dated platform payout policy with required validity; invalid/missing/contradictory states create blocking issues; two-person override with reason.
- **Required tests:** valid, invalid, missing, conflicting tabs, overridden and reversed override.

### A-006 — Payroll journal posts net expense and loses deduction control accounts

- **Severity:** P0 / confirmed.
- **Affected:** `ApproveSalaryAsync`, `RiderMonthlySalaryLine`, chart accounts.
- **Evidence:** approval posts Dr salary expense / Cr rider payable for `NetSalary` only. Loan, wallet, traffic, iqama, ticket and other deduction lines do not credit their receivable/control accounts.
- **Financial impact:** payroll expense and rider receivables are understated; deductions cannot reconcile.
- **Failure scenario:** gross SAR 2,000 less SAR 500 loan posts only SAR 1,500 expense/payable, leaving loan receivable unchanged.
- **Correction:** line-type account routing: debit gross earnings/allowances/bonuses; credit each deduction control/receivable; credit net payable; validate total debit=credit.
- **Required tests:** each built-in financial item, mixed deductions, negative net, waived item, partial recovery and reversal.

### A-007 — Loan and financial-item balances are not settled by payroll payment

- **Severity:** P0 / confirmed.
- **Affected:** salary generation/payment confirmation, `RiderLoan`, `RiderLoanInstallment`, `RiderFinancialItem`.
- **Evidence:** salary lines read installment unpaid amount; bank/cash confirmation updates salary paid amount only. No code updates installment `PaidAmount`, loan `RemainingAmount` or financial-item `RemainingAmount`.
- **Financial impact:** outstanding balances diverge and may be charged or reported again.
- **Failure scenario:** a May loan installment is withheld and salary paid, but rider profile continues to show the loan as unpaid.
- **Correction:** create allocation records linking salary deduction lines to source receivables; settle allocations atomically on salary posting/payment according to policy; reverse allocations on reversal.
- **Required tests:** full/partial deduction, insufficient salary, hold, mixed payment, payment rejection and reversal.

### A-008 — P&L and cost-center reports are not ledger-derived

- **Severity:** P0 / confirmed.
- **Affected:** `CompanyFinanceService.GetProfitLossAsync`, `GetCostCentersAsync`.
- **Evidence:** queries receivables, salaries, expenses and supplier payables directly; includes draft/reversed salary states and all expense/payable states. It uses net salary as expense and then adds deductions recovered again. Breakdown omits supplier expenses and deduction adjustments.
- **Financial impact:** reports do not reconcile with journals and can materially overstate profit.
- **Failure scenario:** SAR 2,000 gross less SAR 500 deduction is treated as SAR 1,500 expense plus SAR 500 recovered, effectively SAR 1,000 expense.
- **Correction:** all financial statements aggregate posted journal lines by account type and dimensions; operational reports reconcile to the ledger through source IDs.
- **Required tests:** trial balance=P&L/balance sheet, drafts/reversals, deductions, supplier expenses, cost-center allocation and date boundaries.

### A-009 — Input VAT on expenses is capitalized into expense

- **Severity:** P0 for VAT reporting / confirmed.
- **Affected:** `CreateExpenseAsync`, account seed, `CompanyExpense`.
- **Evidence:** expense posting debits `Amount + VatAmount` to expense and has only `VAT Payable`; no recoverable input-VAT account/routing.
- **Financial impact:** expenses overstated, input VAT missing and VAT return/control reconciliation wrong.
- **Failure scenario:** SAR 1,000 expense + VAT posts SAR 1,150 expense instead of SAR 1,000 expense and SAR 150 input VAT.
- **Correction:** tax-code/rate/jurisdiction model; Dr expense net, Dr input VAT when deductible, Cr AP/cash gross; handle exempt, zero-rated, non-deductible and reverse charge.
- **Required tests:** inclusive/exclusive, recoverable/nonrecoverable, partial recovery, credit note and tax-period lock.

### A-010 — Journal invariants and posting idempotency are not database-enforced

- **Severity:** P0 / confirmed.
- **Affected:** `AddJournalEntryAsync`, `JournalEntryConfigration`, `JournalEntryLineConfigration`.
- **Evidence:** duplicate check is `AnyAsync` followed by insert with no unique source constraint; no check requires one non-negative debit or credit per line; no row version. Balance exists only in service code.
- **Financial impact:** concurrent requests or alternate writers can create duplicate/unbalanced/invalid lines.
- **Failure scenario:** two approval requests pass `AnyAsync` before either commits.
- **Correction:** unique posting key, filtered unique reversal key, per-line DB checks, posting stored invariant/domain service, serializable or optimistic concurrency and immutable posted rows.
- **Required tests:** concurrent approval/reversal, both-sides line, negative line, zero line, direct EF invalid insert and retry after timeout.

### A-011 — Legal entities, platforms and charts of accounts are conflated/global

- **Severity:** P0 for multi-company / confirmed.
- **Affected:** `Company`, `RiderDetails.CompanyId`, `AccountingAccount`, seeded account IDs, company hard-coded constants.
- **Evidence:** current companies represent Hunger/Keeta/Amazon operating channels; accounting accounts have no legal-entity ownership and posting uses global numeric account constants.
- **Financial impact:** no isolated books, tax registrations, currencies, fiscal calendars or consolidated/intercompany accounting.
- **Failure scenario:** two legal entities use the same customer/platform and all entries land in one global chart.
- **Correction:** `Tenant`, `LegalEntity`, `Branch`, `PlatformAccount/Customer`, legal-entity chart/account mapping and dimensions; migrate current Company to platform account.
- **Required tests:** cross-entity denial, shared platform, per-entity numbering, trial balances and consolidation elimination.

### A-012 — Fiscal-period model is monthly and global only

- **Severity:** P1 / confirmed.
- **Affected:** `AccountingPeriod`, `EnsureOpenPeriodAsync`, close/lock/reopen APIs.
- **Evidence:** unique `(Year, Month)` with no legal entity, fiscal year, tax period or close checklist. Reopening does not require reason/approval hierarchy.
- **Financial impact:** one entity blocks all others; no year close/retained earnings/tax lock; unauthorized backdating risk.
- **Correction:** legal-entity fiscal calendar, period types, close tasks, retained-earnings transfer, tax lock and controlled reopen workflow.
- **Required tests:** multiple calendars/entities, backdate, close dependencies, reopen/recose and year-end.

### A-013 — Inventory and purchasing remain disconnected from accounting

- **Severity:** P1 / confirmed.
- **Affected:** operational `SparePart`, `Bill`, `BillItem`, `Transfer`, `Return`, usages; modeled `PurchaseInvoice`/supplier payable.
- **Evidence:** stock quantities/average costs are mutated operationally; no immutable stock ledger, valuation layers or automatic inventory/AP/COGS journals. PurchaseInvoice/AP models lack complete workflow.
- **Financial impact:** inventory value, supplier balances and COGS can diverge.
- **Failure scenario:** return or backdated bill changes stock cost without a matching journal/revaluation.
- **Correction:** receiving/invoice matching, inventory transaction ledger, valuation engine, posting profiles and stock-account reconciliation.
- **Required tests:** receipt/invoice variance, return, transfer, backdate, negative-stock concurrency and valuation-to-GL.

### A-014 — Receipts without allocation are not modeled as unapplied cash

- **Severity:** P1 / confirmed.
- **Affected:** `CreateReceiptAsync`, `CompanyPaymentReceipt`, AR posting.
- **Evidence:** receipt may omit `CompanyReceivableId` yet credits company receivables directly; there is no unapplied receipt/customer credit allocation document.
- **Financial impact:** customer AR can become misleading/negative and aging cannot explain credits.
- **Correction:** receipt header + allocation lines; unallocated amount posts to customer advances/unapplied receipts; later allocation reclassifies.
- **Required tests:** partial, overpayment, multi-invoice, unapplied, refund, reversal and foreign-currency receipt.

### A-015 — Posted history and audit data are mutable application tables

- **Severity:** P1 / confirmed.
- **Affected:** all accounting entities, `AccountingAuditLog`, draft replacement in `GenerateMonthlySalariesAsync`.
- **Evidence:** public setters, no concurrency token/append-only protection; draft replacement physically removes salary and award rows; no audit trigger/outbox/version record.
- **Financial impact:** history and evidence can be altered or lost without a complete trace.
- **Correction:** immutable posted aggregates, version/supersede/reversal, append-only audit store, actor/correlation/request metadata and database restrictions.
- **Required tests:** update/delete posted row, draft replacement audit, tamper attempts, background job actor and audit retention.

## Missing accounting reports/flows

- balance sheet, cash-flow statement and changes in equity derived from the ledger;
- retained earnings/year close, accrual/deferral/recurring/adjusting journals;
- customer/supplier subledger aging and reconciliation;
- bank-statement import/matching and reconciliation adjustments;
- multi-currency transaction/base amounts and realized/unrealized FX;
- asset capitalization, depreciation, impairment and disposal posting;
- budget/commitment accounting and project profitability;
- VAT return/control reconciliation and ZATCA invoice lifecycle.
