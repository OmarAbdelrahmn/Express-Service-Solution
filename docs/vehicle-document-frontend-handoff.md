# Vehicle document expiry — frontend handoff

Prepared: 2026-10-18. This document describes the implemented backend contract. UI layouts and refresh timings below are frontend recommendations.

## 1. Delivery status

- Backend implementation is in this checkout; the project test suite passed all 140 tests.
- Hosted database migration `20261018095249_AddVehicleDocumentExpiryDates` was applied on 2026-10-18. All three expiry columns were verified as nullable SQL `date` columns; no migrations remained pending at that point.
- Deployment of the updated API application has not been verified in this task. Confirm that the new paths appear in the target environment's Swagger before enabling the feature there. A database update alone does not deploy the API code.
- Use the application's existing API base URL and authenticated HTTP client. The paths below are relative to the API host; do not add a second `/api` if the client base URL already includes it.

## 2. Fields and permissions

| API document type | Arabic label | Field on the vehicle |
| --- | --- | --- |
| `Registration` | الاستمارة | `licenseExpiryDate` |
| `Insurance` | التأمين | `insuranceExpiryDate` |
| `Inspection` | الفحص الدوري | `inspectionExpiryDate` |

All three fields are `string | null`, containing Gregorian dates in `yyyy-MM-dd` format. The existing `licenseExpiryDate` field now permits null. Registration continues using that existing field name; there is no `registrationExpiryDate` field.

Null means **expiry date not recorded**, not proof that the physical document does not exist. Use `تاريخ الانتهاء غير مسجل` rather than `لا يوجد تأمين` for missing insurance dates.

Every new document endpoint requires JWT authentication and either the `Master` or `Admin` role. `Member` is not authorized for these new endpoints, even though some existing vehicle read endpoints allow it. Hide the document management navigation and edit controls from unauthorized users, and still handle a server 403.

```http
Authorization: Bearer <access-token>
Accept: application/json
```

Use `Content-Type: application/json` for JSON updates. Follow the existing login/refresh flow for 401 responses.

## 3. Endpoints and query parameters

| Method | Path | Behavior |
| --- | --- | --- |
| GET | `/api/vehicles/documents/expiring` | Dates from today through today + window, inclusive. Excludes expired and missing dates. |
| GET | `/api/vehicles/documents/missing` | Documents with no recorded expiry date. |
| GET | `/api/vehicles/documents/reminders` | Missing, expired, due today, and upcoming documents in one live feed. |
| PUT | `/api/vehicles/documents/{vehicleNumber}` | Replace all three expiry dates for one vehicle; returns the updated vehicle. |

All GET endpoints accept the same query contract:

| Parameter | Type | Default | Rules |
| --- | --- | --- | --- |
| `documentType` | `Registration`, `Insurance`, `Inspection` | Omitted = all three | Send the exact enum name. Omit for the All option; do not send `All`, `null`, or an empty string. |
| `daysBeforeExpiry` | integer | `30` | `0`–`3650`. `0` means today for the expiring endpoint. It does not change missing-date selection. |
| `page` | integer | `1` | Starts at 1. |
| `pageSize` | integer | `50` | `1`–`200`. |

Examples covering the six individual lists and combined reminders:

```http
GET /api/vehicles/documents/expiring?documentType=Registration&daysBeforeExpiry=30
GET /api/vehicles/documents/expiring?documentType=Insurance&daysBeforeExpiry=30
GET /api/vehicles/documents/expiring?documentType=Inspection&daysBeforeExpiry=30
GET /api/vehicles/documents/missing?documentType=Registration
GET /api/vehicles/documents/missing?documentType=Insurance
GET /api/vehicles/documents/missing?documentType=Inspection
GET /api/vehicles/documents/reminders?daysBeforeExpiry=30&page=1&pageSize=50
```

There are no search, location, status, custom sort, or historical `checkDate` query parameters on these new endpoints. Do not send them expecting server filtering. Client-side filtering of one downloaded page is not a complete fleet search. Use the expiring and missing endpoints for the corresponding full-result tabs; the combined reminders endpoint has no separate server-side expired-only filter.

## 4. TypeScript contracts

