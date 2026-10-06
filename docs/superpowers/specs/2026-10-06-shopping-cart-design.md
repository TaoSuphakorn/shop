# Spec: ระบบร้านค้า — สต๊อกสินค้าและตะกร้าสินค้า

|         |                                       |
| ------- | ------------------------------------- |
| วันที่  | 2026-10-06                            |
| สถานะ | อนุมัติและสร้างเสร็จแล้ว (ดูข้อ 17 สำหรับจุดที่ต่างจากแบบเดิม) |
| ผู้อ่าน | นักพัฒนาที่จะลงมือสร้างหรือรับช่วงต่อ |

## 1. เป้าหมาย

ร้านค้าออนไลน์ที่ลูกค้าเลือกสินค้า ใส่ตะกร้า และชำระเงินได้ โดย stock ถูกต้องเสมอแม้มี request พร้อมกันจำนวนมาก

**เกณฑ์สำเร็จ**

1. แสดงรายการสินค้าพร้อมจำนวนคงเหลือ
2. เพิ่มสินค้าลงตะกร้าได้ และเพิ่มไม่ได้เมื่อคงเหลือไม่พอ
3. ในตะกร้า เพิ่ม ลด ลบรายการ และล้างทั้งหมดได้ โดย stock ปรับตามทุกครั้ง
4. กด "ชำระเงิน" แล้วระบบแสดงยอดที่ต้องชำระและตัด stock
5. ยกเลิกออเดอร์แล้ว stock กลับคืน
6. ขายเกิน stock ไม่ได้ ไม่ว่า request จะชนกันอย่างไร
7. request ที่ส่งซ้ำ (retry, กดซ้ำ) ไม่สร้างผลซ้ำ
8. รันได้หลาย replica หลัง load balancer บน Kubernetes

**อยู่นอกขอบเขต:** ระบบสมาชิกและล็อกอิน, payment gateway จริง, หน้า admin, โปรแกรมนำเข้าข้อมูลสินค้า, ค่าส่งและภาษี

## 2. การตัดสินใจหลัก

| เรื่อง             | ตัดสินใจ                                            | เหตุผล                                                             |
| ------------------ | --------------------------------------------------- | ------------------------------------------------------------------ |
| สถาปัตยกรรม        | Modular monolith ที่แยกเป็น microservice ได้ภายหลัง | deploy ก้อนเดียว scale แนวนอนได้ ไม่ต้องมี distributed transaction |
| ตัวตนลูกค้า        | Guest ผ่าน cookie ที่เซ็นกำกับ                      | โจทย์ไม่ต้องการระบบสมาชิก                                          |
| จังหวะตัด stock    | จองเมื่อเพิ่มลงตะกร้า ตัดจริงเมื่อชำระเงิน          | โจทย์กำหนดให้ตะกร้าปรับยอด stock                                   |
| ตะกร้าถูกทิ้ง      | คืนการจองหลังนิ่ง 15 นาที                           | กัน stock ค้างในตะกร้าที่ไม่มีคนซื้อ                               |
| การชำระเงิน        | จำลอง: กดแล้วถือว่าจ่ายสำเร็จ                       | โจทย์ต้องการแค่แสดงยอดและตัด stock                                 |
| Outbox | ตารางใน SQL Server + ตัว relay ที่ publish เข้า Kafka | event ไม่หายแม้ Kafka ล่ม และโมดูลผู้รับแยกเป็น service ได้โดยไม่แก้ผู้ส่ง |
| Ingress controller | Traefik                                             | โครงการ ingress-nginx ยุติการดูแลตั้งแต่มีนาคม 2026                |

## 3. Tech stack

| ส่วน     | เทคโนโลยี                                                        |
| -------- | ---------------------------------------------------------------- |
| Frontend | Next.js (App Router, TypeScript), Tailwind CSS, shadcn/ui        |
| Backend  | .NET 10, ASP.NET Core Minimal API, EF Core                       |
| Messaging | Apache Kafka (โหมด KRaft), client `Confluent.Kafka` |
| Database | SQL Server 2022                                                  |
| ทดสอบ    | xUnit, `WebApplicationFactory`, Testcontainers (SQL Server จริง) |
| รัน      | Docker, docker compose (dev), Kubernetes + Kustomize             |

shadcn/ui คือ component ที่ copy เข้ามาอยู่ในโปรเจกต์ จึงแก้ได้เต็มที่และไม่มี runtime library เพิ่ม ใช้เฉพาะ: Button, Card, Badge, Sheet, Skeleton, Sonner (toast), AlertDialog

## 4. โครงสร้าง repository

```
E:\TNC
├─ backend/
│  ├─ Shop.sln
│  ├─ src/
│  │  ├─ Shop.Api/          host: Program.cs, middleware, ลงทะเบียนโมดูล
│  │  ├─ Shop.Shared/       contracts ระหว่างโมดูล, outbox, idempotency, unit of work
│  │  ├─ Shop.Catalog/
│  │  ├─ Shop.Inventory/
│  │  ├─ Shop.Cart/
│  │  └─ Shop.Ordering/
│  ├─ tests/Shop.IntegrationTests/
│  └─ Dockerfile
├─ frontend/
│  ├─ src/app/              หน้าเว็บ
│  ├─ src/components/       ui/ (shadcn) และ component ของร้าน
│  ├─ src/lib/              api client, format เงิน
│  ├─ public/products/      รูปสินค้า mock
│  └─ Dockerfile
├─ deploy/k8s/              Kustomize manifests
├─ docker-compose.yml
└─ docs/superpowers/specs/
```

## 5. สถาปัตยกรรม

### 5.1 ภาพการ deploy

```
Browser ──HTTPS──> Traefik Ingress (load balancer, TLS, rate limit, security headers)
                     ├── /      → Service web → Pod web  x2  (Next.js)
                     └── /api/* → Service api → Pod api  x3..10 (HPA)
                                                  └── Service mssql → StatefulSet mssql x1 (PVC)
```

- เว็บและ API อยู่ origin เดียวกัน จึงไม่เปิด CORS
- Pod `api` ไม่เก็บ state ในหน่วยความจำ ทุก pod ทำงานแทนกันได้
- ตอน dev ไม่มี ingress: Next.js ส่งต่อ `/api/*` ไปที่ API ผ่าน `rewrites` ตามตัวแปร `API_ORIGIN`
- Server component ของ Next.js ดึงรายการสินค้าจาก API โดยตรงภายใน cluster ผ่าน `API_ORIGIN` (ใน Kubernetes คือ `http://api.shop.svc`) จึงต้องตั้ง `API_ORIGIN` ในทุกสภาพแวดล้อม

### 5.2 โมดูล

แต่ละโมดูลเป็น class library หนึ่งตัว มี SQL schema ของตัวเอง มี `DbContext` ของตัวเอง และเปิดเผยตัวเองผ่านสองทางเท่านั้น: HTTP endpoint และ interface ใน `Shop.Shared/Contracts`

