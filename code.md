---
name: E-commerce Backend Code Structure Guide
description: คู่มืออธิบายโครงสร้าง C# backend จากโค้ดจริง ใช้อ่านก่อนพัฒนา feature แบ่งเจ้าของ business rules หรือ review architecture และ sequence diagram ของ e-commerce
status: draft
approved_by: null
approved_at: null
observed_on: 2026-11-07
source_revision: bd2fa1cade2a449074198cc53586ff7bb531ebf5
---

# คู่มือโครงสร้างและการเขียนโค้ด E-commerce Backend

## 1. ขอบเขตและวิธีอ่านเอกสาร

เอกสารนี้อธิบาย backend ภายใต้ `e-commerce/repository/` จาก source code ที่ตรวจเมื่อวันที่ 7 พฤศจิกายน 2026 พร้อมหลักแบ่งหน้าที่ของ Domain, Application และ Infrastructure เพื่อใช้คุยเรื่อง architecture และเป็นข้อมูลประกอบการ review diagram ในรอบถัดไป

ใช้สถานะต่อไปนี้แยกข้อเท็จจริงกับข้อเสนอ:

| สถานะ | ความหมาย |
| --- | --- |
| มีในโค้ด | พบ implementation ใน source ที่อ้างอิง สามารถไล่ call flow ได้ |
| ช่องว่างที่พบ | โค้ดยังรับผิดชอบไม่ครบ หรือแตกต่างจากเอกสารที่อ้างอิง ต้องระบุสิ่งที่ยังไม่รับประกัน |
| ข้อเสนอเพื่อ review | แนวทางที่นำมาคุยได้ แต่ยังไม่ใช่ architecture หรือ behavior ที่อนุมัติให้ implement |

เอกสารมีสถานะ draft เพราะมีข้อเสนอด้านความรับผิดชอบและขอบเขตโมเดลรวมอยู่ด้วย การขอให้เขียนคู่มือนี้ไม่ได้เป็นการอนุมัติเปลี่ยน backend, API, schema หรือ architecture ข้อเสนอที่กระทบพฤติกรรมต้องผ่าน governing spec และ approval ตาม [SDD workflow](../../.agents/workflows/spec-driven-development.md) ก่อน implementation

`source_revision` ระบุ revision ที่ใช้ตรวจในรอบเขียนเอกสารนี้ ข้อความเกี่ยวกับ implementation อ้างอิง source code ส่วนข้อเปรียบเทียบกับ API specs ต้องอ่านสถานะอนุมัติของแต่ละไฟล์ประกอบ โดย login และ refresh spec ณ วันที่ตรวจมีสถานะ `in-review` และไม่มีผู้อนุมัติใน frontmatter

เอกสารที่เกี่ยวข้อง:

- [project-structure.md](project-structure.md): ภาพรวมโครงสร้างและ conventions ที่มีอยู่เดิม
- [README.md](README.md): วิธีรันและ configuration ของ backend
- [API specifications](../doc/api/): พฤติกรรม API ที่ออกแบบไว้ ต้องตรวจ approval ของแต่ละ spec
- [API contract](../api/api-contract.json): contract ที่ต้องเทียบกับ approved spec ก่อนทำ API implementation
- [Schema specifications](../doc/schema/) และ [migrations](../db/migrations/): โครงสร้างและข้อบังคับข้อมูล

## 2. Architecture ที่มีอยู่จริง

Backend ใช้การแยก layer เป็นห้า C# projects และจัด use case ตาม feature ภายใน Application ส่วนที่ implement อยู่คือ Authen ได้แก่ register, login, ขอ reset password, ตั้ง password ใหม่ และอ่านผู้ใช้ปัจจุบัน

คำหลักสามคำอธิบายคนละมิติของโครงสร้างนี้:

| มิติ | คำถามที่ตอบ | สิ่งที่เห็นใน backend |
| --- | --- | --- |
| Layering / dependency direction | project ใดอ้างอิง project ใดได้ | API, Application, Domain, Infrastructure, Shared แยก `.csproj` |
| CQRS | operation ใดเปลี่ยน state และ operation ใดอ่านข้อมูล | แยก command/query และ handler ของแต่ละ use case |
| Domain modeling | ใครเป็นเจ้าของ state, identity และ business rules | `User`, `PasswordResetToken` และ value objects ของ Authen |

การมี command/query แยกกันไม่ได้บอกว่า aggregate boundary สมบูรณ์แล้ว และการมี Domain project ก็ยังต้องตรวจว่า handler เรียก behavior ของโมเดลจริงหรือเพียงใช้เป็นที่เก็บข้อมูล

ในโค้ดนี้ handler เรียก `User.Register()`, `User.EnsureCanSignIn()` และ `PasswordResetToken.Consume()` จริง จึงมี domain behavior ที่ใช้งานอยู่ ส่วน Product, Order, Stock และ Campaign ยังไม่พบ implementation ใน `src/` แม้มีเอกสารออกแบบในโครงการแล้ว

## 3. โครงสร้างไฟล์

```text
repository/
├── Ecommerce.sln
├── Directory.Build.props
├── README.md
├── project-structure.md
├── code.md
├── src/
│   ├── Ecommerce.Api/
│   │   ├── Program.cs
│   │   ├── Controllers/AuthenController.cs
│   │   ├── Contracts/Authen/AuthenRequests.cs
│   │   └── Extensions/ResultExtensions.cs
│   ├── Ecommerce.Application/
│   │   ├── AssemblyReference.cs
│   │   ├── Abstractions/
│   │   ├── Common/ValidationErrors.cs
│   │   └── Features/Authen/
│   │       ├── Register/
│   │       ├── Login/
│   │       ├── ResetPassword/
│   │       ├── GetCurrentUser/
│   │       └── Mapping/AuthenMappings.cs
│   ├── Ecommerce.Domain/
│   │   ├── Common/
│   │   ├── DuplicateKeyException.cs
│   │   └── Authen/
│   │       ├── Entities/
│   │       ├── ValueObjects/
│   │       ├── Repositories/IAuthenRepositories.cs
│   │       ├── AuthenErrors.cs
│   │       └── UserStatus.cs
│   ├── Ecommerce.Infrastructure/
│   │   ├── DependencyInjection.cs
│   │   ├── Options/InfrastructureOptions.cs
│   │   ├── Persistence/
│   │   │   ├── DbSession.cs
│   │   │   ├── UnitOfWork.cs
│   │   │   └── Authen/
│   │   ├── Security/
│   │   └── Email/LoggingEmailSender.cs
│   └── Ecommerce.Shared/
│       ├── Cqrs/
│       └── Results/
└── tests/
    ├── Ecommerce.Domain.Tests/Authen/
    └── Ecommerce.Application.Tests/Authen/
```

ชื่อ `Authen` ใช้ตาม source ปัจจุบัน `ResetPassword/` รวมสอง use cases คือขอลิงก์และยืนยัน password ใหม่ ไม่ได้มี handler เดียวรับผิดชอบทั้งสองขั้นตอน

## 4. Framework และ dependency direction

### 4.1 Build configuration

[Directory.Build.props](Directory.Build.props) กำหนดค่าร่วม:

| Configuration | ค่าปัจจุบัน |
| --- | --- |
| Target framework | `net10.0` |
| Nullable reference types | enabled |
| Implicit usings | enabled |
| Treat warnings as errors | enabled |

โค้ดใช้ file-scoped namespaces, records, primary constructors ใน handler/service, `async`/`await`, `CancellationToken` และ `TimeProvider` ตามที่พบใน source การเพิ่ม feature ต้องรักษา framework และ dependency ของโครงการจนกว่าจะมีขอบเขตอนุมัติให้เปลี่ยน

### 4.2 Project references ที่ประกาศจริง

ลูกศร `A -> B` ในตารางนี้หมายถึง A มี compile-time project reference ไป B ไม่ใช่ลำดับการทำงานของ request

