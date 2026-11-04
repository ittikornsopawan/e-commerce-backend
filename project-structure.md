# Project Structure

The e-commerce backend is written in .NET 10 using **CQRS organised by feature**. It is split into 5 projects, one per layer of the code.
All projects are in `Ecommerce.sln`.

## Overview

```
repository/
├── Ecommerce.sln
├── Directory.Build.props          Shared settings for every project (net10.0, Nullable, TreatWarningsAsErrors)
├── README.md                      How to run, environment variables, security notes
├── project-structure.md           This file
├── src/
│   ├── Ecommerce.Api              Entry point for requests (controllers)
│   ├── Ecommerce.Application      Use cases: command, query, handler, validator, mapping
│   ├── Ecommerce.Domain           Entities, value objects, business rules, repository interfaces
│   ├── Ecommerce.Infrastructure   Everything that talks to the outside world: DB, hashing, JWT, email
│   └── Ecommerce.Shared           Result/Error and the CQRS building blocks
└── tests/
    ├── Ecommerce.Domain.Tests
    └── Ecommerce.Application.Tests
```

## Dependency direction

```
Api ──────────> Application ──> Domain ──> Shared
 │                   ▲             ▲
 └──> Infrastructure ┴─────────────┘
          (Infrastructure also references Shared)
```

| Project          | May reference                             |
| ---------------- | ----------------------------------------- |
| `Shared`         | Nothing                                   |
| `Domain`         | `Shared`                                  |
| `Application`    | `Domain`, `Shared`                        |
| `Infrastructure` | `Application`, `Domain`, `Shared`         |
| `Api`            | `Application`, `Infrastructure`, `Shared` |

The key rule: **`Domain` and `Application` must never reference `Infrastructure`.**
Anything that needs the database or an external service is an interface on the inner side (`Domain` or `Application`),
and `Infrastructure` implements it. `Api` is the only place that wires both sides together (in `Program.cs`).

---

## `Ecommerce.Shared`

Common code that does not belong to any feature.

```
Shared/
├── Results/
│   ├── Result.cs         Result and Result<T>: the outcome of a use case (success, or failure with a list of Errors)
│   └── Error.cs          Error(Code, Description, Type) and the types Validation/NotFound/Conflict/Unauthorized/Forbidden
└── Cqrs/
    ├── Messaging.cs      ICommand, ICommand<T>, IQuery<T>, their handlers, IValidator<T>, IDispatcher
    ├── Dispatcher.cs     Runs the validators first, then calls the handler
    └── CqrsServiceCollectionExtensions.cs   AddCqrs(assembly) scans for handlers and validators and registers them in DI
```

- A use case does not throw for expected failures (duplicate email, wrong password). It returns `Result.Failure(error)`.
- A command changes state. A query only returns data and never changes state.

## `Ecommerce.Domain`

The heart of the system: business rules live here. It knows nothing about the database, HTTP or external libraries.

```
Domain/
├── Common/
│   ├── Entity.cs          Base class for entities (equal when the Id is equal)
│   ├── ValueObject.cs     Base class for value objects (equal when the values are equal)
│   └── IAggregateRoot.cs  Marker for the entity that is the root of an aggregate
├── DuplicateKeyException.cs   Thrown by a repository on a unique-constraint violation (for example two simultaneous sign-ups)
└── Authen/                    Folder for the Authen feature
    ├── Entities/
    │   ├── User.cs                 Aggregate root: Register(...), EnsureCanSignIn()
    │   └── PasswordResetToken.cs   Single use, and only before it expires: IsUsableAt(), Consume()
    ├── ValueObjects/
    │   ├── UserId.cs, Username.cs, EmailAddress.cs, PersonName.cs, DisplayName.cs
    │   ├── PlainPassword.cs        A new password that passed the password policy
    │   ├── PasswordCredential.cs   Hash + salt (the real password is never kept)
    │   └── LoginIdentifier.cs      What is typed into the "username or email" login field
    ├── Repositories/
    │   └── IAuthenRepositories.cs  IUserRepository, IPasswordRepository, ITokenRepository (interfaces only)
    ├── UserStatus.cs               Active / Inactive / Deleted / Locked / Archived
    └── AuthenErrors.cs             The Errors this feature uses
```

- **Value objects** are created through `Create(...)`, which returns `Result<T>`. It checks the rules (for example a username must be 3-32 characters)
  and normalizes the input (trim, lower-case), so each rule lives in exactly one place.
- `Restore(...)` rebuilds an object from the database without re-checking the rules. Only `Infrastructure` uses it.

## `Ecommerce.Application`

The use cases: they coordinate the domain and the external ports. Folders are organised **by feature, then by use case**.

```
Application/
├── AssemblyReference.cs        Passed to AddCqrs so it scans this assembly
├── Abstractions/               Ports the handlers need (Infrastructure implements them)
│   ├── IUnitOfWork.cs          Runs work in a single transaction
│   ├── IPasswordHasher.cs
│   ├── ITokenService.cs        Issues JWT / refresh / reset tokens and hashes tokens
│   └── IEmailSender.cs
├── Common/
│   └── ValidationErrors.cs     Collects the errors of several results for a validator
└── Features/
    └── Authen/
        ├── Register/           RegisterCommand, RegisterCommandHandler, RegisterCommandValidator, RegisterResponse
        ├── Login/              LoginCommand, LoginCommandHandler, LoginCommandValidator, LoginResponse
        ├── ResetPassword/      RequestPasswordReset* (ask for a link) and ResetPassword* (set the new password)
        ├── GetCurrentUser/     GetCurrentUserQuery, GetCurrentUserQueryHandler, CurrentUserResponse   <- the query side
        └── Mapping/
            └── AuthenMappings.cs   Maps the domain (User) to response records, written by hand
```