| โมดูล     | schema      | หน้าที่               | Contract ที่ให้โมดูลอื่นเรียก                                |
| --------- | ----------- | --------------------- | ------------------------------------------------------------ |
| Catalog   | `catalog`   | ข้อมูลสินค้า          | `ICatalogApi.GetProducts(ids)`                               |
| Inventory | `inventory` | stock และการจอง       | `IInventoryApi.Reserve / Release / Commit / GetAvailability` |
| Cart      | `cart`      | ตะกร้า, หมดอายุการจอง | `ICartApi.TakeItemsForCheckout(guestId)`                     |
| Ordering  | `ordering`  | ชำระเงิน, ยกเลิก      | ไม่มี (ส่ง event `OrderCancelled`)                           |
| Shared    | `shared`    | outbox, idempotency   | —                                                            |

**กฎขอบเขตโมดูล (ห้ามละเมิด)**

1. โมดูลห้าม query ตารางของ schema อื่น และห้ามมี foreign key ข้าม schema
2. โมดูลอ้างอิงได้เฉพาะ `Shop.Shared` ห้ามอ้างอิง project ของโมดูลอื่น
3. การเรียกข้ามโมดูลแบบ synchronous ผ่าน interface ใน `Shop.Shared/Contracts` เท่านั้น
4. การแจ้งข้ามโมดูลแบบ asynchronous ผ่าน outbox event เท่านั้น

### 5.3 Transaction ข้ามโมดูล

ทุกโมดูลใช้ฐานข้อมูลเดียวกัน หนึ่ง HTTP request ใช้ `SqlConnection` และ transaction เดียวกัน ผ่าน `UnitOfWork` (scoped) ใน `Shop.Shared` ทุก `DbContext` ผูกกับ transaction นั้นด้วย `Database.UseTransaction`

- Isolation level: `READ COMMITTED`
- เมื่อปรับ stock หลายสินค้าในครั้งเดียว ให้เรียงตาม `ProductId` เสมอ เพื่อลด deadlock
- ถ้าเจอ deadlock (SQL error 1205) ให้ทำ transaction ใหม่ทั้งก้อน สูงสุด 3 ครั้ง โดยเขียน retry เองรอบ `UnitOfWork` **ห้ามเปิด `EnableRetryOnFailure` ของ EF Core** เพราะมันโยน exception เมื่อเจอ transaction ที่เราเปิดเอง
- แต่ละโมดูลมีตารางประวัติ migration ของตัวเองใน schema ของตัวเอง (`MigrationsHistoryTable`)

### 5.4 เส้นทางแยกเป็น microservice

เมื่อจำเป็นต้องแยกโมดูลออกเป็น service:

1. ย้าย schema ของโมดูลนั้นไปฐานข้อมูลใหม่ (ทำได้เพราะไม่มี join และ foreign key ข้าม schema)
2. เปลี่ยน implementation ของ contract interface จากเรียกใน process เป็น HTTP client
3. ย้าย Kafka consumer ของโมดูลนั้นไปรันใน service ใหม่ (ผู้ส่งไม่ต้องแก้ เพราะ event ผ่าน Kafka อยู่แล้ว)
4. จุดที่เคยอยู่ใน transaction เดียว (จอง stock ตอนเพิ่มตะกร้า, ตัด stock ตอนชำระเงิน) ต้องเปลี่ยนเป็น saga ที่มีขั้นตอนชดเชย

## 6. โมเดลข้อมูล

ทุกตารางใช้เวลาเป็น UTC (`datetime2`) เงินใช้ `decimal(18,2)` สกุลบาท

### catalog.Products

| คอลัมน์     | ชนิด           | หมายเหตุ                                                             |
| ----------- | -------------- | -------------------------------------------------------------------- |
| Id          | int            | PK                                                                   |
| Sku         | nvarchar(32)   | unique, รหัสสินค้า                                                   |
| Name        | nvarchar(200)  |                                                                      |
| Description | nvarchar(1000) |                                                                      |
| Price       | decimal(18,2)  | CHECK `Price >= 0`, ราคาขายต่อหน่วย                                  |
| ImageUrl    | nvarchar(300)  | path ของไฟล์ static ใน frontend; CHECK `ImageUrl LIKE '/products/%'` |

### inventory.Stock

| คอลัมน์   | ชนิด | หมายเหตุ                 |
| --------- | ---- | ------------------------ |
| ProductId | int  | PK                       |
| OnHand    | int  | ของที่ยังไม่ถูกขาย       |
| Reserved  | int  | ของที่อยู่ในตะกร้าลูกค้า |

- CHECK `OnHand >= 0`, `Reserved >= 0`, `Reserved <= OnHand`
- **คงเหลือที่ขายได้ (Available) = OnHand − Reserved**

### cart.Carts และ cart.CartItems

| ตาราง     | คอลัมน์                  | หมายเหตุ                                                      |
| --------- | ------------------------ | ------------------------------------------------------------- |
| Carts     | GuestId uniqueidentifier | PK                                                            |
|           | ExpiresAt datetime2      | index; เลื่อนเป็น "ตอนนี้ + 15 นาที" ทุกครั้งที่ตะกร้าเปลี่ยน |
| CartItems | GuestId, ProductId       | PK ร่วม                                                       |
|           | Quantity int             | CHECK `Quantity BETWEEN 1 AND 99`                             |

ตะกร้าหนึ่งมีได้สูงสุด 50 รายการ

### ordering.Orders และ ordering.OrderItems

| ตาราง      | คอลัมน์                  | หมายเหตุ                  |
| ---------- | ------------------------ | ------------------------- |
| Orders     | Id uniqueidentifier      | PK                        |
|            | GuestId uniqueidentifier | index; เจ้าของออเดอร์     |
|            | Status nvarchar(16)      | `Paid` หรือ `Cancelled`   |
|            | Total decimal(18,2)      |                           |
|            | CreatedAt, CancelledAt   | CancelledAt เป็น null ได้ |
| OrderItems | OrderId, ProductId       | PK ร่วม                   |
|            | Sku, Name, UnitPrice     | สำเนา ณ เวลาที่ซื้อ       |
|            | Quantity, LineTotal      |                           |

### shared.OutboxMessages, shared.ProcessedMessages, shared.IdempotencyKeys

| ตาราง             | คอลัมน์                                    | หมายเหตุ                             |
| ----------------- | ------------------------------------------ | ------------------------------------ |
| OutboxMessages    | Id uniqueidentifier                        | PK                                   |
| | [Key] nvarchar(100) | key ของข้อความใน Kafka |
|                   | Type nvarchar(100), Payload nvarchar(max)  | Payload เป็น JSON                    |
|                   | OccurredAt, ProcessedAt                    | index บนแถวที่ `ProcessedAt IS NULL` |
|                   | Attempts int, LastError nvarchar(2000)     |                                      |
| ProcessedMessages | MessageId, Handler                         | PK ร่วม; กัน handler ทำงานซ้ำ        |
| IdempotencyKeys   | GuestId, [Key] nvarchar(64)                | PK ร่วม                              |
|                   | RequestHash binary(32)                     | SHA-256 ของ method + path + body     |
|                   | StatusCode int, ResponseBody nvarchar(max) | response ที่จะส่งซ้ำ                 |
|                   | CreatedAt                                  | ลบเมื่อเก่ากว่า 24 ชั่วโมง           |

