# Shopping Cart & Stock System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** ร้านค้าออนไลน์ที่ลูกค้าเลือกสินค้า ใส่ตะกร้า ชำระเงิน และยกเลิกออเดอร์ได้ โดย stock ถูกต้องเสมอเมื่อรันหลาย replica

**Architecture:** Modular monolith บน .NET 10 แบ่งเป็นโมดูล Catalog / Inventory / Cart / Ordering ที่มี SQL schema และ `DbContext` ของตัวเอง คุยกันผ่าน interface ใน `Shop.Shared` และ outbox event ทุก request ใช้ transaction เดียวผ่าน `UnitOfWork` หน้าบ้านเป็น Next.js ที่อยู่ origin เดียวกับ API หลัง Traefik บน Kubernetes

**Tech Stack:** .NET 10 Minimal API, EF Core (SqlServer), SQL Server 2022, Apache Kafka (KRaft) + `Confluent.Kafka`, xUnit + Testcontainers.MsSql + Testcontainers.Kafka + `WebApplicationFactory`, Next.js (App Router, TypeScript), Tailwind, shadcn/ui, Docker, Kustomize, Traefik

**Spec:** `docs/superpowers/specs/2026-10-06-shopping-cart-design.md` — ผู้ลงมือต้องอ่านคู่กับแผนนี้ เลขข้อที่อ้าง (เช่น "spec 7") หมายถึงหัวข้อใน spec

## Global Constraints

- Target framework `net10.0`; Node 22
- เงินเป็น `decimal(18,2)` สกุลบาท; เวลาเป็น UTC `datetime2`
- ห้ามใช้ `ExecuteSqlRaw`, `FromSqlRaw`, `dangerouslySetInnerHTML`, `innerHTML`, `eval` (spec 11.2, 11.3)
- SQL ที่เขียนเองใช้ `ExecuteSqlInterpolatedAsync` / `FromSqlInterpolated` / `SqlParameter` เท่านั้น
- ห้ามเปิด `EnableRetryOnFailure` (spec 5.3)
- โมดูลอ้างอิงได้เฉพาะ `Shop.Shared`; ห้าม query schema อื่น; ห้าม foreign key ข้าม schema (spec 5.2)
- การเปลี่ยน stock เป็น `UPDATE` มีเงื่อนไขคำสั่งเดียว แล้วตรวจจำนวนแถว (spec 7)
- Pod `api` ไม่เก็บ state ในหน่วยความจำ และไม่รัน migration ตอนเริ่ม
- ข้อความบนหน้าเว็บเป็นภาษาไทย
- Error ทุกตัวตอบเป็น ProblemDetails ที่มี field `code` ตามตารางใน spec 8
- JSON เป็น camelCase; field ที่ไม่รู้จักถูกปฏิเสธ; body ใหญ่สุด 4 KB
- ทุก task จบด้วย commit; ข้อความ commit ลงท้ายด้วย `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`

## Review Focus

สิ่งที่ spec บอกเป็นนัยแต่ตารางทดสอบใน spec 15 ไม่ครอบคลุม แต่ละข้อมี test อยู่ใน task ที่ระบุ

1. Guest คนเดียวเปิดสองแท็บแล้วแก้จำนวนรายการเดียวกันพร้อมกัน → `Reserved` ต้องเท่าผลรวมตะกร้า (Task 5)
2. ตะกร้าหมดอายุแล้วแต่ตัวกวาดยังไม่มา แล้วลูกค้าเพิ่มสินค้าหรือกดชำระเงิน → ของเดิมต้องถูกคืน ไม่ถูกขาย (Task 5, Task 7)
3. Body เป็น JSON เสีย, ใหญ่เกิน 4 KB, หรือ `Content-Type` ไม่ใช่ JSON → 400 ไม่ใช่ 500 (Task 9)
4. ข้อความใน outbox ที่ประมวลผลไม่ได้ → ต้องไม่ขวางข้อความถัดไปตลอดกาล (Task 8)
5. `id` ของออเดอร์ที่ไม่ใช่ GUID หรือ `productId` ที่ไม่มีอยู่ → 404 ไม่ใช่ 500 (Task 5, Task 7)

## File Structure

```
backend/
  Shop.sln
  .config/dotnet-tools.json            dotnet-ef
  src/Shop.Shared/
    Contracts.cs                       ICatalogApi, IInventoryApi, ICartApi, ProductInfo, StockLine, OrderCancelled
    ApiException.cs                    error ที่มี status + code
    UnitOfWork.cs                      connection + transaction ต่อ request, retry 1205
    ModuleDbContext.cs                 base class ที่ผูกกับ UnitOfWork
    SharedDbContext.cs                 OutboxMessages, ProcessedMessages, IdempotencyKeys
    Guest.cs                           cookie sid: สร้าง, ตรวจ, endpoint filter
    Idempotency.cs                     endpoint filter
    Outbox.cs                          IOutbox, OutboxRelay (ตาราง outbox → Kafka)
    EventConsumer.cs                   IEventHandler, EventConsumer (Kafka → handler, DLQ)
    SharedModule.cs                    AddShared()
  src/Shop.Inventory/                  InventoryDbContext, InventoryApi, OrderCancelledHandler, InventoryModule
  src/Shop.Catalog/                    CatalogDbContext, CatalogApi, CatalogModule (GET /api/products)
  src/Shop.Cart/                       CartDbContext, CartService, CartSweeper, CartModule
  src/Shop.Ordering/                   OrderingDbContext, OrderService, OrderingModule
  src/Shop.Api/
    Program.cs                         ประกอบโมดูล, middleware, คำสั่ง migrate
    DbProvisioner.cs                   migrate ทุก context + สร้าง login shop_app
    ErrorHandling.cs                   exception → ProblemDetails
  tests/Shop.IntegrationTests/
    ShopFactory.cs, HttpExtensions.cs  fixture และ helper
    *Tests.cs                          หนึ่งไฟล์ต่อ task
  Dockerfile
frontend/
  src/middleware.ts                    CSP nonce (ชื่อ proxy.ts ถ้าเป็น Next.js 16+)
  src/lib/api.ts, format.ts, messages.ts
  src/components/ui/*                  shadcn
  src/components/*.tsx                 ตาม spec 12.2
  src/app/layout.tsx, page.tsx, orders/[id]/page.tsx
  public/products/tnc-001.svg … tnc-012.svg
  Dockerfile
deploy/k8s/*.yaml, secrets.example.env
docker-compose.yml
README.md
```

## Shared Interfaces

ชื่อและชนิดที่ใช้ข้าม task กำหนดไว้ที่นี่ที่เดียว ทุก task ต้องใช้ตามนี้

```csharp
// Shop.Shared/Contracts.cs
public record StockLine(int ProductId, int Quantity);
public record ProductInfo(int Id, string Sku, string Name, string Description, decimal Price, string ImageUrl);
public record OrderCancelled(Guid OrderId, IReadOnlyList<StockLine> Items);

public interface IInventoryApi {
    Task ReserveAsync(int productId, int quantity, CancellationToken ct);              // ไม่พอ → ApiException 409 out_of_stock + extension "available"
    Task ReleaseAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct);     // 0 แถว → InvalidOperationException
    Task CommitAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct);      // 0 แถว → InvalidOperationException
    Task RestockAsync(IReadOnlyCollection<StockLine> lines, CancellationToken ct);
    Task<IReadOnlyDictionary<int, int>> GetAvailabilityAsync(IReadOnlyCollection<int>? productIds, CancellationToken ct); // null = ทุกสินค้า
}
public interface ICatalogApi {
    Task<IReadOnlyList<ProductInfo>> GetProductsAsync(IReadOnlyCollection<int>? ids, CancellationToken ct);            // null = ทุกสินค้า
}
public interface ICartApi {
    Task<IReadOnlyList<StockLine>> TakeItemsForCheckoutAsync(Guid guestId, CancellationToken ct);                      // ล็อก, อ่าน, ลบตะกร้า
}

// Shop.Shared/ApiException.cs
public sealed class ApiException(int status, string code, string? detail = null,
    IReadOnlyDictionary<string, object?>? extensions = null) : Exception(detail ?? code);

// Shop.Shared/UnitOfWork.cs  (scoped)
public sealed class UnitOfWork {
    public SqlConnection Connection { get; }
    public SqlTransaction? Transaction { get; }
    public void Enlist(DbContext context);
    public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}

// Shop.Shared/Guest.cs
public static class Guest {
    public const string CookieName = "sid";
    public static string CreateCookieValue(Guid guestId, byte[] key);
    public static bool TryParse(string? value, byte[] key, out Guid guestId);
    public static Guid GetGuestId(this HttpContext http);                          // ใช้ได้เฉพาะใต้ RequireGuest()
    public static RouteGroupBuilder RequireGuest(this RouteGroupBuilder group);
}

// Shop.Shared/Idempotency.cs
public static RouteHandlerBuilder RequireIdempotencyKey(this RouteHandlerBuilder builder);

// Shop.Shared/Outbox.cs
public interface IOutbox { void Add<T>(string key, T message) where T : notnull; } // Type = typeof(T).Name; key = Kafka message key
public interface IEventHandler { string EventType { get; } string Name { get; } Task HandleAsync(string payloadJson, CancellationToken ct); }
public sealed class OutboxRelay { public Task<bool> RunOnceAsync(CancellationToken ct); }        // true = ส่งหรือบันทึกความล้มเหลวไปหนึ่งข้อความ
public sealed class EventConsumer { public Task<bool> ConsumeOnceAsync(TimeSpan timeout, CancellationToken ct); } // true = รับและจัดการไปหนึ่งข้อความ

// ทุกโมดูล
public static class XModule {
    public static IServiceCollection AddXModule(this IServiceCollection services);
    public static IEndpointRouteBuilder MapXEndpoints(this IEndpointRouteBuilder app);
}
```

