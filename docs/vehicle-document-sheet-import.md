# Bulk registration and inspection expiry update

## Endpoint

```http
POST /api/vehicles/documents/import?dryRun=true
Authorization: Bearer <token>
Content-Type: multipart/form-data; boundary=<browser-generated>
```

Requires `Master` or `Admin`. Upload one `.xlsx` file in multipart field `file`. Maximum file size: 10 MB; maximum expanded workbook size: 100 MB; maximum data-row span: 5,000 rows. Old binary `.xls` files are not supported.

Query parameters:

| Parameter | Default | Meaning |
| --- | --- | --- |
| `dryRun` | `false` | `true` returns proposed changes without changing tracked vehicle dates or saving data. |
| `worksheetName` | Automatic detection | Required only when more than one worksheet has a matching header. Exact worksheet name, case-insensitive. |

Re-upload the file with `dryRun=false` to save after preview. A preview is not a stored import session; the apply request rechecks current database values.

## Spreadsheet layout

The supplied layout is supported directly:

| الرقم التسلسلي | تاريخ انتهاء رخصة السير | تاريخ انتهاء الفحص |
| --- | --- | --- |
| 819465020 | 1448-08-03 | 1449-03-28 |
| 335822020 | 1451-03-26 | - |

Match existing vehicles by `Vehicle.SerialNumber`. The import updates **only** `licenseExpiryDate` and `inspectionExpiryDate`; it does not create vehicles or modify insurance, assignment, location, or plate data.

Supported headers, in any column order:

| Field | Headers |
| --- | --- |
| Serial number, required | `الرقم التسلسلي`, `الرقم التسلسلى`, `SerialNumber`, `Serial Number`, `Serial No` |
| Registration expiry | `تاريخ انتهاء رخصة السير`, `تاريخ انتهاء الاستمارة`, `تاريخ انتهاء الرخصة`, `LicenseExpiryDate`, `License Expiry Date`, `Registration Expiry Date` |
| Inspection expiry | `تاريخ انتهاء الفحص`, `تاريخ انتهاء الفحص الدوري`, `InspectionExpiryDate`, `Inspection Expiry Date` |

At least one expiry column is required. A missing expiry column preserves that document's existing date. Header discovery checks the first 20 rows. Spaces, underscores, letter case, and Unicode direction marks are ignored when matching headers. Duplicate recognized headers reject the file.

## Dates and validation

- The supplied text dates are Hijri. Text years from 1300 through 1600 are interpreted as Hijri and must be within the .NET Umm al-Qura calendar's supported date range. Conversion happens before storage in the existing Gregorian date fields.
- Example: `1448-08-03` becomes `2027-01-11`; `1449-03-28` becomes `2027-08-30`.
- Gregorian text years must be 1900 or later. Supported text layouts are year-first `yyyy-MM-dd` / `yyyy/MM/dd` and day-first `dd/MM/yyyy` / `dd-MM-yyyy`. Slash-separated `01/02/2027` means February 1, not January 2.
- True Excel date cells and numeric Excel serial dates are accepted as Gregorian, including a workbook's 1904 date system. Prefer unambiguous ISO text or true date cells.
- Arabic and Persian decimal digits are normalized in serial numbers and text dates.
- Blank dates and `-`, `—`, or `–` mean **no date supplied** and preserve stored values. This endpoint does not clear dates.
- Rows with no supplied dates return `Skipped`. Fully blank document/serial rows are omitted from the report.
- Invalid dates reject the entire row: neither registration nor inspection is changed for that row.
- Serial numbers must be positive whole numbers within the entity's `int` range. Leading zeros on text serials are normalized numerically. Fractional serials and formula cells are rejected.
- Every occurrence of a repeated serial number in the selected worksheet is rejected. A serial matching multiple database vehicles returns `Ambiguous`; no vehicle in that group is changed.
- Vehicles not found are reported and are not created. Valid independent rows can still be applied.

All accepted updates use one audited database save. Rejected rows are excluded; a database failure fails the save rather than returning a successful import report. Cancellation is propagated. No schema migration is needed.

## Response

HTTP 200 returns the report directly, including when some or all individual rows are rejected. Inspect the row statuses and counts. Workbook-level errors return HTTP 400 ProblemDetails (`VehicleDocuments.InvalidSheet` or `VehicleDocuments.InvalidFile`). Standard authentication/authorization and unexpected-error handling also apply.