### ข้อมูล mock

Seed ผ่าน EF Core `HasData` ใน migration: สินค้า 12 รายการพร้อม stock โดยตั้งใจให้มีสินค้าที่เหลือน้อย (1–3 ชิ้น) และสินค้าที่หมดแล้ว (0 ชิ้น) อย่างละอย่างน้อยหนึ่งรายการ เพื่อให้เห็นพฤติกรรมครบ

## 7. กฎของ stock

ทุกการเปลี่ยน stock เป็น `UPDATE` คำสั่งเดียวที่มีเงื่อนไขในตัว แล้วตรวจจำนวนแถวที่ถูกแก้ **ห้ามอ่านค่ามาคำนวณใน C# แล้วเขียนกลับ**

| การทำงาน | SQL                                                                         | ถ้าแก้ได้ 0 แถว       |
| -------- | --------------------------------------------------------------------------- | --------------------- |
| Reserve  | `SET Reserved += @q WHERE ProductId = @id AND OnHand - Reserved >= @q`      | 409 `out_of_stock`    |
| Release  | `SET Reserved -= @q WHERE ProductId = @id AND Reserved >= @q`               | error ภายใน, rollback |
| Commit   | `SET OnHand -= @q, Reserved -= @q WHERE ProductId = @id AND Reserved >= @q` | error ภายใน, rollback |
| Restock  | `SET OnHand += @q WHERE ProductId = @id`                                    | error ภายใน           |

| การกระทำของลูกค้า               | ผล                              |
| ------------------------------- | ------------------------------- |
| เพิ่มสินค้า / เพิ่มจำนวน        | Reserve ตามจำนวนที่เพิ่ม        |
| ลดจำนวน / ลบรายการ / ล้างตะกร้า | Release ตามจำนวนที่ลด           |
| ตะกร้านิ่งเกิน 15 นาที          | Release ทุกรายการ แล้วลบตะกร้า  |
| ชำระเงิน                        | Commit ทุกรายการ                |
| ยกเลิกออเดอร์                   | Restock ทุกรายการ (ผ่าน outbox) |

**การกันชนกันภายในตะกร้าเดียว:** ทุกการแก้ตะกร้าเริ่มด้วยการล็อกแถวใน `cart.Carts` (`WITH (UPDLOCK, HOLDLOCK)`) ถ้ายังไม่มีแถวให้สร้าง ถ้าแถวหมดอายุแล้วให้ Release ของเดิมและเริ่มเป็นตะกร้าว่างก่อนทำงานต่อ

**ตัวกวาดตะกร้าหมดอายุ:** `BackgroundService` ในทุก pod ทำงานทุก 30 วินาที หยิบตะกร้าทีละใบด้วย `SELECT TOP 1 ... WITH (UPDLOCK, READPAST, ROWLOCK) WHERE ExpiresAt < SYSUTCDATETIME()` แล้ว Release และลบ ใน transaction เดียว `READPAST` ทำให้หลาย pod ไม่แย่งใบเดียวกัน งานเดียวกันนี้ลบ `IdempotencyKeys` ที่เก่ากว่า 24 ชั่วโมงด้วย

## 8. REST API

- Base path: `/api`
- Body เป็น JSON (`Content-Type: application/json`)
- ตัวตน: cookie `sid` (ข้อ 11.1) endpoint ของตะกร้าและออเดอร์ออก cookie ใหม่ให้เมื่อ request ไม่มี cookie ที่ถูกต้อง ส่วน `GET /products` ไม่ใช้และไม่ออก cookie
- Error ใช้รูปแบบ ProblemDetails (RFC 9457) และมี field `code`

| Method | Path                              | ทำอะไร                               | Idempotency-Key | สำเร็จ |
| ------ | --------------------------------- | ------------------------------------ | --------------- | ------ |
| GET    | `/products`                       | รายการสินค้าพร้อม `available`        | —               | 200    |
| GET    | `/cart`                           | ตะกร้าปัจจุบัน                       | —               | 200    |
| POST   | `/cart/items`                     | เพิ่มสินค้า (บวกจำนวนเข้ากับของเดิม) | บังคับ          | 200    |
| PUT    | `/cart/items/{productId}`         | ตั้งจำนวนเป็นค่าที่ระบุ              | —               | 200    |
| DELETE | `/cart/items/{productId}`         | ลบรายการ                             | —               | 200    |
| DELETE | `/cart`                           | ล้างตะกร้า                           | —               | 200    |
| POST   | `/orders`                         | ชำระเงิน                             | บังคับ          | 201    |
| GET    | `/orders/{id}`                    | ดูออเดอร์                            | —               | 200    |
| POST   | `/orders/{id}/cancel`             | ยกเลิกออเดอร์                        | บังคับ          | 200    |
| GET    | `/healthz/live`, `/healthz/ready` | health check (ไม่อยู่ใต้ `/api`)     | —               | 200    |

`PUT` และ `DELETE` ให้ผลเหมือนเดิมเมื่อส่งซ้ำอยู่แล้ว จึงไม่ต้องใช้ key ทุก endpoint ของตะกร้าคืนตะกร้าทั้งใบกลับมา

**รูปข้อมูล**

```jsonc
// GET /api/products
[{ "id": 1, "sku": "TNC-001", "name": "...", "description": "...",
   "price": 1290.00, "imageUrl": "/products/tnc-001.svg", "available": 3 }]

// ตะกร้า (GET /api/cart และทุก endpoint ที่แก้ตะกร้า)
{ "items": [{ "productId": 1, "sku": "TNC-001", "name": "...", "imageUrl": "...",
              "unitPrice": 1290.00, "quantity": 2, "lineTotal": 2580.00, "available": 1 }],
  "total": 2580.00, "expiresAt": "2026-10-06T03:15:00Z" }   // expiresAt เป็น null เมื่อตะกร้าว่าง

// POST /api/cart/items
{ "productId": 1, "quantity": 1 }

// PUT /api/cart/items/1
{ "quantity": 3 }

// ออเดอร์ (POST /api/orders ไม่มี body, GET, cancel)
{ "id": "…guid…", "status": "Paid", "total": 2580.00,
  "createdAt": "…", "cancelledAt": null,
  "items": [{ "productId": 1, "sku": "TNC-001", "name": "...",
              "unitPrice": 1290.00, "quantity": 2, "lineTotal": 2580.00 }] }
```

**รหัส error**