| Project | อ้างอิง project | บทบาท |
| --- | --- | --- |
| [Ecommerce.Api](src/Ecommerce.Api/Ecommerce.Api.csproj) | Application, Infrastructure, Shared | HTTP และ composition root |
| [Ecommerce.Application](src/Ecommerce.Application/Ecommerce.Application.csproj) | Domain, Shared | ประสานงาน use case |
| [Ecommerce.Domain](src/Ecommerce.Domain/Ecommerce.Domain.csproj) | Shared | โมเดลและกฎของ domain |
| [Ecommerce.Infrastructure](src/Ecommerce.Infrastructure/Ecommerce.Infrastructure.csproj) | Application, Domain, Shared | implement ports ด้วย database และ technology |
| [Ecommerce.Shared](src/Ecommerce.Shared/Ecommerce.Shared.csproj) | ไม่มี project reference | Result/Error และ CQRS building blocks |

Domain และ Application ไม่มี project reference ไป Infrastructure การเรียก repository ใน handler เกิดผ่าน interface ที่ประกาศอยู่ฝั่งด้านใน แล้ว DI จับคู่ให้ใช้ implementation จาก Infrastructure ตอน runtime

ตัวอย่าง: `LoginCommandHandler` รู้จัก `IUserRepository` จาก Domain ส่วน `UserRepository` implement interface นั้นใน Infrastructure ดังนั้น runtime เรียก implementation ได้โดยที่ Application ไม่ต้องอ้างอิง Infrastructure

Shared มี package dependency สำหรับ DI abstractions อยู่แล้ว คำว่าไม่มี project reference จึงไม่ได้หมายถึงไม่มี dependency ต่อ external package และ dependency ที่ไหลผ่าน Shared ยังต้องพิจารณาเมื่อเพิ่ม common code

## 5. API layer: HTTP boundary และ composition root

### 5.1 Controller

[AuthenController](src/Ecommerce.Api/Controllers/AuthenController.cs) ทำหน้าที่:

1. รับ HTTP request ตาม request contracts
2. สร้าง command หรือ query ของ Application
3. เรียก `IDispatcher.SendAsync()` พร้อม cancellation token
4. แปลงผลสำเร็จหรือ `Result` ที่ล้มเหลวเป็น HTTP response

Controller ปัจจุบันไม่มี SQL และไม่ได้ hash password หรือสร้าง JWT เอง สำหรับ `me` จะอ่าน `sub` จาก authenticated principal และแปลงเป็น `Guid` ก่อนส่ง `GetCurrentUserQuery`

[AuthenRequests.cs](src/Ecommerce.Api/Contracts/Authen/AuthenRequests.cs) เป็น HTTP input DTO ส่วน command เป็น input ของ use case การแยกชนิดช่วยให้ HTTP contract และ Application มีขอบเขตของตนเอง แม้ข้อมูลบางส่วนจะเหมือนกัน

### 5.2 Endpoints ที่มีใน controller

| HTTP | Route ปัจจุบัน | Application request | Success response |
| --- | --- | --- | --- |
| POST | `/api/authen/register` | `RegisterCommand` | 201 พร้อม `RegisterResponse` |
| POST | `/api/authen/login` | `LoginCommand` | 200 พร้อม `LoginResponse` |
| POST | `/api/authen/password-reset/request` | `RequestPasswordResetCommand` | 202 ไม่มี response DTO |
| POST | `/api/authen/password-reset/confirm` | `ResetPasswordCommand` | 204 ไม่มี body |
| GET | `/api/authen/me` | `GetCurrentUserQuery` | 200 พร้อม `CurrentUserResponse`; ต้องมี Bearer token |

ตารางนี้อธิบาย source code ปัจจุบัน เส้นทางและ response อาจต่างจาก API specs ที่ใช้ `v1/auth/...` การวาด diagram ของ implementation ต้องใช้ route จาก controller หรือระบุชัดว่าเป็น diagram ของ spec

### 5.3 Program.cs

[Program.cs](src/Ecommerce.Api/Program.cs) เป็น composition root:

- ลงทะเบียน controllers, OpenAPI และ ProblemDetails
- เรียก `AddCqrs(AssemblyReference.Assembly)` เพื่อ scan Application
- เรียก `AddInfrastructure(configuration)` เพื่อจับคู่ ports กับ implementation
- ตั้งค่า JWT Bearer authentication และ authorization
- ต่อ middleware แล้ว map controllers

JWT configuration ระบุ issuer, audience, signing key และ clock skew ปัจจุบันไม่มี callback ที่อ่าน ACTIVE access-token row จากฐานข้อมูลระหว่างตรวจ Bearer token การมี token rows ใน database จึงยังไม่ได้ทำให้การ revoke มีผลกับ JWT authentication โดยอัตโนมัติ

## 6. Shared layer: CQRS dispatch และ Result

### 6.1 Request และ handler contracts

[Messaging.cs](src/Ecommerce.Shared/Cqrs/Messaging.cs) ประกาศชนิดหลัก:

| Contract | การใช้งาน |
| --- | --- |
| `ICommand` | operation ที่เปลี่ยน state และคืน `Result` |
| `ICommand<TValue>` | operation ที่เปลี่ยน state และคืน `Result<TValue>` |
| `IQuery<TValue>` | operation อ่านข้อมูลและคืน `Result<TValue>` |
| `ICommandHandler<...>` | handler ของ command |
| `IQueryHandler<...>` | handler ของ query |
| `IValidator<TRequest>` | validation ของ request ก่อนเข้า handler |
| `IDispatcher` | entry point ที่ controller ใช้ส่ง request |

Command สามารถคืนข้อมูล เช่น user id หรือ tokens ได้ การแยก command/query พิจารณาจากผลต่อ state ไม่ได้พิจารณาเพียงว่ามี return value หรือไม่ Login จัดเป็น command เพราะเขียน token rows

### 6.2 Dispatcher ทำงานอย่างไร

[Dispatcher.cs](src/Ecommerce.Shared/Cqrs/Dispatcher.cs) ทำงานตามลำดับ:

1. ตรวจว่า request ไม่เป็น null
2. หา executor ตาม runtime request type และ response type โดยมี cache ของ executors
3. resolve validators ทั้งหมดของ request จาก DI
4. เรียก synchronous `Validate()` และรวม errors
5. หากมี errors คืน failure ตาม response type โดยไม่เรียก handler
6. resolve `IRequestHandler<TRequest, TResponse>` แล้วเรียก `HandleAsync()`

[CqrsServiceCollectionExtensions.cs](src/Ecommerce.Shared/Cqrs/CqrsServiceCollectionExtensions.cs) scan concrete types ใน assembly ที่ส่งมา และลงทะเบียน handler/validator เป็น scoped services ผ่าน inherited request-handler interface

Dispatcher ปัจจุบันไม่มี transaction, authorization, retry หรือ logging pipeline เพิ่มให้อัตโนมัติ และ marker `IQuery<T>` ไม่ได้บังคับระดับ type system ว่า handler เขียนข้อมูลไม่ได้ ต้องตรวจ behavior และ dependency ของ query handler ด้วย

Validators ไม่มี async API ใน contract นี้ การค้น database จึงอยู่ใน async handler ผ่าน repository ปัจจุบัน ไม่ควรใส่ database I/O ที่ซ่อนเป็น synchronous call เพื่อให้พอดีกับ validator contract

### 6.3 Result และ Error

[Result.cs](src/Ecommerce.Shared/Results/Result.cs) ใช้สื่อ expected outcomes เช่นข้อมูลไม่ถูกต้อง ชื่อซ้ำ หรือ credentials ไม่ผ่าน โดยมี `IsSuccess`, `IsFailure`, `Errors`, `FirstError` และ `Result<T>.Value`

อ่าน `.Value` ได้เมื่อ result สำเร็จ หากอ่านจาก failure จะเกิด exception ส่วน `IFailureFactory<T>` ทำให้ dispatcher สร้าง failure ของ response type ที่ถูกต้องได้

[Error.cs](src/Ecommerce.Shared/Results/Error.cs) เก็บ `Code`, `Description` และ `ErrorType` การใช้ Result ไม่ได้จัดการ unexpected exceptions ทุกชนิดให้เอง และไม่ได้ทำให้ database transaction rollback อัตโนมัติ

## 7. Application layer: โครงสร้าง use case และการประสานงาน

### 7.1 หนึ่ง use case มีอะไรบ้าง

