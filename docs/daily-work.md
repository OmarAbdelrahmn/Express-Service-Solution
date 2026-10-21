# Daily Work API Guide

Separate daily orders and working hours for riders/employees. These records do not change rider shifts, payroll, or platform imports. Access requires the existing `Master` or `Admin` role.

## Endpoints

Requests require authentication with the `Master` or `Admin` role.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| GET | `/api/daily-work?date=2026-10-20` | Get the saved list for a date. |
| PUT | `/api/daily-work` | Save or edit entries for a date. |

Only two endpoints are exposed:

- `GET /api/daily-work?date=2026-10-20` returns the date and saved items, ordered by employee Iqama number. A date with no records returns an empty `items` array.
- `PUT /api/daily-work` saves/edits the supplied entries for one date and returns that date's full saved list. Entries absent from the request are preserved. The first PUT can save a new entry; subsequent PUTs update it. There are no POST or DELETE endpoints.

## Save or edit entries

Send `Content-Type: application/json` and the authorization header used by the application.

PUT body:

```json
{
  "date": "2026-10-20",
  "items": [
    { "riderId": 10, "orders": 15, "workingHours": 8.25 },
    { "employeeIqamaNo": 1000000002, "orders": 7, "workingHours": 4 }
  ]
}
```

Supply exactly one identifier per request item: `riderId` is `RiderDetails.Id` (the database ID), and `employeeIqamaNo` is `Employees.IqamaNo`. Responses include both identifiers, with a null rider ID for an employee without rider details. When submitting a response item for editing, retain only one identifier.

| Field | Type | Usage |
| --- | --- | --- |
| `date` | string | Date in `YYYY-MM-DD` format. |
| `items` | array | Between 1 and 1000 entries. |
| `items[].riderId` | integer or null | Positive rider database ID; supply this or `employeeIqamaNo`. |
| `items[].employeeIqamaNo` | integer or null | Positive employee Iqama number; supply this or `riderId`. |
| `items[].orders` | integer | Nonnegative number of orders. Send the desired value on each edit. |
| `items[].workingHours` | number | Hours from 0 to 24 with at most two decimal places. Send the desired value on each edit. |

Each supplied entry overwrites both its orders and working hours. For example, sending the same rider and date again with `orders: 20` updates that entry to 20 orders; it does not add 20 to the previous value.

## Successful response

Both GET and PUT return HTTP `200` with the full saved list for the requested date:

```json
{
  "date": "2026-10-20",
  "items": [
    {
      "riderId": 10,
      "employeeIqamaNo": 1000000001,
      "orders": 15,
      "workingHours": 8.25
    },
    {
      "riderId": null,
      "employeeIqamaNo": 1000000002,
      "orders": 7,
      "workingHours": 4
    }
  ]
}
```

An empty date returns HTTP `200`:

```json
{
  "date": "2026-10-20",
  "items": []
}
```

The response is ordered by employee Iqama number. Rider IDs in the response reflect the employee's current rider details.

## Validation and errors

Records are unique per date and employee, regardless of which identifier was used to save them. Requests containing the same employee twice, including via different identifiers, fail before any changes are tracked. Unknown people return 404. Invalid dates/items return 400. Orders must be nonnegative; working hours must be between 0 and 24, with at most two decimal places. Each edit accepts 1–1000 items. The entire request is validated before a single database save.

| HTTP status | Error code | Meaning |
| --- | --- | --- |
| 400 | `DailyWork.InvalidDate` | Date is missing or has the default value. |
| 400 | `DailyWork.InvalidItems` | Items are null, empty, or exceed 1000 entries. |
| 400 | `DailyWork.InvalidItem` | An item is null, its identifiers are invalid, or its orders/hours are outside the allowed values. |
| 400 | `DailyWork.DuplicateEmployee` | Multiple items resolve to the same employee. |
| 404 | `DailyWork.PersonNotFound` | At least one supplied rider or employee does not exist. |
| 401 | Authentication failure | Authentication is missing or invalid. |
| 403 | Authorization failure | The authenticated user does not have an allowed role. |

Service errors use the application's standard `ToProblem()` response mapping. Malformed JSON and invalid date formats are handled by ASP.NET Core model binding.

## Database setup

The `DailyWorkEntries` table stores `Date`, `EmployeeIqamaNo`, `Orders`, and `WorkingHours`. Its composite primary key is `(Date, EmployeeIqamaNo)`. An employee foreign key prevents entries from referencing missing employees.

Apply the `AddDailyWorkEntries` EF migration through the normal deployment process before using these endpoints. For the intended configured database, run from the repository root:

```powershell
dotnet ef database update --project Domain/Domain.csproj --startup-project "Express Service/Express Service.csproj"
```

This command applies pending migrations to the database configured for the startup project.

## Implementation files

- Controller: `Express Service/Controllers/DailyWorkController.cs`
- Service: `Application/Service/DailyWork/DailyWorkService.cs`
- Interface: `Application/Service/DailyWork/IDailyWorkService.cs`
- Contracts: `Application/Contracts/DailyWork/DailyWorkContracts.cs`
- Errors: `Application/Abstraction/Errors/DailyWorkErrors.cs`
- Entity: `Domain/Entities/DailyWorkEntry.cs`
- Configuration: `Domain/EntitiesConfigrations/DailyWorkEntryConfigration.cs`
- Migration: `Domain/Migrations/20261020083747_AddDailyWorkEntries.cs`
- Tests: `Accounting.Tests/DailyWorkServiceTests.cs`

Run the focused service tests with:

```powershell
dotnet test Accounting.Tests/Accounting.Tests.csproj --filter FullyQualifiedName~DailyWorkServiceTests
```