| HTTP | code                       | เมื่อไหร่                                   |
| ---- | -------------------------- | ------------------------------------------- |
| 400  | `validation_failed`        | input ผิดรูปแบบหรือเกินขอบเขต               |
| 400  | `idempotency_key_required` | ไม่ส่ง key หรือ key ผิดรูปแบบ               |
| 400  | `cart_empty`               | ชำระเงินขณะตะกร้าว่าง                       |
| 404  | `not_found`                | ไม่พบสินค้า หรือออเดอร์ไม่ใช่ของ guest นี้  |
| 409  | `out_of_stock`             | คงเหลือไม่พอ (แนบ `available` มาด้วย)       |
| 409  | `request_in_progress`      | request ที่ใช้ key เดียวกันยังทำงานไม่เสร็จ |
| 422  | `idempotency_key_reuse`    | key เดิมแต่เนื้อหา request ต่างจากเดิม      |
| 429  | `rate_limited`             | เกิน rate limit                             |

**การตรวจ input**

- `productId`: จำนวนเต็มบวก และต้องมีสินค้านั้น
- `quantity`: จำนวนเต็ม 1–99
- `Idempotency-Key`: 16–64 ตัวอักษร ชุด `[A-Za-z0-9_-]`
- body ใหญ่สุด 4 KB; field ที่ไม่รู้จักถูกปฏิเสธ

## 9. Flow หลัก

### 9.1 เพิ่มสินค้า และตั้งจำนวน

1. ล็อกแถวตะกร้า (ข้อ 7)
2. คำนวณส่วนต่างระหว่างจำนวนใหม่กับจำนวนเดิม
3. ส่วนต่างเป็นบวก → `Reserve`; เป็นลบ → `Release`
4. บันทึก `CartItems` และเลื่อน `ExpiresAt`
5. Commit transaction แล้วคืนตะกร้า

ถ้า `Reserve` ไม่ผ่าน transaction ถูก rollback ตะกร้าไม่เปลี่ยน และตอบ 409 พร้อมจำนวนที่เหลือจริง

### 9.2 ชำระเงิน (`POST /orders`)

ทั้งหมดอยู่ใน transaction เดียว:

1. จอง idempotency key (ข้อ 10.1)
2. `ICartApi.TakeItemsForCheckout` — ล็อกตะกร้า, อ่านรายการ, ลบตะกร้า; ถ้าว่างตอบ 400 `cart_empty`
3. `ICatalogApi.GetProducts` — อ่านราคาปัจจุบันจากฐานข้อมูล
4. `IInventoryApi.Commit` ทุกรายการ เรียงตาม `ProductId`
5. สร้าง `Orders` สถานะ `Paid` และ `OrderItems` โดย `Total` = ผลรวมของ `UnitPrice × Quantity`
6. บันทึก response ลง `IdempotencyKeys` แล้ว commit

ราคาและยอดรวมคำนวณที่ server เท่านั้น API ไม่รับราคาจาก client

### 9.3 ยกเลิกออเดอร์ (`POST /orders/{id}/cancel`)

1. `UPDATE Orders SET Status = 'Cancelled', CancelledAt = ... WHERE Id = @id AND GuestId = @guest AND Status = 'Paid'`
2. แก้ได้ 1 แถว → เขียน `OrderCancelled` ลง `OutboxMessages` ใน transaction เดียวกัน
3. แก้ได้ 0 แถวและออเดอร์เป็น `Cancelled` อยู่แล้ว → ตอบ 200 ด้วยสถานะปัจจุบัน ไม่เขียน event
4. แก้ได้ 0 แถวและไม่พบออเดอร์ของ guest นี้ → 404

Stock กลับคืนเมื่อ event เดินทางผ่าน Kafka ถึง consumer ของ Inventory ซึ่งปกติภายใน 1–2 วินาที (eventual consistency) หน้าเว็บดึงรายการสินค้าใหม่หลังยกเลิก

## 10. Idempotency และ Outbox

### 10.1 Idempotency key

ทำเป็น endpoint filter ตัวเดียวใน `Shop.Shared` ใช้กับ POST สามตัวในตาราง API

1. ตรวจรูปแบบ key ไม่ผ่าน → 400
2. `INSERT` แถวลง `IdempotencyKeys` **ภายใน transaction เดียวกับงานธุรกิจ**
3. Insert สำเร็จ → ทำงานต่อ, บันทึก status และ body ของ response ลงแถวนั้น, commit
4. Insert ชน primary key → อ่านแถวเดิม
   - `RequestHash` ตรงกัน → ส่ง response เดิมกลับ
   - ไม่ตรง → 422 `idempotency_key_reuse`

ผลที่ได้จากการอยู่ใน transaction เดียวกัน:

- request ซ้ำที่มาพร้อมกันจะรอที่ lock ของ primary key จนตัวแรกเสร็จ แล้วได้ response เดียวกัน ถ้ารอเกิน 5 วินาทีตอบ 409 `request_in_progress`
- ถ้างานธุรกิจล้มเหลว transaction ถูก rollback แถว key หายไปด้วย การ retry จึงทำงานใหม่ได้อย่างปลอดภัย เพราะไม่มีอะไรถูกเปลี่ยน

Frontend สร้าง key ด้วย `crypto.randomUUID()` หนึ่ง key ต่อหนึ่งการกดของผู้ใช้ และใช้ key เดิมเมื่อ retry

### 10.2 Outbox และ Kafka

Event เดินทางสองช่วง: ผู้ส่งเขียนลงตาราง outbox → relay publish เข้า Kafka → consumer ของโมดูลผู้รับประมวลผล

```
Ordering ──(transaction เดียวกับออเดอร์)──> shared.OutboxMessages
                                              │  OutboxRelay (ทุก pod)
                                              ▼
                                   Kafka topic  shop.events
                                              │  consumer group  shop-inventory
                                              ▼
                              Inventory: OrderCancelledHandler (Restock)
```

**Event ที่มี**

| Event            | ผู้ส่ง   | ผู้รับ              | Payload                                   |
| ---------------- | -------- | ------------------- | ----------------------------------------- |
| `OrderCancelled` | Ordering | Inventory (Restock) | `orderId`, `items[{productId, quantity}]` |

**รูปข้อความใน Kafka**

| ส่วน | ค่า |
| --- | --- |
| Topic | `shop.events` (3 partition) |
| Key | รหัสของสิ่งที่ event พูดถึง (`orderId`) เพื่อให้ event ของออเดอร์เดียวกันเรียงลำดับ |
| Value | Payload เป็น JSON |
| Header `message-id` | `OutboxMessages.Id` ใช้กันประมวลผลซ้ำ |
| Header `type` | ชื่อ event เช่น `OrderCancelled` |

**OutboxRelay:** `BackgroundService` ในทุก pod วนทุก 1 วินาที ส่งทีละข้อความ หนึ่งข้อความต่อหนึ่ง transaction:

1. `SELECT TOP 1 ... FROM shared.OutboxMessages WITH (UPDLOCK, READPAST, ROWLOCK) WHERE ProcessedAt IS NULL ORDER BY Attempts, OccurredAt`
2. Publish เข้า Kafka และรอ broker ยืนยัน (`acks=all`, `enable.idempotence=true`)
3. ตั้ง `ProcessedAt` แล้ว commit