**DTO ของ HTTP** (ตรงกับ spec 8)

```csharp
public record ProductDto(int Id, string Sku, string Name, string Description, decimal Price, string ImageUrl, int Available);
public record CartItemDto(int ProductId, string Sku, string Name, string ImageUrl, decimal UnitPrice, int Quantity, decimal LineTotal, int Available);
public record CartDto(IReadOnlyList<CartItemDto> Items, decimal Total, DateTime? ExpiresAt);
public record AddItemRequest(int ProductId, int Quantity);
public record SetQuantityRequest(int Quantity);
public record OrderItemDto(int ProductId, string Sku, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);
public record OrderDto(Guid Id, string Status, decimal Total, DateTime CreatedAt, DateTime? CancelledAt, IReadOnlyList<OrderItemDto> Items);
```

**ข้อมูล seed** (ใช้ทั้งใน migration และใน test)

| Id | Sku | Name | Price | OnHand |
|---|---|---|---|---|
| 1 | TNC-001 | หูฟังไร้สาย AirBeat Pro | 2990 | 25 |
| 2 | TNC-002 | คีย์บอร์ด Mechanical K87 | 3490 | 12 |
| 3 | TNC-003 | เมาส์ไร้สาย Glide M2 | 1290 | 40 |
| 4 | TNC-004 | ลำโพงบลูทูธ BoomMini | 1890 | 3 |
| 5 | TNC-005 | สมาร์ทวอทช์ Pulse S | 4990 | 8 |
| 6 | TNC-006 | พาวเวอร์แบงค์ 20000mAh | 990 | 60 |
| 7 | TNC-007 | เว็บแคม Full HD | 1590 | 1 |
| 8 | TNC-008 | แท่นวางโน้ตบุ๊กอะลูมิเนียม | 790 | 30 |
| 9 | TNC-009 | สายชาร์จ USB-C 100W | 390 | 100 |
| 10 | TNC-010 | จอมอนิเตอร์ 27 นิ้ว 4K | 11900 | 5 |
| 11 | TNC-011 | ไมโครโฟน USB StudioCast | 2490 | 0 |
| 12 | TNC-012 | กระเป๋าโน้ตบุ๊ก 15.6 นิ้ว | 1190 | 2 |

`ImageUrl` = `/products/{sku ตัวพิมพ์เล็ก}.svg`; `Description` เป็นประโยคไทยสั้น ๆ หนึ่งประโยคต่อสินค้า; `Reserved` เริ่มที่ 0

**คำสั่งทดสอบ** (รันจาก `backend/`, ต้องเปิด Docker): `dotnet test --filter "FullyQualifiedName~<ชื่อคลาส>"`

---

### Task 1: โครง backend, UnitOfWork, และ fixture ของ test

**Files:**
- Create: `backend/Shop.sln`, project ทั้ง 7 ตัวตาม File Structure, `backend/.config/dotnet-tools.json`
- Create: `Shop.Shared/{ApiException,UnitOfWork,ModuleDbContext,SharedDbContext,SharedModule}.cs`
- Create: `Shop.Api/{Program,DbProvisioner,ErrorHandling}.cs`, `appsettings.json`, `appsettings.Development.json`
- Create: `tests/Shop.IntegrationTests/{ShopFactory,HttpExtensions,InfrastructureTests}.cs`
- Create: `docker-compose.yml` (เฉพาะ service `mssql`: image `mcr.microsoft.com/mssql/server:2022-latest`, port 1433, volume, `MSSQL_SA_PASSWORD` จาก `.env`)

**Interfaces:**
- Produces: `ApiException`, `UnitOfWork`, `ModuleDbContext`, `SharedDbContext`, `ShopFactory`, `HttpExtensions` ตามด้านล่างและ Shared Interfaces

```csharp
// ModuleDbContext: constructor รับ (DbContextOptions, UnitOfWork) แล้วเรียก uow.Enlist(this)
// ลงทะเบียน: AddDbContext<T>((sp, o) => o.UseSqlServer(sp.GetRequiredService<UnitOfWork>().Connection,
//     x => x.MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")))

// ShopFactory : WebApplicationFactory<Program>, IAsyncLifetime   — ใช้ร่วมผ่าน [Collection("shop")]
HttpClient CreateGuest();                       // BaseAddress https://localhost, เก็บ cookie
Task ResetAsync();                              // ลบ cart/ordering/shared ทั้งหมด, คืน Stock เป็นค่า seed
Task<(int OnHand, int Reserved)> StockAsync(int productId);
Task<T> ScalarAsync<T>(FormattableString sql);
Task ExecAsync(FormattableString sql);
Task AssertInvariantsAsync();                   // spec 15: Reserved = ผลรวม CartItems; OnHand = seed − ผลรวมออเดอร์ Paid
T Service<T>() where T : notnull;               // จาก scope ใหม่ (ผู้เรียก dispose เอง)

// HttpExtensions
Task<HttpResponseMessage> PostKeyedAsync(this HttpClient c, string url, object? body = null, string? key = null); // key null = GUID ใหม่
Task<string> CodeAsync(this HttpResponseMessage r);   // อ่าน field "code" จาก ProblemDetails
```

**พฤติกรรมที่ต้องตรงตามนี้**

- `UnitOfWork.ExecuteAsync`: เปิด connection, ส่ง `SET LOCK_TIMEOUT 5000`, เริ่ม transaction (`READ COMMITTED`), เรียก `Database.UseTransaction` กับทุก context ที่ enlist แล้ว (และที่ enlist ทีหลัง), รัน `work`, commit
  - เรียกซ้อนขณะมี transaction อยู่ → รัน `work` ตรง ๆ ใน transaction เดิม
  - `SqlException.Number == 1205` → rollback, `ChangeTracker.Clear()` ทุก context, ทำใหม่ สูงสุด 3 ครั้ง
  - `SqlException.Number == 1222` → `ApiException(409, "request_in_progress")`
- `ErrorHandling`: `ApiException` → status และ `code` ของมัน พร้อม extensions; `BadHttpRequestException` และ `JsonException` → 400 `validation_failed`; อื่น ๆ → 500 `internal_error` ไม่มี stack trace
- `Program`: argument `migrate` → เรียก `DbProvisioner.RunAsync` แล้วจบ; นอกนั้นรันเว็บ
  - `GET /healthz/live` → 200 เสมอ; `GET /healthz/ready` → 200 เมื่อ `SELECT 1` ผ่าน ไม่เช่นนั้น 503
  - JSON: `UnmappedMemberHandling = Disallow`; Kestrel `MaxRequestBodySize = 4096`
  - `public partial class Program;` ท้ายไฟล์
- `DbProvisioner.RunAsync`: `Database.MigrateAsync()` ทุก context; ถ้ามี env `APP_DB_PASSWORD` สร้าง login และ user `shop_app` ให้สิทธิ์ `SELECT, INSERT, UPDATE, DELETE` บน schema `catalog, inventory, cart, ordering, shared`
  - `CREATE LOGIN` รับ parameter ไม่ได้ จุดนี้เป็นที่เดียวที่ประกอบ SQL เป็น string: ใช้ `SqlCommand` ตรง ๆ, escape `'` เป็น `''`, และ comment ว่าค่ามาจาก secret ของผู้ดูแลระบบ ไม่ใช่จากผู้ใช้
- `ShopFactory`: เริ่ม `MsSqlContainer`, ตั้ง `ConnectionStrings:Shop`, `CART_SIGNING_KEY` (32 ไบต์ base64), `BackgroundJobs:Enabled=false`, `RateLimit:CartPerMinute=100000`, `RateLimit:CheckoutPerMinute=100000` แล้วเรียก `DbProvisioner.RunAsync`

- [ ] **Step 1:** สร้าง solution, project, reference (โมดูล → `Shop.Shared`; `Shop.Api` → ทุกโมดูล; test → `Shop.Api`), ติดตั้ง `dotnet-ef` เป็น local tool, package: `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Design`, `Testcontainers.MsSql`, `Microsoft.AspNetCore.Mvc.Testing`
- [ ] **Step 2: เขียน test ที่ยังไม่ผ่าน** ใน `InfrastructureTests.cs`

```csharp
[Fact] public async Task Ready_returns_200_when_database_is_up() =>
    Assert.Equal(HttpStatusCode.OK, (await f.CreateGuest().GetAsync("/healthz/ready")).StatusCode);

[Fact] public async Task UnitOfWork_rolls_back_when_work_throws() {
    using var scope = f.Services.CreateScope();
    var uow = scope.ServiceProvider.GetRequiredService<UnitOfWork>();
    var db = scope.ServiceProvider.GetRequiredService<SharedDbContext>();
    await Assert.ThrowsAsync<ApiException>(() => uow.ExecuteAsync<int>(async ct => {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT shared.ProcessedMessages (MessageId, Handler) VALUES ({Guid.NewGuid()}, {"t"})", ct);
        throw new ApiException(400, "x");
    }, default));
    Assert.Equal(0, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM shared.ProcessedMessages"));
}

[Fact] public async Task Unknown_route_error_is_problem_details_without_stack_trace() {
    var r = await f.CreateGuest().GetAsync("/api/does-not-exist");
    Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    Assert.DoesNotContain("   at ", await r.Content.ReadAsStringAsync());
}
```

- [ ] **Step 3:** รัน `dotnet test --filter "FullyQualifiedName~InfrastructureTests"` — ต้อง FAIL (compile error)
- [ ] **Step 4:** เขียนไฟล์ทั้งหมดของ task นี้; `SharedDbContext` มีสามตารางตาม spec 6 (schema `shared`); สร้าง migration: `dotnet ef migrations add Initial -p src/Shop.Shared -s src/Shop.Api -c SharedDbContext`
- [ ] **Step 5:** รัน test เดิม — ต้อง PASS ทั้ง 3
- [ ] **Step 6:** Commit `feat(backend): solution skeleton, unit of work, test fixture`

