# Notification API and database contract

These routes are implemented in the .NET backend; see [deployment details](backend-setup.md).
All timestamps
are UTC. Use existing ProblemDetails/error conventions and propagate request
cancellation through database work. A rider identity is never trusted from a
mobile body or query string.

## 1. Mobile device registration

```http
PUT /api/rider-mobile/devices/{installationId}
Authorization: Bearer <current-mobile-session-token>
Content-Type: application/json

{
  "firebaseInstallationId": "<FID emitted by FirebaseMessaging.onRegistered>",
  "platform": "android",
  "permission": "authorized",
  "appVersion": "1.0.0+1"
}
```

Return `204 No Content`. `installationId` is a random UUID generated once by
the app and persisted in secure storage, separate from its login session.
It is not an IMEI, Iqama number, Firebase Installation ID, or authentication secret.

Registration rules:

1. Validate the existing mobile session, including employee eligibility.
2. Take RiderId and SessionId from validated `rider_id` and `sid` claims.
3. Accept `android` and `authorized` for this version. Reject blank or oversized
   FIDs; use a defensive input cap of 256 characters, without assuming an exact
   fixed format. AppVersion is optional, maximum 64 characters.
4. Upsert atomically by installation ID. Replace its previous FID/session
   association; never retain another active owner for the same installation.
5. Deduplicate active registrations by a SHA-256 destination hash. If an existing
   registration has the same FID, transfer/deactivate it atomically so a FID
   is not simultaneously owned by two riders. Retry/map unique-key races.
6. Set LastSeenAtUtc, Permission, and IsActive. Registration is idempotent.
7. This endpoint cannot prove ownership of a supplied Firebase ID. Never
   treat knowledge of a FID/installation ID as access to rider data. Use
   authenticated, limited requests and generic push text.

Errors: `400` malformed body, `401` missing/expired/revoked session, `403` wrong
token purpose, `429` excessive requests. Do not return another rider's details.

Unregister the current installation:

```http
DELETE /api/rider-mobile/devices/{installationId}
Authorization: Bearer <current-mobile-session-token>
```

Return `204` even if already absent. The mutation must include the authenticated
RiderId AND current SessionId, so an older session cannot unregister a new owner.

The existing `/logout` must also deactivate registrations for the revoked `sid`
in the same transaction. On `/language`, transfer device bindings from the
revoked session to the replacement session atomically, because the current API
replaces the login token. Use the rider's current saved language when sending.
An offline local logout cannot immediately notify the server: require valid
session bindings at delivery time, keep text generic, and treat client FCM
unregistration as best effort. Do not promise immediate revocation of queued Android
notifications already accepted by Firebase.

## 2. Staff sends a message to one rider

```http
POST /api/admin/riders/42/notifications
Authorization: Bearer <staff-token>
Idempotency-Key: 82472d7a-6985-4caf-a8b5-9872d1e948f7
Content-Type: application/json

{
  "content": {
    "ar": { "title": "تحديث جديد", "body": "لديك تحديث جديد في التطبيق." },
    "en": { "title": "New update", "body": "You have a new update in the app." },
    "bn": { "title": "নতুন আপডেট", "body": "অ্যাপে আপনার জন্য নতুন আপডেট রয়েছে।" }
  },
  "data": { "type": "home" },
  "expiresInSeconds": 86400
}
```

Require a dedicated `Notifications.Send` policy and enforce the staff member's
existing organization/company/rider scope. A valid staff login alone is not
permission to send. Rider mobile credentials must receive `403`.

Validate the rider exists in the staff scope and is eligible. Persist a request
and its durable expansion work atomically. Return:

```http
HTTP/1.1 202 Accepted
Location: /api/admin/notifications/9f30744b-28fe-4901-9060-c138bf5b5919

{
  "requestId": "9f30744b-28fe-4901-9060-c138bf5b5919",
  "status": "queued"
}
```

Send to all currently eligible active installations for that rider. If there are
none, status becomes `completed` with `skippedNoDeviceRiders = 1`; it is not an
FCM error. Do not send to other riders or to staff devices.

## 3. Staff broadcasts to all eligible riders

```http
POST /api/admin/notifications/broadcasts
Authorization: Bearer <staff-token>
Idempotency-Key: d94b5af5-b64f-4fc2-9fc4-83a6c04a658f
Content-Type: application/json

{
  "content": {
    "ar": { "title": "إعلان", "body": "يرجى مراجعة آخر تحديثات التطبيق." },
    "en": { "title": "Announcement", "body": "Please check the latest app updates." },
    "bn": { "title": "ঘোষণা", "body": "অ্যাপের সর্বশেষ আপডেট দেখুন।" }
  },
  "data": { "type": "home" },
  "expiresInSeconds": 86400
}
```

Require `Notifications.Broadcast`, with an audit entry identifying the actor.
Global broadcasts require explicit global scope. An organization-scoped staff
member can only broadcast within their authorized organization. Reject any
attempt to widen the scope through body values. Return `202` as above.

