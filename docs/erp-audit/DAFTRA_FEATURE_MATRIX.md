# Daftra-Style Feature Matrix

Benchmark: [Daftra official feature catalog](https://www.daftra.com/en/features/all_features), [official product overview](https://www.daftra.com/en/), and [knowledge-base module index](https://docs.daftra.com/en/). “Daftra-style” is a scope/workflow benchmark, not a claim of identical implementation.

Status legend: **Complete**, **Mostly complete**, **Partial**, **Modeled**, **Missing**, **Incorrect**, **Unsafe**, **Accounting redesign**, **Technical redesign**.

## Accounting and finance

| Capability | `master` | Accounting branch | Priority | Evidence/decision |
|---|---|---|---|---|
| Chart of accounts | Missing | Partial | P0 | Global seeded accounts; no legal-entity charts/routing lifecycle. |
| Double-entry journal | Missing | Unsafe | P0 | Balanced service helper exists; no DB line/source invariants or maker-checker. |
| General ledger/trial balance | Missing | Partial | P0 | Ledger reports exist and use journal lines; source postings are incomplete/wrong. |
| P&L | Missing | Incorrect | P0 | Operational-table calculation, invalid statuses and deduction double counting. |
| Balance sheet/cash flow/equity | Missing | Missing | P1 | No services/APIs. |
| Fiscal periods | Missing | Partial | P0 | Global monthly close only; no entity/fiscal-year/tax close. |
| Manual/recurring/adjusting journals | Missing | Missing | P1 | Automatic journal helper only. |
| Cost centers | Missing | Partial/incorrect | P1 | Models/report exist; report is not ledger-derived and allocation is incomplete. |
| Receivables/receipts | Missing | Partial | P0 | Company AR/receipt exists; no customer subledger, allocations/aging/advances. |
| Payables/supplier payments | Missing | Modeled | P1 | Entities exist; workflow/API/aging/posting incomplete. |
| Expenses | Operational costs only | Partial/incorrect VAT | P0 | Expense API exists; input VAT/control routing wrong. |
| Treasury/bank reconciliation | Missing | Modeled | P1 | Accounts/reconciliation entity only. |
| Fixed assets | Missing | Modeled | P2 | Entities only. |
| Multi-currency/FX | Missing | Missing | P2 | No currencies/rates/base amounts. |
| Audit trail | Fragmented | Partial/unsafe | P0 | Selected logs exist; mutable and incomplete. |

## Sales and customer accounting

| Capability | Status | Priority | Notes |
|---|---|---|---|
| Customers/platform accounts | Accounting redesign | P0 | Current `Company` is an operating platform label, not legal entity or full customer subledger. |
| Quotations/estimates/sales orders/delivery | Missing | P2 | No sales document chain. |
| Sales/tax invoices | Missing | P0 | Imported platform bill is treated as receivable but is not a controlled sales invoice. |
| Credit/debit notes/returns | Missing | P1 | No source-document reversal chain. |
| Receipts/allocation/overpayment | Partial/incorrect | P0 | Receipt exists; unapplied cash/allocation absent. |
| Aging/customer statements/credit limits | Missing | P1 | No AR subledger reports. |
| Recurring invoices/subscriptions/installments | Missing | P3 | No workflow. |
| Sales commissions/price lists/discounts | Missing | P2 | Rider bonus rules are payroll-specific, not sales commissions. |
| POS/e-commerce | Missing | P4 | Not current logistics priority. |

## Purchasing, AP and inventory

| Capability | Status | Priority | Notes |
|---|---|---|---|
| Suppliers/spare bills/returns | Partial | P1 | Operational CRUD, no AP/tax/approval/payment subledger. |
| Requisition/RFQ/PO/goods receipt | Missing | P2 | No purchasing lifecycle/three-way match. |
| Purchase invoice/AP | Modeled on branch | P1 | Header model only; no complete lines/workflow/posting. |
| Products/SKUs/barcodes | Partial | P2 | Spare/accessory entities; incomplete catalog/UOM/variant design. |
| Warehouses/locations/bins | Partial | P1 | Main/housing transfer logic; no warehouse/location/bin master. |
| Stock receipt/issue/transfer/return | Partial/unsafe | P1 | Mutable operational tables, incomplete immutable movement ledger. |
| Stock counts/reservations/reorder | Mostly missing | P2 | Reminders exist, not full inventory controls. |
| Valuation/FIFO/weighted average/COGS | Accounting redesign | P0 | Average prices exist but no trusted valuation layers/GL reconciliation. |
| Negative stock/concurrency | Unsafe | P0 | No universal DB/concurrency invariant. |
| Supplier aging/reconciliation/landed cost | Missing | P2 | No AP reporting/allocation. |

## HR, payroll and rider accounting

| Capability | `master` | Accounting branch | Priority | Notes |
|---|---|---|---|---|
| Employee/rider records/documents | Partial | Partial | P1 | Broad records; document access/storage controls weak. |
| Org structure/branches/positions/contracts | Mostly missing | Missing | P1 | Housing/company are not full organization structure. |
| Attendance/shifts | Partial | Partial | P1 | Platform-specific and mutable; no one attendance source/policy. |
| Payroll runs/payslips | Missing | Partial/unsafe | P0 | Rider salary/batches exist; source gating and accounting wrong. |
| Salary structures/components | Missing | Partial | P0 | Salary/bonus/financial-item rules exist without approval/version controls. |
| Loans/advances/installments | Missing | Partial/incorrect | P0 | Models/workflow exist; repayment balances do not settle. |
| Allowances/deductions/violations/tickets | Operational fragments | Partial | P0 | Flexible items exist; control-account posting absent. |
| Bank/cash payout | Missing | Partial | P0 | Batch workflow exists; segregation/bank reconciliation missing. |
| GOSI/tax/EOS/vacation accrual | Missing | Partial EOS model only | P1 | Legal/accounting validation required. |
| Payroll confidentiality | Missing | Unsafe | P0 | Broad roles/report access, no field/data partitions. |
| Substitutions | Partial | Partial | P0 | Effective dates exist; monthly ambiguity handled by review, but source aggregation unsafe. |

## Platform operations and integrations

| Capability | Status | Priority | Notes |
|---|---|---|---|
| Hunger operational import/report | Partial | P1 | Multiple imports and hard-coded targets/company IDs. |
| Keeta shift/attendance/freelancer | Partial | P1 | Useful detail, fragmented batches and no canonical reconciliation. |
| Amazon schedule/orders | Partial/incorrect | P1 | Wide schedule parser; Company 3/4 inconsistency. |
| Accounting company-bill import | Unsafe prototype | P0 | Raw cells retained, but generic parser/duplicate/multi-tab/source-state defects. |
| Original file retention/hash | Missing | P0 | Filename/raw cells only. |
| Webhooks/API connectors | Missing | P3 | File imports only. |
| Outbox/idempotent retries | Missing | P0 | Required before automated posting/integration. |
| Import exception/reconciliation UI | API partial, UI missing | P1 | Resolution records exist; no frontend. |

## Saudi tax and compliance readiness

| Capability | Status | Priority | Notes |
|---|---|---|---|
| VAT rates/codes/input-output accounts | Incorrect/incomplete | P0 | One output account and amount fields; no full tax engine/input VAT. |
| VAT return/reconciliation/period lock | Missing | P0 | No tax ledger/return. |
| Arabic/English tax invoice | Missing | P0 | No invoice aggregate/templates. |
| ZATCA Phase 1 generation | Missing | P0 | No compliant invoice/QR/archive controls. |
| ZATCA Phase 2 XML/hash/stamp/UUID | Missing | P0/P1 | No XML, onboarding, clearance/reporting, retry or rejection workflow. |
| Legal compliance assertion | Not permitted | P0 | Must be validated against current ZATCA rules and licensed tax/accounting advice. |

## Enterprise and reporting

| Capability | Status | Priority | Notes |
|---|---|---|---|
| Tenant/legal entity/branch isolation | Missing | P0 | Company/platform conflation. |
| Multi-company charts/calendars/currencies | Missing | P1 | No isolated books. |
| Intercompany/consolidation | Missing | P3 | No due-to/from/eliminations. |
| Projects/profit centers/budgets | Mostly missing | P2 | Generic cost centers only on branch. |
| Approval workflow/delegation/limits | Missing | P0 | Status fields are not an approval engine. |
| Financial dashboards/reports | Partial/incorrect | P0 | Operational dashboards broad; financial reports not reconciled. |
| System activity/exception reports | Partial | P1 | Fragmented logs, no finance-wide exception dashboard. |
| Frontend/self-service/mobile | Missing from repo | P2 | Cannot verify UX/workflow/accessibility. |

## Recommended implementation order

1. P0 security, secrets, legal-entity boundary, immutable import/posting/idempotency and ledger-derived reporting.
2. Correct platform reconciliation, AR, rider payroll/deductions and bank controls.
3. Purchasing/AP and inventory valuation/COGS.
4. Expenses/claims, HR/payroll statutory features, bank reconciliation and assets.
5. VAT/ZATCA, budgets/projects, enterprise reporting and consolidation.
