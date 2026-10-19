# Petrol follow-up — 19 October 2026

## Requested behavior

The user confirmed that only unknown plates should be excluded from “not attached.” Registered vehicles remain eligible for that list, including returned vehicles whose petrol has no rider assignment. Unknown petrol costs are retained in daily and summary reports; no financial records are deleted to hide an exception.

The code now filters the assignment queue to registered vehicles. Daily reports expose `totalUnknownVehicleRows` and `unknownVehicleCost` separately from `totalUnattributedRows`. Full vehicle summaries retain separate identities for unknown plates instead of merging them into one empty vehicle row.

## AD3686: committed employee assignment

The current employee for AD3686 already has rider profile 306, so the repair reused it. The user explicitly requested assigning the pending petrol to this employee. Three pending costs were assigned to Ahmed Mohamed Ibrahim Elsayed as audited manual overrides:

| Petrol date | Source ID | Amount |
|---|---:|---:|
| 2026-10-01 | 35932 | SAR 78.24 |
| 2026-10-02 | 36244 | SAR 8.65 |
| 2026-10-03 | 36546 | SAR 15.00 |
| Total | | SAR 101.89 |

The recorded possession starts on 4 October. The repair preserves that history and records the user's explicit correction as manual attribution for the three earlier dates. Previously assigned dates and historical holders were preserved. A fresh connection confirmed zero unassigned rows for AD3686 after commit.

The transaction preserved all 193 earlier manual overrides and the complete source and allocation monetary totals. Backups are `dbo.PetrolEmployeeCosts_20261019_072146_66f624` and `dbo.PetrolEmployeeAllocations_20261019_072146_66f624`. The local receipt is `artifacts/petrol-repair-2026-10-19/ad3686-assignment.applied.json`.

## Minimal employee profiles

Explicit manual petrol assignment now creates a missing employee rider profile in the same transaction as the allocation. Existing profiles are reused. Optional working ID, license and shirt-size fields remain empty. A required company reference uses a neutral `Unassigned` company until actual details are provided. The employee's employment classification is preserved.

A profile receives a current vehicle only when one distinct active take exists and no other profile occupies that vehicle. Failed or cancelled assignment discards the staged profile and any new company as well as the petrol changes. No current active employee vehicle holder was missing a rider profile in the final database audit.

## Remaining exceptions and diagnostics repair

Another 44 unassigned rows conflict with manual petrol charges on other vehicles for the matching riders and dates. The code and database notes now identify the conflicting source IDs through `ManualOverrideConflict` and `conflictingVehiclePetrolCostIds`. A backed-up transaction refreshed these 44 diagnostic notes without changing amounts, identities, dates, attribution or any of the 196 manual overrides now present.

Diagnostic backups are `dbo.PetrolDiagnosticSources_20261019_072458_74a451` and `dbo.PetrolDiagnosticAllocations_20261019_072458_74a451`. The receipt is `artifacts/petrol-repair-2026-10-19/diagnostics-refresh.applied.json`.

Resolving those conflicts requires confirming the earlier manual charge or the actual vehicle switch. Missing historical assignments also require verified employee/date information. Neither existing manual charges nor possession history were invented or overwritten.

## Final database verification

The latest petrol date is 17 October 2026. A fresh read after both transactions found:

| Check | Result |
|---|---:|
| Source rows | 32,807 |
| Source cost | SAR 580,052.01 |
| Allocation cost | SAR 580,052.01 |
| Sources missing allocations | 0 |
| Sources with unbalanced allocations | 0 |
| Allocations with mismatched vehicle/date | 0 |
| Automatic allocations outside assignment history | 0 |
| Current profile assignments without an active take | 0 |
| Active employee vehicle holders without rider profiles | 0 |

| Remaining unassigned reason | Rows | Amount |
|---|---:|---:|
| Unknown plate — excluded from assignment queue | 432 | SAR 45,390.43 |
| Registered vehicle returned before petrol date | 177 | SAR 3,015.34 |
| Registered vehicle unavailable | 7 | SAR 102.00 |
| Missing assignment history | 94 | SAR 5,132.57 |
| Conflicting manual charge | 44 | SAR 640.81 |

The registered-vehicle queue therefore contains 322 unassigned allocation rows across all dates. The actual endpoint returns the requested month. Four fleet plates account for 92 of the 94 missing-history rows: LGA5577, GUR4762, UBR8475 and DJR6306.

Detailed audit evidence is retained locally at `artifacts/petrol-repair-2026-10-19/final.audit.json`; the independent AD3686 verification is `vehicle-3686-after.json` in the same folder. These files contain operational identifiers and are ignored by Git.

## Validation and deployment

The complete solution test run passed **158 tests, zero failures and zero skips**, including native SQL Server tests using session-local temporary tables. Coverage includes unknown-plate exclusion, retaining registered returned vehicles, distinct unknown-plate summaries, manual-conflict diagnostics, minimal profile creation, reuse and failed-save cleanup.

The database corrections above are committed. The API code has not been deployed to the hosted service; deployment is still required for the changed queue, report fields and automatic minimal-profile behavior to appear there.
