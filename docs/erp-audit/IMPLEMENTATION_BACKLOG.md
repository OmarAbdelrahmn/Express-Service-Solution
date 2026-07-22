# Sol Ultra Implementation Backlog

Each task is independently reviewable. “Frontend” names the required screen even though that repository is not currently present. All migrations use expand-migrate-contract and a feature flag; rollback means disable writes and forward-correct/reverse posted test batches, never delete posted history.

## Phase 0 and ledger P0

| ID | Pri | Task / problem / business requirement | Backend, DB and frontend | Controls and accounting | Tests, acceptance, dependencies and rollback |
|---|---|---|---|---|---|
| SEC-001 | P0 | Rotate and externalize tracked DB/JWT/SMTP secrets. | Secret-provider configuration; remove tracked values; deployment variables; security settings checklist. | Restrict read to service identity; audit secret rotation. No journal. | Secret scan current/history, old credential rejection, startup test. Dependency none; roll back provider reference only, not old secrets. |
| SEC-002 | P0 | Enforce authentication/authorization by default and secure Hangfire/Swagger/debug/import/document endpoints. | Fallback policy, dashboard filter, endpoint-permission registry, private document download API; admin security page. | Explicit permission + legal-entity scope; no anonymous sensitive actions. | Integration tests for every endpoint/role/entity; gateway staged deny logs. Depends SEC-001. |
| REL-001 | P0 | Freeze and publish the exact accounting candidate; current local branch is six commits ahead of remote. | Merge/rebase decision, CI workflow, signed/tagged audit revision. | No functional accounting change. | Build, 20 tests, diff review and remote readback. Rollback tag/branch; preserve current worktree. |
| REL-002 | P0 | Upgrade affected `Microsoft.OpenApi` dependency. | Pin/upgrade patched compatible version and lockfile/assets; no UI. | Availability/security only. | Build, Swagger generation, dependency scan no GHSA. Depends REL-001. |
| ORG-001 | P0 | Separate legal entity/branch from Hunger/Keeta/Amazon platform accounts. | `Tenant`, `LegalEntity`, `Branch`, `PlatformAccount`, legacy `CompanyPlatformMapping`; org settings/mapping UI. | Entity-scoped books, tax registrations, sequences and permissions. | Migration mapping, cross-entity denial, shared platform. Rollback compatibility read mapping. |
| AUD-001 | P0 | Remove actor fallbacks and add request context. | `ICurrentActor`, legal-entity/branch/correlation middleware; replace `system/member/omar` fallbacks; audit viewer. | Interactive actions require authenticated actor; service principals registered explicitly. | Missing claim rejected, background principal retained, correlation audit. Depends SEC-002/ORG-001. |
| AUD-002 | P0 | Make accounting audit append-only and complete. | Audit event model/store, old/new values, config/document/posting/export events; audit search/export UI. | Separate DB permission, hash chain/WORM export, retention. | Tamper/update/delete denied; event completeness tests. Depends AUD-001. |
| GL-001 | P0 | Add legal-entity chart and account-routing profiles; remove numeric account constants. | Account hierarchy, control flags, posting profiles/versioning APIs; chart/routing UI. | Control accounts cannot be posted manually; approved effective versions. | Account-code uniqueness per entity, route lookup and closed-period version. Depends ORG-001. |
| GL-002 | P0 | Database-enforce journal invariants/idempotency. | Unique posting key/reversal key, line check constraints, row version, posting service and migration. | One positive side per line, balanced batch, immutable posted entry. | Concurrent approval/retry, invalid direct inserts and reversal race. Depends GL-001/AUD-002. |
| GL-003 | P0 | Implement financial document state machine and maker-checker. | Reusable document/approval records, submit/approve/post/reverse APIs; approval inbox. | Creator != approver/poster; reason/evidence; amount/entity rules. | Self-approval, delegation/expiry, illegal transition. Depends AUD-001. |
| GL-004 | P0 | Redesign periods by legal entity/fiscal year. | Fiscal calendars/periods/close checklist/tax lock APIs; close workspace. | Period controller separate; controlled reopen with approval/reason. | Multiple calendars, backdate, year close, reopen. Depends ORG-001/GL-003. |
| GL-005 | P1 | Add manual/adjusting/recurring/accrual journals. | Journal document contracts/services/controllers; journal editor/recurring schedule UI. | Prepare/approve/post; reversal not edit. | Balanced/invalid, recurring idempotency, accrual reversal. Depends GL-002/GL-004. |

## Platform import and rider payroll P0