---

### Task 2: โมดูล Inventory

**Files:**
- Create: `Shop.Shared/Contracts.cs`
- Create: `Shop.Inventory/{InventoryDbContext,InventoryApi,InventoryModule}.cs` + migration
- Modify: `Shop.Api/Program.cs`, `Shop.Api/DbProvisioner.cs` (เพิ่มโมดูล)
- Test: `tests/Shop.IntegrationTests/InventoryTests.cs`

**Interfaces:**
- Consumes: `UnitOfWork`, `ModuleDbContext`, `ApiException`
- Produces: `IInventoryApi` (implementation `InventoryApi`, scoped), `StockLine`, ตาราง `inventory.Stock` พร้อม CHECK สามข้อและ seed ตามตาราง

SQL ทั้งสี่คำสั่งตาม spec 7 ทุก method ที่รับหลายบรรทัดวนตาม `ProductId` จากน้อยไปมาก `ReserveAsync` เมื่อแก้ได้ 0 แถวให้อ่าน `OnHand - Reserved` แล้วโยน `ApiException(409, "out_of_stock", extensions: { ["available"] = n })`

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
// helper ในคลาส: Task Run(Func<IInventoryApi, Task> work) — scope ใหม่, เรียกผ่าน UnitOfWork.ExecuteAsync

[Fact] public async Task Reserve_then_commit_moves_stock() {
    await Run(i => i.ReserveAsync(4, 2, default));
    Assert.Equal((3, 2), await f.StockAsync(4));
    await Run(i => i.CommitAsync([new(4, 2)], default));
    Assert.Equal((1, 0), await f.StockAsync(4));
}
[Fact] public async Task Reserve_more_than_available_throws_out_of_stock_and_changes_nothing() {
    var ex = await Assert.ThrowsAsync<ApiException>(() => Run(i => i.ReserveAsync(7, 2, default)));
    Assert.Equal("out_of_stock", ex.Code);  Assert.Equal(1, ex.Extensions!["available"]);
    Assert.Equal((1, 0), await f.StockAsync(7));
}
[Fact] public async Task Fifty_parallel_reserves_of_five_units_succeed_exactly_five_times() {
    var results = await Task.WhenAll(Enumerable.Range(0, 50).Select(async _ => {
        try { await Run(i => i.ReserveAsync(10, 1, default)); return true; } catch (ApiException) { return false; } }));
    Assert.Equal(5, results.Count(ok => ok));
    Assert.Equal((5, 5), await f.StockAsync(10));
}
[Fact] public async Task Release_more_than_reserved_throws() =>
    await Assert.ThrowsAsync<InvalidOperationException>(() => Run(i => i.ReleaseAsync([new(1, 1)], default)));
[Fact] public async Task Restock_adds_on_hand() {
    await Run(i => i.RestockAsync([new(11, 4)], default));
    Assert.Equal((4, 0), await f.StockAsync(11));
}
[Fact] public async Task Availability_is_on_hand_minus_reserved() {
    await Run(i => i.ReserveAsync(4, 1, default));
    IReadOnlyDictionary<int,int> a = null!;
    await Run(async i => a = await i.GetAvailabilityAsync([4, 11], default));
    Assert.Equal(2, a[4]);  Assert.Equal(0, a[11]);
}
```

`ApiException` ต้องเปิด property `Status`, `Code`, `Extensions`

- [ ] **Step 2:** รัน `--filter "FullyQualifiedName~InventoryTests"` — FAIL
- [ ] **Step 3:** เขียนโค้ดและ migration (`-p src/Shop.Inventory -c InventoryDbContext`); ทำให้ `ShopFactory.ResetAsync` คืน `Stock` เป็นค่า seed
- [ ] **Step 4:** รัน test — PASS ทั้ง 6
- [ ] **Step 5:** Commit `feat(inventory): stock table and atomic reserve/release/commit/restock`

---

### Task 3: โมดูล Catalog และ `GET /api/products`

**Files:**
- Create: `Shop.Catalog/{CatalogDbContext,CatalogApi,CatalogModule}.cs` + migration
- Modify: `Shop.Api/Program.cs`, `DbProvisioner.cs`
- Test: `CatalogTests.cs`

**Interfaces:**
- Consumes: `IInventoryApi.GetAvailabilityAsync`
- Produces: `ICatalogApi`, `ProductInfo`, `ProductDto`; ตาราง `catalog.Products` (unique `Sku`, CHECK `Price >= 0`, CHECK `ImageUrl LIKE '/products/%'`) พร้อม seed 12 แถว

Endpoint เรียงตาม `Id`, ไม่ใช้ `RequireGuest`, ไม่ตั้ง cookie

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
[Fact] public async Task Lists_twelve_products_with_availability() {
    var r = await f.CreateGuest().GetAsync("/api/products");
    var list = await r.Content.ReadFromJsonAsync<List<ProductDto>>();
    Assert.Equal(12, list!.Count);
    Assert.Equal(new ProductDto(4, "TNC-004", "ลำโพงบลูทูธ BoomMini", list[3].Description, 1890m, "/products/tnc-004.svg", 3), list[3]);
    Assert.Equal(0, list.Single(p => p.Id == 11).Available);
    Assert.False(r.Headers.Contains("Set-Cookie"));
}
[Fact] public async Task Image_url_outside_products_folder_is_rejected_by_database() =>
    await Assert.ThrowsAsync<SqlException>(() => f.ExecAsync($"UPDATE catalog.Products SET ImageUrl = {"javascript:alert(1)"} WHERE Id = 1"));
[Fact] public async Task Script_in_product_name_is_returned_as_json_text() {   // spec 15 กรณี 12 ฝั่ง API
    await f.ExecAsync($"UPDATE catalog.Products SET Name = {"<script>alert(1)</script>"} WHERE Id = 1");
    var r = await f.CreateGuest().GetAsync("/api/products");
    Assert.Equal("application/json", r.Content.Headers.ContentType!.MediaType);
    Assert.Equal("<script>alert(1)</script>", (await r.Content.ReadFromJsonAsync<List<ProductDto>>())![0].Name);
}
```

- [ ] **Step 2:** รัน `--filter "FullyQualifiedName~CatalogTests"` — FAIL
- [ ] **Step 3:** เขียนโค้ดและ migration; `ResetAsync` คืน `Products` เป็นค่า seed ด้วย
- [ ] **Step 4:** รัน test — PASS ทั้ง 3
- [ ] **Step 5:** Commit `feat(catalog): products table, seed data, product listing`

---

### Task 4: ตัวตน guest ผ่าน cookie

**Files:**
- Create: `Shop.Shared/Guest.cs`
- Modify: `Shop.Shared/SharedModule.cs` (อ่าน `CART_SIGNING_KEY` เป็น base64; น้อยกว่า 32 ไบต์ → โยน exception ตอนเริ่ม)
- Test: `GuestTests.cs`

**Interfaces:**
- Produces: `Guest` ตาม Shared Interfaces

รูปค่า cookie: `{guestId รูปแบบ "N"}.{base64url(HMACSHA256(key, guestId bytes))}` เทียบด้วย `CryptographicOperations.FixedTimeEquals` คุณสมบัติ cookie ตาม spec 11.1 (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, `MaxAge` 30 วัน) `RequireGuest` ออก cookie ใหม่เมื่อไม่มีหรือลายเซ็นไม่ผ่าน

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน** (ลงทะเบียน endpoint ทดสอบ `GET /api/_test/whoami` ใต้ `RequireGuest()` ผ่าน `WithWebHostBuilder` คืน `GetGuestId()`)

```csharp
static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

[Fact] public void Round_trips() {
    var id = Guid.NewGuid();
    Assert.True(Guest.TryParse(Guest.CreateCookieValue(id, Key), Key, out var parsed));
    Assert.Equal(id, parsed);
}
[Theory] [InlineData(null)] [InlineData("")] [InlineData("abc")] [InlineData("abc.def")]
public void Rejects_malformed(string? v) => Assert.False(Guest.TryParse(v, Key, out _));
[Fact] public void Rejects_tampered_signature_and_wrong_key() {
    var v = Guest.CreateCookieValue(Guid.NewGuid(), Key);
    Assert.False(Guest.TryParse(v[..^1] + (v[^1] == 'A' ? 'B' : 'A'), Key, out _));
    Assert.False(Guest.TryParse(v, RandomNumberGenerator.GetBytes(32), out _));
}
[Fact] public async Task Same_client_keeps_identity_and_cookie_has_security_attributes() {
    var c = Client();
    var first = await c.GetAsync("/api/_test/whoami");
    var cookie = first.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant();
    foreach (var attr in new[] { "sid=", "httponly", "secure", "samesite=strict", "path=/" }) Assert.Contains(attr, cookie);
    Assert.Equal(await first.Content.ReadAsStringAsync(), await (await c.GetAsync("/api/_test/whoami")).Content.ReadAsStringAsync());
}
[Fact] public async Task Tampered_cookie_gets_a_new_identity() {            // spec 15 กรณี 8
    var c = Client(handleCookies: false);
    var forged = Guest.CreateCookieValue(Guid.NewGuid(), RandomNumberGenerator.GetBytes(32));
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/_test/whoami") { Headers = { { "Cookie", $"sid={forged}" } } };
    Assert.True((await c.SendAsync(req)).Headers.Contains("Set-Cookie"));
}
```

- [ ] **Step 2:** รัน `--filter "FullyQualifiedName~GuestTests"` — FAIL
- [ ] **Step 3:** เขียน `Guest.cs`
- [ ] **Step 4:** รัน test — PASS
- [ ] **Step 5:** Commit `feat(shared): signed guest cookie`