ใช้ [Register](src/Ecommerce.Application/Features/Authen/Register/) เป็นตัวอย่าง:

| ไฟล์ | หน้าที่ |
| --- | --- |
| [RegisterCommand.cs](src/Ecommerce.Application/Features/Authen/Register/RegisterCommand.cs) | input record ของ use case |
| [RegisterCommandValidator.cs](src/Ecommerce.Application/Features/Authen/Register/RegisterCommandValidator.cs) | เรียก value-object factories เพื่อตรวจข้อมูลก่อนเข้า handler |
| [RegisterCommandHandler.cs](src/Ecommerce.Application/Features/Authen/Register/RegisterCommandHandler.cs) | โหลดข้อเท็จจริง เรียก Domain และ ports จัด transaction แล้วคืนผล |
| [RegisterResponse.cs](src/Ecommerce.Application/Features/Authen/Register/RegisterResponse.cs) | output record ของ use case |

Mapping อยู่ใน [AuthenMappings.cs](src/Ecommerce.Application/Features/Authen/Mapping/AuthenMappings.cs) ซึ่งแปลง `User` เป็น response records ด้วยมือ Domain entity ไม่ถูกส่งออกเป็น API response โดยตรง

ตัวอย่าง declaration จาก source ปัจจุบัน แสดงเฉพาะ request types โดยละ namespace/usings:

```csharp
public sealed record RegisterCommand(
    string Username,
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? DisplayName) : ICommand<RegisterResponse>;
```

ส่วน [GetCurrentUserQuery](src/Ecommerce.Application/Features/Authen/GetCurrentUser/GetCurrentUserQuery.cs) ประกาศ operation อ่านข้อมูล:

```csharp
public sealed record GetCurrentUserQuery(Guid UserId) : IQuery<CurrentUserResponse>;
```

ทั้งสองเป็น input records โดยไม่มี SQL หรือขั้นตอนการทำงานอยู่ในตัว request; behavior ของ use case อยู่ใน handler และ behavior ของโมเดลอยู่ใน Domain

### 7.2 Handler ควรเป็นเจ้าของอะไร

Handler เป็นเจ้าของลำดับขั้นตอนของ use case เช่น โหลด user -> ตรวจสิทธิ์ตามกฎ -> เรียก password verifier -> สร้าง tokens -> บันทึกใน transaction -> map response

การประสานงานที่เหมาะกับ Application ได้แก่:

- เรียก repository เพื่อโหลดข้อมูลที่จำเป็น
- ส่งข้อมูลหรือเวลาให้ Domain ตัดสินกฎ
- เรียก hasher, token issuer และ email sender ผ่าน ports
- เลือก transaction boundary ให้ตรง use case ที่อนุมัติ
- ตัดสินว่าจะเปิดเผยหรือซ่อนเหตุผลของ failure ตามนโยบายของ operation
- map entity เป็น response DTO

Business formula หรือ state transition ที่เป็นของโมเดลควรมีเจ้าของชัดเจนใน Domain แทนการคัดลอกเงื่อนไขซ้ำในหลาย handlers แต่เงื่อนไขทุกตัวใน handler ไม่ได้แปลว่าเป็น domain leak เช่นการคืนคำตอบทั่วไปเพื่อป้องกัน account enumeration เป็น use-case response policy ได้

### 7.3 Ports

| Interface | ประกาศที่ | Implementation ปัจจุบัน |
| --- | --- | --- |
| `IUserRepository` | [Domain repository contracts](src/Ecommerce.Domain/Authen/Repositories/IAuthenRepositories.cs) | `UserRepository` |
| `IPasswordRepository` | Domain repository contracts | `PasswordRepository` |
| `ITokenRepository` | Domain repository contracts | `TokenRepository` |
| `IUnitOfWork` | [Application abstractions](src/Ecommerce.Application/Abstractions/IUnitOfWork.cs) | `UnitOfWork` |
| `IPasswordHasher` | [Application abstractions](src/Ecommerce.Application/Abstractions/IPasswordHasher.cs) | `Pbkdf2PasswordHasher` |
| `ITokenService` | [Application abstractions](src/Ecommerce.Application/Abstractions/ITokenService.cs) | `TokenService` |
| `IEmailSender` | [Application abstractions](src/Ecommerce.Application/Abstractions/IEmailSender.cs) | `LoggingEmailSender` |

Repository interfaces รวมอยู่ไฟล์เดียวใน Authen ไม่ได้แปลว่าเป็น repository เดียวกัน ส่วน `ITokenService` ปัจจุบันทำหน้าที่ออกและ hash tokens ไม่ใช่โมเดล session lifecycle ของ Domain

### 7.4 Validation และการสร้าง value object

Register validator เรียก `Username.Create`, `EmailAddress.Create`, `PlainPassword.Create`, `PersonName.Create` และ `DisplayName.Create` ผ่าน [ValidationErrors.From](src/Ecommerce.Application/Common/ValidationErrors.cs) เพื่อรวม errors หลาย field

จากนั้น handler เรียก factories อีกครั้งเพื่อรับ typed values ก่อนทำงาน การประเมินซ้ำนี้ยังใช้ implementation ของกฎชุดเดิม ไม่ใช่กฎคนละชุด อย่างไรก็ตาม handler ที่อ่าน `.Value` ทันทีอาศัยเงื่อนไขว่าเรียกผ่าน dispatcher ซึ่งรัน validators แล้ว การเรียก handler โดยตรงต้องคำนึงถึง precondition นี้

Login validator ตรวจ presence/length ของ password โดยไม่ใช้ password-complexity rule ของการตั้ง password ใหม่ การนำกฎสองสถานการณ์มาปนกันอาจทำให้ผู้ใช้เข้าสู่ระบบด้วย password เดิมไม่ได้

## 8. Domain layer: Entity, Aggregate, Value Object และ Policy

### 8.1 Entity

[Entity<TId>](src/Ecommerce.Domain/Common/Entity.cs) ให้ identity และ equality โดยพิจารณา runtime type กับ id

Entity เหมาะกับสิ่งที่มี identity และ lifecycle กฎที่ใช้ state ของ entity ตัวเองและควบคุมการเปลี่ยน state ควรอยู่ใน behavior ของ entity นั้น

| Entity ที่มีอยู่ | State สำคัญ | Behavior ที่มี |
| --- | --- | --- |
| [User](src/Ecommerce.Domain/Authen/Entities/User.cs) | id, username, email, name, display name, status | `Register()`, `Restore()`, `IsActive`, `EnsureCanSignIn()` |
| [PasswordResetToken](src/Ecommerce.Domain/Authen/Entities/PasswordResetToken.cs) | id, user id, token hash, expiry, consumed time | `Issue()`, `Restore()`, `IsUsableAt()`, `Consume()` |

ตัวอย่าง behavior ที่มีใน source:

```csharp
public bool IsUsableAt(DateTime nowUtc) =>
    ConsumedAtUtc is null && nowUtc < ExpiresAtUtc;

public Result Consume(DateTime nowUtc)
{
    if (!IsUsableAt(nowUtc))
    {
        return Result.Failure(AuthenErrors.InvalidResetToken);
    }

    ConsumedAtUtc = nowUtc;
    return Result.Success();
}
```

Application ส่งเวลาเข้า method ทำให้กฎทดสอบได้โดยไม่ต้องอ่าน clock จริงใน entity แต่ method นี้รับประกันเฉพาะ state ของ object instance การแข่งขันระหว่าง requests ต้องมี persistence guard เพิ่มตามหัวข้อ transaction

### 8.2 Aggregate root และขอบเขตที่ยังต้องตัดสินใจ

[IAggregateRoot](src/Ecommerce.Domain/Common/IAggregateRoot.cs) เป็น marker interface ปัจจุบัน `User` implement marker นี้ ส่วน `PasswordResetToken` เป็น entity ที่ไม่มี marker และถูกโหลด/บันทึกผ่าน `ITokenRepository` โดยตรง

User ไม่มี collection ของ passwords หรือ tokens และ handler สามารถเรียก `IPasswordRepository` กับ `ITokenRepository` โดยใช้ user id ได้โดยไม่ผ่าน behavior ของ User จึงยังไม่ควรอธิบายว่า User aggregate ควบคุม lifecycle ของ credentials และ sessions ทั้งหมดแล้ว

