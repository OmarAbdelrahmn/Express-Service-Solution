# Backend notification setup

The backend implements the contract in file 01 for Android FID destinations:

| Method | Route | Authentication |
| --- | --- | --- |
| PUT / DELETE | `/api/rider-mobile/devices/{installationId}` | Current rider mobile session |
| POST | `/api/admin/riders/{riderId}/notifications` | Staff + `Notifications.Send` |
| POST | `/api/admin/notifications/broadcasts` | Staff + `Notifications.Broadcast` |
| GET | `/api/admin/notifications/{requestId}` | Staff + scoped `Notifications.Read` |

Staff commands require a UUID `Idempotency-Key` header. Replays with the same
canonical content return the original request; changed content returns 409.
Unknown, disabled or out-of-scope riders return 404. Accepted requests return
202 and a Location for status polling. Firebase acceptance does not establish
device display or read status. Examples of request bodies remain in file 01.

## Firebase and hosting configuration

1. In Firebase project settings, verify project `express-service-fcbe5`, project
   number `647652633668`, and Android app `com.fastexpress.express_mobile`.
2. Enable **Firebase Cloud Messaging API** in Google Cloud APIs & Services for
   this project. The Web Push VAPID public key is not used for this Android flow.
3. On Google Cloud, attach a runtime service account with FCM send permission.
   Elsewhere, generate a Firebase Admin service-account JSON under Firebase
   **Project settings > Service accounts > Generate new private key**. Store it
   outside the repository and web root and restrict file access to the app identity.
4. Set `GOOGLE_APPLICATION_CREDENTIALS` in the backend process environment to
   the protected JSON file's absolute path. Never put the JSON in Flutter assets,
   appsettings, logs, source control, or an HTTP request.
5. Configure a persistent data-protection key folder. Device FIDs are encrypted
   using ASP.NET Core Data Protection; losing its keys requires devices to register
   again. Grant the application identity access and preserve this folder during
   deployments. On Windows, stored keys use machine DPAPI by default. Multiple
   Windows hosts must share the key ring and a certificate configured through
   `Notifications__DataProtectionCertificateThumbprint`, with its private key
   accessible to each app identity. On Linux, protect the mounted key folder using
   host permissions/storage encryption or configure a certificate.
6. Set these environment variables (paths are examples for IIS/Windows):

```text
GOOGLE_APPLICATION_CREDENTIALS=C:\ProgramData\ExpressService\Secrets\firebase-admin.json
Notifications__ProjectId=express-service-fcbe5
Notifications__DataProtectionKeyPath=C:\ProgramData\ExpressService\DataProtectionKeys
Notifications__Enabled=true
```

`Notifications:Enabled` defaults to false, allowing deployment and API testing
without Firebase credentials. Requests persist and expand while sending is
disabled. Configure the permanent key ring before accepting real device
registrations. Firebase credentials are loaded lazily and a credential error
does not break rider login/report requests. `/health` includes a notifications
check; it verifies local provider initialization, not remote API permission or
actual device delivery. Use a real test device for those checks.

Allow outbound HTTPS to Google authentication/FCM services. Keep Hangfire's
existing SQL connection configured. For IIS, ensure the application pool stays
running and the application is preloaded; otherwise jobs stop when the host stops.

## Staff authorization

Existing `Master` staff can send, broadcast and read across companies. Other
staff have no notification access unless explicitly granted in server configuration:

```json
{
  "Notifications": {
    "StaffGrants": [
      { "UserId": "actual-staff-user-id", "CompanyId": 900, "Send": true, "Broadcast": false }
    ]
  }
}
```

Use one grant per user. A null CompanyId explicitly grants global scope; do not
use it for company-restricted staff. Scope always comes from trusted server
configuration, never from the request body. Read visibility requires a matching
company/global grant. Requests persist actor, authorized company scope, payload
hash and creation time as the notification audit record. Device values are excluded
from the general audit serializer. Device and staff routes have separate per-session
and per-actor rate limits of 30 requests/minute.

## Database deployment

Migration: `20261022105437_AddRiderNotifications` creates four tables without
changing existing data. Generate/review the deployment script against the current
database migration before applying it. The committed incremental SQL script
`rider-notifications-migration.sql` assumes the immediately preceding migration.

```powershell
dotnet ef database update --project Domain/Domain.csproj --startup-project Domain/Domain.csproj
```

