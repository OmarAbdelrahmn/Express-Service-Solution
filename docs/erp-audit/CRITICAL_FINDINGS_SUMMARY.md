# Sol Ultra Critical Findings Summary

## Direct answers

- **Current ERP maturity:** operational delivery-management system with an unmerged accounting prototype; not a complete ERP.
- **Current accounting maturity:** no accounting on `master`; early prototype on `codex/accounting-module-20260624` with serious correctness gaps.
- **Safe for financial production:** **No.** Do not use current imports, salaries, receivables, P&L or journal reports as statutory books.
- **Largest missing areas:** sales/AR lifecycle, purchasing/AP lifecycle, trusted inventory valuation, payroll/HR accounting, bank reconciliation, assets, VAT/ZATCA, budgeting, multi-legal-entity isolation, consolidated reporting and frontend workflows.
- **Recommended first implementation phase:** Phase 0 financial containment and security, followed by a redesigned ledger/posting and immutable import foundation.

## P0 findings

| ID | Risk | Evidence | Immediate control |
|---|---|---|---|
| P0-01 | Duplicate workbook can create duplicate receivable/revenue | Accounting import stores filename but no content hash/idempotency key or unique batch fingerprint. | Disable approval of repeated source files; reconcile by company/period/file totals until fixed. |
| P0-02 | Multi-sheet bill can double-count revenue and rider pay | Generic parser adds every summary-like row from every worksheet; totals sum all summaries. Prior Keeta/FTR files contain multiple summary/detail tabs. | Require accountant reconciliation against invoice grand total; do not auto-post. |
| P0-03 | Unapproved/reversed import can enter salary | `EnsureEarningsFromSummariesAsync` filters period/company and paid rider, not import `Posted` status. | Generate salaries only from manually approved import IDs; reconcile earnings source list. |
| P0-04 | Hunger monthly target can be calculated multiple times | Salary formula is applied per normalized summary row, not one aggregated rider/company/month basis. | Manually consolidate accepted orders before payroll. |
| P0-05 | Keeta invalid rider may be paid | `صالح/غير صالح` is stored but not enforced in salary generation. | Block Keeta salary release until validity and exceptions are reviewed. |
| P0-06 | Rider deduction accounting is wrong | Salary approval debits only net salary expense and credits net payable; loans, advances, violations and fees do not credit their receivable/control accounts. | Post payroll outside the system or create accountant-reviewed adjustment journals. |
| P0-07 | Loan balances do not clear when salary deduction is paid | No update to `RiderLoanInstallment.PaidAmount`/loan remaining balance in salary payment flow. | Maintain an external loan reconciliation and stop automated installment carry-forward. |
| P0-08 | P&L is not ledger-derived and can be materially wrong | Report includes draft/reversed salaries, all expense statuses, supplier payables by due date and adds deductions after already using net salary. | Do not issue P&L from this service; derive interim reports from reconciled journals. |
| P0-09 | Maker-checker is absent | One `Master,Admin,Accountant` controller role can import, resolve, approve, reverse, manage rules, create/send/confirm payments and reopen periods. | Separate preparer, approver, payer and period-controller users immediately. |
| P0-10 | Sensitive endpoints/dashboard are exposed | Hangfire dashboard uses `Authorization = []`; many import/shift/member/debug actions have no effective backend role attribute. | Restrict dashboard/network access and deny unreviewed endpoints at the gateway. |
| P0-11 | Production secrets are committed | Connection, JWT and SMTP credentials exist in tracked `appsettings.json`. | Rotate all credentials, move to secret storage and scrub Git history. |
| P0-12 | Legal entity and customer platform are conflated | `Company`/hard-coded IDs represent Hunger/Keeta/Amazon rider channels, not legal entities/books. | Do not attempt multi-company accounting until `LegalEntity` and `PlatformAccount` are separated. |

## P1 findings

- Journal duplicate prevention is an application query, not a unique database invariant; concurrent approval can post twice.
- Journal lines lack database checks requiring one non-negative debit or credit and posted entries lack a concurrency token.
- Expense VAT is debited into expense instead of a recoverable input-VAT account.
- Account chart is global, hard-coded by numeric IDs and lacks per-legal-entity control accounts/currencies.
- Audit logs and posted records are normal mutable tables with no append-only protection.
- Raw cells are retained, but the original workbook binary/hash/parser version is not retained.
- Generic header detection is not a trustworthy contract for the four complex source formats.
- The accounting branch is six local commits ahead of its remote, so the reviewed state is not fully published.
- The build reports an affected Microsoft.OpenApi package; upgrade to a patched line and verify dependency use.

## Modules that must not be used as financial truth

- operational `Wallet`, `Bill`, `BillItem`, stock totals and petrol costs;
- company-bill normalized totals until duplicate and multi-sheet reconciliation is fixed;
- rider salary and loan balances until source-status and deduction posting are fixed;
- company P&L/cost-center reports;
- bank/cash batches without role separation and bank-statement reconciliation;
- VAT/ZATCA output, because complete invoice/tax architecture is not implemented.

## First controlled delivery

1. Freeze and fingerprint all imports; retain original files.
2. Rotate secrets, secure Hangfire and sensitive APIs, and require explicit actor context.
3. Introduce legal entity/branch/platform-account separation.
4. rebuild the posting engine with database-enforced idempotency, immutable posted entries, correct control-account routing and ledger-derived reports.
5. add exact Hunger, Keeta and Amazon adapters with batch reconciliation before enabling salary/AR posting.
6. run shadow accounting for at least two closed periods and reconcile every platform bill, rider payroll, bank payment, VAT control account and trial balance before production cutover.

## Verification performed

- Accounting branch solution build: succeeded, 11 warnings.
- Accounting tests: 20 passed, 0 failed.
- Build dependency scan reported high-severity `Microsoft.OpenApi` advisory [GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc).
- No application code, migrations, configuration or tests were changed by this audit.
