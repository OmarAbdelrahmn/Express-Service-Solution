# Mobile integration, testing, and delivery checklist

The current frontend handoff is [here](../notifications/frontend/README.md).
Use its [mobile guide](../notifications/frontend/03-mobile-integration.md) for
current source wiring and its API contract for implemented endpoint behavior.

## Implemented in this mobile repository

- Firebase initializes before the app starts; initialization failure falls back
  to the normal rider app rather than blocking login.
- Firebase background handler initializes the SDK. Android handles visible
  notification payloads; no private report fetch runs in that isolate.
- Android permission declaration, white small icon, and high-importance
  `rider_updates` channel are configured.
- Notification permission is requested after sign-in/restored session.
- Explicit register(), onRegistered FID updates, and resume retry are implemented.
  Automatic initialization is disabled so registration follows sign-in/permission.
- Foreground notifications appear as tappable in-app banners, not duplicate
  Android tray notifications.
- Background/terminated notification taps open home, profile, or a valid report
  period after the UI and login are ready. Unknown/invalid actions open home.
- On logout/session clear, foreground state is cleared and FCM unregister() is
  attempted. This is best effort and is not a substitute for backend unbinding.

Relevant source: `lib/notifications/app_notifications.dart`, `lib/main.dart`,
`lib/ui/shell.dart`. Firebase configuration is Android-only.

## Implemented mobile integration to verify after deployment

The current app receives a FID in AppNotifications.firebaseInstallationId and
uploads it through `DeviceRegistration` and `RiderApi.registerDevice`. Backend
routes exist. The following lifecycle requirements are implemented in source;
verify the installed build and live environment before marking acceptance complete.

1. Add a secure installation UUID store separate from SecureSessionStore's session
   record. Persist across logouts, regenerate after a clean installation.
2. Add RiderApi methods for file 01's PUT/DELETE routes. Use the current login
   token; never embed Firebase service-account credentials in Flutter.
3. Upload the FID after valid login/restoration and on onRegistered updates; PUT is
   idempotent. Retry transient upload failures on resume/network recovery.
4. Pass permission/appVersion and unregister when permission is revoked. Do not
   ask for permission on every resume; read the current Android setting.
5. Guard every asynchronous registration using the controller's session epoch.
   Cancel/discard old-session work and serialize unregister/register transitions.
6. Unregister while the old authenticated token is still available on logout.
   The backend's `/logout` must independently unbind devices by sid. Clear local
   state and attempt FCM unregistration even on network failure.
7. After `/language` replaces the token, use the new token. The backend must
   transfer device session bindings in that operation's transaction; re-PUT the
   registration for reconciliation without re-registering with Firebase needlessly.
8. Inspect the endpoint status: do not turn a notification registration 404/500
   into a failed login or a generic report error. Keep registration failures
   separate and observable. Handle 401 through normal session-expiry logic.

The frontend handoff supplies route examples, status/error semantics and release
checks. Live device/Firebase confirmation remains distinct from source verification.

## First device test without backend code

1. Build/run on an Android phone with Google Play services or an emulator image
   that includes them. Open the app at least once.
2. Sign in with a test rider. On Android 13+, allow notification permission.
3. In a Flutter debugger, inspect `AppNotifications.firebaseInstallationId` after
   onRegistered fires. Use the FID for a controlled server test using file 02's
   message format; do not put it into production logs or a shared issue.
4. For a Firebase Console test, use a FID target if the Console offers that option.
   If its test dialog requires a legacy registration token, do not paste a FID
   there. For that diagnostic only, evaluate the still-supported deprecated
   FirebaseMessaging.instance.getToken() in a local debugger and use the returned
   token in the Console. The production app/server contract remains FID-based.
5. Test with the app in the background and confirm the Android notification.
6. Test while foregrounded and confirm the in-app banner. Tap it to open home.
7. Use a controlled backend/admin test for custom data such as
   `{ "type": "performance_report", "year": "2026", "month": "10" }`.

Android force-stop from Settings prevents receipt until the user opens the app
again. Notification/channel settings and device power restrictions affect
display. Normal app termination differs from force-stop. Permission denial must
not block the app's core features.

References: [Flutter FCM setup and console test](https://firebase.google.com/docs/cloud-messaging/flutter/get-started),
[foreground/background handling](https://firebase.google.com/docs/cloud-messaging/flutter/receive-messages).

## Backend automated acceptance checks

- Register the same installation repeatedly: exactly one active registration.
- Renew its registration: old generation is not eligible for future sends.
- Register one installation under a different rider: no dual ownership.
- Two different installations for one rider: both eligible, no other rider sent.
- Missing/expired/revoked mobile session: registration is rejected.
- Rider token on a staff-send route: rejected. Scoped staff cannot target another
  company's rider or create a global broadcast.
- Reusing an idempotency key with the same command returns the original request;
  changing its payload returns 409; concurrent duplicates create one request.
- One-rider sends target only that rider's valid, active, permitted devices.
- Broadcast expansion is bounded, checkpointed, restartable, deduplicated, and
  excludes disabled/deleted/out-of-country/revoked/expired registrations.
- Localized messages follow the rider's latest preference; missing locale content
  falls back to English. No cross-recipient language leakage in batch assembly.
- Event transaction rollback creates neither a committed outbox event nor a send.
- Committed event survives process crash before scheduler enqueue.
- Event/reminder job reruns produce one logical notification per event key.
- Multiple workers cannot own the same valid lease concurrently.
- Mixed provider batch results update corresponding delivery rows only.
- Unregistered destination deactivates only that registration generation; an old failed
  response cannot disable a destination registered while the send was in flight.
- Transient failures retry without resending already accepted batch members.
- Leases recover after a crash; provider-accepted-before-DB-crash ambiguity is
  documented and does not result in infinite retries.
- Expired work is not sent. Retry TTL is the remaining lifetime.
- Logout deactivates the session's registrations; language replacement transfers
  bindings; another session's unregister cannot mutate the new association.
- No-device riders have an explicit skip outcome. All final status counters
  reconcile with recipient/delivery rows and do not claim display/read receipts.
- Secrets and tokens never appear in staff status responses or normal logs.

Use provider fakes for these backend automated tests. Real pushes are a separate
staging integration test, not a side effect of unit tests.

## End-to-end checks before release

| Scenario | Expected result |
| --- | --- |
| Staff sends to rider A | Only A's eligible phones receive |
| Staff broadcasts | All and only authorized eligible riders receive |
| Report is published | That rider receives localized report notification |
| Recurring job retries | Same logical reminder is not enqueued twice |
| App foreground | Tappable in-app banner |
| App background | Android tray notification |
| App terminated normally | Notification tap starts app and opens target |
| Login required | Tap waits for authentication; protected API controls access |
| Permission denied | App works; no repeated permission prompt loop |
| Switch riders on same phone | Old association is inactive; new rider's data only |
| Arabic / English / Bengali | Correct content and correct target screen |
| Phone offline | Delivery can resume within remaining TTL; reports still load normally |

Push is a best-effort announcement mechanism. This implementation does not add a
mobile notification history/read-receipt inbox. If that is required later, define
separate authenticated inbox/list/read APIs rather than treating FCM status as
user-read evidence.

## Backend handover artifacts

Provide the implemented route DTOs/OpenAPI, migrations and rollback notes,
deployment/credential environment-variable names (no secret values), scheduler
configuration, event/template mapping, staff permissions, retention policy,
sample send-one/broadcast/status requests, automated test results, and a staging
request ID proving a successful device test. Confirm the API contract before
the mobile registration upload is enabled in production.
