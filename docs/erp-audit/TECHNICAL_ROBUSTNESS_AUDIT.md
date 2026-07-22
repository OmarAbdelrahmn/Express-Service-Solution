# Technical Robustness Audit

## Baseline verification

- `codex/accounting-module-20260624` at `ac44e61`: solution build succeeded with 11 warnings; 20 accounting tests passed.
- Build reported `Microsoft.OpenApi` 2.4.1 under high-severity advisory [GHSA-v5pm-xwqc-g5wc](https://github.com/advisories/GHSA-v5pm-xwqc-g5wc).
- Live `master` worktree had concurrent uncommitted API-versioning/role/config edits and was excluded from stable baseline conclusions.

## Findings

| ID | Priority | Evidence | Risk | Correction/acceptance |
|---|---|---|---|---|
| T-001 | P0 | Accounting branch is six local commits ahead of remote; accounting is absent from `master`. | Reviewed code may be lost or deployed from the wrong branch. | Publish exact revision, resolve PR/merge strategy, tag audit baseline and require CI on merge commit. |
| T-002 | P0 | Restore/build reports affected Microsoft.OpenApi 2.4.1. | Crafted OpenAPI parsing can terminate a process where untrusted documents are parsed. | Upgrade/pin patched dependency, inspect transitive source and rerun build, tests and dependency scan. |
| T-003 | P0 | `ApplicationDbcontext` globally forces decimals to `decimal(38,0)` on `master`; only accounting fields are selectively reconfigured to `(18,2)` on branch. | Cents/rounding are lost in operational money feeding accounting. | Explicit money value objects/precision per domain, migration data analysis, rounding policy and boundary tests. |
| T-004 | P1 | Working and shift durations use `float`; time uses repeated `UtcNow.AddHours(3)`. | Precision and daylight/timezone semantics are inconsistent; backdating/cutoffs can drift. | Store minutes/seconds as integers or decimal, UTC timestamps + `DateOnly` business date + configured IANA/Windows zone. |
| T-005 | P0 | No business `RowVersion`/concurrency tokens; posting duplicate checks are read-then-insert. | Lost updates, overspend, duplicate payments/postings under concurrency. | Row versions, unique invariants, transaction isolation and concurrency tests for posting/stock/payments/imports. |
| T-006 | P1 | `ImportService`, `ReportService`, `MemberService` and vehicle service are multi-thousand-line classes. | High regression risk, duplicated rules and untestable boundaries. | Split bounded-context application services and pure policies; keep controller-service pattern but isolate parsers/posting/report queries. |
| T-007 | P1 | Accounting import loads full workbook and materializes every cell; historical Keeta detail is around 162k rows. | Memory/CPU exhaustion and long SQL transactions. | Stream rows, batch insert, parser isolation, bounded limits, progress checkpoint and resumable batch state. |
| T-008 | P1 | Background imports use `Task.Run`, static dictionaries and temp JSON, then delete after two hours. | State loss on restart, no multi-instance safety, uncontrolled retry and weak auditability. | Durable queue/job table, lease/heartbeat, outbox, idempotent checkpoints and retention policy. |
| T-009 | P1 | EF pending-model warnings are globally ignored. | Code/model drift can reach runtime unnoticed. | Remove suppression from production/CI; fail CI on pending model changes and validate migration snapshot. |
| T-010 | P1 | Migrations include opaque names and historical seeded password-hash churn; accounting adds several large snapshots/migrations. | Review/rollback and data-conversion risk. | Baseline migration policy, named migrations, data preflight, backup/restore rehearsal, expand-migrate-contract deployment. |
| T-011 | P1 | FluentValidation/Mapster registration scans the web executing assembly while validators/mappings live in Application. | Validation/mapping may silently not run. | Register the Application assembly explicitly and add startup tests that resolve all validators/mappings. |
| T-012 | P1 | Numerous catches return raw `ex.Message`; debug endpoints and console logging expose internals. | Sensitive data/SQL/file paths leak and errors are inconsistent. | Central exception handling, public error catalog, structured redacted logs and correlation IDs. |
| T-013 | P1 | Accounting list/report queries frequently materialize all rows; API pagination is inconsistent. | Large periods produce slow queries and memory pressure. | Cursor/page contracts, projections, query plans/index validation, read replicas/materialized reporting where justified. |
| T-014 | P1 | Reports recompute totals in many services from mutable tables. | Same metric has different definitions and stale/mismatched totals. | Semantic reporting layer based on posted journal/stock/payroll facts; versioned metric definitions and reconciliation tests. |
| T-015 | P1 | File attachments/static uploads lack content hash/integrity lifecycle; source accounting workbook binary is not retained. | Evidence cannot be reproduced and files can be orphaned/tampered. | Content-addressed private storage, hash/size/MIME, retention/legal hold and database linkage. |
| T-016 | P1 | No documented backup, restore, RPO/RTO, monitoring or financial alerts. | Data loss/fraud/failed jobs may be undetected. | Encrypted backups, point-in-time restore drills, RPO/RTO, posting imbalance/import/payment alerts and runbooks. |
| T-017 | P2 | No frontend repository or end-to-end tests in scope. | Permissions, workflows and user-visible state cannot be verified. | Bring frontend into audit scope; contract tests plus critical accounting E2E flows. |

## Migration risks

- Current `Company` rows must be migrated to platform/customer accounts without treating them as legal entities.
- Current operational wallet/bill/stock totals require opening-balance reconciliation, not direct copying into the ledger.
- Accounting branch migrations must be tested against a sanitized production-size database, including rollback and re-run.
- Decimal scale conversion must identify truncated historical values before changing precision.
- Seed IDs used as accounting constants require stable mapping or replacement with account codes/routing profiles.

## Required nonfunctional gates

- import of the largest expected workbook completes within an agreed memory/time budget and can resume after interruption;
- concurrent duplicate upload/approval/payment/stock tests create one result only;
- no formula/reference errors in exported bank/cash workbooks and CSV/Excel injection is neutralized;
- ledger reports meet performance targets with at least three years of production-scale data;
- restore drill reproduces a closed period and its source files, journals, audit trail and bank references;
- observability dashboards alert on unbalanced posting attempts, unresolved imports, stale jobs, duplicate fingerprints, payment mismatch and reconciliation difference.
