# Petrol system diagnosis — 18 October 2026

This records the findings before repair. The subsequent code changes and committed database repair are documented in [the repair report](petrol-repair-2026-10-18.md).

The two reported symptoms have concrete causes in the assignment importer and petrol timeline. The database was inspected using SELECT queries and untracked reads only. No application code, schema, assignments, or petrol charges were changed.

## 1. Assignment imports return vehicles that are present in the sheet

In `Application/Service/Import/ImportService.cs:8356`, `ImportVehicleAssignmentsAsync` builds its Excel lookup using `rowData.VehicleNumber` (the vehicle/chassis identifier). At line 8393, the cleanup step instead tests `current.PlateNumberA` against that lookup. These are different identifiers. The actual assignment step uses `VehicleNumber` again at line 8561.

The resulting sequence is:

1. A vehicle/chassis identifier is present in Excel.
2. Its Arabic plate is not a key in the Excel lookup.
3. Cleanup writes a Returned event and clears the rider's vehicle.
4. The later assignment loop assigns the vehicle again.

Each return commits before the later assignment is validated and committed. An invalid row, a partial sheet, or a later failure can therefore leave previously assigned vehicles returned. Even a complete valid sheet can generate unnecessary return/take history.

Database evidence: 333 distinct vehicles have an `Auto-returned by system sync - Not in import sheet` return recorded on 12 January 2026. All 295 current assignments have different chassis and Arabic plate identifiers. The three examples below contain an auto-return followed seconds later by a new active assignment.

The evidence establishes the identifier mismatch and unnecessary returns in those examples. It does not establish that every historical auto-return was incorrect.

## 2. An old return closes a newer active assignment in petrol

`Application/Service/Petrol/PetrolAssignmentTimeline.cs:37` sorts events by `EffectiveTime`. For Taken/switched events, line 102 substitutes `PermissionStartDate` for the event's recorded `Timestamp`. Returned events retain their recorded timestamp.

A newly recorded assignment can have a backdated permission start. Sorting by that start moves the new take before an older return. The old return then closes the new active assignment at line 59.

Confirmed example, plate **BE7298**:

| Event | Recorded timestamp | Permission start |
|---|---|---|
| Auto-return, status 685 | 12 January 2026, 14:28:03 | — |
| New active take, status 1021 | 12 January 2026, 14:28:05 | 6 July 2025 |

The newer take is processed as a July 2025 event, and the January return closes it. Petrol therefore finds no rider on 17 October 2026 even though both the current rider record and active Taken status show an assignment.

The same pattern was confirmed for **BE7284** and **AD6017**. Running the existing timeline builder over the stored history produces closed windows for **57 active assignments** as of 17 October. October contains **441 unassigned allocation rows for vehicles that currently have riders**; that count is a symptom count, not proof that every historical row should belong to today's rider.

This explains why another attribution retry can reproduce the problem: the resolver derives the same incorrect end date from the same history.

## 3. The unassigned report mixes distinct situations

`Application/Service/Petrol/PetrolService.cs:816` selects saved allocation rows with `RiderIqamaNo == null`. It does not distinguish a returned vehicle, a timeline defect, missing history, and an unresolved plate in its response structure.

Example: **BE7238** was returned on 19 September, has no current rider, and has a SAR 15 fuel cost on 17 October. That is an unmatched cost after a recorded return. It should be presented as a returned-vehicle exception for review. It must not be automatically charged to a current or former rider without evidence.

The report also hides four hardcoded plates at lines 113 and 836. Across the database, those exclusions hide **89 unassigned allocation rows totaling SAR 4,906.57** from this report.

## Other confirmed database inconsistencies

These are separate from the main assignment failure:

| Check | Result |
|---|---:|
| Source vehicle-cost records | 33,130 |
| Source cost total | SAR 580,052.01 |
| Allocation total, including unassigned | SAR 554,579.53 |
| Difference | SAR 25,472.48 |
| Source records with no allocation | 263, totaling SAR 25,515.48 |
| Existing sources with overallocations | 45, totaling SAR 45.00 excess |
| Existing sources with underallocations | 2, totaling SAR 2.00 shortfall |
| Duplicate resolved vehicle/date groups | 265 |
| Source records with unresolved vehicle plates | 442 |
| Null-rider allocations incorrectly marked attributed at source | 204 |

The difference reconciles as `25,515.48 missing + 2.00 shortfall − 45.00 excess = 25,472.48`.

The 263 missing allocations are all unresolved-vehicle sources from April–August. An earlier importer saved raw costs before allocations without a surrounding transaction; this provides a historical failure path consistent with partial data, but logs are needed to establish the exact cause of each missing allocation. The current importer saves costs and allocations together.

The duplicate groups contain 317 rows beyond one row per vehicle/date. For example, AJ6149 has four source records for 5 October, uploaded together, with costs 14.26, 14.48, 15.00, and 88.74. Original workbooks are needed to decide whether these represent repeated import rows or multiple genuine fuel transactions that need aggregation. Do not remove them based only on the duplicate count. Current code rejects repeated normalized plates; the database has no unique vehicle/date constraint.

The current SQL cost columns are `decimal(18,2)`, the nullable vehicle column is present, and the money-precision migration is applied. An unapplied petrol schema migration does not explain the reported assignment symptoms. Older petrol migrations used zero decimal places, and pre-July source values have no fractional amounts; any recovery of lost fractions would require original workbooks.

## Recommended correction order

1. Use canonical vehicle identifiers consistently in assignment import comparison. Validate the entire import before changing assignments. Make omission-based returns an explicit full-snapshot operation; partial updates must not imply returns. Preserve unchanged assignments rather than manufacturing another return/take pair.
2. Correct timeline event ordering so a return recorded before a newer take cannot close that take merely because its permission was backdated. Preserve legitimate historical coverage, real returns, and same-day switches. Separate possession event order from permit validity dates.
3. Add regression cases based on the January return/reassignment sequence. Also verify that an unchanged assignment import does not create return events and that invalid or partial imports do not clear unrelated assignments. Use SQL Server integration coverage for transactions and constraints.
4. Preview allocation changes for affected vehicle/date records, retain manual overrides, and compare totals before applying a targeted repair. A global rebuild with the current faulty resolver is not a repair.
5. Separate report reasons: unknown plate, missing assignment history, returned before report date, assignment conflict, and stale allocation. Show the relevant status dates rather than one generic unassigned label.
6. Reconcile historical missing allocations and duplicate source records against original uploads. Then enforce the agreed source-record uniqueness and cost-conservation rules.

## Verification performed

- Connected to the SQL Server database selected by this checkout's `appsettings.json` and inspected its petrol schema, migration history, assignment history, and reconciliation totals. The deployed application's effective configuration was not independently inspected.
- Reproduced the assignment-window defect by executing the existing `PetrolAssignmentTimeline.Build` against untracked database history. No write service was invoked.
- Ran `dotnet test Accounting.Tests/Accounting.Tests.csproj --filter FullyQualifiedName~PetrolServiceTests --no-restore --verbosity quiet`: **30 passed**. These tests use EF InMemory and omit the confirmed old-return/new-backdated-take sequence, so their passing result does not rule out the defect or establish SQL transaction behavior.
- Existing unrelated working-tree changes were preserved.

Reconciliation queries are retained in `docs/diagnostics/petrol-read-only-checks.sql`.
