# New Project Foundation Analysis Report

## Executive conclusion

The repository is a .NET 10 layered monolith with several useful infrastructure ideas, but it is not a clean starter template and should not be cloned or renamed.

The correct approach is to create a blank repository, reproduce only selected architectural concepts, and rewrite the cross-cutting infrastructure behind proper Clean Architecture boundaries. All accounting-related components are explicitly excluded, as requested.

Important findings:

- The current `Domain` project contains EF Core, Identity, migrations, database context, and many infrastructure packages. It is therefore not a pure Domain layer.
- The `Application` project contains business services, HTTP abstractions, EF queries, JWT implementations, file storage, email, background processing, and imports.
- Authorization is primarily hard-coded role authorization. There is no general permission-based authorization framework.
- FluentValidation validators are registered but there is no clear automatic validation pipeline executing them.
- Secrets and live-looking credentials are committed in tracked configuration and the design-time database factory. They must be considered compromised and rotated before any reuse.
- There are 72 existing migrations. None should move to the new repository.
- There is no Docker or deployment configuration.
- The initial repository analysis was read-only and did not modify the existing application code.

---

## 1. Current architecture

### Solution composition

The solution currently contains:

- `Express Service`: ASP.NET Core API host and composition root.
- `Application`: application services, contracts, validation, mapping, JWT implementation, jobs, imports, email, storage, and business workflows.
- `Domain`: entities, ASP.NET Core Identity models, EF Core context/configurations, auditing, and migrations.
- `Accounting.Tests`: all tests, including some non-accounting infrastructure tests.

The project dependency graph is:

```text
Express Service
      ↓
 Application
      ↓
   Domain
```

This is a layered architecture, but not strict Clean Architecture because:

- `Domain` depends on EF Core, Identity, Hangfire, Swagger, MailKit, Mapster, and other infrastructure packages.
- `Application` directly references `Domain.ApplicationDbcontext`.
- `Application` also depends heavily on ASP.NET Core types such as `IFormFile` and `StatusCodes`.
- Infrastructure implementations are distributed between all three projects.

### Request flow

The normal request path is:

```text
Controller → Service interface → Service implementation → ApplicationDbcontext
                                                   ↓
                                              Result<T>
                                                   ↓
                                          ProblemDetails/DTO
```

This is a useful general pattern. Controllers are usually thin, while business logic and EF queries live in services. However, several services and controllers have become very large, including services with thousands of lines.

There is no repository or unit-of-work abstraction. Services use the EF context directly.

### API host

`Express Service/Program.cs` currently configures:

- Controllers
- OpenAPI and Swagger
- CORS
- Global exception handling
- Correlation IDs
- Forwarded headers
- Health checks
- Hangfire and recurring jobs
- Static files
- Response caching
- HTTPS redirection
- Rate limiting
- Authentication and authorization
- Audit request context

The composition root is centralized in `Express Service/ApplicationDependencies.cs`, but it mixes generic registration with dozens of business-specific service registrations.

### Authentication and authorization

The current foundation uses:

- ASP.NET Core Identity
- Custom `ApplicationUser` and `ApplicationRole`
- JWT bearer authentication
- Symmetric JWT signing
- Security-stamp claim in tokens
- Per-request account/security-stamp validation
- Lockout policies
- A fallback policy requiring authentication
- Fixed role names such as `Master`, `Admin`, `Member`, and `Accountant`
- `[Authorize(Roles = "...")]` throughout controllers

The security-stamp validation in `Application/Authentication/JwtProvider.cs` and `Application/Authentication/IJwtAccountValidator.cs` is a worthwhile security concept.

However, this is not permission-based authorization. Role claims are sometimes loaded, but there is no reusable permission catalog, dynamic permission policy provider, or permission handler.

### Persistence and auditing

`Domain/ApplicationDbcontext.cs` extends:

```csharp
IdentityDbContext<ApplicationUser, ApplicationRole, string>
```

It contains:

- Identity tables
- Every business `DbSet`
- EF configuration scanning
- Inline business relationship configuration
- Global decimal conventions
- Cascade-delete rewriting
- System audit generation
- Save transaction management

