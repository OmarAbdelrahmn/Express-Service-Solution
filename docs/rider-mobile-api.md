# Rider mobile API

Base path: `/api/rider-mobile`. All routes except login require
`Authorization: Bearer <accessToken>` from the mobile login endpoint. Staff tokens
cannot access these routes, and mobile tokens cannot authenticate to staff routes.
Every private route takes its rider identity from the signed token; the mobile app
does not send a rider ID or Iqama again to select data. Responses must not be cached.

## Routes

| Method | Route | Purpose |
| --- | --- | --- |
| POST | `/login` | Sign in using Iqama and optional language; returns session and profile |
| GET | `/me` | Rider's own profile |
| GET | `/dashboard` | Profile, current month, previous month, daily details and improvement notes |
| GET | `/performance?year=2026&month=10` | Full performance and attendance for one month |
| GET | `/shifts?year=2026&month=10` | Daily hours, orders, attendance totals and break restrictions |
| PUT | `/language` | Save preferred language; returns a replacement token and profile |
| POST | `/logout` | Revoke the current session immediately; returns HTTP 204 |

Login example:

```json
{
  "iqamaNo": "2100000001",
  "language": "bn"
}
```

Use `ar` (Arabic), `en` (English), or `bn` (Bengali/Bangla). Iqama is a string of
exactly 10 ASCII digits. Omitting language uses the rider's saved preference;
existing riders default to Arabic. The successful login response includes
`accessToken`, `tokenType` (`Bearer`), `expiresAtUtc`, `language`, and `rider`.
The rider must have an employee record with `Status = enable`, `INKSA = true`,
and `IsDeleted = false`. Unknown and ineligible riders receive the same 401 error.
Malformed bodies receive 400; excessive login requests receive 429 using the
existing login limiter (10 requests per minute per source IP, shared with staff login).

Language update example:

```json
{ "language": "ar" }
```

Replace the stored access token with the token from this response. Updating the
language revokes the previous session and creates a replacement with the **same
expiry**. Other devices keep their existing session language; subsequent logins
without a language use the new saved preference.

## Sessions and identity

Sessions last 30 days by default. Store the token in the mobile operating system's
secure storage and reuse it until logout or expiry. On 401, discard it and return
to login. This version has no refresh-token endpoint; sign in again after expiry.
Expired, revoked, deleted-employee, disabled-employee and out-of-country sessions
are rejected against the database on each request.

JWT claims include `sub` and `rider_id` (internal rider ID), `sid` (session ID),
`lang`, `purpose = RiderMobile`, `jti`, `nbf`, and `exp`. The token has a dedicated
audience and domain-separated signing key derived from the existing configured JWT
secret. No new secret needs to be copied into appsettings. Rider sessions confer no
staff roles. Logout revokes only the current device/session. Session rows contain
no raw bearer tokens and are excluded from entity auditing.

**Identity limitation:** Iqama-only sign-in is implemented as requested. An Iqama
number is an identifier rather than proof of identity: someone knowing another
rider's number can obtain that rider's mobile session. Rate limiting and token
isolation do not prevent that impersonation. Add verified-phone OTP before this
endpoint if proof of ownership is required. No OTP/SMS service is included here.

## Performance interpretation

Each monthly response contains `performance`, `coachingTargets`, `attendance`, and
`notes`, plus period dates, language and localized month name. Months are Gregorian;
the reporting day is Saudi time (UTC+3). Current month data stops at today, previous
months stop at month end, and future imported rows are excluded. Month queries must
specify a valid current or past month from year 2000 onward. The dashboard handles
January/December and leap-year boundaries automatically.

The official `rank` is the **latest imported Segment in the month**, unchanged;
`rankSource` is `latest_imported_segment`. A/B/C are never recalculated from coaching
targets. A null rank means no score has been imported for this period.

Performance imports store daily counter deltas. Therefore:

- Gross, completed, on-time, failed orders and verification counts are summed.
- `onTimeDeliveryPercentage = completedOrdersInTime / completedOrders * 100`.
- `verificationSuccessPercentage = successfulVerifications / verificationRequests * 100`.
- `completionPercentage = completedOrders / grossOrders * 100`.
- A zero denominator produces **null**, rather than a fictitious zero score.
- `onTimeDeliveryScorePercentage` and `deliveryQualityPercentage` use the latest
  platform snapshot in the period, multiplied by 100. They are not averaged across
  daily cumulative snapshots. `lastUpdatedDate` exposes the snapshot date.
- Shift accepted orders and score completed orders remain separate measures.

The advice compares measured on-time delivery, verification success and latest
quality score with independently configurable targets (95% each by default).
The numeric percentage gap is returned alongside the localized explanation and
actionable advice. Failed orders, ranks B/C, missing scores, short days, prohibited
breaks and monthly progress also produce notes. Rank A receives maintenance advice.
Meeting coaching targets does not guarantee the platform will award A.

An English note example (the same fields are translated for `ar` and `bn`):

```json
{
  "code": "OnTime",
  "severity": "problem",
  "message": "On-time deliveries: 80%; coaching target: 95%.",
  "advice": "Check the route before leaving, collect orders promptly, and report restaurant or traffic delays through the platform. Follow road safety rules.",
  "actual": 80,
  "target": 95,
  "gap": 15,
  "date": null
}
```

