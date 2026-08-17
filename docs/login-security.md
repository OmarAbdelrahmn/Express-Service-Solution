# Login and phone support reset

## Why a correct password could fail

Both login services call ASP.NET Core Identity with lockout-on-failure enabled. Five failed attempts lock the account for 15 minutes. During the lockout window Identity rejects even the correct password and reports a locked-out result. The old reset implementation changed the password but did not clear `LockoutEnd`, so the newly assigned password also failed until the original 15-minute window expired.

The reset operation now changes the password and clears `AccessFailedCount` and `LockoutEnd` in one database transaction. Login failures record the account ID, failed-attempt count, lockout end, and reason in server logs without recording the password.

## Phone help endpoint

The endpoint intentionally remains anonymous so it can be called from a phone, but possession of a separate support key is required.

```http
POST /api/Admin/adminreset
Content-Type: application/json
X-Support-Key: <support key of at least 32 random characters>

{
  "userName": "the exact account username"
}
```

A successful response returns a newly generated 20-character temporary password and the UTC reset time:

```json
{
  "userName": "account-name",
  "temporaryPassword": "generated value returned here",
  "resetAtUtc": "2026-08-17T07:30:00Z"
}
```

The response is marked `no-store`; the temporary password is not logged and no shared default password is used. Copy it when the request succeeds and send it to the user through the approved support channel. A later reset generates a different password and immediately invalidates the previous one.

Expected status codes:

| Status | Meaning |
|---|---|
| `200` | Password changed and lockout cleared |
| `401` | `X-Support-Key` is missing or incorrect |
| `404` | The support key is valid, but the username does not exist |
| `429` | More than five reset attempts were made from the client IP in ten minutes |
| `503` | The server has no valid support key configured |

Do not put the support key in a URL, query string, source code, screenshot, chat message, or tracked settings file. Use an HTTPS client such as the phone app or Postman and store the key in the phone's secure credential storage.

## Required secure configuration

Tracked `appsettings.json` contains empty placeholders. Supply these values with deployment secrets or environment variables:

| Environment variable | Requirement |
|---|---|
| `ConnectionStrings__DefaultConnection` | SQL Server connection string |
| `Jwt__Key` | New high-entropy JWT signing key |
| `DailyReport__SmtpPassword` | Rotated SMTP password |
| `SupportPasswordReset__Key` | At least 32 high-entropy characters; separate from every user password and the JWT key |
| `IdentityBootstrap__AdminPassword` | Strong unique password used only if Admin is missing or still has the legacy seeded hash |
| `IdentityBootstrap__MasterPassword` | Strong unique password used only if Master is missing or still has the legacy seeded hash |
| `ForwardedHeaders__KnownProxies__0` | IP address of the trusted reverse proxy, when one is used |

For local development, the web project has a User Secrets ID, so values can be set without editing `appsettings.json`:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<connection string>" --project "Express Service"
dotnet user-secrets set "Jwt:Key" "<new random signing key>" --project "Express Service"
dotnet user-secrets set "SupportPasswordReset:Key" "<new random support key>" --project "Express Service"
```

The database, JWT, and SMTP values previously tracked in Git must be rotated at their providers. Removing a value from the current file does not remove it from Git history and does not revoke it.

## Token and account behavior after deployment

- JWT expiry is calculated from UTC with a five-hour configured lifetime and one minute of validation tolerance.
- Every JWT carries the user's current Identity security stamp. Each authenticated request checks that the account is enabled and the stamp still matches.
- Password reset or password change rotates the security stamp, so prior tokens stop working immediately.
- Tokens issued before this change do not contain the security-stamp claim and will require the user to log in again.
- Disabled users are rejected even if they hold an otherwise valid, unexpired token.
- Admin and member login are limited to ten attempts per client IP per minute in addition to the five-failure account lockout.
- Reverse-proxy forwarding is accepted only from explicitly trusted proxy IPs, preventing a client from choosing a fake rate-limit IP through `X-Forwarded-For`.

## Deployment order

1. Rotate the exposed database, JWT, and SMTP credentials at their providers.
2. Generate a new random support key and configure all required environment variables.
3. Configure the trusted proxy IP if the API is behind a reverse proxy or load balancer.
4. Apply the `RemoveModelSeededIdentityAccounts` migration. It updates only the EF model snapshot and deliberately does not delete deployed users.
5. Deploy the API. Startup rotates Admin/Master only when their hashes still match the known legacy seed; otherwise their current passwords are preserved.
6. Confirm old JWTs are rejected, then log in again to obtain new tokens.
7. Test one locked support account: make the reset request, confirm `AccessFailedCount = 0` and `LockoutEnd = null`, and verify the returned password logs in immediately.