```ts
export type VehicleDocumentType =
  | "Registration"
  | "Insurance"
  | "Inspection";

export type VehicleDocumentStatus =
  | "Missing"
  | "Expired"
  | "DueToday"
  | "Upcoming";

export interface VehicleDocumentQuery {
  documentType?: VehicleDocumentType;
  daysBeforeExpiry?: number;
  page?: number;
  pageSize?: number;
}

export interface VehicleDocumentReminder {
  vehicleNumber: string;
  plateNumberA: string;
  location: string;
  documentType: VehicleDocumentType;
  expiryDate: string | null;
  daysUntilExpiry: number | null;
  status: VehicleDocumentStatus;
  message: string;
}

export interface VehicleDocumentReport {
  checkDate: string;
  daysBeforeExpiry: number;
  page: number;
  pageSize: number;
  totalDocuments: number;
  missingCount: number;
  expiredCount: number;
  dueTodayCount: number;
  upcomingCount: number;
  items: VehicleDocumentReminder[];
}

export interface UpdateVehicleDocumentsRequest {
  licenseExpiryDate: string | null;
  insuranceExpiryDate: string | null;
  inspectionExpiryDate: string | null;
}

export interface VehicleResponse {
  vehicleType: string;
  vehicleNumber: string;
  serialNumber: number;
  plateNumberA: string;
  ownerId: number;
  ownerName: string;
  plateNumberE: string;
  manufactureYear: number;
  manufacturer: string;
  licenseExpiryDate: string | null;
  vehicleImagePath: string | null;
  licenseImagePath: string | null;
  exstraImage: string | null;
  exstraImage1: string | null;
  createdAt: string;
  location: string;
  insuranceExpiryDate: string | null;
  inspectionExpiryDate: string | null;
}
```

Properties are camelCase. Document type and status are strings with the exact casing above. Successful document GET responses contain the report directly; there is no `{ data: ... }` or `{ isSuccess: ... }` envelope. Successful PUT responses contain `VehicleResponse` directly, not a report or a success-only message.

## 5. Example response and status rules

Synthetic example for `GET /api/vehicles/documents/reminders?daysBeforeExpiry=30&pageSize=50` with a server check date of 2026-10-18:

```json
{
  "checkDate": "2026-10-18",
  "daysBeforeExpiry": 30,
  "page": 1,
  "pageSize": 50,
  "totalDocuments": 4,
  "missingCount": 1,
  "expiredCount": 1,
  "dueTodayCount": 1,
  "upcomingCount": 1,
  "items": [
    {
      "vehicleNumber": "V-002",
      "plateNumberA": "د هـ و ٤٥٦",
      "location": "الرياض",
      "documentType": "Insurance",
      "expiryDate": null,
      "daysUntilExpiry": null,
      "status": "Missing",
      "message": "تاريخ انتهاء تأمين المركبة غير مسجل للمركبة د هـ و ٤٥٦."
    },
    {
      "vehicleNumber": "V-001",
      "plateNumberA": "أ ب ج ١٢٣",
      "location": "الرياض",
      "documentType": "Registration",
      "expiryDate": "2026-10-16",
      "daysUntilExpiry": -2,
      "status": "Expired",
      "message": "انتهى استمارة المركبة للمركبة أ ب ج ١٢٣ منذ 2 يوم."
    },
    {
      "vehicleNumber": "V-001",
      "plateNumberA": "أ ب ج ١٢٣",
      "location": "الرياض",
      "documentType": "Inspection",
      "expiryDate": "2026-10-18",
      "daysUntilExpiry": 0,
      "status": "DueToday",
      "message": "ينتهي الفحص الدوري للمركبة للمركبة أ ب ج ١٢٣ اليوم."
    },
    {
      "vehicleNumber": "V-001",
      "plateNumberA": "أ ب ج ١٢٣",
      "location": "الرياض",
      "documentType": "Insurance",
      "expiryDate": "2026-11-01",
      "daysUntilExpiry": 14,
      "status": "Upcoming",
      "message": "ينتهي تأمين المركبة للمركبة أ ب ج ١٢٣ خلال 14 يوم."
    }
  ]
}
```

Display the supplied `message` for Arabic reminder copy. Status labels and recommended visual treatments:

