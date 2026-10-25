# Firebase sending and automatic notification jobs

This is an implementation brief for the backend developer. Inspect the backend
solution first; the mobile repository cannot establish its namespaces, database
provider, actual staff policies, or installed job scheduler.

The .NET implementation is now in this repository. See [backend setup](backend-setup.md)
for its actual scheduler intervals, scope grants, transactional request outbox,
business event, configuration and deployment status. Examples below describe the
contract; proposed intervals/events are not all enabled production behavior.

## 1. Configure the Firebase provider

For a .NET backend, add the Firebase Admin SDK to the appropriate infrastructure
project and pin the tested version in its package references:

```powershell
dotnet add package FirebaseAdmin
```

Set the project to `express-service-fcbe5`. Configure Application Default
Credentials with the required Firebase Cloud Messaging permissions. Prefer an
attached runtime identity on Google Cloud. If hosting elsewhere requires a
service-account JSON key, generate it for this project, store it in server secret
storage, and point GOOGLE_APPLICATION_CREDENTIALS at the protected file. Keep it
outside the public web root and repository. Never put it in Flutter assets.

The app's `android/app/google-services.json` is client configuration and cannot
authorize server sends. Interactive `firebase login` on the developer's computer
is also not the production backend's credential configuration. Enable the FCM
HTTP v1 API and verify the deployed host can reach Google over HTTPS.

Initialize one FirebaseApp/provider client through dependency injection at
startup, not inside every controller or recipient loop. Fail provider readiness
checks clearly without breaking unrelated report/login endpoints.