การกำหนด aggregate ต้องตอบจาก invariant และความสอดคล้องที่ต้องรักษา เช่น การเปลี่ยน password ต้องสัมพันธ์กับ session revocation อย่างไร และ session ของแต่ละ device แยก lifecycle กันหรือไม่ ความสัมพันธ์ foreign key หรือการอยู่ใน transaction เดียวกันเพียงอย่างเดียวยังไม่กำหนดว่าเป็น aggregate เดียวกัน

ข้อเสนอเพื่อ review: พิจารณาขอบเขต User, Credential, PasswordResetToken และ Session ก่อนเพิ่ม behavior ใหม่ ยังไม่มีข้อสรุปในเอกสารนี้ว่าจะรวมทุกอย่างใต้ User หรือแยก aggregate และยังไม่มี `AuthSession` implementation

### 8.3 Value object

[ValueObject](src/Ecommerce.Domain/Common/ValueObject.cs) ให้ equality จากชนิดและชุดค่า ส่วน `UserId` ใช้ readonly record struct แยกต่างหาก

| Value object | กฎหรือความหมายปัจจุบัน |
| --- | --- |
| [UserId](src/Ecommerce.Domain/Authen/ValueObjects/UserId.cs) | typed user identity; `New()` ใช้ UUID version 7 |
| [Username](src/Ecommerce.Domain/Authen/ValueObjects/Username.cs) | trim, lower-case และ regex ตัวอักษร `a-z`, ตัวเลข, `.`, `_`, `-`; ยาว 3–32 |
| [EmailAddress](src/Ecommerce.Domain/Authen/ValueObjects/EmailAddress.cs) | trim, lower-case, ตรวจด้วย `MailAddress.TryCreate`, จำกัด 254 ตัวอักษรและเงื่อนไขเพิ่มเติมใน factory |
| [PersonName](src/Ecommerce.Domain/Authen/ValueObjects/PersonName.cs) | first/last name ต้องมีค่า ยาวไม่เกินส่วนละ 64 และมี `Full` |
| [DisplayName](src/Ecommerce.Domain/Authen/ValueObjects/DisplayName.cs) | สูงสุด 128; เมื่อไม่ระบุใช้ชื่อเต็มและตัดความยาวตามขีดจำกัด |
| [PlainPassword](src/Ecommerce.Domain/Authen/ValueObjects/PlainPassword.cs) | 8–128 ตัวอักษร ต้องมี upper/lower/digit; `ToString()` ปิดบังค่า |
| [PasswordCredential](src/Ecommerce.Domain/Authen/ValueObjects/PasswordCredential.cs) | hash และ salt; equality ใช้ค่าของ bytes |
| [LoginIdentifier](src/Ecommerce.Domain/Authen/ValueObjects/LoginIdentifier.cs) | trim, lower-case, ต้องไม่ว่างและยาวไม่เกิน 254 |

`Create()` เป็นจุดตรวจ input สำหรับ factories ที่คืน `Result<T>` ส่วน `Restore()` สร้างกลับจาก storage โดยไม่ตรวจซ้ำ ตาม convention ใช้เมื่อ hydrate ใน Infrastructure แต่ methods เหล่านี้เป็น public จึงไม่ได้บังคับสิทธิ์การเรียกด้วย compiler

ช่องว่างที่พบ: `PasswordCredential` รับและเปิดเผย `byte[]` โดยไม่ copy แม้ property มี getter อย่างเดียว แต่ผู้ถือ array ยังแก้ byte ภายในได้ จึงยังไม่ควรอ้างว่า value objects ทุกตัว immutable อย่างสมบูรณ์ `PlainPassword.Value` ยังเป็น plain string ระหว่างใช้งาน การปิดบัง `ToString()` ไม่ได้ทำให้ log/serialization ของ property ปลอดภัยโดยอัตโนมัติ

### 8.4 Domain policy และ domain service

ใช้ entity method เมื่อกฎเป็นของ state/lifecycle ของ entity ใช้ value object เมื่อกฎเป็นความถูกต้องหรือความหมายของค่า และพิจารณา policy/service เมื่อการตัดสินใจต้องอาศัยหลาย object หรือเป็นนโยบายที่ไม่มี entity ตัวเดียวเป็นเจ้าของโดยธรรมชาติ

Domain policy อาจเป็น pure function หรือคลาสที่รับข้อเท็จจริงแล้วคืนคำตัดสิน Domain service เป็น operation ของ domain ที่ประสานโมเดลหลายตัวได้ แต่ไม่ควรกลายเป็นตัวรวมทุก use case การมี service แยกทุกเงื่อนไขไม่ได้เพิ่มความชัดเจนเสมอไป

ยังไม่พบ Domain policy/service แบบแยกคลาส หรือ domain-event infrastructure ใน Domain ปัจจุบัน ตัวอย่างต่อไปนี้เป็นข้อเสนอเพื่อ review:

| กฎ | เจ้าของการตัดสินใจที่เสนอ | หน้าที่ของ Application / Infrastructure |
| --- | --- | --- |
| บัญชี Active จึง sign in ได้ | `User.EnsureCanSignIn()` ที่มีอยู่ | โหลด user และนำผลไปใช้ใน use case |
| token หมดอายุหรือเคยใช้แล้ว consume ไม่ได้ | `PasswordResetToken` ที่มีอยู่ | โหลดและบันทึกโดยป้องกัน concurrent consume |
| password ใหม่ผ่าน complexity | `PlainPassword` หรือ policy ที่ factory เรียก | ส่งค่ากำหนดที่อนุมัติแล้วและ hash หลังผ่านกฎ |
| password ใหม่ต้องต่างจากเดิม | credential behavior หรือ password-change policy | hasher ตรวจความเท่ากัน Application ส่งผลให้กฎตัดสิน |
| จำกัด failed logins ในช่วงเวลา | Authen policy หากถือเป็นกฎของ domain นี้ | โหลด count/state และ serialize การนับกับการบันทึกให้ถูกต้อง |
| จำกัดคำขอ reset ต่อผู้ใช้ | reset-request policy | โหลดประวัติและบันทึกภายใต้ concurrency guard |
| refresh reuse ต้อง revoke session หรืออยู่ใน grace period | session behavior ร่วมกับ reuse policy | reread หลัง lock และ commit ผลตาม branch ของ use case |
| first/default address และการเลือก default ใหม่ | behavior ของโมเดลที่เป็นเจ้าของชุด addresses ซึ่งยังต้องกำหนด | โหลด addresses ที่เกี่ยวข้องและบันทึกอย่าง atomic |

การจำกัด traffic ระดับ transport สามารถเป็น Application/Infrastructure policy ได้ ต้องแยกจากกฎ Authen ที่มีผลต่อ state และผลลัพธ์ธุรกิจก่อนเลือก layer ตัวเลข threshold และระยะเวลาต้องมีแหล่งที่มาจาก approved spec/configuration ไม่ควรเกิดเป็นค่าคนละชุดใน handler กับ Domain

กฎ username/email ไม่ซ้ำเป็นข้อกำหนดข้ามหลาย users ปัจจุบัน handler เรียก repository เช็กก่อน insert และ database constraint ช่วยตัดสินเมื่อเกิดการแข่งขัน การเพิ่ม domain service ที่เพียงส่งต่อ repository call ยังไม่จำเป็น เว้นแต่มีนโยบายการจองชื่อหรือข้อยกเว้นธุรกิจที่ต้องรวมไว้ในเจ้าของเดียว

## 9. Infrastructure layer: persistence และ external services

### 9.1 Dapper, Npgsql และ DbSession

[DbSession](src/Ecommerce.Infrastructure/Persistence/DbSession.cs) เก็บ connection ที่เปิดเมื่อจำเป็นและ transaction ปัจจุบัน Repository หลายตัวใน DI scope เดียวกันใช้ session ร่วมกัน จึงทำงานภายใต้ transaction เดียวกันได้

`ExecuteAsync`, `QuerySingleOrDefaultAsync` และ `ExecuteScalarAsync` สร้าง Dapper `CommandDefinition` โดยส่ง transaction และ cancellation token ต่อไปด้วย Session implement `IAsyncDisposable` เพื่อ dispose transaction/connection เมื่อสิ้น scope