| Status | Meaning | Arabic label | Recommended treatment |
| --- | --- | --- | --- |
| `Missing` | No recorded expiry date; days are null | التاريخ غير مسجل | Neutral badge with document icon |
| `Expired` | Date is before `checkDate`; days are negative | منتهي | Red badge plus text/icon |
| `DueToday` | Date equals `checkDate`; days are 0 | ينتهي اليوم | Strong warning badge plus text/icon |
| `Upcoming` | Date is after `checkDate` and within the selected window | قريب الانتهاء | Amber badge plus text/icon |

The report contains documents requiring attention only. There is no `Valid`/`Safe` status in this feed; dates beyond the window are omitted. A zero result does not mean the fleet has no vehicles.

The server calculates dates using Saudi time, UTC+3. Trust `checkDate`, `daysUntilExpiry`, and `status`; do not recalculate them from the browser's timezone. Store and submit date input values as `yyyy-MM-dd`, without a timestamp or timezone. Avoid `new Date(date).toISOString()` round trips for form values. If localized display is needed, format date-only values without allowing a timezone shift.

## 6. Pagination and counts

- Each item is one vehicle-document pair. A single vehicle can appear up to three times. Use `${vehicleNumber}:${documentType}` as the row key.
- `totalDocuments` is not a count of unique vehicles.
- All four status counts describe the entire result after the endpoint/document-type/window filters, before pagination.
- `totalDocuments = missingCount + expiredCount + dueTodayCount + upcomingCount`.
- Compute total pages as `Math.ceil(totalDocuments / pageSize)`. With zero results, show an empty state and disable pagination.
- `hasNextPage = page * pageSize < totalDocuments`.
- Reset page to 1 when endpoint/tab, document type, date window, or page size changes.
- Fixed sort: missing dates first; then earliest expiry date, vehicle number, and document type (`Registration`, `Insurance`, `Inspection`). Preserve server order.
- A page beyond the last page returns an empty `items` array while retaining total counts. If a save removes the final row of the current page, fetch the previous valid page.
- A request for the missing endpoint has only missing counts. A request for the expiring endpoint has only due-today/upcoming counts. Obtain global dashboard counters from a separate unfiltered reminders request; do not overwrite them with tab-specific counts.

## 7. Screen handoff

Add an admin navigation item `وثائق المركبات`.

Recommended page:

1. Four summary cards: `منتهي`, `ينتهي اليوم`, `قريب الانتهاء`, `التاريخ غير مسجل`.
2. Tabs: `جميع التنبيهات` (reminders), `قريب الانتهاء` (expiring, including today), `تواريخ غير مسجلة` (missing).
3. Document selector: `الكل`, `الاستمارة`, `التأمين`, `الفحص الدوري`.
4. Window selector: `اليوم`, `7 أيام`, `30 يومًا`, `60 يومًا`, `90 يومًا`; map to 0/7/30/60/90. Default 30. The window controls which documents are included; it is not a saved notification schedule. Disable/hide it on the missing tab if that is clearer.
5. Columns: Arabic plate, vehicle number, location, document type, expiry date, days remaining, status, `تحديث التواريخ` action.
6. Show `غير مسجل` for a null date and `—` for null days. For negative days, show `متأخر منذ X يوم`; do not drop the sign silently or show a negative number as time remaining.
7. Use RTL layout and isolate vehicle identifiers and ISO dates with `dir="ltr"`/`bdi` as appropriate. Do not rely on color alone for status.

Loading: use skeleton rows and retain the last successful badge counts during background refresh. Empty: `لا توجد وثائق مطابقة للفلاتر الحالية`. Failure: show a retry action and preserve filters. A failed request must not be rendered as a genuine zero count.

There is no new upload endpoint or new insurance/inspection attachment field. This feature records expiry dates; policy numbers and document files are not part of its new contract.

## 8. Edit dates flow and route identifiers

Open a modal/drawer titled `تحديث تواريخ وثائق المركبة`, displaying the plate and three nullable Gregorian date pickers. Load **all three existing dates** before enabling save. A reminder row contains only its own document's date and cannot by itself populate the other two fields safely.

Available read sources:

