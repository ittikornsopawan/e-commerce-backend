---
name: E-commerce Backend Project Structure
description: Explain the backend layers, the folder of every kind of file, the project references and the conventions for adding a use case; read before changing a use case or adding a feature in this Component Repository.
---

# Project Structure

The e-commerce backend is written in .NET 11 (C# 15) with **CQRS organised by feature**. Five production projects, one per layer, and four test projects are in `Ecommerce.sln`. `Directory.Build.props` sets `net11.0`, nullable reference types and `TreatWarningsAsErrors` for every project.

The behaviour comes from the approved specifications of the Showcase (`e-commerce/doc/api/<domain>/<nn-api>/spec.md` and `sequence.wsd`). The shared functions are listed in the Showcase Function Book (`e-commerce/doc/function-book.md`); read it before writing a new function.

## Layers and references

```
Api ──> Application ──> Domain ──> Shared
 └────> Infrastructure ──> Application, Domain, Shared
```

| Project | References | Owns |
| --- | --- | --- |
| `Ecommerce.Api` | Application, Infrastructure, Shared | HTTP: routes, request shape, the response envelope, JWT verification, rate limits, the composition root |
| `Ecommerce.Application` | Domain, Shared | Use cases: command or query, validator, handler; the ports a handler needs |
| `Ecommerce.Domain` | Shared | Business rules, value objects, repository interfaces, the feature's errors |
| `Ecommerce.Infrastructure` | Application, Domain, Shared | EF Core persistence, the transaction runner, security adapters, email stub, options and DI |
| `Ecommerce.Shared` | none | Result/Error, CQRS building blocks, validation messages, `Optional<T>`, paging |

Domain and Application never reference Infrastructure. A runtime call to an adapter goes through an interface that Domain or Application declares.

## Source tree

```
src/
├── Ecommerce.Api/
│   ├── Program.cs                  composition root: filters, CQRS, Infrastructure, JWT, rate limits
│   ├── Controllers/
│   │   ├── AuthenController.cs     v1/auth: specs 01-06
│   │   └── UserController.cs       v1/user: specs 07-10
│   ├── Contracts/
│   │   ├── ApiEnvelope.cs          {status, data} of api-conventions
│   │   ├── JsonBody.cs             body read by key presence (partial updates)
│   │   ├── QueryParameters.cs      GET query read: known keys, each once, no body
│   │   └── Authen/                 request records and body field maps
│   ├── Extensions/                 ResultExtensions (ToSuccess/ToFailure), HttpEnvelope
│   ├── Filters/                    RequiredHeadersFilter, InvalidBody, EnvelopeExceptionHandler
│   └── Security/                   VerifiedCaller, CallerClaims, RateLimiting
├── Ecommerce.Application/
│   ├── Abstractions/               ITransactionRunner, IPasswordHasher, ITokenService, IEmailSender (+ settings records)
│   ├── Common/CommonErrors.cs      Timeout (408 E90001)
│   └── Features/Authen/
│       ├── Common/                 Caller + CallerCredential, SessionTokens, TokenPairResponse
│       └── <UseCase>/              <UseCase>Command|Query, <UseCase>…Validator, <UseCase>…Handler (+ response)
├── Ecommerce.Domain/
│   ├── Common/                     ValueObject, EmailAddressFormat, ProfileFieldRules
│   ├── DuplicateKeyException.cs, MissingReferenceException.cs
│   └── Authen/
│       ├── Domain/                 business rules ("Service = Domain"): AuthorizationClaims, RefreshTokenReuse,
│       │                           AddressDefaultPolicy, RegistrationConsentPolicy, PasswordResetThrottle
│       ├── ValueObjects/           PlainPassword, PasswordCredential and the records the repositories exchange
│       ├── Repositories/           one interface per use case or shared step
│       └── AuthenErrors.cs         every authen error with its spec status, bizCode and text
├── Ecommerce.Infrastructure/
│   ├── DependencyInjection.cs      AddInfrastructure: security, persistence, repositories, settings
│   ├── Persistence/
│   │   ├── EfTransactionRunner.cs  ITransactionRunner on EF Core
│   │   ├── Ef/                     EcommerceDbContext, record classes, ForUpdate, AdvisoryLock
│   │   └── Authen/                 one LINQ repository per Domain interface, SessionRevocation
│   ├── Security/                   Pbkdf2PasswordHasher, TokenService, FieldProtector
│   ├── Email/LoggingEmailSender.cs development stub
│   └── Options/                    Database, Jwt, Security options
└── Ecommerce.Shared/
    ├── Cqrs/                       ICommand, IQuery, handlers, IValidator, IDispatcher, AddCqrs
    ├── Results/                    Result, Result<T>, Error, ErrorType
    ├── Validation/                 ValidationMessages
    ├── Paging/                     PageRequest, Page<T>
    └── Optional.cs
```

## Request flow (example: `POST v1/user/address/upsert`, spec 09)

```
HTTP request
  └─ RequiredHeadersFilter                 (Api)          timestamp, content-type → 400 E10001
  └─ UserController.UpsertAddress          (Api)          JsonBody shape check; VerifiedCaller.Of(this)
      └─ IDispatcher.SendAsync             (Shared)
          ├─ UpsertUserAddressCommandValidator (Application) → 400 before any SQL, before the JWT check
          └─ UpsertUserAddressCommandHandler   (Application)
              └─ ITransactionRunner.RunAsync(ReadCommitted)       (EfTransactionRunner)
                  ├─ CallerCredential.VerifyAsync   lock the user FOR UPDATE, fresh now, stored token → 401
                  ├─ IUserAddressRepository         LINQ reads and guarded writes
                  └─ AddressDefaultPolicy / AddressQuota (Domain) decide the default and the limit
  └─ ResultExtensions                     (Api)          success → 201/200/204; failure → status envelope
```

## Endpoints

| Method | Route | Spec | Caller |
| --- | --- | --- | --- |
| POST | `/v1/auth/register` | 01-register | public, 10/min |
| POST | `/v1/auth/login` | 02-login | public, 10/min |
| POST | `/v1/auth/reset-password` | 03-reset-password | public, 10/min |
| POST | `/v1/auth/create-new-password` | 04-create-new-password | public, 10/min |
| POST | `/v1/auth/refresh-token` | 05-refresh-token | public, 60/min |
| POST | `/v1/auth/logout` | 06-logout | Bearer |
| POST | `/v1/user/profile/upsert` | 07-upsert-user-profile | Bearer |
| GET | `/v1/user/profile` | 08-inquiry-user-profile | Bearer |
| POST | `/v1/user/address/upsert` | 09-upsert-user-address | Bearer |
| GET | `/v1/user/address` | 10-inquiry-user-address | Bearer |

## Adding a use case

1. Read the approved spec and its sequence diagram, then the Function Book; reuse before writing.
2. Domain: put a business rule in `<Feature>/Domain/`, its values in `ValueObjects/`, its error in `<Feature>Errors`, and one repository interface in `Repositories/`.
3. Application: create `Features/<Feature>/<UseCase>/` with the command or query, an `IValidator<T>` (every 400 before SQL) and the handler. The handler runs its work through `ITransactionRunner` at the spec's isolation level (`readOnly: true` for a READ ONLY inquiry) and keeps each step a short named method.
4. Infrastructure: implement the repository with EF Core LINQ; lock with `ForUpdate()`, never raw SQL. Register it in `AddAuthenRepositories` (or the feature's equivalent).
5. Api: add the action; read the caller with `VerifiedCaller.Of(this)` for a protected API, so a 400 answers before a 401.
6. Tests: write `test-cases.md` with api-test-case-creator in the Showcase, automate every case (test names start with the case ID), run mutation checks, and add any new shared function to the Function Book.

## Tests

```
tests/
├── Ecommerce.Domain.Tests/          business rules and value objects (no database)
├── Ecommerce.Application.Tests/     handlers with in-memory fakes (no database)
├── Ecommerce.Infrastructure.Tests/  repositories and the transaction runner on a throw-away PostgreSQL 17
│                                    (PostgresFixture loads every Showcase migration; RegistrationFixtures seed data)
└── Ecommerce.Api.Tests/             each spec's test cases through the HTTP pipeline and the real database
```

`PostgresFixture` needs PostgreSQL 17's `initdb` and `pg_ctl` on the PATH (a missing tool fails the run; it is never skipped). It reads the migrations from `e-commerce/db/migrations/up` two levels above this checkout, or from `ECOMMERCE_MIGRATIONS_DIR`. Run everything with `dotnet test Ecommerce.sln`.