| ID | Pri | Task / requirement | Backend, DB and frontend | Controls and accounting | Tests / dependencies / rollback |
|---|---|---|---|---|---|
| IMP-001 | P0 | Retain original workbook and prevent duplicate upload. | Private content-addressed file store; SHA-256, size, MIME, provider reference, parser version on batch; import preview UI. | Unique entity/platform/hash/reference; upload posts nothing. | Same bytes/name change/concurrent retry/supersede. Depends ORG-001/AUD-001. |
| IMP-002 | P0 | Replace generic parser with exact Hunger/FTR adapter. | Versioned tab schemas for WR/RLVL/FTR cost; normalized facts/control totals; mapping UI. | One authoritative metric per rider/period; distance separate from rider pay. | Historical golden file, duplicate tabs, penalties/hours/days/IBAN lineage. Depends IMP-001. |
| IMP-003 | P0 | Add exact Keeta pay-per-order adapter. | Partner/rider/detail schemas, streamed large-sheet parser; exception UI. | Preserve transaction/work/amount/face data; no salary on import. | 162k-row performance, row/cell lineage and totals. Depends IMP-001. |
| IMP-004 | P0 | Add exact Keeta segment/freelancer adapter. | Validity/reason/connection/distance/pricing/incentive/fee/violation mappings. | Invalid/missing validity is blocking; override needs independent approval. | Valid/invalid/conflict/override and all detail fields. Depends IMP-001/GL-003. |
| IMP-005 | P0 | Add exact Amazon payment and schedule adapters. | Daily-column unpivot, payment summary and schedule fact models; reconciliation UI. | Daily total=grand total; payout policy separate from company amount. | Date columns, EID/OT/incentives, schedule join and year boundary. Depends IMP-001. |
| IMP-006 | P0 | Effective-dated platform worker identity and substitutions. | `PlatformWorkerAccount`, mapping history, substitution allocation service and issue UI. | Exact service-date routing; monthly overlap blocks posting. | before/start/inside/end/after, reused IDs, cross-platform IDs. Depends ORG-001. |
| IMP-007 | P0 | Cross-tab/source reconciliation gate. | Reconciliation rules/results, control totals, issue severity, approve API. | Maker resolves, checker approves; all blocking differences zero/tolerated. | duplicate summary, mismatched invoice, hidden/renamed sheet. Depends IMP-002..006/GL-003. |
| PAY-001 | P0 | Generate earnings only from posted, non-reversed approved facts. | Posting-event-driven earning creation; source version/status FK/invariant. | Reversal/supersede reverses dependent earnings; no pending source. | pending/posted/reversed/superseded/regenerate. Depends IMP-007/GL-002. |
| PAY-002 | P0 | Aggregate monthly facts before applying rider policy. | Payroll input aggregate by entity/platform/contract/paid rider/period and calculation trace. | One policy application per aggregation key. | split rows/files, substitutions and Hunger thresholds. Depends PAY-001. |
| PAY-003 | P0 | Version/approve salary, bonus, eligibility and penalty policies. | Effective-dated policy versions with draft/review/approved; configuration UI with diff. | No retroactive closed-period edit; self-approval forbidden. | overlaps/priority/effective dates and rollback to prior version. Depends GL-003/004. |
| PAY-004 | P0 | Enforce Keeta validity and explicit exception override. | Eligibility result/override records in payroll preview. | Invalid/missing blocks; different approver and reason. | valid/invalid/missing/conflict/override/reversal. Depends IMP-004/PAY-003. |
| PAY-005 | P0 | Correct payroll posting and deduction allocations. | Payroll result lines mapped to expense/liability/receivable accounts; allocation model. | Gross expense, control-account credits and net payable; atomic balanced journal. | every item type, mixed/negative net, reversal. Depends GL-001/002/PAY-002. |
| PAY-006 | P0 | Settle loans/advances/violations when payroll deduction is paid. | Receivable schedules/allocation settlement and reversal services; rider balance UI. | Partial/insufficient salary allocation; no duplicate recovery. | partial, held, failed bank, cash, reversal and final settlement. Depends PAY-005. |
| PAY-007 | P1 | Build controlled payroll run/payslip workflow. | Pay run/result/payslip APIs, preview/exceptions/approval pages. | Payroll confidentiality and maker-checker. | rerun idempotency, locked period, payslip totals. Depends PAY-002..006. |

## Financial reporting, VAT and treasury

| ID | Pri | Task / requirement | Backend, DB and frontend | Controls/accounting | Tests / dependencies / rollback |
|---|---|---|---|---|---|
| RPT-001 | P0 | Replace operational P&L/cost-center calculations with ledger queries. | Account classifications, posted-line projections; P&L/cost center pages. | Posted/nonreversed only; dimensions; source reconciliation. | deduction scenario, drafts/reversals, breakdown=total. Depends GL-002/PAY-005. |
| RPT-002 | P1 | Add balance sheet, cash flow, equity and comparative statements. | Report service/read models/export UI. | Ledger-derived with closing/FX policies. | statements reconcile to trial balance and prior period. Depends RPT-001/GL-004. |
| TAX-001 | P0 | Correct VAT account routing and tax-code engine. | Input/output VAT accounts, tax codes/rates/recoverability and calculation service; tax settings UI. | Net expense + recoverable input VAT; tax-period lock. | inclusive/exclusive, exempt/zero/reverse-charge/credit. Depends GL-001. |
| TAX-002 | P1 | VAT return and reconciliation. | Tax ledger/return/adjustment APIs and workspace. | Return totals reconcile to control accounts and invoice sources. | period lock, amendment and rounding. Depends TAX-001/AR/AP. |
| TRS-001 | P0 | Separate payment prepare/release/confirm and require bank evidence. | Payment proposal/release/ack hash/reference; treasury queue UI. | Three-way role separation; beneficiary approval. | creator cannot release/confirm; partial reject/retry. Depends GL-003/PAY-005. |
| TRS-002 | P1 | Bank statement import/matching/reconciliation. | Statement/line/match/reconciliation models/services/UI. | Unique statement lines, reconciliation close and controlled adjustments. | duplicate statement, auto/manual match, bank-to-GL zero. Depends TRS-001. |
| AR-001 | P1 | Platform customer invoices/credit notes and AR posting. | Contract/rate card, billing run, invoice/line/tax APIs/UI. | Approved reconciled facts only; AR/revenue/VAT posting. | rate dates, duplicate activity, credit/reversal, AR=GL. Depends IMP-007/GL/TAX-001. |
| AR-002 | P1 | Receipt allocation and unapplied cash. | Receipt header/allocation/customer advance models/UI. | Unapplied credit separate from AR; allocation/reversal audit. | partial/over/multi-invoice/refund. Depends AR-001/TRS-002. |

