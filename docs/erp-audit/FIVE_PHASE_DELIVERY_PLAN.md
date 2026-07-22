# Five-Phase ERP Delivery Plan

## Conclusion from the eleven audit documents

The system is an operational delivery-management platform with useful finance-related data and an accounting prototype.  It is **not yet a production-safe financial ERP**.  In particular, the audits confirm gaps in source evidence, identity and legal-entity isolation, posting controls, payroll correctness, sales and purchasing subledgers, inventory valuation, tax compliance, reporting, frontend workflows, and operational resilience.

The order below is deliberate: later phases must consume immutable, reconciled facts from the earlier phases.  Automated financial posting, payroll payment release, financial statements, and VAT/ZATCA output must not be treated as production truth until their applicable exit gates have passed.

> **Baseline note:** this plan consolidates the findings in the eleven audit documents.  It is not an acceptance of later uncommitted implementation work; any such work must be independently tested, migration-rehearsed, reviewed, and mapped to the exit gates below.

## The five phases at a glance

| Phase | Outcome | Principal missing capabilities addressed | Production gate |
|---|---|---|---|
| 1. Containment and release baseline | The existing platform is safe to operate while finance is rebuilt. | Remaining operational acceptance: secret rotation, authorization proof, durable imports, release/restore discipline and concurrency coverage. | No exposed secrets or unauthenticated sensitive actions; recoverable, traceable releases. |
| 2. Controlled financial core | Each legal entity has a controlled, auditable general ledger. | Organisation boundary, chart, periods, approvals, journals, currencies/dimensions, ledger reporting. | Only balanced, authorized, immutable postings enter the ledger. |
| 3. Trusted source-to-cash and payroll | Controlled backend subledgers now create maker-checked source documents; external source reconciliation remains to be proven. | Evidence envelope, platform settlement, AR, receipt allocation and effective-dated payroll foundations. | Two closed shadow periods reconcile source, subledgers, payroll and GL. |
| 4. Procure-to-pay and operational accounting | Controlled AP, stock movement, expenses and bank-reconciliation foundations now exist. | Supplier invoice/payment allocation, immutable stock events, expense claims and statement matching. | AP, inventory, payroll and bank control accounts reconcile to GL. |
| 5. Compliance, decisions and scale | Tax, reporting, asset and budget foundations are implemented; statutory integrations and operational readiness remain. | VAT tax ledger/return workflow, balance sheet/cash movement, fixed assets and budgets. | Compliance validation, management reporting and disaster-recovery evidence are accepted. |

## Phase 1 — Containment and release baseline (substantially implemented; remaining acceptance work)

**Goal:** finish the operational proof needed to accept the security and platform-hardening changes already implemented; do not expand financial automation yet.

**Completed and removed from the remaining scope**

- Tracked database, JWT and SMTP values were removed from `appsettings.json`; secure configuration is now required at startup.
- A default authenticated-user policy, authentication middleware, rate limiting, development-only Swagger, protected Hangfire dashboard, and controller authorization updates were added.
- The vulnerable OpenAPI dependency was upgraded, application validator/mapper scanning was corrected, and EF pending-model warning suppression was removed.
- The default operational decimal mapping was changed to `decimal(18,2)` where an explicit precision is absent, with a normalization migration supplied.
- A correlation-ID middleware, redacted global exception handler, automated build/test/migration/secret-scan workflow, and an accounting test project were added.

**Remaining work**

- Provision secure production configuration, rotate the previously exposed credentials, and remediate any Git history/artifacts containing them.
- Prove the authorization boundary with automated endpoint tests and a role/permission review; remove any remaining fallback or hard-coded actor identities from financial and operational actions.
- Enforce the new CI workflow with protected branches, dependency-review policy, migration approval, and deployment approval evidence.
- Apply and rehearse the money-precision and accounting migrations against representative data; define rounding, timezone/business-date, rollback and data-correction procedures.
- Add row-version/unique-invariant coverage to all critical import, payment, stock and posting operations, then run concurrency tests.
- Replace legacy in-process background-import state, temporary JSON and `Task.Run` processing with durable, idempotent jobs; retain source files in private content-addressed storage with audit correlation.
- Deliver financial alerts, encrypted backups, documented RPO/RTO, and a successful restore drill.

**Exit evidence**