Auditing captures create/update/delete events and redacts fields by name. The audit-context concept is generic, but the implementation includes business-specific exclusions, display-field names, and scope detection.

There are no reusable base entity classes. Timestamps and audit fields are repeated across entities, often using `DateTime.UtcNow.AddHours(3)`.

### Testing and delivery

The repository has one test project with xUnit and EF Core InMemory. Infrastructure-oriented authentication, Swagger, and auditing tests exist, but they are mixed into the accounting-named project.

The CI workflow restores, builds, tests, checks migrations, and scans configuration for secrets.

Missing operational foundations include:

- Dockerfile
- Docker Compose
- Deployment manifests
- Integration-test host
- Relational database integration tests
- Architecture dependency tests
- Coverage enforcement
- Central package management
- Structured observability configuration

---

## 2. Components safe to reuse

“Reuse” here means reproduce the idea in clean, neutral code—not copy the repository wholesale.

| Component | Recommendation |
|---|---|
| Layered request flow | Reuse the thin-controller → application use case → result pattern. |
| Global exception handler | Reuse with a new namespace and expanded exception-to-Problem-Details mapping. The current handler is a sound starting concept. |
| Correlation IDs | Reuse, but extract into dedicated middleware and include the correlation ID in every error response and logging scope. |
| Authentication fallback policy | Keep the fail-closed default requiring authenticated users. Public endpoints must explicitly use `AllowAnonymous`. |
| JWT security-stamp validation | Keep the account-revocation concept, redesigned for permission cache invalidation and token versioning. |
| Identity lockout and password policy | Keep as configurable security defaults. |
| Pagination records | `Application/Contracts/Common/Pagination.cs` is mostly generic and can be rewritten almost directly. |
| EF configuration scanning | Keep `ApplyConfigurationsFromAssembly`, but place configurations in Infrastructure. |
| `AsNoTracking`/async EF conventions | Retain as coding conventions. |
| `ILogger<T>` usage | Keep structured logging through the standard logging abstraction. |
| Options binding | Keep the options pattern, adding startup validation. |
| Hangfire | Keep only as optional background-job infrastructure, separated from business schedules. |
| Swagger schema collision handling | Keep full-name schema IDs if DTO names can collide. |
| Authentication/audit tests | Port the scenarios into properly named test projects after rewriting the infrastructure. |
| CI workflow shape | Reuse the restore/build/test/migration-check concept after removing repository-specific names and correcting secret/configuration problems. |

---

## 3. Components that should not be reused

### Explicitly excluded accounting area

Do not copy any accounting-related:

- Entities or EF configurations
- Controllers
- Services and contracts
- Ledger or financial access logic
- Financial operations
- Compensation or payroll
- Platform accounting imports
- Accounting storage or outbox
- Accounting migrations
- Accounting documentation
- Accounting tests
- Accounting roles or permissions

### Other business-specific code

Do not carry over:

- Employees, riders, companies, housing, vehicles, shifts, substitutions
- Inventory, spare parts, suppliers, bills, transfers, returns
- Petrol, wallets, vacations, reports, imports
- Keeta/Hunger/platform-specific logic
- AI tool definitions tied to this data model
- File parsers and spreadsheet formats
- Daily reports and email templates
- Existing controllers, DTOs, errors, and service interfaces
- Existing images, fonts, company branding, domains, email addresses, and CORS origins
- `DebugController`
- `temp` services/contracts/entities
- Placeholder export files
- Existing `ApplicationDbcontext`
- All 72 existing migrations and the model snapshot
- Existing database contents or seeded user records
- Existing fixed role IDs, user IDs, concurrency stamps, and role names
- Existing bootstrap Admin/Master accounts
- The support-key password-reset endpoint
- The in-memory singleton `BackgroundImportService`
- Hard-coded recurring job schedules

Even apparently generic organization entities should not be copied. Their design is connected to the excluded accounting model.

---

## 4. Components requiring refactoring before reuse

