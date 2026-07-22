# Sol Ultra ERP Current State

## Audit basis

This audit separates three states so that work from previous Codex conversations is not lost or mistaken for production code.

| Baseline | Revision/state | Audit treatment |
|---|---|---|
| Checked-out product | `master` at `7d89bda` | Current deployed-style operational baseline; it has no accounting bounded context. |
| Accounting candidate | local `codex/accounting-module-20260624` at `ac44e61` | Target candidate from previous chats. It contains the accounting entities, services, APIs, migrations and tests and is six commits ahead of its remote branch. |
| Live working tree | Uncommitted role, API-versioning, rate-limit and settings work | User-owned concurrent work. It was not treated as a stable architecture baseline and was not modified by this audit. |

The isolated accounting-branch build succeeds with 11 warnings. Its test project passes 20/20 tests. The checked-out live tree failed during the audit on an unresolved `Asp.Versioning` namespace introduced by concurrent uncommitted work; this is not attributed to `master` HEAD.

## Executive maturity assessment

| Area | Level | Conclusion |
|---|---|---|
| Operational rider management | Intermediate | Broad rider, employee, housing, vehicle, shift, order, spare-part, petrol and reporting coverage, but inconsistent authorization and audit controls. |
| Delivery-platform imports | Intermediate but fragmented | HungerStation, Keeta and Amazon formats are parsed by separate operational services; source identity and import controls are inconsistent. |
| Accounting on `master` | None | No chart of accounts, journals, posting engine, fiscal periods, receivables, payroll ledger or financial statements. |
| Accounting candidate branch | Prototype / unsafe for production | Substantial models and workflows exist, but confirmed P0 posting, import, payroll, VAT, reporting and segregation-of-duties defects remain. |
| Full Daftra-style ERP | Early foundation | Sales/AR, purchasing/AP, complete inventory accounting, treasury, assets, tax, budgets, consolidation, workflow and frontend are incomplete or missing. |
| Financial production readiness | Not safe | Neither `master` nor the accounting branch is safe to use as the financial source of truth. |

## Repository map

- `Express Service`: ASP.NET Core host, 40 controllers, Swagger, static files, Hangfire and health endpoint.
- `Application`: 100+ service files and about 52,000 lines; controller-service-`Result<T>` pattern, ClosedXML imports, reports and background jobs.
- `Domain`: one EF Core/Identity `ApplicationDbcontext`, 43 operational entity files and 40+ migrations on `master`.
- `Accounting.Tests`: empty and absent from the `master` solution; the accounting branch adds one project with 20 workflow tests.
- Frontend: no frontend project, pages, menus or end-to-end UI tests are present in this repository.
- Documentation: effectively absent on `master`; the accounting branch adds an Arabic accounting guide and PDF.

## Current operational modules

| Module | State | Evidence and limitations |
|---|---|---|
| Identity and roles | Partial | ASP.NET Identity/JWT and `Master/Admin/Member` roles exist. Many controllers/actions lack backend authorization; the host exposes an unauthenticated Hangfire dashboard. |
| Employees and riders | Broad operational support | `Employees`, `RiderDetails`, documents, status logs, working-id history, substitutions and deleted/escaped employee flows exist. No complete HR contracts, leave, payroll, GOSI, accrual or final-settlement accounting on `master`. |
| Companies | Mis-modeled for ERP | `Company` is a platform/operator label used by rider records and hard-coded IDs. It is not a legal entity/tenant/branch model and has no tax registration, base currency or fiscal calendar. |
| Housing and vehicles | Operational | Assignment, status, permission, petrol and reporting exist. No asset register, depreciation or controlled capitalization. |
| Shifts and orders | Operational | `RiderShift`, `TransporterShift`, `EmployeeOrder`, substitutions and platform validations exist. Records remain mutable/deletable and are not accounting source documents. |
| Spare parts and accessories | Partial inventory | Items, suppliers, bills, transfers, returns and usage exist. There are no bins, reservations, immutable stock ledger, valuation layers, counts or inventory-control-account reconciliation. |
| Supplier bills | Operational purchasing only | `Bill`/`BillItem` capture spare-part acquisition and average cost. There is no AP subledger, three-way match, payment allocation, tax posting or supplier aging. |
| Wallet | Operational | A daily rider amount imported by working ID. Previous requirements explicitly reject it as the financial source of truth. |
| Reports and AI | Broad operational reporting | Many shift/rider/housing reports and an AI dispatcher exist. Reports are calculated from mutable operational tables, not a posted ledger. |
| Background jobs | Partial | Daily/absence/monthly reports, vehicle renewal, email warm-up and in-process import jobs exist. Actor/tenant/correlation context and durable outbox semantics are absent. |

## Platform data currently present

### HungerStation / FTR

- `RiderShift` stores working ID, date, accepted/rejected/real-rejected orders, stacked deliveries, float working hours, company and free-text status.
- `HungerDisability` imports rider ID, working days and completed deliveries for a caller-supplied date.
- `HungerReportService` hard-codes company ID `1`, 26 working days, 450 orders and eight minimum hours; this conflicts with the later accounting requirement of a configurable 500-order salary target.
- Bulk rider-shift imports recognize `driverId`, `reqDate`, `dailyRec` and housing aliases, but default working hours to 9 and derive pass/fail from 14 orders.
- The prior workbook analysis identified WR summary, RLVL totals and FTR cost tabs containing completed orders, base pay, rejection/declined penalties, no-show, missed days/weekends/end-of-month, distance pay, rider balance, IBAN, bank, hours and days.