## Purchasing, inventory and remaining ERP modules

| ID | Pri | Task / requirement | Backend/DB/frontend | Controls/accounting | Tests / dependencies |
|---|---|---|---|---|---|
| AP-001 | P1 | Supplier invoice/AP subledger. | Invoice/lines/tax/credit/aging/payment allocation APIs and pages. | Duplicate supplier invoice, approval; expense/input VAT/AP. | partial/credit/payment/AP=GL. Depends GL/TAX-001. |
| PUR-001 | P2 | Requisition-RFQ-PO-receipt and three-way match. | Purchasing aggregates/services/pages. | Limits/tolerance/maker-checker; commitments. | partial receipt, variance and cancellation. Depends AP-001. |
| INV-001 | P1 | Immutable stock movement/reservation ledger. | Item/UOM/warehouse/bin/transaction/reservation APIs and pages. | Atomic quantity invariants and object scope. | concurrent oversell, transfer/return/count. Depends ORG-001. |
| INV-002 | P1 | Weighted-average valuation, landed cost and COGS posting. | Valuation layers/costing jobs/reconciliation page. | Inventory/GRNI/COGS/variance control accounts. | backdate, return, landed cost, valuation=GL. Depends INV-001/PUR-001/GL. |
| HR-001 | P1 | Employee/contract/org/attendance/leave foundation. | Contracts, positions/departments, attendance result, leave balances and HR UI. | Effective dates, confidentiality and approvals. | contract/leave/attendance transitions. Depends ORG/AUD. |
| HR-002 | P1 | GOSI/EOS/vacation accrual and final payroll. | Statutory configuration/calculators/accrual runs. | Licensed Saudi validation; liability/expense posting. | official scenarios, retro/reversal. Depends HR-001/PAY-007. |
| EXP-001 | P1 | Expense claims/petty cash/employee advances. | Policy/request/receipt/advance/settlement APIs and self-service. | limits, duplicate receipt, approver/payee separation. | policy violations, advance settle, VAT posting. Depends TAX/GL. |
| AST-001 | P2 | Fixed asset register and depreciation. | Asset/category/book/schedule/run/disposal APIs/pages. | Capitalization/depreciation/impairment/disposal journals. | partial month, change estimate, disposal and asset=GL. Depends GL/AP. |
| ZAT-001 | P1 | ZATCA Phase 1/2 invoice lifecycle. | XML/QR/hash/stamp/UUID, onboarding credential reference, submit/retry/archive UI. | Immutable issued invoices, clearance/reporting states, tax/legal approval. | official validators/sandbox, retry/idempotency/rejection. Depends AR/TAX. |
| BUD-001 | P2 | Projects/cost centers/budgets/commitments. | Project, budget/version/line/transfer/allocation APIs/pages. | Approved budgets, dimension posting and commitment checks. | transfer/lock, PO/payroll allocation, variance=ledger. Depends subledgers. |
| CON-001 | P3 | Multi-entity consolidation/intercompany. | Groups/mappings/due-to/from/elimination services/pages. | Entity ledgers remain truth; approved elimination journals. | intercompany reconciliation, FX translation, consolidated=entities+elims. Depends multi-entity maturity. |
| OPS-001 | P1 | Monitoring, backup and disaster recovery. | Metrics/alerts/runbooks, PITR backup, evidence retention. | Alert on duplicate/import/posting/payment/reconciliation differences. | restore closed period/source files/journals/audit; RPO/RTO drill. Depends Phase 0 onward. |

## Definition of done for each backlog item

- schema, migration preflight, data conversion, rollback/forward-correction and feature flag;
- service interface/implementation, thin controller, validation and standardized errors;
- explicit permission/object scope, actor, audit and segregation tests;
- correct posting profile/reversal or explicit “no accounting entry” statement;
- frontend workflow and accessibility/error/empty/loading states;
- unit, integration, concurrency, migration and reconciliation tests;
- observability, runbook and acceptance evidence linked to the task.