ถ้า publish ล้มเหลว (เช่น Kafka ล่ม): เพิ่ม `Attempts` และบันทึก `LastError` ข้อความยังอยู่ในตารางและถูกส่งใหม่ไปเรื่อย ๆ **ไม่มีเพดานจำนวนครั้ง** เพราะความล้มเหลวช่วงนี้เกือบทั้งหมดเป็นปัญหาโครงสร้างพื้นฐาน ข้อความที่เคยล้มเหลวถูกเรียงไปท้ายคิว (`ORDER BY Attempts`) จึงไม่ขวางข้อความอื่น เมื่อ `Attempts` ถึง 10 ระบบ log ระดับ error เพื่อให้คนเข้ามาดู

**Consumer:** `BackgroundService` ในทุก pod ใช้ consumer group `shop-inventory`, `enable.auto.commit=false`:

1. รับข้อความ หา handler จาก header `type`
2. เปิด transaction: handler `INSERT` ลง `ProcessedMessages` (`message-id`, ชื่อ handler) ก่อน ถ้าชน primary key แปลว่าเคยทำแล้ว ให้ข้ามงาน
3. Handler ทำงาน (Restock) แล้ว commit transaction
4. Commit offset ให้ Kafka

ถ้า handler ล้มเหลว ลองใหม่สูงสุด 5 ครั้ง ถ้ายังไม่ผ่าน หรือไม่มี handler สำหรับ `type` นั้น หรือข้อความไม่มี header ครบ ให้ส่งข้อความไป topic `shop.events.dlq` แล้ว commit offset เพื่อไม่ให้ข้อความเสียขวาง partition

**การรับประกัน:** ทั้งสองช่วงส่งอย่างน้อยหนึ่งครั้ง (at-least-once) relay อาจ publish ซ้ำถ้าล้มหลัง publish ก่อน commit และ consumer อาจได้ข้อความซ้ำถ้าล้มก่อน commit offset ตาราง `ProcessedMessages` ทำให้ผลเกิดครั้งเดียว

**ความปลอดภัยของ Kafka:** ใน cluster ใช้ listener แบบ `PLAINTEXT` และจำกัดการเข้าถึงด้วย NetworkPolicy ให้เฉพาะ pod `api` ใน production ต้องเปิด TLS และ SASL และใช้ Kafka แบบ managed หรืออย่างน้อย 3 broker

## 11. ความปลอดภัย

### 11.1 ตัวตนและสิทธิ์

- Cookie `sid` มีค่า `{guestId}.{HMAC-SHA256(guestId)}` เข้ารหัส base64url
  - คุณสมบัติ: `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, อายุ 30 วัน
  - กุญแจเซ็นมาจาก Secret `CART_SIGNING_KEY` (สุ่ม 32 ไบต์ขึ้นไป) ทุก pod ใช้กุญแจเดียวกัน
  - เทียบลายเซ็นด้วย `CryptographicOperations.FixedTimeEquals`
  - ลายเซ็นไม่ถูกต้อง → ทิ้ง cookie นั้นและออก guest ใหม่
- ตะกร้าและออเดอร์ถูกค้นด้วย `GuestId` จาก cookie เสมอ ออเดอร์ของคนอื่นตอบ 404 เหมือนไม่มีอยู่ (กัน IDOR)
- **CSRF:** กันด้วย `SameSite=Strict` ร่วมกับการที่ API รับเฉพาะ `application/json` และไม่เปิด CORS

### 11.2 SQL injection

- คำสั่ง SQL ทุกคำสั่งส่งค่าผ่าน parameter เท่านั้น **ห้ามต่อ string หรือใช้ string interpolation ธรรมดาประกอบ SQL**
  - Query ทั่วไปใช้ LINQ ของ EF Core
  - SQL ที่เขียนเอง (กฎ stock ข้อ 7, การล็อกแถว) ใช้ `ExecuteSqlInterpolatedAsync` / `FromSqlInterpolated` หรือ `SqlParameter`
  - ห้ามใช้ `ExecuteSqlRaw` / `FromSqlRaw` กับ string ที่มีค่าจากภายนอกปนอยู่
  - ชื่อตาราง ชื่อคอลัมน์ และ `ORDER BY` เป็นค่าคงที่ในโค้ด ไม่รับจาก request
- Input ถูกตรวจชนิดและขอบเขตก่อนถึงชั้นข้อมูล (ข้อ 8): id เป็น `int` หรือ `Guid`, จำนวนเป็น `int` 1–99, key ตรงตาม pattern ไม่มี endpoint ใดรับข้อความอิสระจากผู้ใช้
- Login `shop_app` ของ API มีสิทธิ์แค่ `SELECT/INSERT/UPDATE/DELETE` บน schema ของระบบ ไม่มีสิทธิ์ DDL และไม่ใช่ `sa` ถ้าหลุดก็สร้างความเสียหายได้จำกัด
- Error จากฐานข้อมูลไม่ถูกส่งถึง client (ข้อ 11.5)

### 11.3 Cross-site scripting (XSS)

- แสดงข้อมูลผ่าน JSX ของ React เท่านั้น ซึ่ง escape ให้อัตโนมัติ **ห้ามใช้ `dangerouslySetInnerHTML`, `innerHTML`, หรือ `eval`**
- `Content-Security-Policy` แบบ nonce ต่อ request ตั้งใน Next.js middleware (ใน Next.js 16 ขึ้นไปไฟล์นี้ชื่อ `proxy.ts`):
  `default-src 'self'; script-src 'self' 'nonce-…' 'strict-dynamic'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`
  - `script-src` ไม่มี `'unsafe-inline'` และ `'unsafe-eval'` ใน production
  - `style-src` ยอม `'unsafe-inline'` เพราะ component ของ shadcn/ui (Radix, Sonner) ใส่ style ตอน runtime ซึ่ง nonce ครอบไม่ถึง ความเสี่ยงจาก style ต่ำกว่า script มาก
  - CSP ของหน้าเว็บตั้งที่ Next.js **ที่เดียว** ห้ามตั้งซ้ำที่ Traefik เพราะ browser จะบังคับใช้ทั้งสองชุดและ nonce จะใช้ไม่ได้
  - ระหว่างพัฒนาต้องดู console ของ browser ว่าไม่มี CSP violation
- `ImageUrl` ต้องเป็น path ที่ขึ้นต้นด้วย `/products/` เท่านั้น ตรวจทั้งใน CHECK constraint ของฐานข้อมูลและก่อน render กัน URL แบบ `javascript:` และรูปจากภายนอก
- API ตอบเป็น `application/json` พร้อม `X-Content-Type-Options: nosniff` เสมอ browser จึงไม่ตีความ response เป็น HTML
- Cookie `sid` เป็น `HttpOnly` สคริปต์อ่านไม่ได้ แม้มี XSS หลุดเข้ามา
- ข้อความ error ที่แสดงบนหน้าเว็บมาจากตารางข้อความใน frontend ที่จับคู่กับ `code` ไม่นำข้อความจาก server มาแสดงตรง ๆ

### 11.4 ข้อมูลและฐานข้อมูล

- API ใช้ login `shop_app` ที่มีสิทธิ์แค่อ่านเขียนข้อมูล ไม่มีสิทธิ์ DDL; migration Job ใช้ login อีกตัวที่มีสิทธิ์สูงกว่า
- Connection ใช้ `Encrypt=True`; ในเครื่อง dev ยอมรับใบรับรอง self-signed ด้วย `TrustServerCertificate=True` ซึ่ง **ห้ามใช้ใน production**
- CHECK constraint ในตาราง `Stock` เป็นด่านสุดท้ายถ้าโค้ดผิด

### 11.5 ขอบระบบ

- **Rate limit สองชั้น**
  - Traefik: 100 request ต่อ 10 วินาที ต่อ IP
  - API (ต่อ IP เพราะ guest เปลี่ยน cookie ได้เอง): แก้ตะกร้า 30 ครั้งต่อนาที, ชำระเงินและยกเลิก 5 ครั้งต่อนาที
  - ตัวนับใน API อยู่ในหน่วยความจำของแต่ละ pod เพดานจริงจึงเท่ากับค่าที่ตั้งคูณจำนวน pod
- API เชื่อ header `X-Forwarded-For` จาก Traefik เท่านั้น
- **Header ความปลอดภัยที่ Traefik ใส่:** `Strict-Transport-Security`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin` ส่วน CSP ตั้งที่ Next.js (ข้อ 11.3)
- TLS สิ้นสุดที่ Traefik; HTTP ถูก redirect ไป HTTPS
- Error ที่ไม่คาดคิดตอบ 500 แบบ ProblemDetails ที่ไม่มี stack trace รายละเอียดอยู่ใน log เท่านั้น
- Log ไม่บันทึกค่า cookie, กุญแจ, หรือ connection string