- Credentials are rotated in the live environments, secret scans are clean, and endpoint-authorization tests prove no sensitive anonymous or cross-role access.
- A production-like migration preflight, money-data reconciliation, backup/restore rehearsal and rollback exercise have succeeded.
- Import/job retries are durable, idempotent, observable, and scoped to an authenticated actor and legal entity.
- Protected CI blocks dependency regressions, pending model changes, failing tests, unreviewed migrations and missing release evidence.

## Phase 2 — Controlled financial core

**Goal:** establish one trustworthy book per legal entity before posting operational transactions.

**Implemented in the repository**

- The tenant/legal-entity/branch/platform-account model, scoped chart of accounts, fiscal periods, currencies, exchange rates and financial dimensions are implemented.
- Manual, recurring and reversal journals support draft, submit, approve, post and reverse flows with maker-checker enforcement, period control, idempotency, database posting constraints and append-only accounting audit/outbox records.
- Finance access is now granted explicitly per user and legal entity.  `Master` remains the emergency administrator; every other ledger operation enforces `View`, `Prepare`, `Approve`, `Post`, `ManagePeriods` or `Configure` permissions in the service layer.
- New ledger-derived P&L and dimension/cost-centre balance APIs complement the existing trial balance.  Only finalized journal entries participate in those reports, and required dimensions are now enforced on each manual-journal line.
- A targeted test project covers legal-entity finance permissions and finalized-ledger P&L behavior.  The recurring-journal generator now resolves an effective FX rate instead of incorrectly assuming a rate of one.

**Remaining work and acceptance evidence**

- Apply the migrations and use the financial-access API to grant least-privilege permissions before any non-`Master` user uses the ledger.
- Extend legal-entity scoping from the new ledger services to every legacy import, payment, export and operational route that will eventually feed accounting.
- Add endpoint/integration and database-concurrency tests for the full create-submit-approve-post-reverse workflow, including required-dimension, closed-period and cross-entity cases.
- Deliver the finance frontend for approvals, exceptions, report filtering and audit retrieval; no frontend source is currently in this repository.
- Rehearse the migrations and reconcile a representative opening balance before enabling financial postings.

**Exit evidence**

- Every posted line is entity-scoped, balanced, immutable and traceable to its source and approving actors.
- Period close blocks backdated changes except controlled reversal/adjustment workflows.
- Trial balance and P&L reconcile to posted journal lines, not operational tables.
- Concurrency, idempotency, permission, reversal and migration tests pass under realistic parallel requests.

## Phase 3 — Trusted platform source-to-cash and payroll (backend foundation implemented)

**Goal:** turn HungerStation/FTR, Keeta and Amazon evidence into reconciled financial facts without duplicate or unsafe payroll/revenue posting.

**Implemented in the repository**

- A source-evidence vault records legal-entity/platform scope, external reference, private storage locator, content hash, metadata and review state.  The database migration prevents changing the evidence content/provenance or deleting it after receipt.
- Accepted evidence can support idempotent platform settlements.  Each settlement creates a maker-checked source journal and retains its ledger-document link.
- Customer master, draft/issued AR invoices, output-tax transactions, receipts, unapplied cash and receipt allocations are implemented.  Source documents are created through the ledger's internal source-journal path, which permits control accounts only for trusted subledger services and still requires submit/approve/post separation.
- Effective-dated employee pay contracts, payroll runs, fixed deductions, accrual documents and payment documents are implemented.  A run snapshots the effective salary/deduction amounts into run lines before accounting preparation.

**Remaining work and acceptance evidence**

- Implement exact, versioned adapters for the known workbook layouts rather than generic multi-sheet parsing.
- Retain original private source files, hashes, import envelopes, row-level provenance and immutable normalized facts.
- Add duplicate detection, source identity/effective dating, exception queues, independent approval and source-to-file/row/total reconciliation.
- Build exact, versioned HungerStation/FTR, Keeta and Amazon adapters plus durable source-file storage; the generic evidence envelope deliberately does not parse or trust legacy multi-sheet imports.
- Add AR aging, credit/debit notes, source-to-GL reconciliation, payout matching and automated exception queues.
- Connect approved rider/platform facts and Keeta validity to payroll, then add loans, statutory deductions, payslips, payment evidence and independent payroll role tests.
- Run the workflows in shadow mode for two closed periods.  VAT-return preparation already excludes unposted source documents; the remaining source adapters must preserve that rule.

**Exit evidence**