ค่าจาก request ส่งเป็น SQL parameters ส่วนชื่อ table/column ที่ interpolate ใน `TokenRepository` มาจาก constants ภายใน source ขอบเขตนี้ต้องคงไว้เมื่อเพิ่ม query

### 9.2 Repository implementations

| Implementation | พฤติกรรมสำคัญ |
| --- | --- |
| [UserRepository](src/Ecommerce.Infrastructure/Persistence/Authen/UserRepository.cs) | ตรวจ username/email, ค้น user, encrypt/decrypt fields, insert user และแปลง PostgreSQL unique violation เป็น `DuplicateKeyException` |
| [PasswordRepository](src/Ecommerce.Infrastructure/Persistence/Authen/PasswordRepository.cs) | อ่าน ACTIVE credential ล่าสุด; ทำ password เดิม INACTIVE แล้ว insert PASSWORD row ใหม่ |
| [TokenRepository](src/Ecommerce.Infrastructure/Persistence/Authen/TokenRepository.cs) | บันทึก access/refresh hashes; แทน reset token เดิม; ค้น/consume reset token; revoke token rows ของ user |

Persistence row types เช่น `UserRow`, `CredentialRow`, `ResetRow` เป็นรายละเอียดการ map SQL ภายใน Infrastructure ไม่ใช่ domain entities หลังอ่าน user จะ decrypt แล้วสร้าง `User` กลับด้วย `Restore()`

การมี repository ต่อชุดตารางเป็นข้อเท็จจริงของ implementation นี้ ยังไม่ใช่หลักฐานว่าหนึ่ง repository ตรงกับหนึ่ง aggregate root ตาม DDD ทุกตัว ต้องตรวจขอบเขตที่ระบุในหัวข้อ 8.2 ก่อนอ้างเช่นนั้น

### 9.3 Security และ email implementations

| Implementation | สิ่งที่ทำใน source ปัจจุบัน |
| --- | --- |
| [Pbkdf2PasswordHasher](src/Ecommerce.Infrastructure/Security/Pbkdf2PasswordHasher.cs) | PBKDF2-HMAC-SHA256, salt 16 bytes, hash 32 bytes, 600,000 iterations และ fixed-time comparison |
| [TokenService](src/Ecommerce.Infrastructure/Security/TokenService.cs) | สร้าง HS256 JWT, opaque refresh/reset token จาก random 32 bytes และ SHA-256 token hash |
| [FieldProtector](src/Ecommerce.Infrastructure/Security/FieldProtector.cs) | encrypt/decrypt personal fields ด้วย implementation deterministic encryption ที่มีอยู่ |
| [LoggingEmailSender](src/Ecommerce.Infrastructure/Email/LoggingEmailSender.cs) | development stub; เขียน log แทนการส่ง email |

Domain ไม่เรียก cryptographic API หรือ SMTP เอง Application รู้จัก ports แล้ว Infrastructure ทำงานทางเทคนิคให้ การอธิบาย algorithm ในตารางนี้เป็นการอ่าน source ไม่ใช่ผล security audit หรือการอนุมัติรูปแบบ cryptography ใหม่

`LoggingEmailSender` มี debug log ของ raw reset token จึงไม่ควรวาดว่าเป็น production email integration หรืออ้างว่าไม่มี credential logging ใน backend ปัจจุบัน

### 9.4 Configuration และ DI lifetimes

[DependencyInjection.cs](src/Ecommerce.Infrastructure/DependencyInjection.cs) อ่าน [options](src/Ecommerce.Infrastructure/Options/InfrastructureOptions.cs) และลงทะเบียน services:

| Lifetime | Services สำคัญ |
| --- | --- |
| Singleton | options, `NpgsqlDataSource`, `FieldProtector`, `IPasswordHasher`, `ITokenService`, `IEmailSender`, `TimeProvider` |
| Scoped | `DbSession`, `IUnitOfWork`, repository implementations; dispatcher/handlers/validators จาก `AddCqrs` |

Singleton service ต้องไม่ถือ scoped session/repository โดยตรง ปัจจุบัน security/email services ในตาราง singleton ไม่ได้ inject scoped `DbSession`

ค่า configuration ที่เกี่ยวข้อง ได้แก่ `Database:ConnectionString`, `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey`, `Jwt:AccessTokenMinutes`, `Jwt:RefreshTokenDays`, `Security:FieldEncryptionKey` และ `Security:PasswordResetMinutes`

Default lifetime ที่ options กำหนดคือ access 15 นาที, refresh 14 วัน และ reset 30 นาที ขณะนี้มี startup validation สำหรับ connection string, JWT key/lifetimes และการ decode field key ส่วน validation ของ reset lifetime ยังไม่ได้ทำแบบเดียวกัน ห้ามนำ secrets จริงมาใส่ในเอกสารหรือ source control

## 10. Call flow ของ Authen จากโค้ดจริง

Flow ด้านล่างแสดงพฤติกรรมปัจจุบัน ไม่ได้เติมขั้นตอนตาม spec ที่ยังไม่มีใน implementation โดยทุก public operation เริ่มจาก controller -> dispatcher -> validators -> handler

### 10.1 Register

Source หลัก: [RegisterCommandHandler](src/Ecommerce.Application/Features/Authen/Register/RegisterCommandHandler.cs)

1. Validator เรียก value-object factories และหยุดก่อน handler เมื่อ input ไม่ผ่าน
2. Handler สร้าง typed values จาก request ที่ผ่าน validation
3. เรียก `UsernameExistsAsync()` และ `EmailExistsAsync()` ถ้าซ้ำคืน conflict
4. เรียก `User.Register(UserId.New(), ...)` ให้ user ใหม่เป็น Active
5. เรียก `IPasswordHasher.Hash()` เพื่อได้ credential
6. เปิด transaction แล้วเรียก `users.AddAsync()` กับ `passwords.SetAsync()`
7. หาก unique violation หลุดจาก pre-check repository แปลงเป็น `DuplicateKeyException`; handler จับและคืน `UsernameTaken`
8. Map เป็น `RegisterResponse`; controller คืน 201

ข้อสังเกต: duplicate catch ปัจจุบันแปลงทุก `DuplicateKeyException` ใน block นี้เป็น username conflict แม้ต้นทางอาจเป็น email ส่วน request DTO ของ backend ยังไม่มี phone/address ตาม register spec และ flow นี้ไม่ได้สร้าง default address หรือ login ให้อัตโนมัติ

### 10.2 Login

Source หลัก: [LoginCommandHandler](src/Ecommerce.Application/Features/Authen/Login/LoginCommandHandler.cs)

1. Validator ตรวจ login identifier และ password presence/length
2. Handler สร้าง `LoginIdentifier` ซึ่งปัจจุบัน trim และ lower-case
3. โหลด `User` ตาม username/email และโหลด ACTIVE credential เมื่อพบ user
4. Verify password ผ่าน hasher; ใช้ decoy credential เมื่อไม่พบ user/credential
5. หาก user/credential ไม่มีหรือ verify ไม่ผ่าน คืน `InvalidCredentials`
6. เรียก `user.EnsureCanSignIn()`; หากบัญชีไม่ Active คืน error จาก Domain
7. ออก access JWT และ refresh token
8. เปิด transaction เพื่อบันทึก token hashes สองรายการ
9. Map เป็น `LoginResponse`; controller คืน 200

โค้ดทำ dummy verification เพื่อให้กรณีไม่พบ user ยังมี password-hash work แต่ยังไม่ได้พิสูจน์ว่าเวลารวมของทุก branch เท่ากัน

ยังไม่มีกระบวนการ attempt logging/throttle, user-row/advisory locking, effective role/permission/scope resolution หรือ session id ร่วมกันใน handler นี้ `TokenService` ออก `sub`, `unique_name`, `jti` พร้อม standard JWT metadata แต่ยังไม่ออก authorization claim payload ตาม login spec ล่าสุด

### 10.3 ขอ reset password

Source หลัก: [RequestPasswordResetCommandHandler](src/Ecommerce.Application/Features/Authen/ResetPassword/RequestPasswordResetCommandHandler.cs)