### 11.6 Secret และ container

- Secret ทั้งหมด (`SA_PASSWORD`, `APP_DB_PASSWORD`, `CART_SIGNING_KEY`) อยู่ใน Kubernetes Secret ที่สร้างจากไฟล์ `deploy/k8s/secrets.env` ซึ่งอยู่ใน `.gitignore` ใน repo มีแค่ `secrets.example.env`
- Container ของ `api` และ `web`: รันด้วย user ที่ไม่ใช่ root, `readOnlyRootFilesystem: true`, `allowPrivilegeEscalation: false`, drop capabilities ทั้งหมด, กำหนด resource requests และ limits
- เพราะ filesystem อ่านได้อย่างเดียว ต้อง mount `emptyDir` ที่ `/tmp` ของ `api` และ `web` และที่ `/app/.next/cache` ของ `web`
- Image ของ SQL Server 2022 รันด้วย uid 10001 StatefulSet จึงต้องตั้ง `fsGroup: 10001` ไม่เช่นนั้นเขียน PVC ไม่ได้
- Image แบบ multi-stage; image ที่รันจริงไม่มี SDK

### 11.7 NetworkPolicy

- ค่าเริ่มต้น: ปิดการเข้าออกทั้งหมดใน namespace `shop`
- อนุญาต: Traefik → `web` และ `api`; `web` → `api`; `api` และ migration Job → `mssql`; `api` → `kafka`; ทุก pod → DNS

## 12. Frontend

**หลักการ:** ภาษาไทย, สกุลเงินบาท (`Intl.NumberFormat('th-TH', { style: 'currency', currency: 'THB' })`), mobile-first, เส้นทางจากเห็นสินค้าถึงชำระเงินสั้นที่สุด

### 12.1 หน้า

| Path           | เนื้อหา                                              |
| -------------- | ---------------------------------------------------- |
| `/`            | ตารางสินค้า + ตะกร้าแบบ drawer                       |
| `/orders/[id]` | ผลการชำระเงิน, รายการ, ยอดที่ชำระ, ปุ่มยกเลิกออเดอร์ |

### 12.2 Component

| Component          | พฤติกรรม                                                                  |
| ------------------ | ------------------------------------------------------------------------- |
| `ProductCard`      | รูป, ชื่อ, ราคา, ป้ายคงเหลือ, ปุ่ม "ใส่ตะกร้า"                            |
| `StockBadge`       | คงเหลือ ≤ 5: "เหลือ N ชิ้น" สีเตือน; 0: "สินค้าหมด" และปิดปุ่ม            |
| `CartButton`       | ปุ่มลอยมุมบน แสดงจำนวนชิ้นในตะกร้า                                        |
| `CartDrawer`       | shadcn `Sheet`; เปิดเองเมื่อเพิ่มสินค้าสำเร็จ                             |
| `CartLine`         | ปุ่ม − / + , ปุ่มลบ, ราคารวมของรายการ; ปุ่ม + ปิดเมื่อ `available` เป็น 0 |
| `ReservationTimer` | นับถอยหลังจาก `expiresAt`: "จองสินค้าให้คุณอีก 14:32"                     |
| `CheckoutButton`   | ติดอยู่ล่าง drawer เสมอ แสดงยอดในปุ่ม: "ชำระเงิน ฿2,580.00"               |
| `CartProvider`     | React context เก็บตะกร้าและฟังก์ชันเรียก API                              |

### 12.3 พฤติกรรม

- เปิดหน้า: server component ดึง `/api/products` มาแสดงทันที แล้ว client ดึงซ้ำทุก 30 วินาทีและหลังทุกการแก้ตะกร้า
- เพิ่มสินค้าสำเร็จ: drawer เปิด และตัวเลขที่ `CartButton` อัปเดต
- ได้ 409 `out_of_stock`: toast "สินค้าเหลือไม่พอ" และดึงข้อมูลสินค้าใหม่
- ปุ่มที่กำลังส่ง request ถูกปิดและแสดงสถานะกำลังทำงาน เพื่อกันกดซ้ำ
- เวลานับถอยหลังถึงศูนย์: ดึงตะกร้าใหม่ และแจ้งว่า "หมดเวลาจองแล้ว"
- "ล้างตะกร้า" และ "ยกเลิกออเดอร์" ถามยืนยันด้วย `AlertDialog`
- กด "ชำระเงิน": เรียก `POST /api/orders` แล้วพาไป `/orders/{id}` ซึ่งแสดงยอดที่ชำระ
- ตะกร้าว่าง: แสดงข้อความชวนเลือกสินค้า ไม่แสดงปุ่มชำระเงิน

### 12.4 Accessibility

ปุ่มที่เป็นไอคอนมี `aria-label`, ใช้งานด้วยคีย์บอร์ดได้ครบ, focus เห็นชัด, สีตัดกันตาม WCAG AA, การเปลี่ยนแปลงของตะกร้าประกาศผ่าน `aria-live`

## 13. Kubernetes

Manifest อยู่ใน `deploy/k8s/` จัดการด้วย Kustomize namespace `shop`