| Area | Current issue | Required change |
|---|---|---|
| Project boundaries | Domain owns EF/Identity and references unrelated packages. | Create a pure Domain project and a separate Infrastructure project. |
| Result/Error | `Error` contains an HTTP status and the misspelled `StatuesCode`; `Result.IsSuccess` is mutable. | Use immutable results and an HTTP-independent `ErrorType`; map it to HTTP status only in API. |
| Validation | Validators are registered, but no automatic validation execution is apparent. | Add an explicit validation filter/pipeline producing standardized validation Problem Details. |
| Authorization | Endpoints depend on fixed role strings. | Replace all endpoint role checks with named permissions and a dynamic permission handler. |
| JWT | Token generation depends directly on `ApplicationUser`; expiry is an ambiguous integer; refresh tokens are not implemented. | Use neutral token contracts, `TimeSpan` options, key rotation, token versioning, and optional hashed rotating refresh tokens. |
| Identity bootstrap | Creates fixed Admin/Master users using configured passwords. | Provision the first administrator through a deployment command or one-time secure workflow. Never seed passwords. |
| Auditing | Audit code is embedded in the large DbContext and includes business-specific exclusions and field names. | Move auditing to an EF interceptor driven by marker interfaces/attributes and neutral metadata. |
| Base entities | No generic base entities exist. | Introduce minimal `Entity<TId>` and `AuditableEntity<TId>` types; add soft deletion only when explicitly required. |
| Time handling | Extensive `UtcNow.AddHours(3)` usage. | Store UTC only and inject `TimeProvider`; localize at system boundaries. |
| EF context | One context holds Identity, auditing, jobs, and every business entity. | Separate application persistence and Identity contexts, using separate schemas and migrations. |
| DI | One very large registration class mixes infrastructure and business services; duplicate registrations exist. | Give Application and Infrastructure separate `AddApplication`/`AddInfrastructure` methods. |
| Mapping | Global mutable Mapster configuration and business-only mappings. | Use a dedicated configuration instance, per-feature mappings, and startup validation. |
| CORS | Configured twice with different origins/policies. | Use one named policy bound from validated options. |
| OpenAPI | `AddOpenApi` and Swashbuckle overlap; JWT security definition is missing; versioning is configured but not consistently used. | Select one OpenAPI stack, add Bearer security, permission metadata, API versions, and Problem Details schemas. |
| API responses | Success responses vary between raw DTOs, anonymous objects, misspelled message wrappers, and `NoContent`. | Define predictable success semantics and RFC Problem Details for every failure category. |
| Background jobs | Jobs run in the API process, schedules are hard-coded, and some background work uses `Task.Run`. | Use a dedicated Worker host, durable Hangfire jobs, configured schedules, cancellation, retries, and idempotency. |
| Logging | Standard logging exists, but no complete tracing/metrics/export strategy. | Add JSON console logging and OpenTelemetry-compatible tracing and metrics. |
| Configuration | Secrets are tracked and options are not consistently validated on startup. | Rotate all exposed credentials and use environment/secret-store configuration with `ValidateOnStart`. |
| Tests | One business-named project uses EF InMemory for most tests. | Split unit, integration, API, and architecture tests; use a real relational test database for EF behavior. |
| Docker/deployment | Absent. | Add multi-stage containers for API and Worker plus local Compose infrastructure. |

A critical precondition is rotating every credential currently present in `Express Service/appsettings.json` and `Domain/ApplicationDbcontextFactory.cs`. Do not copy their values into the new repository.

---

## 5. Proposed architecture for the new project

Use a modular Clean Architecture monolith with a separate background worker:

```text
API ──────────────→ Application ──────────────→ Domain
 │                       ↑
 └────→ Infrastructure ──┘
Worker ─→ Infrastructure/Application
```

### Responsibilities

- **Domain:** business entities, value objects, domain events, domain rules, and base entity abstractions. No EF Core, Identity, HTTP, Hangfire, or logging packages.
- **Application:** use cases, feature contracts, validators, application interfaces, results, pagination, permission constants, and mapping definitions.
- **Infrastructure:** EF Core, SQL Server, Identity, JWT, permission evaluation, caching, auditing implementation, file/storage adapters, Hangfire, email adapters, and clock implementation.
- **API:** controllers, HTTP binding, Problem Details, exception handling, authorization attributes, rate limiting, CORS, OpenAPI, and dependency composition.
- **Worker:** Hangfire server, recurring-job registration, job execution, health endpoints if needed.

