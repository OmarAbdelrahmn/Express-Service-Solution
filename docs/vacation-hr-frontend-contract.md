# Vacation HR frontend contract

This contract extends the rider vacation workflow after Operation, Accountant, and Administration have all approved. All endpoints require the normal bearer token. `HR` is a vacation-only assignment and does not change the user's Identity login roles.

## Workflow displayed to the user

The existing vacation `status` and the new `hr.status` are separate:

| `hr.status` | Name | Suggested Arabic label |
|---:|---|---|
| `0` | `PendingApproval` | بانتظار اكتمال الموافقات |
| `1` | `AwaitingTicket` | تمت الموافقة - بانتظار حجز التذكرة |
| `2` | `AwaitingExitReentryVisa` | تم حجز التذكرة - بانتظار تأشيرة خروج وعودة |
| `3` | `Completed` | تم حجز التذكرة وإصدار التأشيرة |
| `4` | `Closed` | مغلق |

The normal vacation status remains:

| Value | Name |
|---:|---|
| `1` | `PendingOperation` |
| `2` | `PendingAccountant` |
| `3` | `PendingAdministration` |
| `4` | `Approved` |
| `5` | `Active` |
| `6` | `Completed` |
| `7` | `Rejected` |
| `8` | `Cancelled` |
| `9` | `Expired` |

Once Administration approves, `fullyApprovedAt` is populated and `hr.status` becomes `1`. The request appears in the HR inbox.

## HR role assignment

Master assigns HR through the existing vacation-access API. The same user may also hold Operation, Accountant, or Administration.

```http
PUT /api/vacation-access/users/{userId}
Content-Type: application/json

{
  "roles": [1, 2, 4]
}
```

Role values: `1` Operation, `2` Accountant, `3` Administration, `4` HR. The call replaces all vacation roles for that user.

## HR inbox

```http
GET /api/vacation-hr/inbox
Authorization: Bearer {token}
```

Only a user assigned vacation role `4` may call this endpoint. It returns fully approved requests that are still waiting for either task.
Requests with a pending member date-change or cancellation request are temporarily removed from the HR inbox; HR uploads return `Vacation.WorkflowPaused` until Master resolves the amendment.

Response: `200 OK`

```json
[
  {
    "id": "9d86e8b8-1684-46d0-a754-77b3e700ce78",
    "rider": {
      "riderId": 15,
      "iqamaNo": 2456789012,
      "nameAR": "اسم السائق",
      "nameEN": "Rider Name",
      "workingId": "R-15",
      "housingId": 3,
      "housingName": "Riyadh Housing"
    },
    "startDate": "2026-08-10",
    "endDate": "2026-09-10",
    "status": 4,
    "currentRole": null,
    "fullyApprovedAt": "2026-07-28T12:30:00",
    "hr": {
      "status": 1,
      "ticketCompleted": false,
      "exitReentryVisaCompleted": false,
      "documents": []
    }
  }
]
```

The real response also includes requester, approval decisions, date changes, cancellation history, and lifecycle timestamps from `VacationRequestResponse`.

## Upload or replace the ticket

```http
POST /api/vacation-hr/{vacationRequestId}/ticket
Authorization: Bearer {token}
Content-Type: multipart/form-data

file: (binary PDF/image)
completed: true
```

- `file` is required on every call.
- `completed` is the checkbox value.
- `completed=false` saves the uploaded file as a draft and keeps the request at `AwaitingTicket`.
- `completed=true` marks ticket booking complete and moves the request to `AwaitingExitReentryVisa`.
- Uploading again creates a new version and marks the previous current ticket as superseded.

## Upload or replace the exit/re-entry visa

```http
POST /api/vacation-hr/{vacationRequestId}/exit-reentry-visa
Authorization: Bearer {token}
Content-Type: multipart/form-data

file: (binary PDF/image)
completed: true
```

- The ticket must already be completed.
- `completed=false` saves a visa draft.
- `completed=true` marks the visa task complete and changes `hr.status` to `Completed`.
- Uploading again creates a new version and preserves the previous visa as superseded history.

