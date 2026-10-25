# Express Service notifications: backend handoff

For frontend implementation, start with the current
[notification frontend handoff](../notifications/frontend/README.md): API contract,
staff dashboard, Flutter lifecycle, error handling and acceptance checks.

Read these files in order:

1. [API and database contract](01-api-and-database.md)
2. [Firebase sending and background jobs](02-sending-and-jobs.md)
3. [Mobile integration and acceptance checks](03-mobile-and-testing.md)

## Required outcome

Staff must be able to send a notification immediately to one rider or to all
eligible riders. Backend events and recurring/background jobs must also be able
to notify the affected rider automatically. All three entry points must use the
same persisted sending workflow, localization rules, and delivery tracking.

"Send immediately" means persist the request and make it immediately eligible
for worker execution. Return 202 with a request ID; do not block a staff HTTP
request until every phone has been contacted. Firebase acceptance is not proof
that a notification was displayed or read.

## Known project values

| Item | Value |
| --- | --- |
| Firebase project | `express-service-fcbe5` |
| Android application ID | `com.fastexpress.express_mobile` |
| Mobile API base path | `/api/rider-mobile` |
| Android channel ID | `rider_updates` |
| Supported languages | `ar`, `en`, `bn` |
| Rider identity | Existing authenticated `rider_id` claim |
| Session identity | Existing authenticated `sid` claim |

## Implementation status and boundary

This checkout contains the .NET backend. The notification APIs, database model,
Firebase Admin sender and Hangfire workers are implemented here. See
[backend setup and deployment](backend-setup.md) for configuration, migration,
implemented behavior and verification limits. Implementation does not imply deployment.

The Flutter Android client is in the separate `Express-Mobile` project. Its
Firebase initialization, permission prompts, FID registration, foreground banners
and tap routing are implemented. The current client also contains backend PUT/DELETE
device calls, secure installation identity, retry coordination and session/logout
wiring. Verify the deployed mobile build and live flow using the frontend handoff.

## Backend delivery order

- [x] Inspect existing controllers, service/Result conventions, EF context,
      session validation, employee eligibility, and background-job infrastructure.
- [x] Implement Firebase Admin configuration and project/environment checks.
- [ ] Configure credentials and the persistent data-protection key ring on the host.
- [x] Prepare migration for device registrations, notification requests, recipients,
      and deliveries; requests also serve as the transactional outbox.
- [x] Apply the reviewed migration to hosted database `db33462` and verify tables/indexes (2026-10-22).
- [x] Implement mobile registration/unregistration and session cleanup.
- [x] Implement a shared notification application service and provider adapter.
- [x] Implement staff send-to-one, broadcast, and request-status APIs.
- [x] Implement durable audience expansion and delivery workers.
- [x] Connect language/profile changes and an optional daily reminder job.
- [x] Verify mobile source contains FID upload/unregister and session lifecycle wiring.
- [x] Deliver API examples and migration/setup instructions.
- [ ] Complete live Android/Firebase and native SQL Server acceptance checks.

Keep controllers thin. Use the backend's existing service/Result/EF patterns and
namespaces; do not invent a new service architecture or install a second job
scheduler when a durable one already exists. The host project's actual structure
must be inspected before creating backend files.
