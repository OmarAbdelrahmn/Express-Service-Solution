# Petrol repair — 18 October 2026

## Code changes

- `ImportVehicleAssignmentsAsync` now resolves and compares canonical chassis/vehicle identifiers consistently. Every workbook row is validated before writes. A partial import preserves omitted assignments. Explicit `POST /api/Import/vehicle-assignments?fullSnapshot=true` performs omission-based returns only after full validation.
- A repeated assignment preserves the existing take event and timestamp. Reassignments and vehicle swaps release old unique vehicle references and create new assignments inside one serializable SQL transaction. A later failure or cancellation rolls the operation back.
- The petrol timeline processes recorded return/take events in their recorded order. Backdated permissions can extend historical coverage but cannot move a later take before an older return or overlap completed possession windows.
- Re-attribution retains allocation IDs and creation timestamps when the financial result and attribution provenance are unchanged. Manual overrides survive recalculation.
- Unassigned results now include `vehiclePetrolCostId`, `vehicleNumber`, `reasonCode`, `statusType`, and `statusRecordedAt`. Reasons distinguish returned vehicles, unavailable vehicles, missing history, and assignment conflicts/stale allocations. Costs from the four previously hidden registered plates are visible. The [19 October follow-up](petrol-follow-up-2026-10-19.md) excludes unknown plates from this assignment queue while retaining their costs in daily and summary reports.
- `EnforcePetrolDailyUniqueness` adds filtered unique indexes for resolved vehicle/date and unresolved plate/date source records.

## Data repair design

The operator tool is `tools/PetrolRepair`. It uses the configured database from `ConnectionStrings__DefaultConnection`, falling back to this checkout's `Express Service/appsettings.json`. It never prints the connection string.

Run from the repository root:

```powershell
dotnet run --project tools/PetrolRepair -- plan
dotnet run --project tools/PetrolRepair -- rehearse
dotnet run --project tools/PetrolRepair -- apply
dotnet run --project tools/PetrolRepair -- verify
```

The default plan location is `artifacts/petrol-repair-2026-10-18/plan.json`; an explicit plan path can be supplied as the second argument. These local artifacts include operational identifiers and are ignored by Git.

Planning reads a consistent database snapshot, retains an original JSON backup, and runs the corrected service against an isolated EF InMemory copy. Repeated source rows for one vehicle/date are consolidated by summing their amounts. No uploaded amount is guessed away; retained source notes list original source IDs. The original workbooks are still needed to establish whether any uploaded transaction amount itself was erroneous.

Rehearsal and application reject a changed baseline using a SHA-256 snapshot fingerprint. They lock the petrol tables, create full SQL backup copies of both petrol tables, stage the preview, update existing allocations, preserve manual override IDs, and apply only this repair's EF migration within the same transaction. An audit event records the snapshot hash, SQL backup table names, and repair summary. Every source must balance to its allocations, flags must agree with allocation state, and original manual override amounts, riders, notes, and timestamps must remain intact.

The SQL rehearsal checks the actual unique constraints using duplicate inserts, then rolls back and confirms that the original snapshot is restored. A failed assertion before commit rolls the entire repair back. SQL identity counters can advance during a rolled-back rehearsal; no business records from that rehearsal are committed.

## Committed repair

Applied to the connected SQL Server database on **18 October 2026 at 16:42 Riyadh time**. A fresh connection subsequently verified the committed data against the reviewed plan. The unique-index migration was applied in the same transaction as the repair.

| Check | Before | After commit |
|---|---:|---:|
| Source rows | 33,130 | 32,807 |
| Source cost | SAR 580,052.01 | SAR 580,052.01 |
| Allocation cost | SAR 554,579.53 | SAR 580,052.01 |
| Sources missing allocations | 263 | 0 |
| Unassigned allocation rows | 847 | 757 |
| Manual override rows | 193 | 193 |
| Unresolved plate sources | 442 | 432 |

The repair consolidates 323 extra source rows and changes the rider/cost breakdown of 1,199 retained sources. BE7298, BE7284, and AD6017 are attributed on 17 October using the corrected history.

Unresolved costs belong to 13 distinct plates. The tool retains them for review rather than inventing vehicles or riders. A local plate/count list is saved at `artifacts/petrol-repair-2026-10-18/unresolved-plates.csv`.

## Application and recovery evidence

The application result is recorded in `artifacts/petrol-repair-2026-10-18/plan.applied.json`. The preserved SQL backup tables are:

- `dbo.PetrolRepairVehicle_20261018_134118_AC374897`
- `dbo.PetrolRepairRider_20261018_134118_AC374897`

The manifest contains the commit timestamp, baseline hash, exact backup names, and final repair summary. The successful `verify` read the committed database again and matched it with the reviewed plan. Assignment status history was preserved.

Validation completed with **153 tests passed, zero failures, zero skips**, including three native SQL Server integration tests. The solution build and EF model/migration consistency check passed, as did the SQL repair rehearsal with complete rollback. `Accounting.Tests/PetrolSqlServerTests.cs` covers canonical chassis matching, unchanged takes, partial versus full snapshots, swaps under a unique assignment index, and rollback of returns, assignments, and audit rows after an injected second-save failure. These tests use session-local temporary tables and do not write application tables. Set `PETROL_SQL_TEST_CONNECTION` to enable them; without it they are explicitly skipped.

For recovery, retain the JSON baseline, reviewed plan, application manifest, and both SQL backup tables. Restoring duplicate source records requires reverting this repair's unique-index migration first. Restore sources before their allocation foreign keys, preserve original IDs using identity insertion, and perform restoration in one transaction. Check for post-repair user activity before restoring anything; a blind restore would overwrite newer work. No recovery operation is automatic.

The source fix must be deployed to the API for future uploads and assignment imports to use the corrected behavior. Applying the database repair does not deploy the API.