- Use the current complete vehicle object if already loaded and fresh.
- `GET /api/vehicles` returns a vehicle array including the three dates.
- `GET /api/vehicles/with-rider/{plate}` returns one vehicle with the three dates. This route matches the **Arabic plate**, even though the underlying service method name mentions vehicle number.
- `GET /api/vehicles/chase/{vehicleNumber}` returns an array using a substring match; do not assume it is a single exact vehicle lookup.

URL-encode path values. The new save endpoint uses the **vehicleNumber**, not the plate:

```http
PUT /api/vehicles/documents/V-001
Content-Type: application/json

{
  "licenseExpiryDate": "2027-10-18",
  "insuranceExpiryDate": "2026-11-01",
  "inspectionExpiryDate": null
}
```

This is a replacement of all three dates, not a PATCH. Null **or an omitted property** clears the corresponding date. Always send all three properties explicitly, carrying forward unchanged dates. To clear a field deliberately, send null; never send an empty string. Keep the modal open if loading or saving fails, and disable duplicate saves while a request is in flight.

Past dates are accepted and may be needed to record an already expired document. Do not automatically replace them with future dates. There is no backend requirement that the three dates have any particular order.

On 200: use the returned full vehicle object to update the local vehicle cache; invalidate/refetch every document report, summary, and notification badge; close the modal and show `تم تحديث التواريخ بنجاح`. A renewed date disappears from the feed if it is outside the selected window. A renewed date within the window remains as `Upcoming`. Clearing a date changes it to `Missing` rather than removing it from the combined feed.

No concurrency/version token is provided. A stale replacement can overwrite another administrator's change; refresh the full vehicle before editing and avoid constructing payloads from old reminder rows.

## 9. Existing create/edit vehicle forms

Add `insuranceExpiryDate` and `inspectionExpiryDate` to form state, validation, request serialization, and response types. Allow `licenseExpiryDate` to be null. Show no fabricated default dates.

Existing routes:

- `POST /api/vehicles`: create a vehicle with all existing required fields plus the three optional date values.
- `PUT /api/vehicles/{plateNumber}`: full vehicle update, where the identifier is the Arabic plate. The body still requires the existing vehicle fields; adding dates does not turn it into a partial update.
- `PUT /api/vehicles/documents/{vehicleNumber}`: dedicated date-only replacement, using vehicle number.

Full vehicle updates also replace all three dates. Update older edit forms so omission of the two new date properties does not clear dates entered elsewhere. Continue preserving unchanged date values when editing an unrelated vehicle field.

## 10. Reminder badge and refresh behavior

Recommended lightweight badge request:

```http
GET /api/vehicles/documents/reminders?daysBeforeExpiry=30&page=1&pageSize=1
```

`totalDocuments` is an attention badge count including missing dates. If the product wants an expiry-only badge, use `expiredCount + dueTodayCount + upcomingCount` and display missing dates separately. Label counts as documents, not vehicles.

Recommended frontend refresh: on page entry, window focus, successful date update, and every 60 seconds while the page is visible. Stop timers when hidden/unmounted or unauthorized. Cancel stale requests when filters change. Keep a badge and its dropdown on the same date window, and include endpoint/type/window/page/pageSize in cache keys.

This is a pull-based live feed. The backend has no email/SMS/push delivery, SignalR subscription, per-user unread state, dismissal, snooze, or persisted alert threshold for this feature. Re-fetching the feed can return the same reminders. Do not display a toast for every item on every refresh. A persistent on-screen list/badge is the default integration.

## 11. Error handling

| HTTP status | Expected frontend behavior |
| --- | --- |
| 200 | Parse the direct response object. |
| 400 | Show query/form validation errors; preserve user input. |
| 401 | Follow the existing expired-session/re-authentication flow. |
| 403 | Show access denied; do not present an empty fleet. |
| 404 on document PUT | Vehicle no longer exists; refresh the list and stop retrying the stale save. |
| 500 / network failure | Keep previous data if available and offer retry. Include the correlation ID when reporting a backend error. |

Business errors use ProblemDetails with `title`, `status`, `detail`, and an `error` object. Example vehicle-not-found response (additional standard ProblemDetails fields may be present):