A use case consists of these files:

| File                      | Purpose                                                       |
| ------------------------- | ------------------------------------------------------------- |
| `XxxCommand` / `XxxQuery` | The input (a record)                                          |
| `XxxValidator`            | Checks the shape of the input by calling the Domain value objects |
| `XxxHandler`              | The steps of the use case                                     |
| `XxxResponse`             | The data that is returned                                     |

Domain objects never leave the Application layer: the handler maps them to a `Response` through the `Mapping/` folder before returning.

## `Ecommerce.Infrastructure`

Implements the Domain and Application interfaces with real technology. It is a separate project.

```
Infrastructure/
├── DependencyInjection.cs      AddInfrastructure(config): registers every service and validates the configuration at startup
├── Options/
│   └── InfrastructureOptions.cs   DatabaseOptions, JwtOptions, SecurityOptions
├── Persistence/
│   ├── DbSession.cs            The connection and transaction for one request
│   ├── UnitOfWork.cs           IUnitOfWork
│   └── Authen/
│       ├── UserRepository.cs
│       ├── PasswordRepository.cs
│       └── TokenRepository.cs  Dapper + SQL that matches the `authen.*` schema in db/migrations
├── Security/
│   ├── Pbkdf2PasswordHasher.cs   IPasswordHasher
│   ├── TokenService.cs           ITokenService (JWT + opaque tokens + SHA-256)
│   └── FieldProtector.cs         Encrypts the bytea columns of m_user (same input gives the same output, so `WHERE x = ...` still works)
└── Email/
    └── LoggingEmailSender.cs     Development stand-in: writes to the log instead of sending mail
```

The repositories convert between database rows and domain objects (`User.Restore(...)`) in this layer.

## `Ecommerce.Api`

The outermost layer: it receives HTTP and hands the work to `IDispatcher`. It contains no business logic.

```
Api/
├── Program.cs                  Composition root: wires DI, JWT auth, OpenAPI
├── Controllers/
│   └── AuthenController.cs     Routes under /api/authen/...
├── Contracts/
│   └── Authen/AuthenRequests.cs   The request body of each endpoint
├── Extensions/
│   └── ResultExtensions.cs     Turns a failed Result into ProblemDetails (400/401/403/404/409)
├── appsettings.json            Contains no secrets (set them through environment variables)
└── Properties/launchSettings.json
```

A controller only does this: receive the request → build the command/query → `dispatcher.SendAsync(...)` → turn the `Result` into an HTTP response.

---

## Request flow (example: `POST /api/authen/register`)

```
HTTP request
  └─ AuthenController.Register          (Api)         builds a RegisterCommand
      └─ IDispatcher.SendAsync          (Shared)
          ├─ RegisterCommandValidator   (Application) calls Username.Create, EmailAddress.Create, PlainPassword.Create ...
          │     └─ invalid → Result.Failure (400), the handler is not called
          └─ RegisterCommandHandler     (Application)
              ├─ builds the value objects, checks for a duplicate username/email via IUserRepository
              ├─ User.Register(...)     (Domain)
              ├─ IPasswordHasher.Hash   (Infrastructure)
              ├─ IUnitOfWork: adds the user + password in one transaction
              └─ user.ToRegisterResponse()  (Mapping)
  └─ ResultExtensions                   (Api)         success → 201 / failure → ProblemDetails
```

## Current endpoints

| Method | Route                                  | Use case                                    |
| ------ | -------------------------------------- | ------------------------------------------- |
| POST   | `/api/authen/register`                 | `RegisterCommand`                           |
| POST   | `/api/authen/login`                    | `LoginCommand`                              |
| POST   | `/api/authen/password-reset/request`   | `RequestPasswordResetCommand` (always 202)  |
| POST   | `/api/authen/password-reset/confirm`   | `ResetPasswordCommand`                      |
| GET    | `/api/authen/me` (needs a Bearer token) | `GetCurrentUserQuery`                       |

## Tests

```
tests/
├── Ecommerce.Domain.Tests/        Value objects and entities (no database needed)
└── Ecommerce.Application.Tests/   Handlers run through the dispatcher with in-memory fake repositories (no database needed)
```

```bash
dotnet test
```

## Adding a new feature (for example Product)

1. **Domain** — create `Domain/Product/` with `Entities/`, `ValueObjects/`, `Repositories/` (interfaces) and the feature's errors.
2. **Application** — create `Application/Features/Product/<UseCase>/` with the command or query, validator, handler and response,
   plus a `Mapping/` folder for the feature. There is no DI registration to write: `AddCqrs` finds them by scanning.
3. **Infrastructure** — add `Persistence/Product/` to implement the repositories, and register them in `DependencyInjection.cs`.
4. **Api** — add a controller and request contracts that call `IDispatcher`.
5. **Tests** — add tests to `Ecommerce.Domain.Tests` and `Ecommerce.Application.Tests`.