---

### Task 5: โมดูล Cart

**Files:**
- Create: `Shop.Cart/{CartDbContext,CartService,CartSweeper,CartModule}.cs` + migration
- Modify: `Shop.Api/Program.cs`, `DbProvisioner.cs`
- Test: `CartTests.cs`

**Interfaces:**
- Consumes: `IInventoryApi`, `ICatalogApi`, `Guest.RequireGuest`, `UnitOfWork`
- Produces: `ICartApi` (implement โดย `CartService`), `CartDto`, `CartItemDto`, `AddItemRequest`, `SetQuantityRequest`

```csharp
public sealed class CartService : ICartApi {
    Task<CartDto> GetAsync(Guid guestId, CancellationToken ct);                              // หมดอายุแล้ว → ตะกร้าว่าง
    Task<CartDto> AddAsync(Guid guestId, int productId, int quantity, CancellationToken ct);  // บวกเข้ากับของเดิม
    Task<CartDto> SetQuantityAsync(Guid guestId, int productId, int quantity, CancellationToken ct); // ไม่มีรายการ → สร้าง
    Task<CartDto> RemoveAsync(Guid guestId, int productId, CancellationToken ct);             // ไม่มีรายการ → ไม่ทำอะไร
    Task<CartDto> ClearAsync(Guid guestId, CancellationToken ct);
}
public sealed class CartSweeper : BackgroundService {   // วนทุก 30 วินาทีเมื่อ BackgroundJobs:Enabled
    public Task<bool> RunOnceAsync(CancellationToken ct);   // true = กวาดไปหนึ่งใบ
}
```

**พฤติกรรมที่ต้องตรงตามนี้**

- ทุก method ที่แก้ตะกร้าทำใน `UnitOfWork.ExecuteAsync` และเริ่มด้วย private `LockCartAsync(guestId)`:
  `SELECT ... FROM cart.Carts WITH (UPDLOCK, HOLDLOCK) WHERE GuestId = …`; ไม่มีแถว → insert; `ExpiresAt` ผ่านไปแล้ว → `ReleaseAsync` ทุกรายการแล้วลบรายการ
- จบทุกการแก้: `ExpiresAt = SYSUTCDATETIME() + Cart:ReservationMinutes` (ค่าเริ่มต้น 15); ตะกร้าที่ไม่เหลือรายการ → ลบแถว `Carts` (`expiresAt` ใน DTO เป็น `null`)
- ตรวจ input: `productId > 0` และมีใน `ICatalogApi` ไม่เช่นนั้น 404 `not_found`; `quantity` 1–99 ไม่เช่นนั้น 400 `validation_failed`; ผลรวมหลังบวกเกิน 99 → 400; รายการที่ 51 → 400
- `CartSweeper.RunOnceAsync`: SQL ตาม spec 7; แล้วลบ `shared.IdempotencyKeys` ที่เก่ากว่า 24 ชั่วโมง
- Endpoint อยู่ใต้ `MapGroup("/api/cart").RequireGuest()` ตามตารางใน spec 8; route `{productId:int}`

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน** (`POST` ใช้ `PostKeyedAsync` ตั้งแต่ตอนนี้ header ยังไม่ถูกตรวจจนถึง Task 6)

```csharp
[Fact] public async Task Add_increase_decrease_remove_clear_keep_reserved_in_sync() {   // spec 15 กรณี 5
    var c = f.CreateGuest();
    var cart = await Cart(await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2)));
    Assert.Equal(5980m, cart.Total);  Assert.Equal(23, cart.Items[0].Available);  Assert.NotNull(cart.ExpiresAt);
    Assert.Equal((25, 2), await f.StockAsync(1));
    await c.PutAsJsonAsync("/api/cart/items/1", new SetQuantityRequest(5));   Assert.Equal((25, 5), await f.StockAsync(1));
    await c.PutAsJsonAsync("/api/cart/items/1", new SetQuantityRequest(1));   Assert.Equal((25, 1), await f.StockAsync(1));
    await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(3, 4));
    await c.DeleteAsync("/api/cart/items/1");                                  Assert.Equal((25, 0), await f.StockAsync(1));
    cart = await Cart(await c.DeleteAsync("/api/cart"));
    Assert.Empty(cart.Items);  Assert.Null(cart.ExpiresAt);  Assert.Equal((40, 0), await f.StockAsync(3));
    await f.AssertInvariantsAsync();
}
[Fact] public async Task Fifty_guests_adding_a_five_unit_product_five_succeed() {       // spec 15 กรณี 1
    var rs = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ =>
        f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(10, 1))));
    Assert.Equal(5, rs.Count(r => r.StatusCode == HttpStatusCode.OK));
    Assert.Equal(45, rs.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    Assert.Equal((5, 5), await f.StockAsync(10));
}
[Fact] public async Task Out_of_stock_returns_409_with_available_and_leaves_cart_unchanged() {
    var c = f.CreateGuest();
    await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(4, 2));
    var r = await c.PutAsJsonAsync("/api/cart/items/4", new SetQuantityRequest(4));
    Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);  Assert.Equal("out_of_stock", await r.CodeAsync());
    Assert.Equal(2, (await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items[0].Quantity);
}
[Theory] [InlineData(0)] [InlineData(-1)] [InlineData(100)]                               // spec 15 กรณี 9
public async Task Invalid_quantity_is_400(int q) =>
    Assert.Equal(HttpStatusCode.BadRequest, (await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, q))).StatusCode);
[Fact] public async Task Non_numeric_quantity_is_400() =>
    Assert.Equal(HttpStatusCode.BadRequest, (await f.CreateGuest().PutAsync("/api/cart/items/1",
        new StringContent("""{"quantity":"abc"}""", Encoding.UTF8, "application/json"))).StatusCode);
[Fact] public async Task Unknown_product_is_404() =>                                      // Review Focus 5
    Assert.Equal(HttpStatusCode.NotFound, (await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(999, 1))).StatusCode);
[Fact] public async Task Sweeper_releases_expired_cart() {                                // spec 15 กรณี 6
    await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 3));
    await f.ExecAsync($"UPDATE cart.Carts SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME())");
    Assert.True(await f.Service<CartSweeper>().RunOnceAsync(default));
    Assert.False(await f.Service<CartSweeper>().RunOnceAsync(default));
    Assert.Equal((25, 0), await f.StockAsync(1));
    Assert.Equal(0, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM cart.Carts"));
}
[Fact] public async Task Adding_to_an_expired_unswept_cart_releases_the_old_items_first() {   // Review Focus 2
    var c = f.CreateGuest();
    await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 3));
    await f.ExecAsync($"UPDATE cart.Carts SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME())");
    Assert.Empty((await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items);
    var cart = await Cart(await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(3, 1)));
    Assert.Equal(3, Assert.Single(cart.Items).ProductId);
    Assert.Equal((25, 0), await f.StockAsync(1));  await f.AssertInvariantsAsync();
}
[Fact] public async Task Concurrent_updates_from_one_guest_keep_reserved_equal_to_cart() {   // Review Focus 1
    var c = f.CreateGuest();
    await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1));
    await Task.WhenAll(Enumerable.Range(1, 20).Select(q => c.PutAsJsonAsync("/api/cart/items/1", new SetQuantityRequest(q))));
    var qty = (await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items[0].Quantity;
    Assert.Equal((25, qty), await f.StockAsync(1));
}
```

- [ ] **Step 2:** รัน `--filter "FullyQualifiedName~CartTests"` — FAIL
- [ ] **Step 3:** เขียนโค้ดและ migration (`cart.Carts`, `cart.CartItems` ตาม spec 6)
- [ ] **Step 4:** รัน test — PASS
- [ ] **Step 5:** Commit `feat(cart): cart endpoints with stock reservation and expiry sweeper`

---

### Task 6: Idempotency key

**Files:**
- Create: `Shop.Shared/Idempotency.cs`
- Modify: `Shop.Cart/CartModule.cs` (ใส่ `.RequireIdempotencyKey()` ที่ `POST /api/cart/items`)
- Test: `IdempotencyTests.cs`

**Interfaces:**
- Consumes: `UnitOfWork`, `SharedDbContext`, `Guest.GetGuestId`
- Produces: `RequireIdempotencyKey()` — handler ที่อยู่ใต้ filter นี้ต้องคืน `TypedResults.Ok(...)` หรือ `TypedResults.Created(...)` (filter อ่าน `IValueHttpResult.Value` และ `IStatusCodeHttpResult.StatusCode`) และแจ้ง error ด้วยการโยน `ApiException`

Filter ทำตาม spec 10.1 ทั้งหมดภายใน `UnitOfWork.ExecuteAsync` ครอบ `next`:

1. Header `Idempotency-Key` ไม่ตรง `^[A-Za-z0-9_-]{16,64}$` → `ApiException(400, "idempotency_key_required")`
2. `RequestHash` = SHA-256 ของ `"{METHOD} {path}\n{body}"` (เปิด `EnableBuffering` เพื่ออ่าน body แล้วกรอกลับ)
3. `INSERT` แถว; `SqlException.Number == 2627` → อ่านแถวเดิม: hash ตรง → `Results.Content(ResponseBody, "application/json", statusCode: StatusCode)`; ไม่ตรง → `ApiException(422, "idempotency_key_reuse")`
4. Insert ผ่าน → เรียก `next`, serialize ค่าเป็น JSON ด้วย option เดียวกับของแอป, `UPDATE` แถว

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
[Fact] public async Task Missing_or_malformed_key_is_400() {
    var c = f.CreateGuest();
    Assert.Equal("idempotency_key_required", await (await c.PostAsJsonAsync("/api/cart/items", new AddItemRequest(1, 1))).CodeAsync());
    Assert.Equal("idempotency_key_required", await (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key: "short")).CodeAsync());
}
[Fact] public async Task Replay_returns_same_response_and_reserves_once() {
    var c = f.CreateGuest();  var key = Guid.NewGuid().ToString();
    var a = await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2), key);
    var b = await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2), key);
    Assert.Equal(await a.Content.ReadAsStringAsync(), await b.Content.ReadAsStringAsync());
    Assert.Equal((25, 2), await f.StockAsync(1));
}
[Fact] public async Task Ten_parallel_requests_with_one_key_reserve_once() {
    var c = f.CreateGuest();  await c.GetAsync("/api/cart");  var key = Guid.NewGuid().ToString();
    var rs = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key)));
    Assert.All(rs, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    Assert.Equal((25, 1), await f.StockAsync(1));
}
[Fact] public async Task Same_key_with_different_body_is_422() {                          // spec 15 กรณี 3
    var c = f.CreateGuest();  var key = Guid.NewGuid().ToString();
    await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key);
    var r = await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2), key);
    Assert.Equal((HttpStatusCode)422, r.StatusCode);  Assert.Equal("idempotency_key_reuse", await r.CodeAsync());
}
[Fact] public async Task Failed_request_does_not_consume_the_key() {
    var c = f.CreateGuest();  var key = Guid.NewGuid().ToString();
    Assert.Equal(HttpStatusCode.Conflict, (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(11, 1), key)).StatusCode);
    await f.ExecAsync($"UPDATE inventory.Stock SET OnHand = 1 WHERE ProductId = 11");
    Assert.Equal(HttpStatusCode.OK, (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(11, 1), key)).StatusCode);
}
[Fact] public async Task Two_guests_can_use_the_same_key() {
    var key = Guid.NewGuid().ToString();
    await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key);
    await f.CreateGuest().PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key);
    Assert.Equal((25, 2), await f.StockAsync(1));
}
```

- [ ] **Step 2:** รัน `--filter "FullyQualifiedName~IdempotencyTests"` — FAIL
- [ ] **Step 3:** เขียน filter และใส่ที่ endpoint
- [ ] **Step 4:** รัน test ของ `IdempotencyTests` และ `CartTests` — PASS ทั้งคู่
- [ ] **Step 5:** Commit `feat(shared): idempotency key filter`

---

### Task 7: โมดูล Ordering — ชำระเงินและดูออเดอร์

**Files:**
- Create: `Shop.Ordering/{OrderingDbContext,OrderService,OrderingModule}.cs` + migration
- Modify: `Shop.Api/Program.cs`, `DbProvisioner.cs`
- Test: `CheckoutTests.cs`

**Interfaces:**
- Consumes: `ICartApi.TakeItemsForCheckoutAsync`, `ICatalogApi.GetProductsAsync`, `IInventoryApi.CommitAsync`, `RequireGuest`, `RequireIdempotencyKey`
- Produces: `OrderDto`, `OrderItemDto`

```csharp
public sealed class OrderService {
    Task<OrderDto> CheckoutAsync(Guid guestId, CancellationToken ct);            // ตาม spec 9.2
    Task<OrderDto> GetAsync(Guid guestId, Guid orderId, CancellationToken ct);   // ไม่พบ หรือไม่ใช่เจ้าของ → ApiException 404 not_found
}
```

- `CartService.TakeItemsForCheckoutAsync`: ใช้ `LockCartAsync` (ตะกร้าหมดอายุจึงถูกคืนและได้รายการว่าง), คืนรายการ, ลบตะกร้า **โดยไม่ Release**
- รายการว่าง → `ApiException(400, "cart_empty")`
- Endpoint ใต้ `MapGroup("/api/orders").RequireGuest()`: `POST /` (`RequireIdempotencyKey`, คืน `TypedResults.Created`), `GET /{id:guid}`

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
[Fact] public async Task Checkout_shows_total_cuts_stock_and_empties_cart() {
    var c = f.CreateGuest();
    await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(4, 2));
    await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(9, 3));
    var r = await c.PostKeyedAsync("/api/orders");
    Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    var o = (await r.Content.ReadFromJsonAsync<OrderDto>())!;
    Assert.Equal("Paid", o.Status);  Assert.Equal(4950m, o.Total);  Assert.Equal(2, o.Items.Count);   // 1890*2 + 390*3
    Assert.Equal((1, 0), await f.StockAsync(4));  Assert.Equal((97, 0), await f.StockAsync(9));
    Assert.Empty((await c.GetFromJsonAsync<CartDto>("/api/cart"))!.Items);
    Assert.Equal(o, await c.GetFromJsonAsync<OrderDto>($"/api/orders/{o.Id}"));   // เทียบ field ทีละตัวถ้า record equality ติด list
    await f.AssertInvariantsAsync();
}
[Fact] public async Task Ten_parallel_checkouts_with_one_key_create_one_order() {         // spec 15 กรณี 2
    var c = f.CreateGuest();  await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2));  var key = Guid.NewGuid().ToString();
    var rs = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => c.PostKeyedAsync("/api/orders", null, key)));
    var bodies = await Task.WhenAll(rs.Select(r => r.Content.ReadAsStringAsync()));
    Assert.Single(bodies.Distinct());
    Assert.Equal(1, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM ordering.Orders"));
    Assert.Equal((23, 0), await f.StockAsync(1));
}
[Fact] public async Task Price_is_read_from_database_at_checkout() {
    var c = f.CreateGuest();  await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(9, 1));
    await f.ExecAsync($"UPDATE catalog.Products SET Price = 500 WHERE Id = 9");
    Assert.Equal(500m, (await (await c.PostKeyedAsync("/api/orders")).Content.ReadFromJsonAsync<OrderDto>())!.Total);
}
[Fact] public async Task Empty_cart_is_400_cart_empty() =>                                // spec 15 กรณี 10
    Assert.Equal("cart_empty", await (await f.CreateGuest().PostKeyedAsync("/api/orders")).CodeAsync());
[Fact] public async Task Expired_unswept_cart_cannot_be_checked_out() {                   // Review Focus 2
    var c = f.CreateGuest();  await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 2));
    await f.ExecAsync($"UPDATE cart.Carts SET ExpiresAt = DATEADD(minute, -1, SYSUTCDATETIME())");
    Assert.Equal("cart_empty", await (await c.PostKeyedAsync("/api/orders")).CodeAsync());
    await f.Service<CartSweeper>().RunOnceAsync(default);
    Assert.Equal((25, 0), await f.StockAsync(1));
}
[Fact] public async Task Another_guests_order_is_404() {                                  // spec 15 กรณี 7
    var a = f.CreateGuest();  await a.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1));
    var o = (await (await a.PostKeyedAsync("/api/orders")).Content.ReadFromJsonAsync<OrderDto>())!;
    Assert.Equal(HttpStatusCode.NotFound, (await f.CreateGuest().GetAsync($"/api/orders/{o.Id}")).StatusCode);
}
[Fact] public async Task Non_guid_order_id_is_404() =>                                    // Review Focus 5
    Assert.Equal(HttpStatusCode.NotFound, (await f.CreateGuest().GetAsync("/api/orders/1;DROP TABLE x")).StatusCode);
```

- [ ] **Step 2:** รัน `--filter "FullyQualifiedName~CheckoutTests"` — FAIL
- [ ] **Step 3:** เขียนโค้ดและ migration (`ordering.Orders`, `ordering.OrderItems` ตาม spec 6)
- [ ] **Step 4:** รัน test — PASS
- [ ] **Step 5:** Commit `feat(ordering): checkout and order lookup`

---

### Task 8: Outbox, Kafka, และการยกเลิกออเดอร์

**Files:**
- Create: `Shop.Shared/Outbox.cs`, `Shop.Shared/EventConsumer.cs`, `Shop.Inventory/OrderCancelledHandler.cs`
- Modify: `Shop.Ordering/{OrderService,OrderingModule}.cs`, `Shop.Inventory/InventoryModule.cs`, `Shop.Shared/SharedModule.cs`, `Shop.Shared/SharedDbContext.cs` (คอลัมน์ `Key` + migration), `tests/.../ShopFactory.cs` (เพิ่ม `KafkaContainer`, ตั้ง `Kafka:BootstrapServers`), `docker-compose.yml` (service `kafka`)
- Test: `CancelTests.cs`

**Interfaces:**
- Consumes: `IInventoryApi.RestockAsync`, `SharedDbContext`, `UnitOfWork`
- Produces: `IOutbox`, `IEventHandler`, `OutboxRelay`, `EventConsumer` (ทั้งสองเป็น singleton และมี hosted service ที่วนเมื่อ `BackgroundJobs:Enabled`; แต่ละรอบสร้าง scope ใหม่), `OrderService.CancelAsync(Guid guestId, Guid orderId, CancellationToken ct) : Task<OrderDto>`, endpoint `POST /api/orders/{id:guid}/cancel` (`RequireIdempotencyKey`)

ค่าคงที่: topic `shop.events`, DLQ `shop.events.dlq`, consumer group `shop-inventory`, header `message-id` และ `type`, config `Kafka:BootstrapServers`

- `IOutbox.Add` เพิ่มแถวลง `SharedDbContext` แล้ว `SaveChanges` ภายใน transaction ปัจจุบัน
- `CancelAsync` ตาม spec 9.3; key ของ event = `orderId`
- `OutboxRelay.RunOnceAsync` และ `EventConsumer.ConsumeOnceAsync` ตาม spec 10.2
  - Producer: `Acks.All`, `EnableIdempotence = true`, `MessageTimeoutMs = 5000`
  - Consumer: `EnableAutoCommit = false`, `AutoOffsetReset.Earliest`; การ insert `ProcessedMessages` ทำใน `EventConsumer` (ไม่ใช่ใน handler) ภายใน transaction เดียวกับ handler
  - สร้าง topic ทั้งสองตอนเริ่ม ถ้ายังไม่มี (3 partition, replication 1)