Official setup: [Firebase Admin SDK](https://firebase.google.com/docs/admin/setup).

## 2. Shared application service

Use the existing controller -> application service -> Result -> EF pattern.
Suggested responsibilities, adapted to existing conventions:

| Component | Responsibility |
| --- | --- |
| Rider device service | Authenticated registration, replacement, unregistration |
| Notification request service | Validation, staff scope, content, idempotency, durable request creation |
| Event notification handler | Maps a business event to a template and audience |
| Audience worker | Expands recipients/devices with bounded queries and checkpoints |
| Delivery worker | Claims work, rechecks current state, sends, records results/retries |
| FCM provider adapter | Builds provider messages and translates provider errors |

Expose internal use cases such as QueueForRiderAsync and QueueBroadcastAsync.
Business jobs call these internal use cases; they do not call staff HTTP routes
or impersonate an administrator. Enforce a trusted event/template allowlist.
Pass IDs and immutable metadata to jobs, never HttpContext or scoped DbContext.

## 3. Build the provider payload

Select the recipient's latest preferred language (`ar`, `en`, `bn`), then choose
that content or fall back to English. Send the same generic message to each
eligible device. An illustrative HTTP v1 message shape is:

```json
{
  "message": {
    "fid": "<current-device-firebase-installation-id>",
    "notification": {
      "title": "Report available",
      "body": "Your monthly performance report is ready."
    },
    "data": {
      "type": "performance_report",
      "year": "2026",
      "month": "10",
      "notificationId": "9f30744b-28fe-4901-9060-c138bf5b5919"
    },
    "android": {
      "priority": "high",
      "ttl": "86400s",
      "notification": {
        "channel_id": "rider_updates",
        "sound": "default",
        "tag": "9f30744b-28fe-4901-9060-c138bf5b5919"
      }
    }
  }
}
```

Use the SDK's typed message model in production. Include both `notification`
and `data` for these visible alerts. Set TTL to the request's **remaining**
lifetime at each attempt; never restart a full TTL on every retry. Use high
priority only for timely, visible notifications; routine announcements can use
normal priority. Stable notification tags can replace repeated tray entries,
but they do not guarantee exactly-once delivery or suppress foreground banners.

The installed mobile Firebase Messaging 17 SDK uses register(), onRegistered,
and unregister(). onRegistered emits a Firebase Installation ID (FID). Select a
server SDK version supporting Message.Fid / MulticastMessage.Fids and test it
against this app. Do not put the FID into a legacy Token field. An app-generated
installationId from file 01 is a separate local UUID and is not the Firebase FID;
never substitute that UUID as a provider destination. Earlier conversation examples
used legacy getToken(); this document and the final code supersede those examples.

Use supported SDK sending methods and batches of at most 500 destinations or
messages, or the lower limit of the pinned SDK. Map each result back to the
corresponding delivery row. Bound parallelism; do not load all rider FIDs into
memory or start unbounded tasks. Group by locale/content where appropriate.

Official sending API: [Admin SDK messaging](https://firebase.google.com/docs/cloud-messaging/send/admin-sdk).

### C# provider adapter example

This illustrates the provider boundary, not a standalone backend application.
Inject a configured FirebaseMessaging instance and the backend's clock. Authorization,
audience selection, persistence, and retries belong in the services/workers above.

```csharp
using FirebaseAdmin.Messaging;

static Task<string> SendVisibleAsync(
    FirebaseMessaging messaging,
    string fid,
    string title,
    string body,
    string notificationId,
    Dictionary<string, string> actionData,
    TimeSpan remainingLifetime,
    CancellationToken cancellationToken)
{
    if (remainingLifetime <= TimeSpan.Zero)
        throw new ArgumentOutOfRangeException(nameof(remainingLifetime));

    var data = new Dictionary<string, string>(actionData)
    {
        ["notificationId"] = notificationId
    };
    return messaging.SendAsync(new Message
    {
        Fid = fid,
        Notification = new Notification { Title = title, Body = body },
        Data = data,
        Android = new AndroidConfig
        {
            Priority = Priority.High,
            TimeToLive = remainingLifetime,
            Notification = new AndroidNotification
            {
                ChannelId = "rider_updates",
                Sound = "default",
                Tag = notificationId
            }
        }
    }, cancellationToken);
}
```

Use this high-priority variant for timely visible events; make priority an
application-controlled template setting for routine broadcasts. Catch and translate
provider exceptions at the worker/provider boundary using section 8. Do not expose
this raw method directly as an unauthenticated endpoint. Compile the adapter
against the selected FID-capable SDK in the backend solution before deployment.

## 4. Send directly to one rider

1. Staff POSTs the one-rider command from file 01.
2. In one local transaction, persist request + durable expansion work.
3. A worker loads that rider's active session-bound registrations.
4. Create a delivery row for each current eligible device generation.
5. Dispatch promptly, outside the database transaction, and record each outcome.
6. Staff polls the status resource. The business data remains available through
   normal authenticated APIs even if a phone is offline or push is unavailable.

## 5. Send to all riders

1. Authorize the broadcast scope and persist its immutable content and cutoff.
2. Materialize recipient IDs in bounded keyset pages with a persisted checkpoint.
3. Deduplicate recipients/deliveries using unique constraints so restarts are safe.
4. Expand eligible devices and process batches through the same delivery worker.
5. Recheck eligibility/session/device ownership before dispatch, including after
   a retry delay. Skip revoked, expired, disabled, deleted, or reassigned targets.
6. Mark the request complete only after audience expansion is complete and no
   pending/in-flight/retryable deliveries remain. Aggregate device/rider counters.

This uses the database audience rather than a public Firebase topic because
these are authenticated rider announcements with eligibility and delivery history.

## 6. Send automatically when an event happens

Example event: a rider's monthly report is published.

The use case that publishes the report must stage an outbox record alongside
the report changes in the same database commit:

```text
Business transaction:
  save report publication/version
  insert outbox event with unique key
  commit both

Outbox worker:
  claim event
  map event to localized template and one rider
  create notification request idempotently
  mark event processed in the same local transaction

Delivery workers:
  expand and send using the shared workflow
```

Suggested event payload:

```json
{
  "eventType": "rider.report-published.v1",
  "eventKey": "report-published:42:2026-10:publication-3",
  "riderId": 42,
  "year": 2026,
  "month": 10,
  "publicationVersion": 3
}
```

Generate keys from a persisted business event/version. An event retried by a job
must reuse its key. A genuinely new publication can use a new version. Put unique
constraints on outbox EventKey and the corresponding notification source key.
Do not notify for a failed/rolled-back update. Do not call Firebase inside the
business transaction; external sends cannot be rolled back with SQL.

Suggested event/template map (connect only to actual confirmed business events):

| Event | Audience | Mobile action |
| --- | --- | --- |
| Report published | Affected rider | performance_report + year/month |
| Rider profile changed | Affected rider | profile |
| Important operational announcement | Authorized eligible audience | home |
| Daily reminder from a scheduled job | Eligible affected riders | home |

Define template text in all three languages. The requested automated coaching
messages include rejection counts and rank changes in tray text; the full report
is still fetched with the rider's mobile token. See the implemented
[coaching jobs](backend-setup.md#automated-rider-coaching-jobs) for timing,
deduplication, language selection, and configuration.

## 7. Recurring/background jobs

Use the backend's installed durable scheduler (for example Hangfire if present).
A timer or Task.Run in a web request is insufficient for reliable delivery.

Proposed jobs:

- Outbox dispatcher: poll promptly, e.g. every 10 seconds; persisted claims.
- Notification delivery worker: run continuously or poll pending work.
- Reminder producer: run at the chosen business time using explicit Saudi time.
- Registration/history cleanup: daily, bounded deletes/deactivation.

For a daily reminder, derive an idempotency key from template + rider ID + Saudi
business date, e.g. `daily-reminder:42:2026-10-22`. Select eligible riders using
paged queries, and persist produced work/checkpoints. Re-running the same day's
job must not enqueue the same logical reminder twice. Convert schedule instants
explicitly; store UTC in new tables and do not silently reinterpret legacy dates.

## 8. Worker reliability and retry policy

Atomically claim rows with a lease and concurrency protection. Recover expired
leases after worker crashes. Ensure only the lease owner can complete a claim;
use the database's supported locking/concurrency mechanism. No open SQL
transaction may span an FCM network call.

Suggested application policy: six total attempts, bounded exponential backoff
with jitter, starting at 60 seconds and capped at one hour. Honor Retry-After and
provider-specific minimum retry guidance. Never retry past ExpiresAtUtc.

| Result | Action |
| --- | --- |
| Provider accepts | Persist provider ID; delivery accepted |
| Unregistered destination | Deactivate only the matching device generation; permanent failure |
| Malformed payload | Permanent request/configuration failure; do not delete all destinations |
| Invalid-argument error | Determine whether payload or destination is invalid before registration cleanup |
| Quota/429, unavailable/503, transient internal error | Retry affected rows with backoff and Retry-After |
| Sender/project mismatch or credentials failure | Operational/configuration error; stop hot-looping and alert operators |
| Revoked session or ownership changed | Skip old delivery; never send a new owner's notification through an old row |
| Request expired | Expire pending work; do not send |

Network timeouts are ambiguous: Firebase may already have accepted the message.
Retried sends may therefore duplicate. The system is at-least-once, not
exactly-once. Database idempotency prevents duplicate logical work; it cannot
atomically commit a remote FCM send and a local database update.

Record attempt counts, provider categories, queue age, dead-letter/permanent
failures, and request IDs. Do not log destination IDs or service-account credentials.
On permanent unregistration use a generation/destination-hash condition so an old
response cannot deactivate a newly registered destination.

References: [FCM errors](https://firebase.google.com/docs/cloud-messaging/error-codes),
[token lifecycle](https://firebase.google.com/docs/cloud-messaging/manage-tokens).