Set `ConnectionStrings__DefaultConnection` for the intended deployment database
before running database commands. On 2026-10-22, at the user's request, the
incremental migration was applied using the configured connection string to
hosted database `db33462` at `db33462.public.databaseasp.net`. The preceding
`AddRiderMobileSessions` migration was verified first; afterwards the migration
history entry and all four notification tables/indexes were verified. This
updates the database schema only; API deployment and Firebase configuration
remain separate steps. Reverting the migration removes notification/device history;
disable workers before a rollback and back up any history you need to retain.

## Jobs, business events and retention

- `rider-notifications` runs every minute on Hangfire's existing default queue.
  Each invocation expands up to 1,000 riders in checkpointed pages, then attempts
  up to 50 device deliveries. Requests are eligible immediately; processing latency
  includes the poll interval and existing queue load. Add workers/increase polling
  through deployment capacity planning if measured traffic requires it.
- `NotificationRequest` is the transactional outbox itself. Business code stages
  a request on its own DbContext before committing; there is no second enqueue
  transaction and no Firebase call in a business transaction. Language/profile
  changes already stage a localized profile notification atomically with session
  replacement and device transfer. No report-publication event is invented: this
  backend exposes performance CRUD/imports without a separate publish operation.
- Optional daily reminder: set `Notifications__DailyReminderEnabled=true` and
  `Notifications__DailyReminderCron=0 12 * * *` for noon Saudi time. Default is off.
  The scheduler explicitly uses Asia/Riyadh. A unique Saudi-date event key prevents
  reruns from creating another logical reminder. Text is in `NotificationTemplates`.
- Broadcasts persist an upper rider ID at creation and materialize currently
  eligible riders up to that bound. Membership is evaluated during expansion and
  rechecked before delivery. Devices registered after expansion are not included.
- Rowversion claims/leases recover after two minutes. Provider calls have a
  20-second budget; no database transaction spans the send. Delivery is at-least-once:
  a crash/timeout after remote acceptance can cause a duplicate on retry.
- Six total attempts; exponential backoff starts at 60 seconds and caps at one
  hour plus jitter, honors provider Retry-After and never sends after expiry.
  A configuration failure stops the current worker run and defers its delivery
  one hour. Invalid payload errors do not deactivate destinations. Unregistered
  destinations deactivate only the same device generation/hash.
- Payloads use English fallback, allowlisted home/profile/report actions, normal
  Android priority, the `rider_updates` channel, and remaining TTL. The automated
  coaching messages below include the requested rejection counts and rank changes
  in notification text, so those values can appear in the Android tray.
- Android tray event time is explicitly set in `FirebaseNotificationSender` from
  the request's saved `CreatedAtUtc`, supplied by the backend `TimeProvider` when
  queued. Delayed sends and retries retain that UTC instant. FirebaseAdmin 3.7.0
  serializes it as `android.notification.event_time`; leaving `EventTimestamp`
  unset serializes year 0001. The adapter rejects missing or non-UTC timestamps.
  Android converts UTC to the phone's local time; do not add three hours. If a
  historical-event workflow is added, persist its validated UTC occurrence time
  separately and pass that instead of the notification creation time.
- Logout deactivates devices with the session revocation. Language changes transfer
  devices to the replacement session and invalidate older queued generations.
- `rider-notification-cleanup` runs at 02:15 UTC. Each run removes up to 100 expired,
  terminal requests older than `HistoryRetentionDays` (default 90, minimum 30),
  explicitly deleting child deliveries/recipients transactionally because this
  repository enforces restrictive foreign keys. It also deactivates up to 500
  devices with expired/revoked/ineligible sessions. Inactive device rows remain
  for referential history; no mobile inbox or read receipt feature is added.

## Automated rider coaching jobs

Three recurring Hangfire jobs run every minute and read committed daily uploads.
No push is sent inside an import transaction. They stage durable requests for
the existing `rider-notifications` delivery worker, so delivery may take another
worker cycle. All existing shift/performance upload paths are covered through
their saved `RiderShifts` and `RiderScorePerformances` records, including accepted
shift-conflict resolutions; uncommitted rows and temporary comparisons are ignored.

| Hangfire job ID | Behavior |
| --- | --- |
| `rider-mobile-rejection-alerts` | Sum yesterday's `RealRejectedDailyOrders` across the rider's working IDs. If greater than zero, send the count, yesterday's date, and a reminder to take care today. |
| `rider-mobile-rank-change-alerts` | Compare the latest performance rank dated today or yesterday against the most recent earlier snapshot. An improvement receives congratulations; a decline receives supportive advice to improve. |
| `rider-mobile-on-time-delivery-alerts` | After a recent daily performance update, encourage the rider to reach 99% on-time delivery, or congratulate them and ask them to keep going when already at or above 99%. |

