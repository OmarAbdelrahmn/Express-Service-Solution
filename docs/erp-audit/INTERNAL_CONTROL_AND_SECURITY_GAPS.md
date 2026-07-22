# Internal Control and Security Gaps

## Findings

| ID | Severity | Confirmed evidence | Misuse/failure scenario | Required control and acceptance criteria |
|---|---|---|---|---|
| S-001 | P0 | `AccountingController` grants `Master,Admin,Accountant` to import, resolve, approve, reverse, manage rules, create/send/confirm payments and close/reopen periods. | One accountant creates a file, approves it, changes salary rules, pays it and reopens the period. | Separate preparer, reviewer, poster, payroll approver, payment preparer, payment releaser and period controller. System must reject self-approval and conflicting-role actions. |
| S-002 | P0 | Accounting actions repeatedly use `User.GetUserId() ?? "system"`; member actions fall back to `"member"`; other imports use `"System"` or hard-coded `"omar"`. | Missing/invalid identity is recorded as a shared actor, destroying accountability. | No fallback for interactive financial actions. Require subject ID, legal entity and correlation ID; dedicated service principals only for scheduled jobs. |
| S-003 | P0 | `ImportController` has about 45 actions and one authorize attribute; `ShiftController` has 20 actions with authorization commented; `MemberController`, `KetaValidation`, `KetaFreeLancer`, `DebugController` and `HistoryController` have no authorization attribute. | Anonymous/client user imports, updates, deletes or exports sensitive rider/employee data. | Default/fallback authorization policy, explicit permission per action and automated endpoint authorization tests. |
| S-004 | P0 | `Program.cs` exposes Hangfire dashboard with `Authorization = []`. | Unauthenticated user views jobs, arguments, failures or triggers operational jobs depending on dashboard capabilities. | Admin-only dashboard authorization, network restriction, CSRF protection and dashboard audit. |
| S-005 | P0 | Tracked `appsettings.json` contains database, JWT and SMTP secrets. | Repository or artifact reader accesses production services; old commits remain exploitable. | Rotate all secrets immediately, use managed secret storage/environment injection, secret scanning and history remediation. Never copy secret values into reports. |
| S-006 | P0 | `Company` is not a tenant/legal-entity boundary; generic accounting routes fetch imports/salaries by ID without legal-entity scoping. | Accountant for one book reads/posts another book or platform. | Legal-entity claims and row-level authorization on every query/mutation/export; cross-entity tests must return not found/forbidden. |
| S-007 | P1 | Import uploader can later approve; issue resolver can approve; salary generator can approve; no `CreatedBy != ApprovedBy` checks. | Fabricated or manipulated workbook is self-approved. | Maker-checker state machine with immutable actor/time/comment evidence and amount/platform approval matrices. |
| S-008 | P1 | Rule/type CRUD is available to the same broad accounting role and effective rules are selected without a rule-approval state. | Accountant changes 500-order rule before payroll and restores it later. | Versioned, effective-dated configurations with draft/review/approved states, diff audit and no retroactive change to closed periods. |
| S-009 | P1 | Bank export, batch send and batch confirmation are all on the same controller/role. | Same user creates bank file and marks it paid without independent bank evidence. | Payment preparation, release and reconciliation roles; bank acknowledgment/file hash/reference required for confirmation. |
| S-010 | P1 | Audit log is a normal EF table and records limited actor/time/value JSON; no request/device/correlation/legal-entity fields or tamper control. | Privileged user edits/deletes evidence or actions cannot be tied to a request. | Append-only audit store, DB permissions, hash chaining/WORM export, actor/service principal, source IP/device/correlation and legal-entity fields. |
| S-011 | P1 | Controllers/services physically delete shifts, Hunger records and other operational facts; posted-source linkage is absent. | Operational history backing payroll/reporting disappears after accounting use. | Referenced operational facts become immutable/versioned; corrections supersede/reverse; prohibit delete when referenced by an import/posting. |
| S-012 | P1 | Employee documents are written under public `wwwroot/uploads`; static files are enabled globally. | A predictable/leaked URL exposes identity or employment documents without object authorization. | Private object storage, randomized keys, malware/content validation, encrypted access, signed short-lived download and per-document permission. |
| S-013 | P1 | Swagger is enabled unconditionally; CORS includes production origins; configuration does not show environment hardening. | Attackers enumerate sensitive endpoints or exploit weak deployment defaults. | Environment-gated Swagger, strict CORS, security headers, API gateway/WAF and production configuration tests. |
| S-014 | P1 | File uploads usually validate extension only; many lack bounded decompression/row/cell limits. | Malicious/oversized XLSX causes memory/CPU exhaustion or formula/payload hazards. | MIME/signature/ZIP-bomb checks, limits, isolated parser process, formula-neutral raw storage and antivirus scanning. |
| S-015 | P1 | Background import uses in-process `Task.Run`, static job dictionaries and shared temp JSON with no durable actor/tenant envelope. | Restart loses job/audit state; stale job ID may expose result; retries duplicate mutations. | Durable queue, import batch state, outbox, explicit actor/legal entity/correlation, leased worker and idempotent retry. |
| S-016 | P2 | Password policy length is six; unique email and confirmed email are disabled; lockout is commented. | Password spraying/account takeover is easier for privileged finance users. | MFA for privileged users, stronger password/lockout/session policies, refresh-token rotation and security-event alerts. |

## Sensitive-role model

Minimum backend permissions:

- `AccountingImportPrepare`, `AccountingImportResolve`, `AccountingImportApprove`.
- `PayrollPrepare`, `PayrollApprove`, `PayrollReverse`, `SalaryConfidentialRead`.
- `PaymentPrepare`, `PaymentRelease`, `PaymentConfirm`, `BankReconcile`.
- `JournalPrepare`, `JournalApprove`, `JournalPost`, `JournalReverse`.
- `PeriodClosePrepare`, `PeriodCloseApprove`, `PeriodReopen`.
- `TaxConfigure`, `TaxSubmit`, `ChartManage`, `AuditRead`, `FinancialExport`.

Conflicting permissions cannot be held simultaneously without an emergency break-glass grant that is time-limited, reasoned and alerted.

## Immediate security response

1. Rotate database/JWT/SMTP secrets and invalidate old tokens.
2. Restrict Hangfire, debug/import/member/shift and document routes at the network/gateway level.
3. Freeze accounting writes and bank/cash release until role separation exists.
4. Publish and scan the exact accounting branch revision; it is six commits ahead of its remote.
5. Upgrade the affected Microsoft.OpenApi dependency to a patched version compatible with the solution; the relevant advisory identifies 2.7.5 and 3.5.4 as patched lines.
6. Add endpoint inventory/authorization integration tests before reopening APIs.

## Acceptance evidence

- a machine-readable endpoint-permission matrix with no unclassified sensitive action;
- tests for anonymous, wrong role, wrong legal entity, self-approval and break-glass expiry;
- secret scanner clean across current tree and remediated history;
- dashboard/document/download access tests;
- immutable audit events for every create/resolve/approve/post/reverse/pay/export/close/config change.
