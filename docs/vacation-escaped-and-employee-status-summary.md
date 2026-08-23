# Vacation, Escaped Employee, and Employee-Status Summary

## Conclusion

The project separates three related HR concerns clearly:

- **Vacation** is a controlled, multi-stage workflow with approvals, amendments,
  HR documents, and lifecycle dates.
- **Escaped/fleeing employees** are a compliance workflow with either a
  reported-to-authorities path or an outage/exit path, each with a removal
  deadline.
- **Employee status history** records every direct or approved status change
  independently from the two workflows.

## Vacation model

`VacationRequest` is the root entity. It can represent either a rider or a
non-rider employee and contains vacation dates, request details, current
status, HR status, lifecycle timestamps, cancellation details, and a row
version for concurrency control.

Related entities:

| Entity | Purpose |
|---|---|
| `VacationUserRoleAssignment` | Vacation-only approver roles; does not change ASP.NET Identity roles. |
| `VacationApprovalDecision` | Immutable decision history by workflow role. |
| `VacationDateChangeRequest` | Proposed date amendments with approval/rejection state. |
| `VacationCancellationRequest` | Cancellation request and resolution history. |
| `VacationHrDocument` | Versioned ticket and exit/re-entry visa files; superseded files remain as audit history. |

### Main workflow

```text
Member creates request
  -> PendingOperation
  -> PendingAccountant
  -> PendingAdministration
  -> Approved
  -> Active
  -> Completed
```

The model also supports `Rejected`, `Cancelled`, `Expired`, and
`PendingKeetaManager`. A decision can be approved, rejected, or returned to a
selected earlier role. HR processing then tracks ticket and exit/re-entry visa
completion separately from the operational approval status.

### Vacation APIs

| Area | Route |
|---|---|
| Member request/list | `POST` / `GET /api/member/vacation-requests` |
| Member date change/cancellation | `POST /api/member/vacation-requests/{id}/date-change` and `/cancellation` |
| Admin request list | `GET /api/vacation-requests` |
| Approver inbox | `GET /api/vacation-requests/inbox` |
| Detail/decision | `GET /api/vacation-requests/{id}`, `POST /{id}/decisions` |
| Master cancellation | `POST /api/vacation-requests/{id}/cancel` |
| HR document access | `/api/vacation-requests/{id}/documents/{documentId}/stream` or `/download` |

## Escaped employee model

`EscapedEmployeeDetails` is a one-to-one compliance record for an employee.
It supports exactly one active path at a time:

| Path | Trigger | Deadline |
|---|---|---|
| `Reported` | Report filed with authorities (`ReportedAt`) | 60 days after report date. |
| `Outage` | Country exit/system outage (`DateOfOutage`) | 60 days after outage date. |

The entity stores the escaped date, active path, outage visa number, calculated
removal deadline, remaining days, 10-day notification state, notes, audit
fields, and a soft-deactivation state. Switching paths clears the previous
path data so both cannot be active together.

### Escaped employee APIs

Base route: `/api/escaped`

- List, detail, statistics, overdue list, and filter by path.
- Set `reported`, set `outage`, or switch the active path.
- Update notes, deactivate, remove, or force-delete the record.
- Backfill legacy employees marked `fleeing` into this model.

## Employee status model

### Current system status

`Employees.Status` is the system's current workforce-status field. It is a
free-text string, defaults to `enable`, and is used across operational logic.
For example, vehicle take and switch operations require the rider status to be
`enable`; a `fleeing` status is also used by the escaped-employee backfill and
report filters. The model does not currently define a single C# enum or a
database constraint for all allowed status values.

Status changes can occur in two ways:

| Path | Flow |
|---|---|
| Direct update | An employee update changes `Employees.Status` immediately. |
| Approval request | A member requests a new status; the request is stored in `TempEmployeeStatusChange` and Master/Admin resolves it through `/api/temp`. |

The status-request endpoints are:

- `POST /api/temp/request-change` — Member submits an employee status change.
- `GET /api/temp/employee-pending-status-changes` — Master/Admin reviews pending changes.
- `POST /api/temp/employee-resolve-status-changes` — Master/Admin approves or rejects a change.

`EmployeeStatusLog` is the audit history for changes to `Employees.Status`.
Every row stores the employee iqama, old/new status, actor, change time,
reason, and source (`StatusRequest` for approval-based changes or
`DirectUpdate` for direct rider updates).

This model complements vacation and escaped records: it records the employee's
general operational status while vacation tracks approved leave and escaped
records track compliance/removal processing.

## Key implementation note

Vacation has the strongest workflow controls: dedicated roles, decisions,
row-version concurrency, amendments, cancellation history, and versioned HR
documents. Escaped employee handling is focused on compliance deadlines and
two mutually exclusive paths. Employee status logs provide the cross-cutting
audit trail for workforce state changes.

## Source files

- `Domain/Entities/Vacation/VacationEntities.cs`
- `Application/Contracts/Vacation/VacationContracts.cs`
- `Express Service/Controllers/VacationRequestsController.cs`
- `Express Service/Controllers/MemberVacationController.cs`
- `Domain/Entities/EscapedEmployeeDetails.cs`
- `Express Service/Controllers/EscapedEmployeeController.cs`
- `Domain/Entities/EmployeeStatusLog.cs`