- Golden-file tests cover duplicate files, multiple tabs, changed layouts, invalid Keeta validity, reversals and retry behavior.
- Two closed shadow periods reconcile platform source totals, imported facts, AR, payroll, deductions/loans, receipts and posted GL.
- Independent preparer, reviewer, approver and payment roles are proven through workflow tests.

## Phase 4 — Procure-to-pay and operational accounting (backend foundation implemented)

**Goal:** make operational procurement, stock, staff costs and cash movement financially complete and reconcilable.

**Implemented in the repository**

- Separate supplier master, draft/recorded supplier invoices, input-tax transactions, supplier payments and unapplied/payment allocation flows now create controlled AP source documents.
- A new inventory-item and immutable inventory-movement register records bins, quantities, unit costs, references and its linked source document; its debit/credit accounts are validated inside the legal entity.
- Employee expense claims support accepted receipt evidence, input VAT and an employee-payable source document.
- Bank accounts, idempotent statement lines and approver-only matching to posted financial documents are implemented.

**Remaining work and acceptance evidence**

- Add purchase requests/orders, receiving/GRNI, supplier credit notes, AP aging and approval-policy thresholds.
- Extend stock into warehouse reservation/counting, negative-stock concurrency protection, valuation layers, landed cost and COGS; the current register intentionally does not infer valuation from the legacy spare-parts tables.
- Add petty cash, advances/settlement, duplicate receipt controls, policy approval and the HR attendance/GOSI/EOS/vacation accrual integrations.
- Add bank-file adapters, treasury proposal/release/confirmation separation, reconciliation close and controlled difference-adjustment workflows.

**Exit evidence**

- AP, inventory/GRNI/COGS, payroll liabilities and bank control accounts each reconcile to their subledgers and the GL.
- Concurrent stock, invoice, payment and reversal scenarios are tested without duplicate or negative unintended balances.
- Bank reconciliation reaches zero unexplained difference at close, with evidence for every manual adjustment.

## Phase 5 — Compliance, decisions and scale (backend foundation implemented)

**Goal:** complete the compliance, management and resilience capabilities expected of a Saudi multi-entity ERP.

**Implemented in the repository**

- Configurable input/output tax codes, tax-account routing, posted-document-only tax transactions, draft VAT-return preparation and approver-controlled submission records are implemented.
- Ledger-derived balance-sheet and explicit cash-account movement APIs complement the trial balance and P&L; they use finalized journal lines only.
- Fixed-asset registers and budget/budget-line master data are implemented with account/dimension validation.
- The new operations service writes chained accounting audit events and outbox messages, and its migration supplies source-evidence immutability triggers.

**Remaining work and acceptance evidence**

- Obtain legal/tax review for VAT configuration, add VAT reconciliation/period locking and build bilingual tax invoice outputs.
- Implement and validate the current ZATCA Phase 1/2 XML, QR, UUID/hash, onboarding, clearance/reporting, retry and archive requirements in the official sandbox before any production use.
- Add depreciation posting, cash-flow classification, statement of changes in equity, comparative reports, forecasts and multi-entity consolidation/elimination policies.
- Build the finance frontend and complete end-to-end approval, reporting, reconciliation, monitoring, retention/legal-hold, capacity and disaster-recovery work.

**Exit evidence**

- Tax and ZATCA behavior has passed current official validation/sandbox requirements and independent professional review.
- Financial statements reconcile to the trial balance and closed-period policies, including FX and consolidation policies where used.
- Critical end-to-end workflows, monitoring, backup restoration and RPO/RTO exercises are accepted by finance and operations owners.

## Dependency rules and immediate priority

1. Complete Phase 1 before enabling or widening financial writes.
2. Complete the Phase 2 financial core before any automated operational posting can be trusted.
3. Run Phase 3 in shadow mode for at least two closed periods before using it for revenue, payroll or receivable truth.
4. Build Phase 4 modules only through the Phase 2 posting, approval and reconciliation services; do not add isolated accounting calculations to operational tables.
5. Treat Phase 5 compliance features as regulated work: validate against current law, ZATCA requirements and licensed tax/accounting advice at implementation time.

## Final assessment

The repository now has the controlled backend spine for Phases 3–5: evidence-backed source documents, AR/AP/payroll/stock/expense/bank workflows, tax records/returns, financial statements, assets and budgets.  It is **not yet accepted as a completed production ERP**: external platform adapters, reconciliations, statutory/ZATCA validation, frontend workflows, production migration rehearsal and operational resilience evidence remain mandatory exit work.
