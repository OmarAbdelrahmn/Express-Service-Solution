# Accounting implementation and activation status

Updated: 2026-07-13

| Area | Code status | Production gate |
|---|---|---|
| Financial document, GL, base/transaction currency, dimensions, immutable posting and reversal | Implemented | Execute SQL Server integration/concurrency suite and migration rehearsal |
| Idempotency, document sequences, audit hash chain, transactional outbox/Hangfire | Implemented | Configure monitoring, retry/dead-letter alerts and validate SQL locks |
| Period locks, close/reopen and corrected core statements | Implemented foundation | Complete retained-earnings year-close acceptance and two shadow periods |
| Encrypted private files and authenticated file IDs | Implemented | Supply production key from secret storage; backup/restore rehearsal |
| Hunger, Keeta pay/order, Keeta segments and Amazon import adapters | Implemented against certified workbook structures | Reattach immutable originals in target environment and pass golden-file tests |
| Keeta large segment review | Bounded sample only by owner instruction | Detail header + first record inspected; never represent this as full detail certification |
| Effective worker mappings, shift substitutions and pre-approval row correction | Implemented | Accountant validates all ambiguous/missing identities before approval |
| Configurable compensation simulation and effective policy versions | Implemented | Accountant creates/activates first real policies; no seeded active rates |
| Combined rider payroll, deductions/carry, loans/installments, validity, adjustments | Implemented | Shadow-run reconciliation against legacy payroll |
| Bank/cash/hold/mixed rider payments, housing-scoped confirmations, reversals and generic CSV/XLSX export | Implemented backend | Bank-specific export profiles/adapters and bilingual cash PDF still required |
| AR/AP invoices, tax lines, receipts/payments and allocations | Implemented foundation and safe GL posting | Aging, statements, credit/debit notes, overpayments and richer reconciliation remain |
| Expenses and evidence | Implemented foundation; new API accepts encrypted `StoredFileId` | Petty cash, employee advance settlement and duplicate-receipt intelligence remain |
| Inventory | Concurrent negative-stock guard and immutable GL-posted movements implemented | Warehouses/bins master, reservations, counts, FIFO/weighted-average layers, GRNI/landed costs remain |
| Treasury | Bank accounts, statement lines and basic match implemented | Batch adapters, split/many-to-many matching, unmatched-amount constraints and close report remain |
| Tax/VAT | Effective codes, tax transactions and draft return foundation implemented | Inclusive/exclusive/reverse charge/recoverability, amendments and control reconciliation remain |
| ZATCA | Not implemented and not claimed compliant | Current official validator/sandbox plus licensed tax review required |
| Fixed assets | Asset register foundation implemented | Books/categories, depreciation schedules/runs, changes, impairment/disposal and GL reconciliation remain |
| Budgets | Version/line foundation implemented | Transfers, purchasing commitments and variance workflows remain |
| Purchasing | Not implemented | Requisitions, RFQ, PO, goods receipt, GRNI and three-way match required |
| PDF/Excel/bank exports | Generic protected rider-payment CSV/XLSX implemented with formula-injection protection | Report exports, bank-specific adapters, bilingual QuestPDF output, Arabic shaping and snapshot tests remain |

Production activation is intentionally blocked until every enabled module passes its reconciliation gate, the SQL Server-specific tests run against a disposable database, two shadow periods close with zero unexplained differences, and restore/monitoring rehearsals succeed.

Current verification note: the solution builds and the accounting test project passes, and EF reports no model/migration drift. A full migration script was generated and contains the expected immutability/overlap triggers and private-file foreign key. Execution against disposable LocalDB could not be completed because the installed `MSSQLLocalDB` SQL process failed to start; therefore SQL Server runtime validation remains open and no configured application database was touched.
