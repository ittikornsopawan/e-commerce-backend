---
name: E-commerce Backend Code Structure Guide
description: คู่มืออธิบายการทำงานของ C# backend จากโค้ดจริง ตั้งแต่ HTTP request จนถึงฐานข้อมูล การจัดการ transaction, error, ความปลอดภัย และการทดสอบ ใช้อ่านก่อนพัฒนา use case ใหม่หรือ review โค้ดของ e-commerce
status: draft
approved_by: null
approved_at: null
observed_on: 2026-11-20
source_showcase_revision: 65be22dc2aef786c04ba5ac001ba30b06d551a75
---

# คู่มือโครงสร้างและการเขียนโค้ด E-commerce Backend

## 1. ขอบเขตและวิธีอ่านเอกสาร

เอกสารนี้อธิบายโค้ดที่มีอยู่จริงใน repository นี้ หลังจาก API authen-and-author 01–10 เสร็จ และหลังจากลบโค้ดชุดแรก (route `api/authen`, Dapper, `GetCurrentUser`, entity `User`) ออกแล้ว

- ผังไฟล์และลำดับการเพิ่ม use case อยู่ใน [project-structure.md](project-structure.md)
- รายการฟังก์ชันที่ใช้ร่วมกันอยู่ใน Function Book ของ Showcase (`e-commerce/doc/function-book.md`) อ่านก่อนเขียนฟังก์ชันใหม่ทุกครั้ง
- ความหมายของแต่ละ layer และ sub-layer ในภาพรวมของระบบอยู่ใน `e-commerce/doc/code-structure.md` ของ Showcase
- พฤติกรรมที่ถูกต้องมาจาก spec ที่ approve แล้ว (`e-commerce/doc/api/<domain>/<nn-api>/spec.md` และ `sequence.wsd`) ถ้าโค้ดกับ spec ไม่ตรงกัน ให้ถือ spec เป็นหลัก

## 2. ภาพรวม

Backend เป็น ASP.NET Core บน .NET 11 แบ่งเป็น 5 project ตาม layer และจัดโค้ดแบบ CQRS ตาม feature

| Layer | หน้าที่ | ห้ามทำ |
| --- | --- | --- |
| Api | รับ HTTP, ตรวจรูปร่าง request, อ่านผู้เรียกจาก JWT, ส่ง command/query, แปลงผลเป็น envelope | ตัดสินกฎธุรกิจ, เขียน SQL |
| Application | ลำดับขั้นของ use case, validation ก่อน SQL, ขอบเขต transaction | รู้จัก EF Core หรือ HTTP |
| Domain | กฎธุรกิจที่ใช้ซ้ำ, value object, interface ของ repository, error ของ feature | ทำ I/O |
| Infrastructure | EF Core LINQ, transaction, การเข้ารหัส, JWT, email | ตัดสินว่า use case ตอบอะไร |
| Shared | Result/Error, CQRS, ข้อความ validation, paging | อ้างอิง layer อื่น |

## 3. เส้นทางของ request หนึ่งครั้ง

ตัวอย่าง `POST v1/user/profile/upsert` (spec 07)

1. **Rate limiter** (`RateLimiting`) ทำงานก่อน ถ้าเกินโควตาตอบ 429 E90002 พร้อม `Retry-After` (เฉพาะ API สาธารณะที่ spec กำหนด)
2. **JWT middleware** ตรวจ signature, issuer, audience, อายุ และ `sub` ต้องเป็น UUID ถ้า token ใช้ไม่ได้ ผู้เรียกจะไม่ถูกยืนยันตัวตน แต่ request ยังไปต่อ เพราะ action ไม่ได้ใส่ `[Authorize]`
3. **`RequiredHeadersFilter`** ตรวจ `timestamp` (ต้องเป็น UTC) และ `content-type` เมื่อมี body ถ้าผิดตอบ 400 E10001 ทันที
4. **Model binding** อ่าน body ถ้าไม่ใช่ JSON object `InvalidBody` จะตอบ 400 E10001
5. **Controller** ตรวจรูปร่าง body ด้วย `JsonBody` (key ที่รู้จัก, ชนิดของค่า, key ไหนส่งมาบ้าง) แล้วสร้าง command โดยใส่ `VerifiedCaller.Of(this)` ซึ่งเป็น `null` ถ้า JWT ไม่ผ่าน
6. **Dispatcher** เรียก validator ก่อน ถ้ามี error ตอบ 400 โดยไม่เรียก handler การทำแบบนี้ทำให้ 400 มาก่อน 401 ตามที่ spec สั่ง
7. **Handler** เรียก `ITransactionRunner.RunAsync` ที่ isolation level ตาม spec ภายใน transaction ตรวจ credential ด้วย `CallerCredential` แล้วอ่าน/เขียนผ่าน repository
8. **Repository** ทำ LINQ บน `EcommerceDbContext` เข้ารหัส/ถอดรหัสฟิลด์ส่วนตัวด้วย `FieldProtector`
9. **ผลลัพธ์**: success ผ่าน `ToSuccess` (200/201 + `data`), failure ผ่าน `ToFailure` (status ตาม `ErrorType`), exception ที่ไม่คาดไว้ไปที่ `EnvelopeExceptionHandler` (500 ไม่มี bizCode)