### Permission-based authorization

Roles may remain as administrative groupings, but endpoints must never authorize directly by role.

Recommended model:

```text
User
 ├─ Roles
 │    └─ RolePermissions
 ├─ Optional direct grants
 └─ Optional direct denies
             ↓
       Effective permissions
             ↓
 [HasPermission("users.read")]
```

Implement:

- Stable permission names such as `users.read`, `users.manage`, `orders.approve`.
- `PermissionAuthorizationRequirement`
- `PermissionAuthorizationHandler`
- Dynamic `IAuthorizationPolicyProvider`
- `[HasPermission(...)]` attribute
- `IPermissionService`
- Permission cache keyed by user and authorization/security version
- Immediate cache invalidation and security-stamp/version rotation after role or permission changes
- Default-deny behavior for missing permissions
- Permission-aware Hangfire dashboard access
- Tests proving that role names alone cannot access protected endpoints

Roles become bundles of permissions; they are not authorization rules themselves.

### Data access

Do not add a generic repository over EF Core.

Use:

- `IApplicationDbContext` for ordinary application use cases.
- Feature-specific repository interfaces only for complex aggregates or storage that cannot be expressed cleanly through the context.
- Query services for complex read models.
- Explicit transactions for multi-step use cases.
- UTC timestamps through `TimeProvider`.

---

## 6. Exact folder/project structure

Use `NewSystem` as a placeholder until the actual product name is selected.

```text
NewSystem/
├─ .github/
│  └─ workflows/
│     ├─ ci.yml
│     └─ container.yml
├─ deploy/
│  ├─ api.Dockerfile
│  ├─ worker.Dockerfile
│  ├─ docker-compose.yml
│  └─ .dockerignore
├─ docs/
│  ├─ architecture/
│  │  ├─ overview.md
│  │  └─ dependency-rules.md
│  └─ adr/
├─ src/
│  ├─ NewSystem.Domain/
│  │  ├─ Common/
│  │  │  ├─ Entity.cs
│  │  │  ├─ AuditableEntity.cs
│  │  │  └─ DomainEvent.cs
│  │  ├─ Exceptions/
│  │  └─ NewSystem.Domain.csproj
│  ├─ NewSystem.Application/
│  │  ├─ Abstractions/
│  │  │  ├─ Authentication/
│  │  │  ├─ Authorization/
│  │  │  ├─ Caching/
│  │  │  ├─ Jobs/
│  │  │  ├─ Persistence/
│  │  │  ├─ Storage/
│  │  │  └─ Time/
│  │  ├─ Common/
│  │  │  ├─ Behaviors/
│  │  │  ├─ Mapping/
│  │  │  ├─ Pagination/
│  │  │  ├─ Results/
│  │  │  └─ Validation/
│  │  ├─ Features/
│  │  │  └─ <FeatureName>/
│  │  │     ├─ Commands/
│  │  │     ├─ Queries/
│  │  │     ├─ Contracts/
│  │  │     ├─ Mapping/
│  │  │     └─ Validators/
│  │  ├─ DependencyInjection.cs
│  │  └─ NewSystem.Application.csproj
│  ├─ NewSystem.Infrastructure/
│  │  ├─ Authentication/
│  │  ├─ Authorization/
│  │  ├─ Auditing/
│  │  ├─ BackgroundJobs/
│  │  ├─ Caching/
│  │  ├─ Identity/
│  │  │  ├─ ApplicationUser.cs
│  │  │  ├─ ApplicationRole.cs
│  │  │  ├─ IdentityDbContext.cs
│  │  │  └─ Migrations/
│  │  ├─ Persistence/
│  │  │  ├─ ApplicationDbContext.cs
│  │  │  ├─ Configurations/
│  │  │  ├─ Interceptors/
│  │  │  └─ Migrations/
│  │  ├─ Storage/
│  │  ├─ Time/
│  │  ├─ DependencyInjection.cs
│  │  └─ NewSystem.Infrastructure.csproj
│  ├─ NewSystem.Api/
│  │  ├─ Authorization/
│  │  ├─ Controllers/
│  │  ├─ ErrorHandling/
│  │  ├─ Extensions/
│  │  ├─ Middleware/
│  │  ├─ OpenApi/
│  │  ├─ Options/
│  │  ├─ Program.cs
│  │  ├─ appsettings.json
│  │  ├─ appsettings.Development.json
│  │  └─ NewSystem.Api.csproj
│  └─ NewSystem.Worker/
│     ├─ Jobs/
│     ├─ Scheduling/
│     ├─ Program.cs
│     ├─ appsettings.json
│     └─ NewSystem.Worker.csproj
├─ tests/
│  ├─ NewSystem.Domain.UnitTests/
│  ├─ NewSystem.Application.UnitTests/
│  ├─ NewSystem.Infrastructure.IntegrationTests/
│  ├─ NewSystem.Api.IntegrationTests/
│  └─ NewSystem.ArchitectureTests/
├─ .editorconfig
├─ .gitignore
├─ Directory.Build.props
├─ Directory.Packages.props
├─ global.json
├─ README.md
└─ NewSystem.slnx
```