"All" means enabled, not-deleted, in-country riders in the actor's authorized
scope, with active device registrations attached to valid sessions. Reuse the
existing backend eligibility predicate rather than duplicating field assumptions.
Persist the authorized audience scope; background workers cannot use HttpContext.

Use database-backed fan-out for this version, not a public client-subscribed
`all_users` topic. This keeps eligibility, localization, and per-device results
under backend control. The installed mobile app does not subscribe to any topic.

At request creation, snapshot the eligible rider IDs durably, or persist a durable
materialization procedure with a documented creation-time upper bound and
checkpoint. Do not include riders newly created after that bound. Recheck
eligibility, permissions, session validity, and current registration generation
immediately before each send. New devices registered after audience expansion
do not need to receive that existing broadcast. Document this behavior in the UI.

## 4. Common validation and idempotency

- Require English content; Arabic and Bengali may be omitted and fall back to
  English. When provided, locale objects must contain both nonblank fields.
- Proposed product limits: title 120 characters, body 500 characters;
  expiresInSeconds from 60 to 604800, default 86400. These are application limits.
- Accept only allowlisted action fields below. Backend generates notificationId.
- Require a UUID Idempotency-Key for staff commands. Scope uniqueness by
  organization, staff actor, operation, and key. Store a canonical payload hash.
- Same key + same normalized body returns the original request ID without
  creating more recipients. Same key + different body returns `409`.
- Concurrent duplicate requests must be resolved by a database unique constraint,
  not only by an application-level existence query.
- Store idempotency records for at least 30 days; document that replay after
  retention can produce a new request. Apply staff/organization rate limits.

Allowed actions:

```json
{ "type": "home" }
```

```json
{ "type": "profile" }
```

```json
{ "type": "performance_report", "year": "2026", "month": "10" }
```

For reports, validate numeric year/month, year >= 2000, month 1..12, and no future
month in Saudi time (UTC+3). FCM data values are strings. Never accept arbitrary
URLs, executable actions, credentials, Iqama, or report details in push payloads.

## 5. Staff request status

```http
GET /api/admin/notifications/{requestId}
Authorization: Bearer <staff-token>
```

Require scoped staff read permission. Return `404` for requests outside the
actor's scope. Example final result:

```json
{
  "requestId": "9f30744b-28fe-4901-9060-c138bf5b5919",
  "status": "completed",
  "audienceRiders": 1,
  "skippedNoDeviceRiders": 0,
  "totalDeliveries": 2,
  "pendingDeliveries": 0,
  "acceptedDeliveries": 2,
  "failedDeliveries": 0,
  "skippedDeliveries": 0,
  "expiredDeliveries": 0
}
```

States: `queued`, `expanding`, `processing`, `completed`, `completed_with_errors`,
`failed`. Completed means no remaining retryable work, not displayed or read.
Count device deliveries separately from riders. Expose safe error categories and
timestamps, never registration IDs, credentials, or raw provider exceptions.

## 6. Database additions

Map names and key types to the actual EF model. Use UTC timestamps and appropriate
foreign keys; keep notification entities out of audit serialization that would
otherwise expose registration IDs.

| Table | Required fields / constraints |
| --- | --- |
| RiderPushDevices | Id, InstallationId, RiderId, SessionId, FirebaseInstallationId, DestinationHash, Platform, Permission, AppVersion, IsActive, LastSeenAtUtc, UpdatedAtUtc, Generation/rowversion; unique InstallationId; unique active DestinationHash; index RiderId + IsActive and SessionId |
| NotificationRequests | Id, AudienceType, TargetRiderId nullable, AuthorizedScope, ActorId nullable, EventKey nullable, IdempotencyKey nullable, PayloadHash, ContentJson, DataJson, CreatedAtUtc, ExpiresAtUtc, Status, expansion checkpoint/cutoff |
| NotificationRecipients | Id, RequestId, RiderId, Status; unique RequestId + RiderId; this is an audience/work ledger, not a mobile notification inbox |
| NotificationDeliveries | Id, RecipientId, DeviceId, DeviceGeneration, Status, Attempts, NextAttemptAtUtc, LeaseOwner, LeaseUntilUtc, ProviderMessageId nullable, LastErrorCode, AcceptedAtUtc; unique RecipientId + DeviceId + DeviceGeneration |
| NotificationOutbox | Id, unique EventKey, Type, PayloadJson, CreatedAtUtc, ProcessedAtUtc, Attempts, NextAttemptAtUtc, LeaseOwner, LeaseUntilUtc |

Use a filtered unique index or equivalent supported by the database for active
destination hashes. Do not expose destination IDs in logs. Protect destination values
at rest using the backend's existing secret/data protection conventions; hash is
for lookup/deduplication, not sending. FIDs must remain recoverable for sending.

Prefer soft deactivation/retention where deliveries reference device rows.
Define retention: operational request/delivery history 90 days by default,
cleanup completed outbox records after the agreed support window. Align these
proposed durations with the system's existing retention requirements.