```json
{
  "title": "Vehicle.NotFound",
  "status": 404,
  "detail": "Vehicle was not found.",
  "error": {
    "code": "Vehicle.NotFound",
    "description": "Vehicle was not found."
  }
}
```

Service query-validation errors use title `VehicleDocuments.InvalidQuery`. ASP.NET query/body binding and range validation can instead produce a validation ProblemDetails response with an `errors` dictionary of arrays. Support both shapes. Authentication failures can have no JSON body. Unexpected-error responses include `correlationId`; the server also adds an `X-Correlation-ID` header, which browser JavaScript can read only if deployment CORS exposes it.

## 12. Excel import updates

For serial-number-only registration/inspection expiry sheets with Hijri dates, use the new dedicated `POST /api/vehicles/documents/import` endpoint. It supports `dryRun=true`, converts Umm al-Qura Hijri dates, preserves blank/dash dates, and reports each row. See [bulk expiry sheet import](vehicle-document-sheet-import.md) for its complete contract. The full-vehicle import described below is a separate workflow.

Existing admin import: `POST /api/Import/vehicles`, multipart form field `file`. Template metadata: `GET /api/Import/vehicle-template-info`. Prefer `.xlsx` files. Let the browser set the multipart boundary.

Optional columns:

| Canonical header | Supported Arabic headers |
| --- | --- |
| `LicenseExpiryDate` | `تاريخ انتهاء الرخصة`, `تاريخ انتهاء الاستمارة` |
| `InsuranceExpiryDate` | `تاريخ انتهاء التأمين`, `تاريخ انتهاء التامين` |
| `InspectionExpiryDate` | `تاريخ انتهاء الفحص`, `تاريخ انتهاء الفحص الدوري` |

Use `yyyy-MM-dd` in updated templates. The legacy exact `Expiry Date` header continues mapping to registration. New vehicles with blank dates retain null. Blank/omitted dates on existing vehicles preserve stored values; clearing a stored date requires the date-update API. No one-year registration date is invented on import anymore. Previously fabricated future registration dates cannot be distinguished automatically from genuine ones.

Unlike document reports, the existing import endpoint wraps successful results as `{ success, data, summary }`; preserve the existing import response handling. Refresh document reports after a successful import.

## 13. Acceptance checklist

- Each of Registration, Insurance, and Inspection has working expiring and missing lists using the correct enum value.
- All-document reminders can display multiple document rows for one vehicle without duplicate-key errors.
- Null dates display as unknown data, not confirmed absence of a physical document.
- Past/today/window-boundary dates produce the expected status; a date after the window is absent from expiring/reminder results.
- Window 0 returns due-today rows from the expiring endpoint.
- Pagination uses total document count; summary cards remain correct on later pages.
- Filters reset the page and stale requests cannot overwrite a newer selection.
- The editor loads all three dates; changing one preserves the other two; explicit clear sends null.
- New and existing vehicle forms retain the two new fields when saving unrelated changes.
- Save success refreshes the vehicle, lists, cards, and badge; save failure retains user input.
- Admin/Master access works; unauthenticated/Member users cannot use document management endpoints.
- Empty, loading, validation, forbidden, not-found, and network-error states are distinct.
- RTL plate/date layout, keyboard access, labeled date inputs, and non-color-only status cues work.
- Excel import handles the new date columns and blank dates preserve existing values.
- The target API application exposes the new Swagger routes before frontend release.

## 14. Backend source references

- `Application/Contracts/Employees/VehicleDocumentContracts.cs`: new report, query, enum, and update contracts.
- `Application/Contracts/Employees/VehicleRequest.cs`: updated standard vehicle contracts.
- `Express Service/Controllers/VehicleDocumentsController.cs`: new routes and permissions.
- `Application/Service/Empolyee/VehicleDocumentService.cs`: filtering, counts, Saudi check date, messages, updates.
- `Express Service/Controllers/VehicleController.cs`: existing reads and full create/update routes.
- `Express Service/ResultExtensions.cs`: business error response shape.
- `Accounting.Tests/VehicleDocumentServiceTests.cs` and `VehicleDocumentImportTests.cs`: tested boundaries and persistence behavior.