- `OrderCancelledHandler`: `EventType = "OrderCancelled"`, `Name = "Inventory.Restock"`

**Test ใน `CancelTests.cs`** (helper: `Buy(client, productId, qty)`, `Pump()` = เรียก relay จนได้ false แล้วเรียก consumer จนได้ false)

| Test | ตรวจว่า |
|---|---|
| `Cancel_returns_stock_after_event_is_consumed` | หลัง cancel สถานะ `Cancelled`, stock ยัง `(1,0)`; หลัง `Pump()` เป็น `(3,0)` และ invariant ผ่าน |
| `Double_cancel_writes_one_event_and_restocks_once` (spec 15 กรณี 4) | cancel พร้อมกันสองครั้งได้ 200 ทั้งคู่, outbox มี 1 แถว, หลัง `Pump()` stock `(3,0)` |
| `Redelivered_message_is_ignored` | ตั้ง `ProcessedAt = NULL` แล้ว `Pump()` อีกรอบ stock ยัง `(3,0)` |
| `Another_guest_cannot_cancel` (spec 15 กรณี 7) | 404 และ outbox ว่าง |
| `Unknown_event_type_goes_to_dlq_and_does_not_block` (Review Focus 4) | แทรกแถว outbox `Type = "Unknown"` ก่อน cancel; หลัง `Pump()` stock `(3,0)` และ topic DLQ มีข้อความที่ header `type` = `Unknown` |
| `Relay_records_failed_attempt_when_kafka_is_unreachable` | relay ที่ชี้ไป broker ผิด: `RunOnceAsync` ได้ true, `Attempts` = 1, `ProcessedAt` ยัง NULL, `LastError` ไม่ว่าง |

- [ ] **Step 1:** เขียน test ที่ยังไม่ผ่าน
- [ ] **Step 2:** รัน `--filter "FullyQualifiedName~CancelTests"` — FAIL
- [ ] **Step 3:** เขียนโค้ด
- [ ] **Step 4:** รัน `dotnet test` ทั้งหมด — PASS
- [ ] **Step 5:** Commit `feat: outbox relay to kafka, event consumer, order cancellation with restock`

---

### Task 9: เสริมความปลอดภัยของ API

**Files:**
- Modify: `Shop.Api/Program.cs`, แต่ละ `*Module.cs` (ใส่ rate limit policy)
- Test: `SecurityTests.cs`

**Interfaces:**
- Consumes: endpoint ทั้งหมด
- Produces: rate limit policy ชื่อ `"cart"` และ `"checkout"`

- `AddRateLimiter`: fixed window 1 นาที แบ่งตาม IP ของผู้เรียก; `"cart"` = `RateLimit:CartPerMinute` (ค่าเริ่มต้น 30) ใช้กับ POST/PUT/DELETE ของตะกร้า; `"checkout"` = `RateLimit:CheckoutPerMinute` (ค่าเริ่มต้น 5) ใช้กับ `POST /api/orders` และ cancel; เกิน → 429 ProblemDetails `rate_limited`
- `UseForwardedHeaders` (`XForwardedFor`, `XForwardedProto`) เชื่อเฉพาะเครือข่ายใน `ForwardedHeaders:KnownNetworks` (รายการ CIDR; ว่าง = ไม่เชื่อใคร)
- ทุก response ของ API มี `X-Content-Type-Options: nosniff`
- Request ที่มี body แต่ `Content-Type` ไม่ใช่ `application/json` → 400 `validation_failed`
- ไม่เรียก `AddCors`
- Log เป็น JSON ทาง stdout (`AddJsonConsole`)

- [ ] **Step 1: เขียน test ที่ยังไม่ผ่าน**

```csharp
[Theory]                                                                                  // spec 15 กรณี 11
[InlineData("/api/cart/items/1;DROP TABLE cart.Carts")] [InlineData("/api/cart/items/1' OR 1=1--")]
public async Task Sql_injection_in_path_is_rejected(string path) {
    Assert.Equal(HttpStatusCode.NotFound, (await f.CreateGuest().DeleteAsync(path)).StatusCode);
    Assert.Equal(12, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM inventory.Stock"));
}
[Fact] public async Task Sql_injection_in_body_and_key_is_rejected() {
    var c = f.CreateGuest();
    var body = new StringContent("""{"productId":"1; DROP TABLE catalog.Products","quantity":1}""", Encoding.UTF8, "application/json");
    body.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
    Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/cart/items", body)).StatusCode);
    Assert.Equal("idempotency_key_required", await (await c.PostKeyedAsync("/api/cart/items", new AddItemRequest(1, 1), key: "x' OR '1'='1' -- pad pad")).CodeAsync());
    Assert.Equal(12, await f.ScalarAsync<int>($"SELECT COUNT(*) FROM catalog.Products"));
}
[Theory]                                                                                  // Review Focus 3
[InlineData("{not json", "application/json")] [InlineData("""{"productId":1,"quantity":1,"price":0}""", "application/json")]
[InlineData("""{"productId":1,"quantity":1}""", "text/plain")]
public async Task Bad_bodies_are_400_validation_failed(string body, string contentType) {
    var content = new StringContent(body, Encoding.UTF8, contentType);  content.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
    var r = await f.CreateGuest().PostAsync("/api/cart/items", content);
    Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);  Assert.Equal("validation_failed", await r.CodeAsync());
}
[Fact] public async Task Body_over_4kb_is_rejected_without_500() {
    var content = new StringContent("{\"productId\":1,\"quantity\":1,\"pad\":\"" + new string('a', 5000) + "\"}", Encoding.UTF8, "application/json");
    content.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
    Assert.InRange((int)(await f.CreateGuest().PostAsync("/api/cart/items", content)).StatusCode, 400, 413);
}
[Fact] public async Task Checkout_is_rate_limited() {
    var c = f.WithWebHostBuilder(b => b.UseSetting("RateLimit:CheckoutPerMinute", "2")).CreateClient(new() { BaseAddress = new("https://localhost") });
    for (var i = 0; i < 2; i++) await c.PostKeyedAsync("/api/orders");
    var r = await c.PostKeyedAsync("/api/orders");
    Assert.Equal((HttpStatusCode)429, r.StatusCode);  Assert.Equal("rate_limited", await r.CodeAsync());
}
[Fact] public async Task Responses_carry_nosniff_and_no_cors_headers() {
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/products") { Headers = { { "Origin", "https://evil.example" } } };
    var r = await f.CreateGuest().SendAsync(req);
    Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
    Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
}
[Fact] public void Source_has_no_banned_calls() {                                         // spec 15 ตรวจโค้ดอัตโนมัติ
    var root = FindRepoRoot();   // เดินขึ้นจาก AppContext.BaseDirectory จนเจอโฟลเดอร์ .git
    var hits = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
        .Where(p => (p.EndsWith(".cs") || p.EndsWith(".ts") || p.EndsWith(".tsx")) && !Regex.IsMatch(p, @"[\\/](bin|obj|node_modules|\.next|tests)[\\/]"))
        .Where(p => Regex.IsMatch(File.ReadAllText(p), @"ExecuteSqlRaw|FromSqlRaw|dangerouslySetInnerHTML|\.innerHTML|\beval\("));
    Assert.Empty(hits);
}
```

- [ ] **Step 2:** รัน `--filter "FullyQualifiedName~SecurityTests"` — FAIL บางตัว
- [ ] **Step 3:** เขียนโค้ด
- [ ] **Step 4:** รัน `dotnet test` ทั้งหมด — PASS
- [ ] **Step 5:** Commit `feat(api): rate limiting, input hardening, security headers`

---

### Task 10: Frontend — โครง, CSP, ตารางสินค้า

**Files:**
- Create: โปรเจกต์ `frontend/` ด้วย `npx create-next-app@latest frontend --typescript --tailwind --eslint --app --src-dir --no-import-alias --use-npm`
- Create: `src/middleware.ts` (ถ้า Next.js ที่ติดตั้งเป็นรุ่น 16+ ให้ใช้ชื่อ `src/proxy.ts` และ export `proxy`)
- Create: `src/lib/{api,format,messages}.ts`, `src/components/{product-grid,product-card,stock-badge}.tsx`
- Create: `public/products/tnc-001.svg` … `tnc-012.svg`
- Modify: `next.config.ts`, `src/app/{layout,page}.tsx`, `src/app/globals.css`

**Interfaces:**
- Consumes: `GET /api/products`
- Produces:

```ts
// src/lib/api.ts
export type Product = { id: number; sku: string; name: string; description: string; price: number; imageUrl: string; available: number };
export type CartItem = { productId: number; sku: string; name: string; imageUrl: string; unitPrice: number; quantity: number; lineTotal: number; available: number };
export type Cart = { items: CartItem[]; total: number; expiresAt: string | null };
export type OrderItem = { productId: number; sku: string; name: string; unitPrice: number; quantity: number; lineTotal: number };
export type Order = { id: string; status: "Paid" | "Cancelled"; total: number; createdAt: string; cancelledAt: string | null; items: OrderItem[] };
export class ApiError extends Error { status: number; code: string; available?: number }
export function getProducts(): Promise<Product[]>;                       // ฝั่ง server ใช้ `${process.env.API_ORIGIN}/api/products`, cache: "no-store"
export function getCart(): Promise<Cart>;
export function addItem(productId: number, quantity: number, key: string): Promise<Cart>;
export function setQuantity(productId: number, quantity: number): Promise<Cart>;
export function removeItem(productId: number): Promise<Cart>;
export function clearCart(): Promise<Cart>;
export function checkout(key: string): Promise<Order>;
export function getOrder(id: string): Promise<Order>;
export function cancelOrder(id: string, key: string): Promise<Order>;
// src/lib/format.ts
export function formatBaht(amount: number): string;                      // Intl.NumberFormat("th-TH", { style: "currency", currency: "THB" })
// src/lib/messages.ts
export function messageFor(code: string): string;                        // ตาราง code → ข้อความไทย; ไม่รู้จัก → "เกิดข้อผิดพลาด กรุณาลองใหม่"
```