1. ตรวจและสร้าง `EmailAddress`
2. โหลด user ด้วย email
3. หากไม่พบหรือไม่ Active คืน success โดยไม่สร้าง token
4. ออก opaque token แล้วเรียก `PasswordResetToken.Issue()` โดยเก็บเฉพาะ hash ใน model
5. เปิด transaction เรียก `AddPasswordResetAsync()` ซึ่งทำ reset rows เดิมเป็น INACTIVE แล้ว insert row ใหม่
6. หลัง transaction สำเร็จ เรียก email sender
7. Controller คืน 202 สำหรับผลสำเร็จ

ไม่มี request throttle หรือ user-row lock ใน implementation นี้ สำหรับ email ที่ไม่พบจะจบก่อนทำ token/transaction/email work ดังนั้นยังไม่รับประกัน response-time behavior ตาม spec ส่วน email sender เป็น logging stub และการส่งหลัง commit ไม่ได้มี durable delivery mechanism รองรับใน source ปัจจุบัน

### 10.4 ยืนยัน password ใหม่

Source หลัก: [ResetPasswordCommandHandler](src/Ecommerce.Application/Features/Authen/ResetPassword/ResetPasswordCommandHandler.cs)

1. Validator ตรวจ token input และ password policy
2. Handler อ่านเวลาจาก `TimeProvider`
3. Hash presented token แล้วโหลด ACTIVE/unexpired reset row
4. หากไม่พบ คืน `InvalidResetToken`
5. เรียก `token.Consume(now)` บน domain object
6. Hash password ใหม่
7. เปิด transaction เพื่อ set password, persist reset-token consumption และ revoke access/refresh rows ของ user
8. คืน success; controller คืน 204

ข้อจำกัดสำคัญ: lookup กับ `Consume()` เกิดก่อน transaction ปัจจุบันไม่มี user-row lock, ไม่มีการโหลด user เพื่อตรวจ Active, ไม่ตรวจ password ใหม่เทียบกับเดิม และ SQL consume ใช้เพียง `WHERE id = @id` จึงยังไม่เป็น atomic single-use guard ระหว่าง requests

การเรียก `RevokeSessionsAsync()` เขียน token rows ของผู้ใช้ทุก session แต่ authentication middleware ยังไม่ได้อ่านสถานะ token rows จึงยังไม่ทำให้ JWT เดิมหยุดใช้ทันที

### 10.5 อ่านผู้ใช้ปัจจุบัน

Source หลัก: [GetCurrentUserQueryHandler](src/Ecommerce.Application/Features/Authen/GetCurrentUser/GetCurrentUserQueryHandler.cs)

1. JWT Bearer middleware ตรวจ credential ก่อนเข้า `[Authorize]` action
2. Controller อ่าน user id จาก `sub`; หากแปลง Guid ไม่ได้คืน 401
3. Dispatcher ส่ง `GetCurrentUserQuery` ให้ handler ซึ่งไม่มี validator เฉพาะ use case นี้
4. Handler โหลด `User` ผ่าน `IUserRepository.FindByIdAsync()`
5. ถ้าไม่พบหรือไม่ Active คืน `UserNotFound`; กรณีอื่น map เป็น `CurrentUserResponse`
6. Controller คืน 200 หรือแปลง failure ตาม `ResultExtensions`

Query นี้ใช้ repository และ domain model ร่วมกับ command แต่ไม่เขียนข้อมูล จึงมีการแยก operation อ่าน/เขียนอยู่จริง ยังไม่มี read store, projection หรือ query-specific repository แยกใน source การเพิ่มสิ่งเหล่านี้เป็น architecture decision เมื่อมีเหตุผลรองรับ ไม่ใช่ข้อกำหนดจากการใช้ชื่อ CQRS เพียงอย่างเดียว

## 11. Transaction, concurrency และการรับประกัน invariant

### 11.1 IUnitOfWork ปัจจุบัน

[IUnitOfWork](src/Ecommerce.Application/Abstractions/IUnitOfWork.cs) รับ `Func<CancellationToken, Task>` ส่วน [UnitOfWork](src/Ecommerce.Infrastructure/Persistence/UnitOfWork.cs) เปิด transaction, await delegate แล้ว commit หากทำงานจบตามปกติ และ rollback เมื่อเกิด exception

หาก session มี transaction อยู่แล้ว จะใช้ transaction นั้นร่วมกันโดยไม่เปิด nested transaction หรือ savepoint แยก การรวม transaction จึงต้องพิจารณาว่าใครเป็นเจ้าของ outer boundary

`Result.Failure` เป็นค่าผลลัพธ์ ไม่ใช่ exception และ delegate ของ interface นี้ไม่ได้คืน `Result` ตัว UnitOfWork จึงไม่สามารถตีความ business failure แล้ว rollback ให้เองได้ ต้องออกแบบ failure branches และ durable writes ให้ชัดเจนตาม use case

### 11.2 ตำแหน่ง transaction ใน use case ปัจจุบัน

| Use case | งานก่อน transaction | งานใน transaction | งานหลัง transaction |
| --- | --- | --- | --- |
| Register | สร้าง values, เช็กซ้ำ, สร้าง user และ hash password | insert user และ password | map response |
| Login | โหลด user/password, verify, ตรวจ Active, ออก tokens | insert access/refresh rows | map response |
| Request reset | หา user, ออก token/model | ปิด reset rows เดิมและ insert ใหม่ | เรียก email sender |
| Confirm reset | หา token, consume ใน memory, hash password | set password, update reset row, revoke tokens | คืน success |
| Get current user | อ่าน authenticated id | ไม่มี explicit write transaction | อ่าน user และ map response |

การเขียนหลายแถวใน transaction เดียวช่วยเรื่อง atomic commit แต่ไม่ได้แก้ read-before-write race ทั้งหมด และการตรวจ invariant ใน memory ไม่ได้ serialize สอง requests ที่อ่าน snapshot คนละชุด

### 11.3 ตัวอย่าง single-use invariant

กฎ “reset token ใช้ได้ครั้งเดียว” ต้องพิจารณาสามส่วนร่วมกัน:

| ชั้น | สิ่งที่รับผิดชอบ | สถานะปัจจุบัน |
| --- | --- | --- |
| Domain | ปฏิเสธ consume เมื่อ token ใน model หมดอายุหรือถูกใช้แล้ว | มี `Consume()` |
| Application | กำหนดขั้นตอนเปลี่ยน password/consume/revoke และผลของแต่ละ failure | มี orchestration บางส่วน แต่ boundary/guards ยังไม่ครบ |
| Infrastructure | รับประกันว่ามี request เดียวที่ consume persisted row สำเร็จ | SQL ปัจจุบันยังไม่มี ACTIVE/expiry guard และไม่คืนผลว่าชนะการแข่งขันหรือไม่ |

ตัวอย่าง shape จาก [create-new-password spec](../doc/api/authen-and-author/04-create-new-password/spec.md) สำหรับคุยเรื่อง guard ไม่ใช่ SQL ที่ backend ปัจจุบันใช้:

```sql
UPDATE authen.t_user_reset_password
SET row_status = 'INACTIVE'
WHERE id = @id
  AND row_status = 'ACTIVE'
  AND expired_at > @now
RETURNING id;
```

นี่เป็น excerpt ย่อเพื่อแสดง predicate และ return semantics SQL ที่นำไป implement ต้องรวม audit fields, owner checks และ lock/transaction order ตาม approved spec ฉบับเต็มด้วย Application ต้องรู้ผล zero-row consume และหยุด/rollback งานที่ไม่ควรเกิด ไม่ใช่อ่าน row ก่อน update แล้วถือว่าชนะตลอด

แนวคิดเดียวกันใช้กับ username uniqueness, default address, จำนวน address และ refresh rotation: Domain สื่อกฎ; Application ประสานขั้นตอน; Infrastructure ใช้ constraints, locks และ conditional writes ป้องกันข้อมูลผิดเมื่อเกิด concurrency

## 12. Error handling และ response boundary

[AuthenErrors](src/Ecommerce.Domain/Authen/AuthenErrors.cs) เก็บ errors ของ feature และ [ResultExtensions](src/Ecommerce.Api/Extensions/ResultExtensions.cs) ใช้ error แรกกำหนด HTTP status:

| ErrorType | HTTP ปัจจุบัน |
| --- | --- |
| Validation | 400 |
| Unauthorized | 401 |
| Forbidden | 403 |
| NotFound | 404 |
| Conflict | 409 |
| ค่าอื่น | 500 |

Response failure ใช้ `ProblemDetails` พร้อม `errors` extension ไม่ใช่ status/data envelope ของ API specs การเปิด `UseExceptionHandler()` ไม่ได้แปลว่าทุก timeout หรือ infrastructure exception ถูก map เป็น bizCode ตาม spec แล้ว

ตัวอย่าง boundary ที่ควร review: `EnsureCanSignIn()` คืน `AccountNotActive` ซึ่งปัจจุบันมีประเภท Forbidden เมื่อ login handler ส่งต่อ error ตรง ๆ API จะตอบ 403 พร้อมเหตุผลเฉพาะบัญชี ในขณะที่ login spec ล่าสุดกำหนด generic 401 สำหรับ authentication failures

ข้อเสนอเพื่อ review: ให้ Domain สื่อเหตุผลภายใน และให้ Application ของ login รวมเหตุผลเป็น public failure ตามนโยบายป้องกัน account enumeration จากนั้น API map เป็น HTTP โดยไม่ต้องย้ายความรู้เรื่องบัญชี Active ออกจาก User

หากเปลี่ยน contract ในอนาคต ต้องตรวจเส้นทาง success, validation, authentication, business failure, rollback, timeout และ unexpected exception ให้ตรง approved spec และ published API contract ไม่ใช่ตรวจเฉพาะ happy path

## 13. Tests และหลักการตรวจสอบ

### 13.1 Tests ที่มี

| Project / ไฟล์ | สิ่งที่ตรวจใน source | สิ่งที่ยังไม่ได้พิสูจน์ด้วย tests ชุดนี้ |
| --- | --- | --- |
| [Domain EntityTests](tests/Ecommerce.Domain.Tests/Authen/EntityTests.cs) | Active user, entity identity, reset expiry และ consume ซ้ำบน object | database locking และ consume พร้อมกันสอง requests |
| [Domain ValueObjectTests](tests/Ecommerce.Domain.Tests/Authen/ValueObjectTests.cs) | normalization, invalid inputs, password policy, equality และ display-name fallback | persistence mappings และ HTTP contracts |
| [Application AuthenHandlerTests](tests/Ecommerce.Application.Tests/Authen/AuthenHandlerTests.cs) | ส่งผ่าน dispatcher, register conflicts, login, reset orchestration ด้วย fakes | SQL จริง, cryptography จริง, JWT middleware และ transaction rollback จริง |

[Fakes.cs](tests/Ecommerce.Application.Tests/Authen/Fakes.cs) ใช้ in-memory repositories, password hasher แบบ reverse bytes และ fake token/email services ส่วน `InlineUnitOfWork` เรียก delegate ตรง ๆ ไม่มี database transaction

ดังนั้น test ที่ชื่อว่า password reset revoke sessions ตรวจว่า Application เรียก fake repository แล้ว ไม่ได้พิสูจน์ว่า JWT ที่เคยออกมาถูก middleware ปฏิเสธจริง และ concurrent-duplicate register test จำลอง exception ไม่ใช่การยิงสอง database requests พร้อมกัน

### 13.2 เลือก test ตามสิ่งที่ต้องรับประกัน

ข้อเสนอสำหรับงาน implementation ถัดไป โดยยังไม่ได้เพิ่ม tests ในงานเอกสารนี้:

- Value rule หรือ entity transition: domain tests ครอบคลุม boundary และ invalid state
- Handler orchestration: application tests ตรวจ side effects และ failure branches
- Mapping/constraint/atomic writes: integration หรือ database tests ด้วย SQL จริง
- Public contract และ credential rejection: API tests ผ่าน middleware/serialization จริง
- Race condition: ใช้อย่างน้อยสอง independent connections/requests ที่ควบคุมจังหวะให้เกิดการแข่งขัน

ให้ expected behavior อ้างอิง approved spec โดยเฉพาะกฎที่เปลี่ยนจาก implementation เดิม เช่น exact login identifier, generic failure, session revocation และ reset consume

### 13.3 คำสั่งสำหรับตรวจ backend เมื่อมีการเปลี่ยน code

รันจาก `e-commerce/repository/`:

```bash
dotnet build Ecommerce.sln
dotnet test Ecommerce.sln
```

คำสั่งนี้เป็นวิธีตรวจสำหรับผู้พัฒนารอบถัดไป ไม่ใช่ผลการรันของงานเขียนคู่มือนี้ และ passing unit tests ไม่ได้แทนการเทียบ implementation กับ approved API/schema specs

## 14. แนวทางเพิ่มหรือเปลี่ยน feature

ขั้นตอนนี้อธิบายการทำงานต่อจากโครงสร้างเดิม ต้องยืนยัน governing spec ก่อนเริ่มเปลี่ยน observable behavior

1. **อ่านข้อกำหนดและสถานะอนุมัติ**: map acceptance criteria, business rules, API operations, SQL และ schema ที่เกี่ยวข้อง หาก spec เปลี่ยนต้องมี `status: approved`, `approved_by` และ `approved_at` ก่อน implementation
2. **ระบุเจ้าของ invariant**: แยก value rule, entity transition, aggregate consistency, cross-object policy และ use-case coordination ก่อนเลือกคลาส
3. **กำหนด Domain เท่าที่ต้องใช้**: entity/value objects/behavior และ repository contracts ตาม boundary ที่อนุมัติ ถ้าต้องเพิ่ม policy/service ให้ระบุการตัดสินใจที่เป็นเจ้าของอย่างชัดเจน
4. **แยก command หรือ query**: วางภายใต้ `Application/Features/<Feature>/<UseCase>/`; ระบุ input, validator เมื่อจำเป็น, handler และ response โดยไม่คืน domain entity ออกไปตรง ๆ
5. **กำหนด ports**: ใช้ interface ภายใน Domain/Application สำหรับ I/O โดย reuse contracts ที่ตรงความหมาย และหลีกเลี่ยงการอ้างอิง Infrastructure ในสองชั้นนี้
6. **Implement Infrastructure**: map rows, parameterize SQL, ส่ง cancellation ต่อ, กำหนด lock/conditional write และ transaction ให้ตรง spec แล้วลงทะเบียน implementation ใน DI
7. **เพิ่ม HTTP boundary**: request contracts และ controller ส่งผ่าน dispatcher; authentication, authorization และ response ต้องตรง published contract
8. **เพิ่ม tests ที่ trace กลับไปยังกฎ**: เลือกระดับที่พิสูจน์ invariant ได้จริง โดยเฉพาะ failure, rollback และ concurrency
9. **ตรวจเอกสารที่สัมพันธ์กัน**: API spec, published contract, schema, sequence, code และ tests ต้องบอก behavior ชุดเดียวกัน
10. **Verify และ handoff**: build/test ตามขอบเขต รายงานผลจริงและความไม่แน่นอน ก่อนพิจารณา commit/PR ตาม authorization ของงานนั้น

หาก feature ต้องเพิ่ม table/column ให้ใช้ data-modeling workflow ก่อน API/backend implementation ส่วน architecture decision, public contract หรือ dependency ใหม่ต้องกลับไป design และ approval ก่อนเปลี่ยนจริง

## 15. ช่องว่างและข้อเปรียบเทียบสำหรับ review

ตารางนี้เป็น static observations และประเด็นออกแบบ ไม่ใช่ข้อสั่งให้เปลี่ยนโค้ดทันที รายละเอียด login/refresh specs ยัง in-review และ artifact อื่นต้องตรวจ approval ของตนเองด้วย