Synthetic preview example:

```json
{
  "worksheetName": "vehicle_list",
  "dryRun": true,
  "totalRows": 1,
  "updatedCount": 0,
  "wouldUpdateCount": 1,
  "unchangedCount": 0,
  "skippedCount": 0,
  "notFoundCount": 0,
  "invalidCount": 0,
  "ambiguousCount": 0,
  "rows": [
    {
      "rowNumber": 2,
      "serialNumber": 819465020,
      "vehicleNumber": "EXISTING-VEHICLE-ID",
      "plateNumberA": "أ ب ج ١٢٣",
      "status": "WouldUpdate",
      "changes": [
        {
          "field": "licenseExpiryDate",
          "previousValue": null,
          "newValue": "2027-01-11",
          "sourceValue": "1448-08-03",
          "sourceCalendar": "Hijri"
        },
        {
          "field": "inspectionExpiryDate",
          "previousValue": null,
          "newValue": "2027-08-30",
          "sourceValue": "1449-03-28",
          "sourceCalendar": "Hijri"
        }
      ],
      "errors": []
    }
  ]
}
```

Status values:

| Status | Meaning |
| --- | --- |
| `Updated` | Matched vehicle's supplied date(s) changed and were saved. |
| `WouldUpdate` | Preview only: changes are proposed, not applied. |
| `Unchanged` | All supplied dates already equal stored Gregorian values. |
| `Skipped` | Both date values are blank/dashes; no change requested. |
| `NotFound` | No vehicle matches the supplied serial. |
| `Invalid` | Invalid serial/date/formula or repeated serial in the worksheet. |
| `Ambiguous` | Multiple existing vehicles share the supplied serial. |

`rowNumber` is the actual worksheet row number. `changes` lists only dates that differ from current values. `previousValue`/`newValue` are Gregorian dates. `sourceValue` retains the supplied value; `sourceCalendar` is `Hijri` or `Gregorian`. `vehicleNumber` and `plateNumberA` are null when a vehicle could not be uniquely resolved. Counters sum to `totalRows`.

## Frontend integration

```ts
async function importVehicleExpiryDates(
  apiOrigin: string,
  token: string,
  file: File,
  dryRun = true,
  worksheetName?: string,
  signal?: AbortSignal
) {
  const query = new URLSearchParams({ dryRun: String(dryRun) });
  if (worksheetName) query.set("worksheetName", worksheetName);
  const form = new FormData();
  form.append("file", file);
  const response = await fetch(
    `${apiOrigin.replace(/\/$/, "")}/api/vehicles/documents/import?${query}`,
    {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
      body: form,
      signal
    }
  );
  const body = await response.json().catch(() => null);
  if (!response.ok) {
    throw new Error(body?.detail ?? `Import failed (${response.status})`);
  }
  return body;
}
```

Use the API origin above, or adapt the path for an existing HTTP client whose base URL already includes `/api`. Let the browser set multipart Content-Type and its boundary.

Show a preview table with worksheet row, serial, plate, field, source date/calendar, previous Gregorian date, new Gregorian date, status, and errors. Apply by re-uploading the same file with `dryRun=false`. Disable duplicate submissions while applying. A successful HTTP response can contain rejected rows; do not display every row as successful. Refresh vehicle lists, document reminders, counts, and badges after application. Re-uploading unchanged data returns `Unchanged`.

This endpoint is separate from the older full-vehicle import at `/api/Import/vehicles`. Use this new endpoint for the supplied serial-and-expiry-only sheet and its Hijri dates.

## Verification

The supplied 502-row worksheet was exercised against locally seeded in-memory vehicles in dry-run mode: 502 registration dates and 250 inspection dates converted from Hijri, zero invalid rows, and no tracked date changes. This validates parsing and preview behavior, not matching against hosted vehicle records. The project test suite completed with 167 passed, 5 skipped, and zero failed tests. Focused coverage includes Gregorian/Hijri dates, both Excel date systems, duplicate and unknown serials, row rejection, unchanged reuploads, dry-run behavior, and Swagger generation. Deploy the updated API application to expose this endpoint; existing date columns require no new migration.