**พฤติกรรมที่ต้องตรงตามนี้**

- `next.config.ts`: `output: "standalone"`; `rewrites` ส่ง `/api/:path*` ไป `${API_ORIGIN}/api/:path*`; `poweredByHeader: false`
- Middleware: สร้าง nonce ต่อ request, ตั้ง header `Content-Security-Policy` ตามสตริงใน spec 11.3 (โหมด dev เติม `'unsafe-eval'` ใน `script-src`), ส่ง nonce ต่อทาง request header `x-nonce`; `layout.tsx` อ่าน `headers()` จึง render แบบ dynamic
- `layout.tsx`: `<html lang="th">`, ฟอนต์ไทยผ่าน `next/font/google` (`Noto Sans Thai`), metadata ชื่อร้าน "TNC Store"
- `stock-badge.tsx`: `available === 0` → "สินค้าหมด"; `1..5` → "เหลือ N ชิ้น" สีเตือน; มากกว่า 5 → ไม่แสดง
- `product-card.tsx`: รูปผ่าน `next/image` เฉพาะเมื่อ `imageUrl.startsWith("/products/")` ไม่เช่นนั้นใช้ `/products/placeholder.svg`; ปุ่ม "ใส่ตะกร้า" ปิดเมื่อ `available === 0` (ใน task นี้ปุ่มยังไม่ผูกการทำงาน)
- `product-grid.tsx`: 2 คอลัมน์บนมือถือ, 3 ที่ `md`, 4 ที่ `lg`
- รูปสินค้า: SVG 400×400 พื้นไล่สีต่างกันต่อสินค้า มีไอคอนเส้นเรียบง่ายสื่อถึงสินค้า และ `placeholder.svg` อีกหนึ่งไฟล์
- shadcn: `npx shadcn@latest init` แล้ว `add button card badge sheet skeleton sonner alert-dialog`
- แนวทางหน้าตา: พื้นขาวอมเทาอ่อน, สีหลักหนึ่งสีที่ใช้กับปุ่มซื้อเท่านั้น, การ์ดมุมมน เงาบาง ยกขึ้นเมื่อ hover, ราคาเป็นตัวหนาขนาดใหญ่กว่าชื่อ

- [ ] **Step 1:** สร้างโปรเจกต์, ติดตั้ง shadcn, เขียนไฟล์ทั้งหมดของ task นี้
- [ ] **Step 2:** รัน `npx tsc --noEmit` และ `npm run lint` ใน `frontend/` — ไม่มี error
- [ ] **Step 3:** รัน backend (`docker compose up -d mssql`, `dotnet run --project backend/src/Shop.Api -- migrate`, `dotnet run --project backend/src/Shop.Api`) และ `npm run dev` (ตั้ง `API_ORIGIN=http://localhost:5080`) เปิด `http://localhost:3000`
  - เห็นสินค้า 12 รายการ; TNC-011 ขึ้น "สินค้าหมด" และปุ่มปิด; TNC-007 ขึ้น "เหลือ 1 ชิ้น"
  - Console ของ browser ไม่มี CSP violation; response header มี `Content-Security-Policy` ที่ `script-src` มี nonce และไม่มี `unsafe-inline`
  - ที่ความกว้าง 375px ไม่มี scroll แนวนอน
- [ ] **Step 4:** ตรวจ XSS (spec 15 กรณี 12): `UPDATE catalog.Products SET Name = N'<script>alert(1)</script>' WHERE Id = 1` แล้วโหลดหน้าใหม่ — เห็นเป็นตัวอักษร ไม่มี alert; แล้วคืนชื่อเดิม
- [ ] **Step 5:** Commit `feat(frontend): next.js app, CSP, product grid`

---

### Task 11: Frontend — ตะกร้า, ชำระเงิน, หน้าออเดอร์

**Files:**
- Create: `src/components/{cart-provider,cart-button,cart-drawer,cart-line,reservation-timer,checkout-button,order-view}.tsx`
- Create: `src/app/orders/[id]/page.tsx`
- Modify: `src/app/{layout,page}.tsx`, `src/components/{product-card,product-grid}.tsx`

**Interfaces:**
- Consumes: ทุกฟังก์ชันใน `src/lib/api.ts`, `formatBaht`, `messageFor`
- Produces:

```ts
// cart-provider.tsx ("use client")
type CartContext = {
  cart: Cart; products: Product[]; busy: boolean; open: boolean; setOpen(open: boolean): void;
  add(productId: number): Promise<void>; setQuantity(productId: number, quantity: number): Promise<void>;
  remove(productId: number): Promise<void>; clear(): Promise<void>; checkout(): Promise<void>; refresh(): Promise<void>;
};
export function CartProvider(props: { initialProducts: Product[]; children: React.ReactNode }): JSX.Element;
export function useCart(): CartContext;
```

**พฤติกรรมที่ต้องตรงตามนี้** (spec 12.3)

- `CartProvider`: โหลดตะกร้าเมื่อ mount; `refresh()` ดึงทั้งตะกร้าและสินค้า; เรียก `refresh()` ทุก 30 วินาทีและหลังทุกการแก้
- `add`: สร้าง key ด้วย `crypto.randomUUID()` หนึ่ง key ต่อหนึ่งการกด; ถ้า network error ให้ลองใหม่หนึ่งครั้งด้วย key เดิม; สำเร็จ → `setOpen(true)`
- ทุก action: ตั้ง `busy` ระหว่างรอ; จับ `ApiError` แล้ว `toast.error(messageFor(code))` และ `refresh()`
- `checkout`: key เดียวต่อการกด; สำเร็จ → `router.push("/orders/" + order.id)`
- `cart-line.tsx`: ปุ่ม − เรียก `setQuantity(q − 1)` (ที่ `q === 1` เรียก `remove`); ปุ่ม + ปิดเมื่อ `available === 0` หรือ `quantity === 99`; ปุ่มลบมี `aria-label="ลบ {ชื่อสินค้า}"`
- `reservation-timer.tsx`: แสดง "จองสินค้าให้คุณอีก mm:ss" จาก `expiresAt`; ถึงศูนย์ → `refresh()` และ `toast("หมดเวลาจองแล้ว")`; ไม่แสดงเมื่อ `expiresAt` เป็น `null`
- `cart-drawer.tsx`: shadcn `Sheet` ด้านขวา (เต็มจอบนมือถือ); ส่วนรายการ scroll ได้; ส่วนล่างติดอยู่กับที่: ยอดรวม, `CheckoutButton`, ลิงก์ "ล้างตะกร้า" ที่ถามยืนยันด้วย `AlertDialog`; ตะกร้าว่าง → ข้อความ "ตะกร้ายังว่างอยู่ เลือกสินค้าที่ถูกใจได้เลย" และไม่มีปุ่มชำระเงิน; มี region `aria-live="polite"` ประกาศจำนวนชิ้นและยอดรวม
- `checkout-button.tsx`: ข้อความ `ชำระเงิน {formatBaht(total)}`; กว้างเต็ม สูงอย่างน้อย 48px; ปิดเมื่อ `busy`
- `cart-button.tsx`: ลอยมุมขวาบน, ตัวเลขจำนวนชิ้นรวม, `aria-label="เปิดตะกร้า มี N ชิ้น"`
- `orders/[id]/page.tsx` + `order-view.tsx` (client): ดึง `getOrder(id)`; แสดง "ชำระเงินสำเร็จ", "ยอดที่ชำระ {formatBaht(total)}", รายการ, เลขออเดอร์ (8 ตัวแรกของ id ตัวพิมพ์ใหญ่); ปุ่ม "ยกเลิกออเดอร์" (ยืนยันด้วย `AlertDialog`) เมื่อ `status === "Paid"`; สถานะ `Cancelled` → ป้าย "ยกเลิกแล้ว คืนสินค้าเข้าสต๊อกแล้ว" และไม่มีปุ่ม; 404 → "ไม่พบออเดอร์นี้"; ลิงก์ "เลือกซื้อสินค้าต่อ" ไป `/`

- [ ] **Step 1:** เขียนไฟล์ทั้งหมดของ task นี้ และผูกปุ่ม "ใส่ตะกร้า" กับ `add`
- [ ] **Step 2:** รัน `npx tsc --noEmit` และ `npm run lint` — ไม่มี error
- [ ] **Step 3:** ตรวจใน browser ที่ `http://localhost:3000` ทั้ง 375px และ 1280px ตามลำดับนี้ และตรวจตาราง `inventory.Stock` คู่ไปด้วย
  1. กด "ใส่ตะกร้า" ที่ TNC-004 → drawer เปิด, ป้ายเปลี่ยนเป็น "เหลือ 2 ชิ้น", `Reserved` = 1
  2. กด + สองครั้ง → จำนวน 3, ปุ่ม + ปิด, การ์ดขึ้น "สินค้าหมด"
  3. กด − → จำนวน 2, `Reserved` = 2
  4. เพิ่ม TNC-009 แล้วกดลบ → รายการหาย, `Reserved` ของ 9 = 0
  5. "ล้างตะกร้า" → ถามยืนยัน → ตะกร้าว่าง, `Reserved` ทุกแถว = 0
  6. เพิ่ม TNC-004 สองชิ้น → กด "ชำระเงิน ฿3,780.00" → ไปหน้าออเดอร์ที่แสดงยอด ฿3,780.00, `OnHand` ของ 4 = 1
  7. "ยกเลิกออเดอร์" → ยืนยัน → ป้าย "ยกเลิกแล้ว"; ภายใน 3 วินาที `OnHand` ของ 4 = 3
  8. เปิดหน้าต่าง private อีกบาน เพิ่ม TNC-007 (เหลือ 1) ในบานแรก แล้วกดในบานที่สอง → toast "สินค้าเหลือไม่พอ"
  9. ใช้คีย์บอร์ดอย่างเดียว (Tab, Enter, Esc) ทำข้อ 1 และ 6 ได้ครบ
  10. Console ไม่มี error และไม่มี CSP violation