| ไฟล์                 | Resource                                                                                      |
| -------------------- | --------------------------------------------------------------------------------------------- |
| `namespace.yaml`     | Namespace                                                                                     |
| `mssql.yaml`         | StatefulSet 1 replica, PVC 5Gi, Service (ClusterIP)                                           |
| `kafka.yaml` | StatefulSet 1 replica (image `apache/kafka`, โหมด KRaft ไม่ใช้ ZooKeeper), PVC 2Gi, Service (ClusterIP) port 9092 |
| `migrate-job.yaml`   | Job: รัน migration, seed, สร้าง login `shop_app`                                              |
| `api.yaml`           | Deployment 3 replicas, Service, HPA 3–10 ที่ CPU 70%, PodDisruptionBudget (`minAvailable: 2`) |
| `web.yaml`           | Deployment 2 replicas, Service                                                                |
| `ingress.yaml`       | Ingress + Traefik Middleware (rate limit, headers, redirect HTTPS)                            |
| `networkpolicy.yaml` | ตามข้อ 11.7                                                                                   |
| `kustomization.yaml` | รวมทุกไฟล์ + `secretGenerator` จาก `secrets.env`                                              |

- **Migration:** image เดียวกับ `api` เรียกด้วย argument `migrate` แล้วจบการทำงาน Pod `api` **ไม่** รัน migration ตอนเริ่ม
- **Probe:** `readinessProbe` ที่ `/healthz/ready` (ตรวจการเชื่อมต่อฐานข้อมูล), `livenessProbe` ที่ `/healthz/live`
- **ปิด pod อย่างนุ่มนวล:** `terminationGracePeriodSeconds: 30` และ background service หยุดเมื่อได้รับสัญญาณปิด
- **Rolling update:** `maxUnavailable: 0`, `maxSurge: 1`

**ข้อจำกัดบน Kubernetes ของ Docker Desktop**

| เรื่อง        | สถานะ                                                                                           |
| ------------- | ----------------------------------------------------------------------------------------------- |
| Traefik       | ต้องติดตั้งเองก่อน (Helm)                                                                       |
| HPA           | ต้องติดตั้ง metrics-server ไม่เช่นนั้น HPA ไม่มีตัวเลขให้ใช้                                    |
| NetworkPolicy | CNI ที่มากับ Docker Desktop ไม่บังคับใช้ manifest ถูกต้องแต่จะมีผลจริงบน cluster ที่ CNI รองรับ |
| TLS           | ใช้ใบรับรอง self-signed กับ host `shop.localhost`                                               |

**ฐานข้อมูลใน production:** SQL Server หนึ่ง replica ใน cluster เหมาะกับ dev และ demo ใน production ควรใช้บริการฐานข้อมูลแบบ managed หรือ Always On availability group แล้วชี้ connection string ไปที่นั่น

## 14. การรันในเครื่อง

| โหมด            | คำสั่ง                                                                                                | ใช้เมื่อ                        |
| --------------- | ----------------------------------------------------------------------------------------------------- | ------------------------------- |
| Dev             | `docker compose up mssql` แล้ว `dotnet run` ใน `backend/src/Shop.Api` และ `npm run dev` ใน `frontend` | เขียนโค้ด                       |
| Compose ทั้งชุด | `docker compose up --build`                                                                           | ลองทั้งระบบโดยไม่ใช้ Kubernetes |
| Kubernetes      | build image สองตัว แล้ว `kubectl apply -k deploy/k8s`                                                 | ทดสอบ deploy จริง               |

ค่าที่ตั้งได้ผ่าน environment variable: `ConnectionStrings__Shop`, `CART_SIGNING_KEY`, `Cart__ReservationMinutes` (ค่าเริ่มต้น 15), `API_ORIGIN` (ฝั่ง frontend, ทุกสภาพแวดล้อม)

## 15. การทดสอบ

Integration test รันกับ SQL Server จริงผ่าน Testcontainers เพราะพฤติกรรมเรื่อง lock และ transaction ทดสอบด้วย mock ไม่ได้

| #   | กรณี                                                                                               | ผลที่ต้องได้                                                                           |
| --- | -------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------- |
| 1   | Guest 50 คนเพิ่มสินค้าที่เหลือ 5 ชิ้นพร้อมกัน                                                      | สำเร็จ 5 คน, ที่เหลือได้ 409, `Reserved` = 5                                           |
| 2   | ชำระเงินด้วย key เดียวกัน 10 ครั้งพร้อมกัน                                                         | มีออเดอร์เดียว, stock ถูกตัดครั้งเดียว, ทุก response เหมือนกัน                         |
| 3   | ใช้ key เดิมกับ body ที่ต่างกัน                                                                    | 422                                                                                    |
| 4   | ยกเลิกออเดอร์เดียวกันสองครั้ง และข้อความถูกส่งซ้ำใน Kafka                                   | `OnHand` กลับคืนครั้งเดียว                                                             |
| 5   | เพิ่ม ลด ลบ ล้างตะกร้า                                                                             | `Reserved` ตรงกับผลรวมของตะกร้าทุกใบเสมอ                                               |
| 6   | ตะกร้าหมดอายุ                                                                                      | ตัวกวาดคืน `Reserved` และลบตะกร้า                                                      |
| 7   | Guest A ขอดูหรือยกเลิกออเดอร์ของ guest B                                                           | 404                                                                                    |
| 8   | Cookie ที่ถูกแก้ลายเซ็น                                                                            | ได้ guest ใหม่ ไม่เห็นตะกร้าเดิม                                                       |
| 9   | `quantity` เป็น 0, ติดลบ, 100, หรือไม่ใช่ตัวเลข                                                    | 400                                                                                    |
| 10  | ชำระเงินขณะตะกร้าว่าง                                                                              | 400 `cart_empty`                                                                       |
| 11  | ส่ง payload SQL injection (`1; DROP TABLE ...`, `' OR 1=1--`) ใน path, body, และ `Idempotency-Key` | 400 หรือ 404, ตารางและข้อมูลไม่เปลี่ยน                                                 |
| 12  | สินค้าที่ชื่อมี `<script>alert(1)</script>` (ใส่ตรงในฐานข้อมูลทดสอบ)                               | API คืนเป็นข้อความใน JSON, หน้าเว็บแสดงเป็นตัวอักษร สคริปต์ไม่ทำงาน                    |
| 13  | ตรวจ response header ของหน้าเว็บและ API                                                            | มี CSP ที่ `script-src` ไม่มี `unsafe-inline` และ `unsafe-eval`, มี `nosniff` และ HSTS |

**ตรวจโค้ดอัตโนมัติ:** build ล้มเหลวถ้าพบ `ExecuteSqlRaw`, `FromSqlRaw`, หรือ `dangerouslySetInnerHTML` ในซอร์ส (ตรวจด้วย `git grep` ใน test หนึ่งตัว)

**Invariant ที่ทุก test ตรวจตอนจบ:** สำหรับทุกสินค้า `Reserved` = ผลรวม `Quantity` ใน `CartItems` และ `OnHand` = ค่าตั้งต้น − ผลรวมของออเดอร์สถานะ `Paid`

**Frontend:** `tsc --noEmit` และ `eslint` ต้องผ่าน และตรวจด้วยมือใน browser ตามเกณฑ์สำเร็จข้อ 1–5 ทั้งหน้าจอมือถือและ desktop