No business feature should be created until its requirements are defined.

---

## 7. Dependencies/packages that should carry over

Carry the capabilities, but centralize versions in `Directory.Packages.props`.

| Package/capability | Target project |
|---|---|
| `Microsoft.EntityFrameworkCore` | Infrastructure |
| `Microsoft.EntityFrameworkCore.SqlServer` | Infrastructure |
| `Microsoft.EntityFrameworkCore.Design` | Infrastructure, `PrivateAssets=all` |
| `Microsoft.EntityFrameworkCore.Tools` | Tooling only if required |
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` | Infrastructure |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | API/Infrastructure |
| `FluentValidation` | Application |
| `FluentValidation.DependencyInjectionExtensions` | API composition |
| `Mapster` and `Mapster.DependencyInjection` | Application/API |
| Swashbuckle OpenAPI packages | API only |
| `Hangfire.AspNetCore` and `Hangfire.SqlServer` | Infrastructure/Worker, optional |
| `Microsoft.Extensions.Caching.StackExchangeRedis` | Infrastructure, optional |
| `Asp.Versioning.Mvc` and API Explorer | API only if formal API versioning is required |
| `Microsoft.NET.Test.Sdk`, xUnit, xUnit runner | Test projects |
| `Microsoft.EntityFrameworkCore.InMemory` | Limited unit tests only |

Recommended new testing/operations dependencies:

- `Microsoft.AspNetCore.Mvc.Testing`
- SQL Server Testcontainers or an equivalent disposable relational database
- Architecture dependency testing
- Coverage collector
- OpenTelemetry hosting, ASP.NET Core, HTTP, and EF instrumentation

---

## 8. Dependencies that should be removed

Remove from the starter unless a future feature explicitly requires them:

- `ClosedXML`
- `DocumentFormat.OpenXml`
- `QuestPDF`
- `HTMLToQPDF`
- `MailKit`
- `KubernetesClient`
- `AspNetCore.HealthChecks.UI`
- Direct `Microsoft.OpenApi` reference
- Duplicate `AddOpenApi`/Swashbuckle stacks
- Business reporting/export packages
- Gemini/AI-specific dependencies
- All accounting packages and code

Also remove misplaced packages:

- No EF Core, Identity, Hangfire, Swagger, MailKit, Mapster, or FluentValidation references in Domain.
- No Swagger or Hangfire references in Application.
- No EF design/tools references in API.
- Do not directly reference `Microsoft.EntityFrameworkCore.Abstractions` unless code genuinely requires it.
- Keep each package in the one project that owns its implementation.

---

## 9. Database and authentication considerations

### Database

- Create a completely new database.
- Do not copy the current schema, migrations, snapshot, or data.
- Use separate schemas, for example:
  - `app` for new business data
  - `identity` for ASP.NET Core Identity
  - `audit` for audit events
  - `jobs` for Hangfire
- Maintain separate migrations for application and Identity contexts.
- Store all timestamps in UTC.
- Define decimal precision explicitly per feature.
- Avoid global delete behaviors that silently rewrite every relationship.
- Use concurrency tokens on entities that can receive competing updates.
- Add soft deletion only when the business needs restoration or historical retention.
- Use migrations to create clean permission and Identity infrastructure, not model-seeded users.

### Identity and tokens

- Keep ASP.NET Core Identity, but define a new minimal `ApplicationUser`.
- Do not inherit `Address`, `FullName`, or other current fields without new requirements.
- Seed permission definitions and optional role templates only.
- Never seed user passwords or password hashes.
- Provision the first administrator through a one-time secured operation.
- Store signing material in a secret manager or environment configuration.
- Support signing-key rotation.
- Use short-lived access tokens.
- If refresh tokens are needed, store only hashed refresh tokens, rotate them on every use, and detect replay.
- Include `sub`, `jti`, issuer, audience, expiry, and token/security version claims.
- Do not rely on role claims to authorize endpoints.
- Make permission changes invalidate the user’s permission cache and active token version.
- Validate JWT and Identity options on startup.
- Test disabled users, permission revocation, token expiry, key changes, and concurrent refresh attempts.

---

## 10. Migration plan into a clean starter foundation

1. Select the new product name, root namespace, database name, and repository name.

2. Create a blank Git repository rather than branching or copying this repository.

3. Scaffold the Domain, Application, Infrastructure, API, Worker, and test projects using the dependency graph above.

4. Add central package management, build properties, nullable enforcement, analyzers, formatting rules, and warnings-as-errors.

5. Implement neutral result, error, pagination, time, current-user, and persistence abstractions.

6. Implement standardized Problem Details, correlation middleware, exception handling, API response conventions, and validation execution.

7. Create new Identity and application contexts without any current business entities.

8. Implement permission-based authorization, permission storage, dynamic policies, caching, and invalidation.

9. Implement JWT generation and validation against the new Identity model, including token/security version handling.

10. Add neutral auditing using EF interceptors and a request/job audit context.

11. Add Hangfire behind abstractions and run its server in the Worker project. Do not add current job schedules.

12. Add validated options for JWT, database, CORS, caching, jobs, forwarding, and storage.

13. Generate brand-new initial migrations for Identity, permissions, auditing, and any selected job schema.

14. Add unit, relational integration, API authorization, validation, audit, and architecture tests.

15. Add Dockerfiles, Docker Compose, health checks, graceful shutdown, and container-safe configuration.

16. Build CI for restore, build, tests, coverage, formatting, architecture rules, migration checks, secret scanning, and container builds.

17. Verify that the new repository contains none of the old:
    - Product names
    - Business namespaces
    - Domains or email addresses
    - Role names
    - Entity names
    - Migrations
    - Database data
    - Credentials
    - Accounting components

18. Only then begin implementing the new system’s features as independent feature slices.

---

## Implementation change sheet

Use this as the implementation checklist when development starts:

- [ ] Create a new repository from an empty directory.
- [ ] Create the five production projects and five test projects.
- [ ] Enforce the Clean Architecture project references.
- [ ] Move all EF Core and Identity implementation into Infrastructure.
- [ ] Keep Domain free of infrastructure packages.
- [ ] Replace the current result/error code with an immutable, HTTP-independent design.
- [ ] Add a real FluentValidation execution pipeline.
- [ ] Replace every role-based endpoint rule with a permission requirement.
- [ ] Add permission catalog, persistence, policy provider, handler, cache, and invalidation.
- [ ] Rebuild Identity and JWT without fixed users or business role names.
- [ ] Create new database contexts and new initial migrations.
- [ ] Implement UTC auditing through EF interceptors.
- [ ] Introduce minimal base entities and `TimeProvider`.
- [ ] Centralize CORS, rate limiting, forwarded headers, and security options.
- [ ] Standardize all failures as Problem Details.
- [ ] Configure OpenAPI with JWT and permission metadata.
- [ ] Move background processing to the Worker host.
- [ ] Add structured logging, traces, metrics, and health checks.
- [ ] Add relational integration and API authorization tests.
- [ ] Add Docker and CI/CD foundations.
- [ ] Rotate all credentials found in the current repository.
- [ ] Copy no current controllers, services, entities, migrations, business DTOs, accounting code, or database data.