Render `message` and `advice` directly. `code`, `severity`, status codes and JSON
property names are stable machine-readable values. Names and company data retain
their stored spelling; Bengali uses the stored English rider name because there is
no Bengali name field. Standard ASP.NET request-validation and authentication
protocol errors retain the application's normal error format.

## Daily attendance and breaks

Attendance comes from `RiderShifts`, scoped by the authenticated internal rider ID,
including historical working IDs attached to that rider. Multiple rows for the same
calendar date are combined before counting days. The report returns every calendar
day with hours, accepted/rejected/real-rejected orders, stacked deliveries, localized
weekday/status, break-date eligibility, restriction codes and date-specific notes.
`breakRestrictionMessages` explains each calendar restriction in the session
language, including future dates, so riders can plan permitted break dates.

- Monthly target: **at least 26 distinct days of at least 8 hours each**. Extra
  hours on one day cannot replace a missing qualified day.
- `workedDays` counts days with recorded activity; `qualifiedWorkingDays` counts
  days meeting the hours target. `totalHours` includes short days as well.
- No breaks on **Thursday, Friday or Saturday**, or the **first/last 3 dates** of
  the month. Date eligibility calls the existing `KeetaBreakScheduler.IsEligible`.
- `maximumBreakDays = min(3, daysInMonth - 26)`, preserving the existing scheduler's
  maximum of 3 and enough days for the 26-day target (2 breaks in a 28-day month).
- A recorded day off requires an actual shift record with zero hours and no order
  activity. **No record means missing data, not absence or an approved break.**
  Recorded days off are reporting evidence, not approval records.
- Today remains `InProgress` until its hours target is reached. Short-hour and
  prohibited-break problems are issued only for completed dates. Future dates
  remain `Future` with no absence or short-hour problem.
- `expectedQualifiedDaysToDate` is the minimum completed qualified days needed to
  leave 26 achievable using all remaining dates, rather than demanding 26 early
  in the month. `canReachMonthlyTarget` includes today when it can still qualify.
- `isCompliant` is null while the month is ongoing or completed-day records are
  missing. For a completed fully recorded month it requires 26 qualified days,
  no short working days, no prohibited days off and no excess breaks.
- `isBreakDateAllowed` indicates the calendar rule only. `remainingBreakDays`
  indicates the rider's recorded remaining allowance. Neither approves or books
  leave, checks housing/shift staffing capacity, or changes the existing scheduler.
  Break booking and supervisor approval remain in the existing scheduling workflow.

The requested 26-day policy is applied to the full calendar month for this mobile
report, including freelancers and newly joined riders; no implicit exemptions or
proration are invented. Existing administrative report policies are unchanged.
Daily short-hour and prohibited-break notes appear both on their calendar day and
in the month's combined `notes` list for the improvement screen.

## Configuration and deployment

Optional configuration (defaults shown):

```json
{
  "RiderMobile": {
    "SessionDays": 30,
    "OnTimeDeliveryTargetPercentage": 95,
    "VerificationTargetPercentage": 95,
    "DeliveryQualityTargetPercentage": 95
  }
}
```

Session duration must be 1–90 days; targets must be 0–100. Options are validated at
startup. Normal `Jwt` settings remain required from secure configuration.

The `AddRiderMobileSessions` EF migration adds `RiderDetails.PreferredLanguage`
(backfilled to `ar`) and the `RiderMobileSessions` table with rider/expiry index and
a restrictive rider foreign key, following the context's existing delete policy. Apply it through the normal deployment workflow before
serving mobile requests on a new deployment.
An idempotent SQL deployment script is included in
[`rider-mobile-migration.sql`](rider-mobile-migration.sql). It applies only this
migration. The immediately preceding migration is `AddDailyWorkEntries` from the
existing working-tree work. Apply preceding migrations first if the deployed schema
is older; the script assumes their schema and migration-history table already exist.

Verification:

```powershell
dotnet test Accounting.Tests/Accounting.Tests.csproj --filter "FullyQualifiedName~RiderMobile"
dotnet test Accounting.Tests/Accounting.Tests.csproj
dotnet ef migrations has-pending-model-changes --project Domain/Domain.csproj --startup-project "Express Service/Express Service.csproj"
```

Tests cover service decisions and real HTTP authentication, authorization, ownership,
language replacement, revocation and rate limiting with an in-memory EF provider.
They do not prove production SQL Server migration execution or database concurrency.

Verified on this change: solution build succeeded; all 28 rider-mobile cases passed
as part of the full suite (209 passed, 5 skipped); EF reported no pending model
changes; the generated SQL adds only the language column, session table and index.
The build still reports existing repository warnings.

Hosted database deployment on 20 October 2026: applied
`20261020105936_AddRiderMobileSessions` to the configured hosted database after
confirming it was the only pending migration. Read-only checks verified the
preferred-language column, all six session columns, foreign key, index, migration
history, and valid existing rider-language values. No pending migrations remain.
This deployment updated the database schema only; it did not deploy the API host.
