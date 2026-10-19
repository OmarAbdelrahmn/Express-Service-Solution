# Vehicle document expiry and reminders

The vehicle API tracks three optional expiry dates:

| Document type | Vehicle field |
| --- | --- |
| `Registration` (استمارة) | `licenseExpiryDate` (existing field) |
| `Insurance` (تأمين) | `insuranceExpiryDate` |
| `Inspection` (فحص دوري) | `inspectionExpiryDate` |

Dates use `yyyy-MM-dd`. `null` means **no recorded expiry date**; it does not prove the physical document does not exist. Legacy `0001-01-01` values are treated as missing. All three fields are supported in vehicle creation, update, and the standard vehicle responses.

## Read endpoints

All document endpoints require the `Master` or `Admin` role.

| Method and path | Result |
| --- | --- |
| `GET /api/vehicles/documents/expiring` | Expiry today through today + the window, inclusive. Already expired documents are excluded. |
| `GET /api/vehicles/documents/missing` | Missing expiry dates for any of the three documents. |
| `GET /api/vehicles/documents/reminders` | Live reminder feed combining missing, expired, due today, and upcoming documents. |

Common query parameters:

- `documentType`: `Registration`, `Insurance`, or `Inspection`. Omit to include all three.
- `daysBeforeExpiry`: default `30`, allowed `0`–`3650`. Zero means due today.
- `page`: default `1`.
- `pageSize`: default `50`, maximum `200`.

Examples for each document:

```http
GET /api/vehicles/documents/expiring?documentType=Registration&daysBeforeExpiry=30
GET /api/vehicles/documents/expiring?documentType=Insurance&daysBeforeExpiry=30
GET /api/vehicles/documents/expiring?documentType=Inspection&daysBeforeExpiry=30
GET /api/vehicles/documents/missing?documentType=Registration
GET /api/vehicles/documents/missing?documentType=Insurance
GET /api/vehicles/documents/missing?documentType=Inspection
GET /api/vehicles/documents/reminders?daysBeforeExpiry=30&page=1&pageSize=50
```

Reports contain `checkDate` (Saudi calendar day), `daysBeforeExpiry`, paging information, `totalDocuments`, and counts for `missing`, `expired`, `dueToday`, and `upcoming` documents. Counts cover the entire filtered result before paging. Each item includes the vehicle number, Arabic plate, location, document type, expiry date, days remaining (negative when expired, null when missing), status, and an Arabic reminder message. A vehicle can appear once for each document requiring attention. Missing documents sort first, followed by earliest expiry, vehicle number, and document type.

The reminder endpoint follows the existing maintenance dashboard approach: the frontend requests the feed on dashboard load/refresh or polls it for a badge/list. The backend computes current reminders on every request; renewing a document removes its old reminder immediately. This repository contains the backend only; this change supplies the reminder API, without an email/SMS delivery channel or a frontend widget.

## Update only the document dates

```http
PUT /api/vehicles/documents/VEHICLE_NUMBER
Content-Type: application/json

{
  "licenseExpiryDate": "2027-10-18",
  "insuranceExpiryDate": "2026-11-01",
  "inspectionExpiryDate": null
}
```

This replaces all three dates; null/omitted fields clear the corresponding date. It returns the updated vehicle, or 404 when the vehicle number does not exist. Existing full vehicle create/update endpoints also accept the two new fields; full updates replace all three dates.

## Excel imports

Vehicle imports recognize optional `LicenseExpiryDate`, `InsuranceExpiryDate`, and `InspectionExpiryDate` columns, including Arabic headers. Missing dates on newly imported vehicles stay null. Blank/omitted dates on existing vehicles preserve their recorded dates; use the document update endpoint to clear a date. Imports no longer invent a one-year registration expiry. Previously invented dates cannot be distinguished from real dates and should be reviewed by an administrator.

## Deployment

EF migration `20261018095249_AddVehicleDocumentExpiryDates` was applied to the hosted database on 2026-10-18. All three nullable date columns were verified, and no migrations remained pending at that point. It makes `Vehicles.LicenseExpiryDate` nullable, adds nullable insurance/inspection columns, and converts legacy minimum registration dates to null. No future expiry dates are backfilled. Deployment of the updated API application has not been verified. Other environments must apply this migration before deploying the updated API. A rollback drops the two new columns and converts null registration dates to the previous minimum-date representation; retain document data before rolling back.

See [the frontend handoff](vehicle-document-frontend-handoff.md) for TypeScript contracts, UI flows, request/response examples, reminder integration, and acceptance criteria.