Dates use Saudi time (UTC+3). Rank order is A (highest), B, C, D, E, F; surrounding
whitespace and letter case are normalized. First ranks, unchanged ranks, unknown
grades, future records, and snapshots older than yesterday produce no rank alert.
The prior rank may be from a previous month or year. If today and yesterday both
have performance data, only today's latest rank is considered.

The on-time job also considers the latest performance date from today or
yesterday. It uses the mobile report's month-to-date on-time percentage:
`sum(CompletedOrdersInTime) / sum(CompletedOrders) * 100`, through that snapshot's
date in its Gregorian month. It uses exact totals for the 99% decision, so a
rounded 98.999% does not qualify. This is separate from the platform's
`OnTimeDeliveryScore` field and does not change the mobile report's configurable
coaching targets. Riders with no completed orders receive general encouragement
to aim for 99%, with no invented percentage. Invalid totals are skipped.

Each rejection alert is unique per rider/shift date. Each rank alert is unique
per rider/performance date/old rank/new rank. Repeated jobs and uploads do not
create the same request again. Rejection corrections after an alert has already
been queued do not send a second daily alert. The existing unique event-key index
also protects concurrent workers. Requests are processed in pages of 100 riders.
On-time alerts are unique per rider/performance date, even when corrections
change that date's figures. A new daily performance snapshot can generate a new
alert; rerunning a job without a new snapshot does not send another reminder.

Messages include Arabic (`ar`), English (`en`), and Bengali (`bn`). The delivery
worker chooses the rider's latest preferred language, with English fallback.
Only active, enabled riders in Saudi Arabia are eligible, and the existing device,
session, ownership, retry, and expiry checks still apply. Tapping a coaching message
uses the existing `performance_report` action for the relevant year and month.
Rejection alerts expire at the next Saudi midnight to keep "yesterday" accurate;
rank and on-time alerts expire after 24 hours. A device with no eligible registration cannot
receive a push and is handled by the existing no-device recipient status.

All three jobs require `Notifications__Enabled=true`. Individual switches default to
true: `Notifications__RejectionAlertsEnabled` and
`Notifications__RankChangeAlertsEnabled`, plus
`Notifications__OnTimeDeliveryAlertsEnabled`. The jobs remain registered while a
switch is off, but do not create requests. No additional database migration or
mobile endpoint is needed beyond the existing notification tables.

## Verification and mobile integration

```powershell
dotnet test Accounting.Tests/Accounting.Tests.csproj
```

Notification unit/HTTP tests use fake providers and EF InMemory. Native SQL tests
use `NOTIFICATION_SQL_TEST_CONNECTION` to create and delete an isolated database
named `ExpressNotificationTests_<random>`. Point this only at a local test SQL
Server whose identity may create databases. These tests exercise native unique
constraints, concurrent registration/request/worker execution, rowversion claims,
logout and cleanup with restrictive foreign keys. Without the variable they skip.

Implementation verification: solution build and 225 automated tests passed, with
seven environment-dependent tests skipped. Migration/model consistency passed and
the incremental idempotent SQL was generated. The attempted native SQL run could
not connect because installed LocalDB crashed during startup; native concurrency,
constraints and migration execution still need a working SQL Server test host.

The current Flutter source includes `DeviceRegistration`, a secure installation
UUID store, authenticated PUT/DELETE device methods, retries and session lifecycle
hooks. See the [frontend handoff](../notifications/frontend/README.md) for the
implemented contract and wiring. Verify the installed build PUTs the current FID
after login/onRegistered/resume, DELETEs before discarding the login token, and
reconciles after session replacement. The backend cannot discover a device FID
without a successful mobile registration upload.

Finish deployment verification with an Android test device: registration, staff
send, foreground banner, background tray, tap routing, language change and logout.
After deploying the timestamp fix, send a new staff notification and confirm its
tray header uses the expected local date/time. Previously displayed notifications
retain their old timestamps. Payload serialization is covered with the installed
SDK in `FirebaseNotificationSenderTests`; deployed sender/version and live device
delivery still require this smoke check.
No real Firebase send can be verified without server credentials and a device.
