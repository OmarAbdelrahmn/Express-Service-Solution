# Vacation workflow endpoints

All endpoints require a bearer token. Vacation permissions are independent of Identity roles.

## Member (`Member` Identity role)

- `POST /api/member/vacation-requests` — `{ "riderId": 1, "startDate": "2026-08-01", "endDate": "2026-08-10" }`.
- `GET /api/member/vacation-requests` — all vacation history for riders in the member's housing.
- `GET /api/member/vacation-riders?fromDate=2026-08-01&toDate=2026-08-31` — approved or active vacation riders; both dates are optional and default to today.
- `POST /api/member/vacation-requests/{id}/date-change` — `{ "startDate": "2026-08-02", "endDate": "2026-08-11", "reason": "Flight changed" }`.
- `POST /api/member/vacation-requests/{id}/cancellation` — `{ "reason": "Rider remains available" }`.

## Approval inbox

- `GET /api/vacation-requests/inbox` returns requests currently actionable by the caller's vacation assignments.
- `POST /api/vacation-requests/{id}/decisions` — `{ "decision": 1, "reason": "Approved after roster review" }`. `1` is approved and `2` is rejected. The server selects the current Operation, Accountant, or Administration stage.

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

Role values are `1` Operation, `2` Accountant, `3` Administration, and `4` HR. One user may hold more than one vacation role.

The HR ticket/visa frontend contract, multipart requests, response fields, statuses, and document-view rules are documented in [vacation-hr-frontend-contract.md](vacation-hr-frontend-contract.md).
