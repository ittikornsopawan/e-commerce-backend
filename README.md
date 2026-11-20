---
name: E-commerce Backend
description: Run and develop the independently versioned e-commerce backend; read when opening this Component Repository or configuring its local runtime.
---

# E-commerce backend (.NET 11)

This is the independent backend Component Repository for the [e-commerce Showcase Project](https://github.com/ittikornsopawan/system-analysis-and-design/tree/48c75ab59f27e915d0bff7acf4395eeaafca330a/e-commerce). It is materialized under `e-commerce/repository/e-commerce-backend/` when used from the Showcase. API contracts, schema specifications, database migrations and system diagrams remain owned by the Showcase.

## Repository provenance

- Source Showcase commit: `48c75ab59f27e915d0bff7acf4395eeaafca330a`.
- History: the four commits affecting the former `e-commerce/repository/` subtree were retained with `git subtree split`; rewritten commit IDs are specific to this independent repository.
- Imported tree: all 81 tracked files matched the original snapshot byte for byte before the documentation-only migration updates. Runtime source, tests and project configuration remain unchanged.
- The code implements the approved authen-and-author specs 01–10 of the Showcase (`e-commerce/doc/api/authen-and-author/`). [project-structure.md](project-structure.md) lists the files and how to add a use case; [code.md](code.md) (Thai) explains the request flow, transactions, errors and tests; the shared functions are in the Showcase Function Book (`e-commerce/doc/function-book.md`).

CQRS, organised by feature. Controllers are the only entry point; infrastructure lives in its own project.

```
src/
  Ecommerce.Api             Controllers, request contracts, JWT verification, envelope, filters, rate limits, Program.cs
  Ecommerce.Application     Features/<Feature>/<UseCase>/: command or query, handler, validator, response
                            Abstractions/: ports the handlers need (transaction runner, hasher, tokens, email)
  Ecommerce.Domain          <Feature>/Domain (business rules), ValueObjects, Repositories (interfaces), errors
  Ecommerce.Infrastructure  EF Core + Npgsql repositories (LINQ), transaction runner, PBKDF2 hasher, JWT/token service,
                            field encryption, email stub
  Ecommerce.Shared          Result/Error, validation messages, paging and the CQRS building blocks
tests/
  Ecommerce.Domain.Tests          Business rules and value objects
  Ecommerce.Application.Tests     Handlers with in-memory fakes (no database)
  Ecommerce.Infrastructure.Tests  Repositories and the transaction runner on a throw-away PostgreSQL 17
  Ecommerce.Api.Tests             Each spec's test cases through the HTTP pipeline and the real database
```

Dependencies point inwards:

```
Api ──> Application ──> Domain ──> Shared
 └────> Infrastructure ──> Application, Domain, Shared
```

Domain and Application never reference Infrastructure. Repository interfaces live in Domain, and Infrastructure implements them.
Each use case runs in one transaction through `ITransactionRunner` at the spec's isolation level (read-only for inquiries).

## Features

| Method | Route                             | Spec                       |
| ------ | --------------------------------- | -------------------------- |
| POST   | `/v1/auth/register`               | 01-register                |
| POST   | `/v1/auth/login`                  | 02-login                   |
| POST   | `/v1/auth/reset-password`         | 03-reset-password          |
| POST   | `/v1/auth/create-new-password`    | 04-create-new-password     |
| POST   | `/v1/auth/refresh-token`          | 05-refresh-token           |
| POST   | `/v1/auth/logout` (Bearer)        | 06-logout                  |
| POST   | `/v1/user/profile/upsert` (Bearer)| 07-upsert-user-profile     |
| GET    | `/v1/user/profile` (Bearer)       | 08-inquiry-user-profile    |
| POST   | `/v1/user/address/upsert` (Bearer)| 09-upsert-user-address     |
| GET    | `/v1/user/address` (Bearer)       | 10-inquiry-user-address    |

To add a use case: put its rules, value objects and repository interface in `Domain/<Feature>`, then create `Application/Features/<Feature>/<UseCase>/` with a command or query, an `IValidator<T>`, a handler and a response record. Implement the repository in Infrastructure with EF Core LINQ.
`AddCqrs` finds handlers and validators by scanning the Application assembly. Then add a controller action that calls `IDispatcher.SendAsync`.

## Running

Needs the `authen` and `consent` schemas from the [Showcase database migrations](https://github.com/ittikornsopawan/system-analysis-and-design/tree/48c75ab59f27e915d0bff7acf4395eeaafca330a/e-commerce/db/migrations/up). Settings that must not be committed come from environment variables:

```bash
export Database__ConnectionString="Host=localhost;Port=5432;Database=ecommerce;Username=postgres;Password=..."
export Jwt__SigningKey="$(openssl rand -base64 48)"
export Security__FieldEncryptionKey="$(openssl rand -base64 32)"   # keep it: losing it makes username/email/name unreadable
# private key per key id for the encrypted request fields of 01-04 (RSA, at least 3072 bits); the frontend gets only the public key and the id
openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out fe-2026-11.pem
export Security__FieldTransport__Keys__fe-2026-11="$(cat fe-2026-11.pem)"
dotnet run --project src/Ecommerce.Api
```

```bash
dotnet test
```

## Notes

- The sensitive fields of register, login, reset-password and create-new-password arrive encrypted (headers `encryptionKeyId`, `encryptedKey`; per field AES-256-GCM, JSON path as AAD) and are decrypted in `EncryptedFieldsFilter` before validation. Any failure is `400 E10001 Encrypted fields could not be decrypted.` See api-conventions "Encrypted sensitive fields".

- `authen.m_user.username`, `email_address`, `first_name`, `last_name` are `bytea`. They are encrypted deterministically (AES-256-CBC with a synthetic IV) so `WHERE username = @x` still works.
- Passwords: PBKDF2-HMAC-SHA256, 600k iterations, 16-byte salt. Access, refresh and reset tokens are stored as SHA-256 hashes.
- Protected APIs check the stored access token on every call, so logout and a password reset end a session at once.
- `IEmailSender` is a logging stub.