### Keeta

- `KeetaDriverShift` and `KeetaShiftSlot` store daily driver report facts, connection minutes, tasks and up to three qualified on-shift slots.
- Attendance import reads driver ID, status and Arabic timestamp and summarizes order activity, but the source import itself is not represented as a durable audited batch.
- Freelancer and monthly-validity tables exist separately.
- Prior workbooks contain partner summary, rider summary and transaction-detail tabs. The segment file includes `صالح/غير صالح`, reasons, connection days/hours, peak hours, delivered orders, distance, order and distance pricing, incentives, discounts, compensation, registration fee, adjustments, TGA and total due. Detail rows include fee type, ticket/violation IDs and punishment/face-verification data.

### Amazon

- `TransporterShift` imports a wide schedule: associate name, transporter ID and one column per date, with one or two textual shift blocks per cell.
- `EmployeeOrder` records operational on/off-order events. Code comments and error codes inconsistently call the same population Company 3 and Company 4; the entity default is 4 while the service queries/inserts 3.
- Prior workbook `EPSR- ANOW Monthly Payment Review for MAY'26.xlsx` contains one rider row with platform ID, iqama, name, DSP/store, daily May order columns, grand total, working days, days off, prorated amount, incentive shipments/amount, EID and EID overtime.

## Accounting candidate branch

The branch adds a meaningful foundation:

- accounting periods, accounts, journal entries/lines and audit logs;
- raw import batch/sheet/row/cell retention plus rider summaries, transaction lines, daily metrics and resolution issues;
- rider earnings, configurable salary/bonus rules, financial items, loans, monthly salaries, bank/cash batches and final settlements;
- receivables, receipts, expenses, supplier payables/payments, cost centers, profit snapshots, banks/treasury, assets, checks and purchase invoices;
- accounting, company-finance, reports and member-cash controllers;
- seven accounting-related migrations and 20 workflow tests.

It is not complete ERP functionality merely because these entities exist. Fixed assets, depreciation, supplier AP, purchase invoices, bank reconciliation, checks and several reports have little or no operational service/API workflow. Confirmed accounting defects are documented in `ACCOUNTING_CORRECTNESS_AUDIT.md`.

## Existing accounting flow on the candidate branch

1. Upload creates a pending import and saves every used worksheet cell.
2. Generic header heuristics create normalized rider summaries, transaction lines and daily metrics.
3. Accountant resolves riders/substitutions and approves the import.
4. Approval creates a company receivable and posts AR/revenue/output-VAT journal lines.
5. Salary generation converts summaries to rider earnings, adds bonuses/financial items/loan installments and creates draft salaries.
6. Salary approval posts salary expense and rider payable.
7. Bank confirmation or housing-manager cash delivery clears rider payable against cash/bank.
8. Company receipts clear company receivables; expenses post expense against cash/bank.

Steps 2, 4, 5, 6 and financial reporting contain P0 correctness gaps.

## Confirmed production blockers

- Accounting code is not merged into `master`.
- The same source workbook can be uploaded and posted more than once; no file hash/idempotency key exists.
- All summary-like rows across all workbook tabs feed totals and salary; multi-tab workbooks can double count.
- Salary generation does not require source imports to be posted and can consume pending/reversed data.
- Keeta validity is stored but not applied to payout eligibility.
- Salary deductions are netted from expense instead of clearing loan/violation/advance receivables.
- Loan/installment balances are not reduced when deductions are paid through salary.
- P&L/cost-center reports query mutable operational tables rather than journal lines and include invalid statuses.
- Accountants can prepare, approve, reverse, pay and reopen periods under one role.
- Secrets are committed in `Express Service/appsettings.json`; rotate them and remove them from history.
- Numerous operational endpoints and the Hangfire dashboard are not protected adequately.
- The accounting branch resolves `Microsoft.OpenApi` 2.4.1, which the build reports under high-severity advisory `GHSA-v5pm-xwqc-g5wc`.

## External benchmark

The comparison uses Daftra's official catalog: sales, accounting, inventory, purchases, HR, operations, CRM and reports, including automatic journals and account routing. Sources: [Daftra features](https://www.daftra.com/en/features/all_features), [Daftra knowledge base](https://docs.daftra.com/en/), and [account routing guide](https://docs.daftra.com/en/user_manual/account-routing-guide/).

Saudi readiness is assessed against current [ZATCA e-invoicing guidance](https://zatca.gov.sa/en/E-Invoicing/Introduction/Guidelines/Pages/default.aspx), [roll-out phases](https://zatca.gov.sa/en/E-Invoicing/Introduction/Pages/Roll-out-phases.aspx?lang=en), and [VAT implementing regulations](https://zatca.gov.sa/en/RulesRegulations/Taxes/Pages/VATImplementingRegulations.aspx). Presence of fields is not a compliance claim.

## Audit limitations

- No live production database, deployed configuration, frontend repository, bank file specification, contracts/rate cards or current original workbooks were available in this task.
- Workbook structures are based on the earlier Codex workbook inspection and the current parsers/entities; the exact source files were no longer present locally.
- Static inspection cannot prove runtime authorization, migration safety against production data or legal/tax compliance.
- Findings distinguish confirmed code facts from strong accounting inferences requiring accountant/legal validation.
