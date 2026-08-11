# Vacation workflow endpoints

All endpoints require a bearer token. Vacation permissions are independent of Identity roles.

## Member (`Member` Identity role)

- `POST /api/member/vacation-requests` — `{ "riderId": 1, "startDate": "2026-08-01", "endDate": "2026-08-10", "memberNotes": "Flight details shared by the rider" }`. `memberNotes` is optional, limited to 1,000 characters, and is displayed to the vacation approvers/supervisor on every vacation-request response.
- `GET /api/member/vacation-requests` — all vacation history for riders in the member's housing.
- `GET /api/member/vacation-riders?fromDate=2026-08-01&toDate=2026-08-31` — approved or active vacation riders; both dates are optional and default to today.
- `POST /api/member/vacation-requests/{id}/date-change` — `{ "startDate": "2026-08-02", "endDate": "2026-08-11", "reason": "Flight changed" }`.
- `POST /api/member/vacation-requests/{id}/cancellation` — `{ "reason": "Rider remains available" }`.

## Frontend response change: member notes

Every response that contains a `VacationRequestResponse` now includes `memberNotes` next to the vacation dates. It is the optional note entered by the housing member for the rider's supervisor/approvers; it is `null` when no note was supplied.

This applies to the create response and to the member request list, approval inbox, HR inbox, vacation detail, and Master/Admin vacation list responses. Display it as read-only in all supervisor/approval views.

## Approval inbox

- `GET /api/vacation-requests/inbox` returns requests currently actionable by the caller's vacation assignments.
- `POST /api/vacation-requests/{id}/decisions` — `{ "decision": 1, "reason": "Approved after roster review" }`. `1` is approved and `2` is rejected. `3` returns the request for rework and requires an earlier workflow stage: `{ "decision": 3, "targetRole": 1, "reason": "Operations must correct the roster details." }`. The server selects the current Keeta Manager, Operation, Accountant, or Administration stage.

The approval sequence is conditional on the rider's company when the request is created:

- `RiderDetails.CompanyId == 2`: Keeta Manager -> Operation -> Accountant -> Administration.
- every other company: Operation -> Accountant -> Administration.

The selected sequence is represented by the vacation status and does not change if the rider's company is edited after the request starts. `PendingKeetaManager` is status value `10`; its `currentRole` is `5`.

### Return for rework

- Only the user assigned to the current approval stage can return a request.
- `targetRole` must be an earlier stage in this request's workflow: Keeta Manager (`5`), Operation (`1`), Accountant (`2`), or Administration (`3`). HR cannot be a target.
- A return changes the request back to the selected pending stage, adds the required reason to `decisions`, and requires approval to replay from that stage through Administration.
- Existing approvals from the selected stage onward are retained as audit history but returned with `isSuperseded: true`; earlier approvals remain effective. Decision history is chronological.
- Every vacation request response includes `availableReturnRoles`, the valid backward destinations for its current stage. An invalid target returns `Vacation.InvalidReturnTarget`.

## Admin and Master oversight

- `GET /api/vacation-requests?status=&stage=&riderId=&fromDate=&toDate=&page=1&pageSize=50`.
- `GET /api/vacation-requests/{id}`.
- `GET /api/vacation-date-changes` and `GET /api/vacation-cancellations`.

Master alone resolves date changes and member cancellation requests with `POST /api/vacation-date-changes/{id}/decision` or `POST /api/vacation-cancellations/{id}/decision`, both accepting `{ "decision": 1|2, "reason": "..." }`.

Master can cancel directly through `POST /api/vacation-requests/{id}/cancel` with `{ "reason": "..." }`.

## Vacation-only role administration

Master manages role assignments through:

- `GET /api/vacation-access`
- `PUT /api/vacation-access/users/{userId}` — `{ "roles": [1, 2] }`

Role values are `1` Operation, `2` Accountant, `3` Administration, `4` HR, and `5` Keeta Manager. One user may hold more than one vacation role.

The HR ticket/visa frontend contract, multipart requests, response fields, statuses, and document-view rules are documented in [vacation-hr-frontend-contract.md](vacation-hr-frontend-contract.md).
