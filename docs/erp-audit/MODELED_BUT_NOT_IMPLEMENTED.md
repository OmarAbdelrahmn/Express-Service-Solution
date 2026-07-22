# Modeled but Not Implemented

## Accounting candidate branch

| Capability/model | Classification | Evidence | Completion required |
|---|---|---|---|
| `FixedAsset`, `AssetDepreciationEntry` | Modeled only | Entities/configuration exist; no acquisition, depreciation-run, impairment, transfer, disposal or journal workflow/API/tests. | Asset books, schedules, run/post/reverse lifecycle, custody and reports. |
| `BankReconciliation` | Modeled only | Balance fields exist; no statement import, matching engine, reconciliation lines, adjustments or approval. | Statement/line models, matching, unmatched queue, close/reopen and GL reconciliation. |
| `BankAccount`, `TreasuryAccount`, `BankTransaction` | Partial model | Salary/receipt journal lines may reference bank account; no complete CRUD, transfer, charges, interest or reconciliation service. | Treasury bounded context and permissions. |
| `CheckCycle` | Modeled only | Entity/status enum exists; no issuance/receipt/deposit/bounce/cancel workflow. | State machine, custody, due dates and journals. |
| `PurchaseInvoice` | Modeled only | Header entity/status exists without invoice lines, receipt match, posting, payment or API. | Full purchasing/AP document aggregate and three-way match. |
| `SupplierPayable`, `SupplierPayment` | Mostly modeled | P&L sums payable amounts; no complete creation/allocation/approval/payment/reversal/aging workflow. | AP subledger and supplier reconciliation. |
| `CompanyProfitSnapshot` | Partial | Upsert test exists, but snapshot is based on non-ledger report logic. | Replace with ledger-derived materialization and close-period version. |
| `AccountingAttachment` | Unused | Entity exists; company import stores filename/raw cells but not original file binary/hash/storage link. | Content-addressed evidence storage and authorized download. |
| Chart of accounts | Partial | Seeded global accounts and journal references exist. No hierarchy management, legal-entity charts, control-account locking or routing API. | Account/routing CRUD with approval and historical versioning. |
| Manual/recurring/adjusting/reversing journals | Missing workflow | Journal entities and automatic helper exist; no preparer/approver manual journal API or recurring/accrual/deferral engine. | Full journal document state machine. |
| Fiscal year/year close | Missing | Monthly global period exists only. | Legal-entity fiscal calendar, close checklist and retained-earnings journal. |
| Balance sheet/cash flow/equity | Missing | No contracts/services/endpoints; only trial balance, GL and operational P&L. | Ledger-derived statements with comparative periods. |
| Multi-currency | Missing | No transaction/base currency, exchange-rate snapshots or FX posting. | Currency/rate books and realized/unrealized gain/loss. |
| Tax/VAT return | Modeled fields only | VAT amounts and one output VAT account exist; no tax codes, input VAT, return, reconciliation or locks. | Tax engine and ZATCA invoice lifecycle. |
| Payroll HR engine | Partial rider payout | Rider salaries/loans/payments exist; no contracts, salary structures, attendance integration policy, GOSI, leave/EOS accrual, bank standards or confidentiality partitions. | HR/payroll bounded context and statutory validation. |
| Approval workflow | Field-level only | Some statuses/actor fields exist, but no workflow definitions, steps, limits, delegation or maker-checker enforcement. | Generic approval engine with policy snapshots. |
| Audit reporting | Partial model | `AccountingAuditLog` written by selected operations; no query/export/integrity workflow and many config changes omit actors. | Append-only audit service/report and completeness tests. |
| Import parser versions | Missing | One generic heuristic parser handles all templates; no schema fingerprint or parser version. | Exact adapters, version registry and golden files. |

## Operational `master`

| Feature | Classification | Evidence |
|---|---|---|
| `ArchivedRiderShift` | Unreachable/unused | Entity file exists but no DbSet or service/controller caller was found. |
| Keeta shift/attendance/freelancer/monthly validity | Partial and fragmented | Multiple tables/imports exist with no one immutable batch, reconciliation or authoritative monthly outcome. |
| Amazon employee order | Partial/incorrect naming | Entity and full service/API exist, but Company 3/4 constants/messages/default conflict. |
| Spare inventory valuation | Modeled operationally, not accounting | Quantity/average prices and usage costs exist; no stock ledger or GL reconciliation. |
| Supplier bills/returns | Partial | Operational CRUD exists; AP/payment/tax/approval/accounting missing. |
| Employee status approval | Partial | Temp/status fields and limited logs exist; no reusable approval engine or segregation matrix. |
| Excel export in `MemberService` | Placeholder | Method explicitly returns 501 `NotImplemented`. |
| Soft-delete/audit request records | Unused/incomplete | Some contracts mention soft delete/audit, while services physically remove records and no complete audit store exists. |
| Frontend pages/menus | Missing from scope | No frontend project in repository; API-only workflows cannot be considered complete. |
| Permissions | Partially declared, often unenforced | Roles/attributes exist on some actions; numerous sensitive controllers/actions have none. |
| Tests | Missing on `master` | `Accounting.Tests` directory is empty and not in solution. No unit/integration/E2E coverage discovered. |

## Missing major modules

- quotations/orders/delivery/invoices/credit notes/receipts/customer aging;
- requisition/RFQ/PO/goods receipt/supplier invoice/three-way match/AP aging;
- warehouse locations/bins/reservations/counts/valuation/COGS;
- expenses/claims/petty cash/advances and policy approvals;
- complete HR/contracts/attendance/leave/payroll/GOSI/EOS;
- bank statement reconciliation and treasury forecasting;
- fixed assets and depreciation;
- ZATCA Phase 1/2 invoice generation/submission/archive;
- budgets, projects, commitments, consolidation and intercompany;
- CRM, subscriptions/POS/e-commerce and configurable workflows expected in a mature Daftra-style scope.

## Completion rule

A capability moves from “modeled” only when it has: persisted invariants, service workflow, API, backend permissions/object scope, UI, audit events, accounting entries where relevant, reports, migrations/rollback, automated tests and production monitoring.