## 4. Authentication: สองชั้น

- **ชั้นแรก (middleware)** ตรวจแค่ว่า JWT ถูกต้องตามรูปแบบและยังไม่หมดอายุ
- **ชั้นที่สอง (ใน transaction)** ตรวจว่า token ยังเป็นของ session ที่ใช้งานอยู่:
  - `CallerCredential.VerifyAsync` สำหรับ API ที่เขียนข้อมูล (07, 09): ล็อก user ที่ ACTIVE ด้วย `FOR UPDATE`, เอาเวลาใหม่หลังล็อก, ตรวจ `exp` ซ้ำ, แล้วหา access token ที่เก็บไว้ (hash ตรง, ACTIVE, ยังไม่หมดอายุ)
  - `CallerCredential.CheckAsync` สำหรับ API อ่านอย่างเดียว (08, 10): ไม่ล็อก แต่ join token กับ user ที่ ACTIVE
  - logout (06) และ create-new-password (04) ปิด token ในฐานข้อมูล ดังนั้น token เดิมจะใช้ไม่ได้ทันทีแม้ JWT ยังไม่หมดอายุ

ไม่มี endpoint ไหนเชื่อ JWT อย่างเดียวโดยไม่ตรวจ token ที่เก็บไว้

## 5. Transaction และ concurrency

ทุก use case ที่แตะฐานข้อมูลทำงานใน `ITransactionRunner.RunAsync(isolation, work, ct, readOnly)` หนึ่งครั้ง

| กลไก | ทำอะไร | ใช้ที่ไหน |
| --- | --- | --- |
| Isolation level | ตาม spec: REPEATABLE READ (01, 08, 10) หรือ READ COMMITTED พร้อม row lock (02–07, 09) | ทุก handler |
| `readOnly: true` | รัน `SET TRANSACTION READ ONLY` หลัง BEGIN ถ้ามีการเขียนจะ error 25006 | 08, 10 |
| `query.ForUpdate()` | ใส่ `FOR UPDATE` ให้ LINQ ผ่าน tag และ interceptor ใช้ใน transaction เท่านั้น | ล็อก user ก่อนเปลี่ยนสถานะ |
| `AcquireTransactionLockAsync(key)` | PostgreSQL advisory lock ระดับ transaction เมื่อไม่มีแถวให้ล็อก | login: ล็อกตาม identifier ที่ส่งมา (hash) เพื่อให้การนับครั้งที่ล้มเหลวของ identifier เดียวกันไม่ชนกัน |
| Guarded update | `ExecuteUpdateAsync` ที่มีเงื่อนไขสถานะใน `WHERE` แล้วตรวจจำนวนแถว | consume reset token, refresh token |
| Retry 40001 | รันงานใหม่ทั้งชุดสูงสุด 3 ครั้ง ถ้ายังชนตอบ 408 | `EfTransactionRunner` |

`EfTransactionRunner` commit เมื่อ `Result` สำเร็จ และ rollback เมื่อ `Result` ล้มเหลวหรือมี exception จากนั้นแปลง error ของ PostgreSQL ดังนี้:
- 23505 → `DuplicateKeyException` (ชื่อ constraint และตาราง)
- 23503 → `MissingReferenceException`
- timeout → `TimeoutException`

Handler จับ exception เหล่านี้แล้วตอบตาม spec เช่น 409 หรือ 408 (`CommonErrors.Timeout`)

ทุก connection ตั้ง `Timezone=UTC` (`EcommerceDbContext.UtcSession`) เพราะคอลัมน์เวลาเป็น `TIMESTAMP WITHOUT TIME ZONE` และเก็บเป็น UTC

## 6. Error และ response

| `ErrorType` | HTTP | bizCode เริ่มต้น |
| --- | --- | --- |
| Validation | 400 | E10001 |
| Unauthorized | 401 | E10002 |
| Forbidden | 403 | E10009 |
| NotFound | 404 | E10004 |
| Conflict | 409 | E10003 |
| Timeout | 408 | E90001 |
| TooManyRequests | 429 | E90002 |
| Failure (อื่น ๆ) | 500 | ไม่มี และข้อความเป็น "An unexpected error occurred." เสมอ |

