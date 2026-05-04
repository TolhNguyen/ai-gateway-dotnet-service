# AI Gateway Service

AI Gateway Service là một service độc lập để các service khác gọi AI bằng contract đơn giản:

```json
{
  "model": "text-fast",
  "systemPrompt": "Bạn là trợ lý viết nội dung bán hàng.",
  "prompt": "Viết mô tả sản phẩm áo thun nam cotton."
}
```

Các service caller không cần biết token, account, đối tác, endpoint, body JSON hay response JSON của từng nhà cung cấp AI. AI Gateway chịu trách nhiệm chọn route, gọi provider, fallback, ghi metric và expose dashboard.

## Stack

- ASP.NET Core Web API `.NET 8`
- PostgreSQL làm database chính cho config, route, error events, metric aggregate
- Redis làm runtime store cho rate limit, quota, cooldown, inflight, metric buffer
- Dashboard HTML/CSS/JS thuần trong `wwwroot/dashboard`
- Dapper + Npgsql cho database access
- StackExchange.Redis cho Redis

## Vì sao dùng cả PostgreSQL và Redis?

PostgreSQL là nguồn dữ liệu chuẩn:

- Client/service caller
- Model alias nội bộ
- Partner AI
- Account/API key đã mã hóa
- Model route
- Error events trong 2 tháng
- Error aggregate
- Hourly metric aggregate

Redis là runtime tốc độ cao:

- Client rate limit bằng Lua atomic
- Account quota bằng Lua atomic
- Token quota reserve trước call và adjust lại theo actual usage nếu provider trả usage
- Config cache cho client/model/route candidates để giảm DB read trên hot path
- Cooldown khi gặp 429/quota
- Inflight counter
- Metric buffer trước khi flush xuống DB
- Error event cap để tránh DB bị flood khi quota/rate-limit lỗi hàng loạt

## Error events có cần lưu DB không?

Có, nhưng chỉ lưu lỗi, không lưu toàn bộ request success.

Lý do:

- Khi account/provider lỗi, cần xem lại lỗi gần đây để debug.
- Dashboard lỗi cần có lịch sử 2 tháng.
- Error aggregate chỉ cho biết số lượng/lần cuối, còn error event giúp trace request cụ thể.
- Không nên lưu prompt/response full để tránh rò dữ liệu nhạy cảm.

Source này lưu:

- `ai_error_aggregates`: tổng hợp lỗi theo client/model/partner/account/error type.
- `ai_error_events`: từng lỗi gần đây, retention mặc định 62 ngày. Lỗi noisy như `rate_limit`/`quota_exceeded` được cap theo account/error/hour để không flood DB.

## Quick start bằng Docker Compose

```bash
docker compose up --build
```

Nếu bạn đã chạy bản cũ trước đó và volume Postgres còn schema cũ, có 2 cách:

```bash
# Cách nhanh cho dev: reset volume
docker compose down -v
docker compose up --build
```

Hoặc chạy migration nâng cấp thủ công:

```bash
psql < migrations/002_upgrade_from_previous_mvp.sql
```

Sau khi chạy:

```txt
API:       http://localhost:8080
Dashboard: http://localhost:8080/dashboard/index.html
Postgres:  localhost:5432
Redis:     localhost:6379
```

Health check:

```bash
curl http://localhost:8080/health
curl -H "X-Admin-Key: change-me-admin-key" http://localhost:8080/v1/debug/db/ping
curl -H "X-Admin-Key: change-me-admin-key" http://localhost:8080/v1/debug/redis/ping
```

Admin/dashboard/debug endpoints mặc định được bảo vệ bằng admin key. Docker compose dùng key dev:

```txt
change-me-admin-key
```

Dashboard: mở `http://localhost:8080/dashboard/index.html`, nhập key trên khi trình duyệt hỏi. Production phải đổi `AdminAuth__ApiKeyHash`.

## Tạo config mẫu

Tạo model:

```bash
curl -X POST http://localhost:8080/v1/admin/models \
  -H "Content-Type: application/json" \
  -H "X-Admin-Key: change-me-admin-key" \
  -d '{
    "code":"text-fast",
    "name":"Fast Text Model",
    "defaultTemperature":0.7,
    "defaultMaxTokens":1000,
    "maxRetry":3
  }'
```

Tạo Gemini partner:

```bash
curl -X POST http://localhost:8080/v1/admin/partners \
  -H "Content-Type: application/json" \
  -H "X-Admin-Key: change-me-admin-key" \
  -d '{
    "code":"gemini",
    "name":"Google Gemini",
    "adapterCode":"gemini",
    "baseUrl":"https://generativelanguage.googleapis.com",
    "weight":100,
    "qualityScore":85
  }'
```

