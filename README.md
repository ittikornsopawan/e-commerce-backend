# E-commerce backend (.NET 10)

CQRS, organised by feature. Controllers are the only entry point; infrastructure lives in its own project.

```
src/
  Ecommerce.Api             Controllers, request contracts, JWT auth, Program.cs (composition root)
  Ecommerce.Application     Features/<Feature>/<UseCase>/: command or query, handler, validator, response
                            Features/<Feature>/Mapping/: domain -> response mapping
                            Abstractions/: ports the handlers need (unit of work, hasher, tokens, email)
  Ecommerce.Domain          <Feature>/Entities, ValueObjects, Repositories (interfaces), errors, enums
                            Common/: Entity, ValueObject, IAggregateRoot
  Ecommerce.Infrastructure  Dapper + Npgsql repositories, PBKDF2 hasher, JWT/token service, field encryption, email stub
  Ecommerce.Shared          Result/Error and the CQRS building blocks (ICommand, IQuery, handlers, validators, dispatcher)
tests/
  Ecommerce.Domain.Tests        Value objects and entities
  Ecommerce.Application.Tests   Handlers with in-memory fakes (no database)
```

Dependencies point inwards:

```
Api ──> Application ──> Domain ──> Shared
 └────> Infrastructure ──> Application, Domain, Shared
```

Domain and Application never reference Infrastructure. Repository interfaces live in Domain, and Infrastructure implements them.
Business rules (username, email and password rules, "only active users sign in", reset-token expiry) live in Domain value objects and entities.
Validators in Application only ask those value objects, so each rule exists once.

## Features

`Features/Authen`: `Register`, `Login`, `ResetPassword` (request + confirm), `GetCurrentUser` (query).

| Method | Route | Use case |
|---|---|---|
| POST | `/api/authen/register` | `RegisterCommand` |
| POST | `/api/authen/login` | `LoginCommand` |
| POST | `/api/authen/password-reset/request` | `RequestPasswordResetCommand` (always 202) |
| POST | `/api/authen/password-reset/confirm` | `ResetPasswordCommand` |
| GET | `/api/authen/me` (Bearer) | `GetCurrentUserQuery` |

To add a feature: put its entities, value objects and repository interfaces in `Domain/<Feature>`, then create `Application/Features/<Feature>/<UseCase>/` with a command or query, an optional `IValidator<T>`, a handler and a response record. Implement the repositories in Infrastructure.
`AddCqrs` finds handlers and validators by scanning the Application assembly. Then add a controller action that calls `IDispatcher.SendAsync`.

## Running

Needs the `authen` and `consent` schemas from `e-commerce/db/migrations/up`. Settings that must not be committed come from environment variables:

```bash
export Database__ConnectionString="Host=localhost;Port=5432;Database=ecommerce;Username=postgres;Password=..."
export Jwt__SigningKey="$(openssl rand -base64 48)"
export Security__FieldEncryptionKey="$(openssl rand -base64 32)"   # keep it: losing it makes username/email/name unreadable
dotnet run --project src/Ecommerce.Api
```

```bash
dotnet test
```

## Notes

- `authen.m_user.username`, `email_address`, `first_name`, `last_name` are `bytea`. They are encrypted deterministically (AES-256-CBC with a synthetic IV) so `WHERE username = @x` still works.
- Passwords: PBKDF2-HMAC-SHA256, 600k iterations, 16-byte salt. Access, refresh and reset tokens are stored as SHA-256 hashes.
- A password reset revokes the user's rows in `t_user_access_token` and `t_user_refresh_token`, but access tokens are plain JWTs and the API does not look them up, so they stay valid until they expire (15 minutes by default).
- There is no refresh-token endpoint yet. `IEmailSender` is a logging stub.