Accepted extensions are `.pdf`, `.jpg`, `.jpeg`, `.png`, and `.webp`. The maximum file size is 20 MB. The backend determines the trusted content type from the extension.

Both upload endpoints return `200 OK`:

```json
{
  "vacation": {
    "id": "9d86e8b8-1684-46d0-a754-77b3e700ce78",
    "status": 4,
    "fullyApprovedAt": "2026-07-28T12:30:00",
    "hr": {
      "status": 2,
      "ticketCompleted": true,
      "exitReentryVisaCompleted": false,
      "documents": [
        {
          "id": "e725de48-6ead-44d4-9e61-324442009010",
          "type": 1,
          "version": 1,
          "fileName": "ticket.pdf",
          "contentType": "application/pdf",
          "fileSize": 180442,
          "uploadedByUserId": "user-id",
          "uploadedByName": "HR User",
          "uploadedAt": "2026-07-28T13:00:00",
          "isCompleted": true,
          "completedAt": "2026-07-28T13:00:00",
          "isSuperseded": false,
          "supersededAt": null,
          "supersededReason": null,
          "streamUrl": "/api/vacation-requests/9d86e8b8-1684-46d0-a754-77b3e700ce78/documents/e725de48-6ead-44d4-9e61-324442009010/stream",
          "downloadUrl": "/api/vacation-requests/9d86e8b8-1684-46d0-a754-77b3e700ce78/documents/e725de48-6ead-44d4-9e61-324442009010/download"
        }
      ]
    }
  },
  "document": {
    "id": "e725de48-6ead-44d4-9e61-324442009010",
    "type": 1,
    "version": 1,
    "fileName": "ticket.pdf",
    "isCompleted": true,
    "isSuperseded": false
  }
}
```

Document type values are `1` Ticket and `2` ExitReentryVisa.

## Member visibility, stream, and download

The existing member endpoint now includes the `hr` object and all current/superseded document versions:

```http
GET /api/member/vacation-requests
```

Use each document's returned URL:

```http
GET /api/vacation-requests/{vacationRequestId}/documents/{documentId}/stream
GET /api/vacation-requests/{vacationRequestId}/documents/{documentId}/download
```

Send the bearer token for both. `stream` supports browser/PDF/image viewing and HTTP range requests. `download` returns the original filename. Access is allowed only to:

- the Member managing the rider's housing;
- a user with the vacation HR assignment;
- Admin or Master.

The physical files are stored below:

```text
wwwroot/vacation-documents/{vacationRequestId}/{ticket|exit-reentry-visa}/
```

Direct static access to `/vacation-documents/...` is blocked. The frontend must use the authorized API URLs.

## Return-date extension rule

When Master approves a date-change request whose new `endDate` is later than the current `endDate`:

1. the existing current exit/re-entry visa becomes `isSuperseded=true`;
2. the ticket remains completed;
3. `hr.exitReentryVisaCompleted` becomes `false`;
4. `hr.status` becomes `2` (`AwaitingExitReentryVisa`);
5. the vacation returns to the HR inbox so HR can upload a new visa version.

The old visa remains in `hr.documents` for audit and can still be viewed or downloaded by authorized users.

## Important error responses

Errors use Problem Details. Read `title` as the stable error code and `detail` as the displayable message.

| HTTP | `title` | Meaning |
|---:|---|---|
| `400` | `Vacation.InvalidDocument` | Empty, unsupported, or larger than 20 MB |
| `403` | `Vacation.AccessDenied` | User lacks HR/document/housing access |
| `404` | `Vacation.NotFound` | Vacation request not found |
| `404` | `Vacation.DocumentNotFound` | Document row or physical file not found |
| `409` | `Vacation.HrNotReady` | Three approvals are not complete or request is closed |
| `409` | `Vacation.TicketRequired` | Visa task attempted before ticket completion |
| `409` | `Vacation.ConcurrentUpdate` | Another user changed the request; refresh and retry |