Tạo Gemini account. API key sẽ được mã hóa bằng AES-GCM trước khi lưu DB:

```bash
curl -X POST http://localhost:8080/v1/admin/partners/gemini/accounts \
  -H "Content-Type: application/json" \
  -H "X-Admin-Key: change-me-admin-key" \
  -d '{
    "code":"gemini_acc_01",
    "name":"Gemini Account 01",
    "apiKey":"REPLACE_WITH_REAL_KEY",
    "rpmLimit":15,
    "rpdLimit":1500,
    "weight":100
  }'
```

Tạo route model -> partner:

```bash
curl -X POST http://localhost:8080/v1/admin/models/text-fast/routes \
  -H "Content-Type: application/json" \
  -H "X-Admin-Key: change-me-admin-key" \
  -d '{
    "partnerCode":"gemini",
    "routeCode":"default",
    "providerModel":"gemini-1.5-flash",
    "timeoutMs":30000,
    "weight":100
  }'
```

Gọi AI:

```bash
curl -X POST http://localhost:8080/v1/ai/generate \
  -H "Content-Type: application/json" \
  -H "X-AI-Client: product-service" \
  -d '{
    "model":"text-fast",
    "systemPrompt":"Bạn là trợ lý viết nội dung bán hàng.",
    "prompt":"Viết mô tả sản phẩm áo thun nam cotton, phong cách trẻ trung.",
    "debug":true,
    "clientCode":"product-service",
    "featureCode":"product_description"
  }'
```

## Các endpoint chính

AI:

```txt
POST /v1/ai/generate
```

Admin config:

```txt
GET  /v1/admin/clients
POST /v1/admin/clients
PATCH /v1/admin/clients/{clientCode}/status

GET  /v1/admin/models
POST /v1/admin/models
PATCH /v1/admin/models/{modelCode}/status

GET  /v1/admin/partners
POST /v1/admin/partners
PATCH /v1/admin/partners/{partnerCode}/status

GET  /v1/admin/partners/{partnerCode}/accounts
POST /v1/admin/partners/{partnerCode}/accounts
PATCH /v1/admin/accounts/{accountCode}/status

GET  /v1/admin/models/{modelCode}/routes
POST /v1/admin/models/{modelCode}/routes
```

Dashboard:

```txt
GET /v1/dashboard/overview
GET /v1/dashboard/models
GET /v1/dashboard/partners
GET /v1/dashboard/accounts
GET /v1/dashboard/clients
GET /v1/dashboard/errors
GET /v1/dashboard/health
```

Debug:

```txt
GET /health
GET /v1/debug/db/ping
GET /v1/debug/redis/ping
```

## Ghi chú bảo mật

- Không lưu raw API key trong DB.
- `api_key_enc` được mã hóa bằng AES-256-GCM.
- Thay `AiGateway__EncryptionKeyBase64` trong production bằng key thật 32 bytes base64.
- `/v1/admin/*`, `/v1/dashboard/*`, `/v1/debug/*` được bảo vệ bằng `X-Admin-Key` hoặc `Authorization: Bearer`.
- Mặc định `AiGateway__RequireClientAuth=false` để demo dễ. Production nên bật `true`.
- Không log full prompt/response mặc định.

## Cấu trúc repo

```txt
src/AiGateway.Api/
  Controllers/             API controllers
  Contracts/               DTO request/response
  Domain/                  Config model nội bộ
  Application/             Service chính, route selector, client auth
  Infrastructure/Database/ PostgreSQL repositories
  Infrastructure/Redis/    Redis runtime stores
  Infrastructure/Partners/ Provider clients/adapters
  Infrastructure/Security/ Secret encryption
  Workers/                 Metric flush, cleanup
  wwwroot/dashboard/       HTML/CSS/JS dashboard

migrations/001_init.sql    PostgreSQL schema
examples/                  Seed SQL và HTTP examples
docs/                      Tài liệu chi tiết
Dockerfile
docker-compose.yml
```

## Trạng thái source

Repo này là bản MVP đã được nâng cấp sau review production-risk:

- Admin/dashboard/debug auth.
- Redis Lua atomic quota/rate-limit.
- Redis config cache cho hot path.
- Token quota reserve + actual usage adjustment.
- Error events có cap.
- `routeCode` cho nhiều route cùng model + partner.

Môi trường tạo artifact hiện tại không có .NET SDK nên chưa build được trực tiếp tại đây. Source được thiết kế để build bằng Dockerfile hoặc máy có .NET 8 SDK.