**Kubernetes:** หลัง deploy ตรวจว่า pod `api` ทั้ง 3 ตัวพร้อม, ซื้อของผ่าน `https://shop.localhost` ได้ครบวงจร, และลบ pod `api` หนึ่งตัวระหว่างใช้งานแล้วระบบยังทำงานต่อ

## 16. สิ่งที่ตั้งใจไม่ทำ และควรทำเมื่อไหร่

| ไม่ทำ                              | ทำเมื่อ                                                              |
| ---------------------------------- | -------------------------------------------------------------------- |
| Kafka หลาย broker, TLS, SASL | ก่อนขึ้น production |
| Redis สำหรับ rate limit หรือ cache | ต้องการเพดาน rate limit ที่แม่นยำข้าม pod หรือ SQL รับโหลดอ่านไม่ไหว |
| OpenTelemetry, dashboard           | ก่อนขึ้น production                                                  |
| Playwright e2e                     | เมื่อหน้าเว็บมีมากกว่าสองหน้า                                        |
| ระบบสมาชิก                         | ต้องการประวัติการสั่งซื้อข้ามอุปกรณ์                                 |
| CI/CD pipeline                     | เมื่อมี remote repository                                            |

## 17. บันทึกจากการลงมือสร้าง

จุดที่ของจริงต่างจากข้อความในข้ออื่นของเอกสารนี้ ถ้าขัดกัน ให้ยึดข้อนี้และโค้ด

| เรื่อง | ของจริง | เหตุผล |
| --- | --- | --- |
| การล็อกตะกร้า (ข้อ 7) | `sp_getapplock` ต่อ guest ร่วมกับ `UPDLOCK, ROWLOCK` บนแถว ไม่ใช่ `UPDLOCK, HOLDLOCK` | `HOLDLOCK` บนแถวที่ยังไม่มี ทำให้สอง request แรกของ guest ใหม่ deadlock กัน app lock ให้ผลแน่นอนกว่า |
| การลบ idempotency key ที่เก่า (ข้อ 7) | ทำโดย `IdempotencyCleanup` ใน `Shop.Shared` ทุก 10 นาที ไม่ใช่ในตัวกวาดตะกร้า | โมดูล Cart ห้ามแตะ schema `shared` ตามกฎข้อ 5.2 |
| Outbox (ข้อ 10.2) | `IOutbox.AddAsync(key, message, ct)`; การ publish ไม่มีเพดานจำนวนครั้ง | เพดานจะทำให้ event ค้างถาวรถ้า Kafka ล่มนานเกินหนึ่งนาที |
| การส่งต่อ `/api` ตอนไม่มี ingress (ข้อ 5.1) | ทำใน `frontend/src/proxy.ts` ตอนรับ request ไม่ใช่ `rewrites` ใน `next.config` | `rewrites` ถูกกำหนดตอน build ทำให้ image เดียวใช้กับทั้ง compose และ Kubernetes ไม่ได้ |
| Next.js และ component (ข้อ 3, 11.3) | Next.js 16 (ไฟล์ CSP ชื่อ `proxy.ts`); shadcn/ui สร้าง component บน Base UI ไม่ใช่ Radix | เป็นสิ่งที่เครื่องมือรุ่นปัจจุบันสร้างให้ เหตุผลของ `style-src 'unsafe-inline'` ยังเหมือนเดิม |
| Forwarded headers (ข้อ 11.5) | ถ้าไม่ตั้ง `ForwardedHeaders:KnownNetworks` API จะไม่อ่าน `X-Forwarded-For` เลย และใน docker compose ตั้งใจไม่ตั้งค่านี้ | ค่าเริ่มต้นของ ASP.NET เมื่อรายการว่างคือเชื่อทุกคน ซึ่งทำให้หลบ rate limit ได้ด้วย header ปลอม |
| เวลา (ข้อ 6) | ทุกคอลัมน์ `DateTime` ถูกอ่านกลับเป็น UTC ผ่าน convention ใน `ModuleDbContext` | ไม่เช่นนั้น JSON ไม่มี `Z` ต่อท้าย และ browser ตีความเวลาหมดอายุผิด |
| การสร้างฐานข้อมูล | `DbProvisioner` สร้างฐานข้อมูลเองก่อนเรียก migrate | EF สร้างผ่าน connection ที่ใช้ร่วมกันไม่ได้ |
| การรัน (ข้อ 14) | คำสั่งเดียว: `scripts/up.ps1`; container ของแอปอยู่ใน compose profile `app`; ตอน dev ใช้ `scripts/dev-api.ps1` หรือ `.sh` และเชื่อมต่อผ่าน `127.0.0.1` | ดู `README.md` |
| การทดสอบ (ข้อ 15) | เพิ่ม unit test: `backend/tests/Shop.UnitTests` (ไม่ต้องใช้ Docker) และ Vitest กับ React Testing Library ใน `frontend` | ผู้ใช้ขอเพิ่มระหว่างทำ |
| pod ของ SQL Server (ข้อ 11.6) | เพิ่ม capability `NET_BIND_SERVICE` กลับเข้าไปหลัง drop ทั้งหมด | ไฟล์ `sqlservr` ต้องการ ไม่เช่นนั้นเริ่มไม่ได้ |
| Kubernetes (ข้อ 13) | deploy และทดสอบจริงแล้ว ยกเว้น Ingress ของ Traefik และการ scale ของ HPA | เครื่องที่ใช้สร้างยังไม่มี Helm, Traefik และ metrics-server ดู `deploy/k8s/README.md` |

**จำนวน test ณ วันที่ส่งมอบ:** backend unit 64, backend integration 83 (SQL Server และ Kafka จริง), frontend unit 58

**ข้อจำกัดที่รู้อยู่**

- สต๊อกกลับคืนหลังยกเลิกช้ากว่าหน้าจอ 1–2 วินาที เพราะเดินทางผ่าน Kafka (eventual consistency)
- event ที่ handler ทำไม่สำเร็จ 5 ครั้งถูกส่งไป `shop.events.dlq` และต้องมีคนนำกลับมาประมวลผลเอง ยังไม่มีเครื่องมือทำให้
- ลำดับของ event ที่มี key เดียวกันไม่รับประกันเมื่อหลาย pod ทำหน้าที่ relay พร้อมกัน ปัจจุบันมี event ชนิดเดียวจึงไม่มีผล
- ตัวนับ rate limit ใน API แยกกันต่อ pod
- การจองสต๊อกตอนใส่ตะกร้าเปิดช่องให้มีคนกันสินค้าไว้โดยไม่ซื้อ สิ่งที่จำกัดความเสียหายคือ rate limit ต่อ IP, จำนวนสูงสุด 99 ชิ้นต่อรายการ และการคืนสต๊อกหลัง 15 นาที ถ้าเป็นปัญหาจริงควรเพิ่มเพดานต่อ IP หรือบังคับล็อกอินก่อนจอง
- ปิดตะกร้าด้วยปุ่ม Esc แล้ว focus ไม่กลับไปที่ปุ่มที่เปิด