| ประเด็น | โค้ดปัจจุบัน | สิ่งที่ต้องตกลงหรือตรวจต่อ |
| --- | --- | --- |
| Domain coverage | มี Authen | กฎ Product/Order/Stock/Campaign ใน specs ยังไม่มี backend implementation |
| Aggregate boundary | User มี marker; password/token ถูกเข้าถึงผ่าน repos แยก | ระบุเจ้าของ invariants ของ credentials, reset และ session |
| Domain policies | ยังไม่มีคลาสแยก | คัดกฎที่ต้องมี policy จริง โดยไม่สร้าง service เพื่อห่อทุกเงื่อนไข |
| Login identifier | trim/lower-case และ query หา username/email | [login spec ล่าสุด](../doc/api/authen-and-author/02-login/spec.md) กำหนด exact comparison และ ambiguous-identifier handling |
| Login failure | inactive account ออก Forbidden | spec ล่าสุดกำหนด generic 401 และบันทึก attempt |
| Login attempts | ไม่มี attempt repository/handler flow | throttle, identifier serialization, counters และ commit ของ failure attempts |
| Authorization/session | ไม่มี effective claims resolver; token insert ไม่มี session id | claim associations, current authorization snapshot และ session lifecycle ตาม [login](../doc/api/authen-and-author/02-login/spec.md)/[refresh](../doc/api/authen-and-author/05-refresh-token/spec.md) |
| Refresh และ logout | ไม่มี endpoints/handlers | rotation, replay branches และ session-scoped revocation ยังเป็น specification-only |
| Request reset | ไม่ throttle, ไม่ lock user, ตอบ 202 | เทียบ [request-reset spec](../doc/api/authen-and-author/03-reset-password/spec.md) เรื่องจำนวนคำขอ, response และเวลา |
| Confirm reset | guard เฉพาะใน memory; ไม่เช็ก user Active/รหัสใหม่ต่างเดิม | atomic consume, lock order และ failure rollback ตาม [create-new-password spec](../doc/api/authen-and-author/04-create-new-password/spec.md) |
| Revoke enforcement | update token rows แต่ middleware ไม่ lookup rows | ให้ protected calls ปฏิเสธ revoked credentials ได้จริงตาม governing behavior |
| Register data | DTO มี username/email/password/name/display name | phone/address/default address ตาม [register spec](../doc/api/authen-and-author/01-register/spec.md) ยังไม่ได้ map |
| Profile/address | มีเพียง `me` response แบบปัจจุบัน | [profile](../doc/api/authen-and-author/07-upsert-user-profile/spec.md)/[profile inquiry](../doc/api/authen-and-author/08-inquiry-user-profile/spec.md)/[address](../doc/api/authen-and-author/09-upsert-user-address/spec.md)/[address inquiry](../doc/api/authen-and-author/10-inquiry-user-address/spec.md) ยังไม่มี implementation ตรงชุดนี้ |
| Value consistency | `PersonName` จำกัด 64 ต่อส่วน, `PasswordCredential` เปิดเผย mutable arrays | เทียบ name constraints กับ governing spec และทบทวน value-object immutability |
| API contract | routes/DTOs/ProblemDetails ตาม controller ปัจจุบัน | เทียบ approved spec และ published contract โดยไม่ถือว่าเหมือนกันแล้ว |
| Database compatibility | repositories ใช้ SQL ของ implementation เดิม | ตรวจ schema ล่าสุด เช่นข้อบังคับ session id ก่อนยืนยันว่า backend รันกับ migrations ปัจจุบันได้ |

ยังไม่ได้รัน backend กับ PostgreSQL schema ล่าสุดในงานนี้ จึงไม่สรุปว่า repository SQL ใช้งานร่วมกับ migrations ปัจจุบันได้ครบ และยังไม่ได้พิสูจน์ปัญหา concurrency ด้วย runtime test

## 16. สิ่งที่จะใช้ review diagram

### 16.1 Architecture / dependency diagram

- [ ] ระบุว่าอธิบาย current implementation หรือ proposed design และใช้ source revision ใด
- [ ] แสดงทั้งห้า projects พร้อมชื่อที่ตรง code
- [ ] แยก compile-time dependency ออกจาก runtime calls และ interface implementation
- [ ] Domain/Application ไม่มี reference ไป Infrastructure
- [ ] แสดง Program/DI เป็นจุดประกอบ ports กับ implementations
- [ ] อธิบายว่า Shared มี Result/CQRS และ DI package dependency ตามจริง
- [ ] แสดง Domain behavior ที่ handler ใช้ ไม่ยุบ Domain ให้เหลือเพียง data records
- [ ] ไม่แสดง Product/Order/Campaign, policy classes, event bus หรือ read projection ว่า implement แล้วเมื่อยังไม่มี source

### 16.2 Domain / aggregate diagram

- [ ] แยก entity, value object, aggregate root, repository และ policy/service ให้ชัด
- [ ] ใส่ `User.Register`, `EnsureCanSignIn` และ reset-token behavior ที่มีอยู่จริง
- [ ] ไม่อนุมาน ownership ของ token/password เพียงเพราะมี `user_id`
- [ ] ระบุ User marker และขอบเขต aggregate ที่ยังไม่ตกลง
- [ ] หากเสนอ Session/Credential/AddressBook model ให้ระบุ proposed และอ้าง invariant ที่ใช้แบ่งขอบเขต
- [ ] ระบุว่ากฎใดตรวจจาก object state และกฎใดต้องอาศัยข้อมูลข้าม requests

### 16.3 Sequence diagram

- [ ] เริ่มที่ route/request DTO ที่ตรง source หรือระบุชัดว่าอ้าง API spec ฉบับใด
- [ ] แสดง dispatcher validation และ branch ที่ handler ไม่ถูกเรียก
- [ ] แยก controller, handler, Domain behavior, repository interface/implementation และ database ตามระดับรายละเอียดของภาพ
- [ ] มี request/result กลับจากทุก I/O และแสดง error branches ที่เปลี่ยน outcome
- [ ] แสดง begin/commit/rollback และงานก่อน/หลัง transaction ตาม flow จริง
- [ ] แสดง single-use/concurrency guard ตาม SQL จริง โดยไม่เติม guard ที่มีเพียงใน proposed spec ลง current-code diagram
- [ ] หากเป็น sequence ตาม API spec ให้ SQL ตรง spec ทุก operation และแสดง table activation -> SQL note -> return -> deactivation ตาม convention ของโครงการ
- [ ] แยกการ update token rows ออกจากการตรวจ revoked credential ที่ middleware
- [ ] แสดง email เป็น logging stub สำหรับ current implementation และแยก send หลัง commit
- [ ] การตอบ 200/201/202/204 หรือ failure ต้องตรง code/spec ที่ diagram ประกาศว่าอ้างอิง

## 17. ขอบเขต verification ของเอกสารนี้

การตรวจในงานเขียนคู่มือนี้เป็นการอ่าน source, project references, tests และ API specs เพื่อยืนยันชื่อไฟล์, dependency, call order, transaction boundaries และสถานะสิ่งที่มี/ยังไม่มีใน implementation

ผลตรวจเอกสารในรอบนี้:

| Verification | ผล |
| --- | --- |
| YAML frontmatter ของคู่มือและ task plan | parse ผ่าน; คู่มือเป็น draft และ approval fields เป็น null |
| Local Markdown references | resolve ไฟล์/โฟลเดอร์ที่อ้างอิงครบ |
| Markdown structure | ตรวจ numbered sections, table column counts และ code fences ผ่าน |
| C# excerpts | เทียบ declarations และ reset-token behavior กับ source ผ่าน |
| Project references | ตรง `.csproj` ครบทั้งห้า projects |
| Controller endpoints | routes ในคู่มือตรง controller ทั้งห้า endpoints |
| Implementation/spec distinction | ยืนยัน Authen-only feature scope และสถานะ in-review ของ login/refresh |
| Changed-file scope | เปลี่ยนเฉพาะคู่มือนี้และ task-plan section ของงาน; ไม่เปลี่ยน source/tests/configuration |
| Whitespace | `git diff --check` ผ่าน และตรวจ whitespace ของไฟล์คู่มือใหม่แยกด้วย |

ไม่ได้รัน `dotnet build`, `dotnet test`, database tests, HTTP integration tests หรือ security audit ในงานเอกสารนี้ จึงไม่มีผล runtime verification ใหม่ และเอกสารนี้ไม่ใช่หลักฐานว่าช่องว่างในหัวข้อ 15 ถูกแก้แล้ว