- [ ] **Step 4:** Commit `feat(frontend): cart drawer, checkout, order page`

---

### Task 12: Dockerfile และ docker compose ทั้งชุด

**Files:**
- Create: `backend/Dockerfile`, `backend/.dockerignore`, `frontend/Dockerfile`, `frontend/.dockerignore`, `.env.example`
- Modify: `docker-compose.yml`, `.gitignore`
- Create: `README.md`

- `backend/Dockerfile`: `mcr.microsoft.com/dotnet/sdk:10.0` → `mcr.microsoft.com/dotnet/aspnet:10.0`; `USER $APP_UID`; `ASPNETCORE_HTTP_PORTS=8080`; `ENTRYPOINT ["dotnet", "Shop.Api.dll"]`
- `frontend/Dockerfile`: `node:22-alpine` สาม stage (deps, build, run); คัดลอก `.next/standalone`, `.next/static`, `public`; `USER node`; `PORT=3000`; `CMD ["node", "server.js"]`
- `docker-compose.yml`: service `mssql` (healthcheck ด้วย `sqlcmd`), `migrate` (image api, command `migrate`, รอ mssql healthy), `kafka` (image `apache/kafka`, โหมด KRaft, healthcheck), `api` (รอ `migrate` สำเร็จและ `kafka` healthy, ใช้ login `shop_app`, `Kafka__BootstrapServers=kafka:9092`), `web` (`API_ORIGIN=http://api:8080`, เปิด port `3000:3000`); ค่า secret อ่านจาก `.env`; `api` ตั้ง `ForwardedHeaders__KnownNetworks` เป็น subnet ของ compose
- `.env.example`: `MSSQL_SA_PASSWORD`, `APP_DB_PASSWORD`, `CART_SIGNING_KEY` พร้อมคำสั่งสร้างค่า (`openssl rand -base64 32`); ไม่มีค่าจริง
- `README.md`: สิ่งที่ต้องติดตั้ง, สามโหมดการรันตาม spec 14, วิธีรัน test, ลิงก์ไป spec และแผน

- [ ] **Step 1:** เขียนไฟล์ทั้งหมด
- [ ] **Step 2:** สร้าง `.env` จาก `.env.example` แล้วรัน `docker compose up --build -d` — `docker compose ps` แสดง `mssql`, `api`, `web` เป็น running และ `migrate` exit 0
- [ ] **Step 3:** ตรวจ: `curl -s http://localhost:3000/api/products` ได้ JSON 12 รายการ; `docker compose exec api id -u` ไม่ใช่ 0; `docker compose exec web id -u` ไม่ใช่ 0; ซื้อของครบวงจรผ่าน browser ได้
- [ ] **Step 4:** Commit `build: dockerfiles, compose stack, readme`

---

### Task 13: Kubernetes

**Files:**
- Create: `deploy/k8s/{namespace,mssql,kafka,migrate-job,api,web,ingress,networkpolicy,kustomization}.yaml`, `deploy/k8s/secrets.example.env`
- Modify: `README.md` (หัวข้อ Kubernetes)

ค่าที่ต้องตรงตามนี้ (นอกเหนือจาก spec 11.6, 11.7, 13)

| Resource | ค่า |
|---|---|
| ทุก resource | namespace `shop`; label `app.kubernetes.io/name` |
| Image | `shop-api:local`, `shop-web:local`; `imagePullPolicy: IfNotPresent` |
| `mssql` | `fsGroup: 10001`; PVC 5Gi ที่ `/var/opt/mssql`; requests 1 CPU / 2Gi |
| `kafka` | StatefulSet 1 replica, image `apache/kafka`, KRaft (broker+controller ใน pod เดียว), PVC 2Gi, Service port 9092, advertised listener `kafka.shop.svc:9092`; requests 250m / 512Mi |
| `api` | port 8080; requests 100m / 256Mi, limits 500m / 512Mi; `emptyDir` ที่ `/tmp`; env `ConnectionStrings__Shop` (login `shop_app`), `CART_SIGNING_KEY`, `Kafka__BootstrapServers=kafka.shop.svc:9092`, `ForwardedHeaders__KnownNetworks=10.0.0.0/8` |
| `web` | port 3000; requests 100m / 256Mi, limits 500m / 512Mi; `emptyDir` ที่ `/tmp` และ `/app/.next/cache`; env `API_ORIGIN=http://api.shop.svc:8080` |
| Pod security (`api`, `web`) | `runAsNonRoot: true`, `readOnlyRootFilesystem: true`, `allowPrivilegeEscalation: false`, `capabilities.drop: [ALL]`, `seccompProfile: RuntimeDefault` |
| `migrate-job` | image `shop-api:local`, args `["migrate"]`, ใช้ `sa`, `backoffLimit: 6`, `ttlSecondsAfterFinished: 600` |
| HPA | `api` 3–10 replica, CPU 70% |
| PDB | `api` `minAvailable: 2` |
| Ingress | `ingressClassName: traefik`; host `shop.localhost`; `/api` และ `/healthz` → `api:8080`; `/` → `web:3000`; TLS secret `shop-tls` |
| Traefik `Middleware` (`traefik.io/v1alpha1`) | `ratelimit` (`average: 10`, `period: 1s`, `burst: 100`), `headers` (HSTS 1 ปี, `contentTypeNosniff`, `referrerPolicy: strict-origin-when-cross-origin`; **ไม่ตั้ง CSP**), `redirect-https` |
| Ingress ตัวที่สอง | entrypoint `web` (HTTP) ใช้ `redirect-https` |
| `kustomization.yaml` | `secretGenerator` ชื่อ `shop-secrets` จาก `secrets.env`; `disableNameSuffixHash: true` |

- [ ] **Step 1:** เขียน manifest ทั้งหมด
- [ ] **Step 2:** ตรวจ syntax: `kubectl kustomize deploy/k8s` — ออก YAML ไม่มี error
- [ ] **Step 3:** เตรียม cluster (ต้องเปิด Kubernetes ใน Docker Desktop; ถ้าไม่มี `helm` ให้ติดตั้งก่อน)
  - `helm repo add traefik https://traefik.github.io/charts && helm upgrade --install traefik traefik/traefik -n traefik --create-namespace`
  - ติดตั้ง metrics-server (เพิ่ม `--kubelet-insecure-tls` สำหรับ Docker Desktop)
  - สร้างใบรับรอง self-signed ของ `shop.localhost` ด้วย `openssl` แล้ว `kubectl create secret tls shop-tls -n shop`
  - สร้าง `deploy/k8s/secrets.env` จากไฟล์ตัวอย่าง
- [ ] **Step 4:** Deploy: `docker build -t shop-api:local backend`, `docker build -t shop-web:local frontend`, `kubectl apply -k deploy/k8s`
- [ ] **Step 5:** ตรวจผล
  - `kubectl -n shop wait --for=condition=complete job/migrate --timeout=300s` สำเร็จ
  - `kubectl -n shop get pods` — `api` 3/3 Ready, `web` 2/2 Ready, `mssql` 1/1 Ready
  - `curl -k https://shop.localhost/api/products` ได้ 12 รายการ; `curl -I http://shop.localhost` ได้ redirect ไป HTTPS
  - Response header ของ `https://shop.localhost` มี CSP ชุดเดียว, HSTS, `nosniff` (spec 15 กรณี 13)
  - ซื้อและยกเลิกครบวงจรผ่าน browser ที่ `https://shop.localhost`
  - `kubectl -n shop delete pod -l app.kubernetes.io/name=api --field-selector status.phase=Running --wait=false` ทีละตัวระหว่างใช้งาน — หน้าเว็บยังใช้ได้
  - `kubectl -n shop get hpa` แสดงค่า CPU เป็นตัวเลข ไม่ใช่ `<unknown>`
- [ ] **Step 6:** บันทึกใน `README.md`: ขั้นตอนข้างบน และข้อจำกัดของ Docker Desktop จาก spec 13
- [ ] **Step 7:** Commit `feat(deploy): kubernetes manifests with traefik ingress, hpa, network policy`

---

## Spec Coverage

| Spec | Task |
|---|---|
| 5 สถาปัตยกรรม, 5.3 transaction | 1 |
| 6 โมเดลข้อมูล | 1 (shared), 2, 3, 5, 7 |
| 7 กฎ stock, ตัวกวาด | 2, 5 |
| 8 REST API | 3, 5, 7, 8 |
| 9 flow หลัก | 5, 7, 8 |
| 10 idempotency, outbox | 6, 8 |
| 11.1 ตัวตน | 4 |
| 11.2 SQL injection | 9 (test), Global Constraints |
| 11.3 XSS | 3, 10 |
| 11.4–11.5 ฐานข้อมูล, ขอบระบบ | 1, 9, 13 |
| 11.6–11.7 container, NetworkPolicy | 12, 13 |
| 12 frontend | 10, 11 |
| 13 Kubernetes | 13 |
| 14 การรัน | 12, 13 |
| 15 ทดสอบ กรณี 1–13 | 5, 7, 6, 8, 5, 5, 7+8, 4, 5, 7, 9, 3+10, 13 |