- error ที่ spec กำหนด bizCode เฉพาะใช้ `Error.Of(...)` และประกาศครั้งเดียวใน `AuthenErrors` ซึ่ง `Error.Of` จะตรวจว่า bizCode นั้นอนุญาตสำหรับ type นั้น
- ข้อความ 400 สร้างจาก `ValidationMessages` ตาม api-conventions ห้ามพิมพ์ข้อความเองในโค้ด
- envelope ไม่ส่ง `bizCode` และ `data` เมื่อเป็น null ส่วน field อื่นใน `data` ส่งค่า null ตามที่ spec กำหนด

## 7. ข้อมูลที่ต้องปกป้อง

| ข้อมูล | วิธีเก็บ |
| --- | --- |
| username, email, ชื่อ, เบอร์โทร, ที่อยู่ | `FieldProtector`: AES-256 แบบ deterministic ลงคอลัมน์ `bytea` ค่าเดิมเข้ารหัสได้ผลเดิม จึงค้นด้วย `=` ได้ |
| password | PBKDF2-HMAC-SHA256 600,000 รอบ, salt 16 bytes (`Pbkdf2PasswordHasher`) |
| access, refresh, reset token | เก็บเฉพาะ SHA-256 hash ไม่เก็บหรือ log ค่าจริง |
| ลิงก์ reset | ส่งผ่าน `IEmailSender` หลัง commit เท่านั้น ตอนนี้เป็น stub ที่ไม่ log ตัว token |

กุญแจทั้งหมด (`Jwt:SigningKey`, `Security:FieldEncryptionKey`) มาจาก environment variable และห้าม commit

## 8. กฎการเขียนโค้ด

ใช้มาตรฐานของ `.agents/skills/dev-like-principal/references/code-quality-standards.md`:

- เปิด Function Book และค้นในโค้ดก่อนเขียนฟังก์ชันใหม่ ฟังก์ชันที่ใช้ร่วมกันวางตามขอบเขตการใช้งาน (ทุก layer ใช้ → Shared, layer เดียวใช้ → layer นั้น)
- เมธอดไม่เกิน 30 บรรทัด (นับจริง) ซ้อนกันไม่เกิน 2 ชั้น ส่วนของ transaction ใน handler แยกเป็นเมธอดที่มีชื่อ
- comment อธิบายเหตุผลและกฎจาก spec ไม่อธิบายสิ่งที่โค้ดบอกอยู่แล้ว
- LINQ เท่านั้น ห้ามเขียน SQL ใน handler หรือ repository ยกเว้นคำสั่งคงที่ที่ไม่มี input และได้รับอนุมัติ (`SET TRANSACTION READ ONLY`, advisory lock)
- `dotnet format --verify-no-changes` ต้องผ่าน และ build ต้องไม่มี warning (`TreatWarningsAsErrors`)

## 9. การทดสอบ

| Project | ทดสอบอะไร | หมายเหตุ |
| --- | --- | --- |
| `Ecommerce.Domain.Tests` | กฎธุรกิจ (policy, throttle, claims) และ value object | ไม่ใช้ฐานข้อมูล |
| `Ecommerce.Application.Tests` | handler กับ fake: ลำดับขั้น, isolation, read-only, การแปลง exception | ไม่ใช้ฐานข้อมูล |
| `Ecommerce.Infrastructure.Tests` | repository และ transaction runner บน PostgreSQL 17 ที่สร้างใหม่ทุกครั้ง | ต้องมี `initdb`/`pg_ctl` |
| `Ecommerce.Api.Tests` | test case ของแต่ละ spec ผ่าน HTTP pipeline จริงและฐานข้อมูลจริง | ชื่อ test ขึ้นต้นด้วย case ID เช่น `TC_ADDRESSQ_011_...` |

ทุก API มี `test-cases.md` ใน Showcase (สร้างด้วย api-test-case-creator) ทุก case ต้องถูก automate และผ่าน การแก้ที่สำคัญต้องตรวจด้วย mutation check คือลบการแก้ออกแล้วดูว่า test ล้มเหลวจริง

## 10. ข้อจำกัดที่รู้อยู่

- ยังไม่มี CI ใน repository นี้ ผลการทดสอบมาจากการรันบนเครื่อง
- `IEmailSender` เป็น stub ที่เขียน log ยังไม่ได้ส่ง email จริง
- `CallerClaims.IsPlatformAdmin` ยังไม่มีผู้เรียก เตรียมไว้สำหรับ API ฝั่ง admin ตาม spec ที่จะมา
- API ของ domain อื่น (product, order, payment, shipping) ยังไม่มีในโค้ด มีแค่ spec ใน Showcase
