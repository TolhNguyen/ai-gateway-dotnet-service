# AI Agent Handoff Document

Tài liệu này dành cho một AI Agent khác đọc để hiểu repo nhanh.

## Source này là gì?

Đây là source MVP của một **AI Gateway Service độc lập**. Các service khác chỉ cần gọi:

```txt
POST /v1/ai/generate
model + systemPrompt + prompt
```

Gateway tự xử lý provider/account/token/endpoint/body/response/fallback/metric.

## Quyết định kiến trúc

1. PostgreSQL là source of truth cho config, route, error aggregate, error events, metric aggregate.
2. Redis là runtime store cho config cache, rate limit, quota, cooldown, inflight, metric buffer.
3. Không lưu toàn bộ success request xuống DB.
4. Error events có lưu DB nhưng có cap cho lỗi noisy như `rate_limit`/`quota_exceeded`.
5. Không có audit logs vì chủ repo hiện chỉ có một dev/admin.
6. Dashboard dùng HTML/CSS/JS thuần.
7. Provider-specific request/response được xử lý bằng adapter/client riêng.

## Những vấn đề production-risk đã được xử lý

### Admin/dashboard/debug auth

Các path sau được bảo vệ bằng `X-Admin-Key` hoặc `Authorization: Bearer`:

```txt
/v1/admin/*
/v1/dashboard/*
/v1/debug/*
```

Cấu hình nằm trong `AdminAuth`.

### Atomic quota/rate-limit

`RedisRateLimitStore` dùng Lua script cho:

```txt
client RPM/RPD
account RPM/RPD/TPM/TPD
```

Không còn check quota ở selector rồi reserve ở service. Flow mới là atomic check-and-reserve trong Redis.

### Token quota reserve/adjust

`TokenEstimator` reserve token bảo thủ trước call:

```txt
estimated input tokens = ceil(charCount / 2)
reserved tokens = estimated input + maxOutputTokens
```

Sau khi provider trả usage thật, `AdjustReservedTokenUsageAsync` điều chỉnh delta.

### Config cache

`AiConfigService` đọc Redis cache trước, DB sau:

```txt
ai:config:client:{clientCode}
ai:config:model:{modelCode}
ai:config:route_candidates:{modelCode}
```

Admin update sẽ invalidate cache tương ứng.

### Error event cap

`ErrorRecordingService` luôn ghi error aggregate, nhưng error events cho `rate_limit`/`quota_exceeded` được cap theo:

```txt
account + errorType + hour
```

Config: `AiGateway:ErrorEventsMaxPerAccountTypeHour`.

### RouteCode

`ai_model_routes` có thêm `route_code`, unique mới là:

```sql
UNIQUE(model_id, partner_id, route_code)
```

Nhờ đó một model + partner có thể có nhiều route/tier/region.

## Files quan trọng

```txt
Program.cs
```

Đăng ký DI, options, PostgreSQL, Redis, auth middleware, partner clients, workers.

```txt
Options/AiGatewayOptions.cs
Options/AdminAuthOptions.cs
```

Typed options.

```txt
Infrastructure/Security/AdminAuthMiddleware.cs
```

Bảo vệ admin/dashboard/debug APIs.

```txt
Application/AiGatewayService.cs
```

Orchestrator chính: auth client, load model, build message, select route, reserve quota, call provider, fallback, metric.

```txt
Application/AiRouteSelector.cs
```

Chọn route/account dựa trên cached config, cooldown, inflight, weighted random. Không check quota tại đây nữa.

```txt
Application/AiConfigService.cs
Infrastructure/Redis/RedisConfigCache.cs
```

Config cache Redis cho hot path.

```txt
Infrastructure/Redis/RedisRateLimitStore.cs
```

Lua atomic client rate limit/account quota, inflight, token adjust.

```txt
Application/TokenEstimator.cs
```

Token reservation heuristic.

```txt
Application/ErrorRecordingService.cs
```

Ghi metric lỗi + error aggregate + sampled/capped error events.

```txt
Infrastructure/Partners/GeminiClient.cs
Infrastructure/Partners/OpenAiCompatibleClient.cs
```

Adapter gọi provider thật, parse success/error, gợi ý cooldown bằng `Retry-After`, error code/status và fallback message detection.

```txt
Infrastructure/Database/AiConfigRepository.cs
```

CRUD config trong PostgreSQL.

```txt
Workers/MetricFlushWorker.cs
```

Flush metric Redis sang `ai_request_metrics_hourly`.

```txt
migrations/001_init.sql
```

Schema PostgreSQL.

```txt
wwwroot/dashboard/
```

Dashboard static. Static HTML public, nhưng API data `/v1/dashboard/*` có admin auth.

## Flow request chính

1. `AiGenerateController.Generate` nhận request.
2. Lấy `X-AI-Client`, `X-AI-Key`.
3. `ClientAuthService` xác thực client nếu `RequireClientAuth=true`; dùng cache qua `AiConfigService`.
4. Load model alias qua cache.
5. Build messages từ `systemPrompt + prompt`.
6. Tính reserved token bằng `TokenEstimator`.
7. `AiRouteSelector` chọn route/account.
8. `RedisRateLimitStore.TryReserveAccountUsageAsync` atomic check-and-reserve quota.
9. Gọi partner client tương ứng.
10. Success: adjust reserved token theo actual usage, ghi metric Redis.
11. Error: ghi metric Redis, update DB aggregate, insert error event có cap.
12. Nếu lỗi limit/quota: set Redis cooldown.
13. Nếu retryable: thử account/provider khác.

## Cách thêm provider mới

1. Tạo file mới trong `Infrastructure/Partners`, ví dụ `ClaudeClient.cs`.
2. Implement `IAiPartnerClient`.
3. Set `AdapterCode => "claude"`.
4. Đăng ký trong `Program.cs`:

```csharp
builder.Services.AddScoped<IAiPartnerClient, ClaudeClient>();
```

5. Tạo partner qua admin API với `adapterCode = "claude"`.

## Cách thêm route mới

```json
{
  "partnerCode": "gemini",
  "routeCode": "default",
  "providerModel": "gemini-1.5-flash",
  "timeoutMs": 30000,
  "weight": 100,
  "priority": 1
}
```

Nếu cần thêm route thứ hai cùng model + partner:

```json
{
  "partnerCode": "gemini",
  "routeCode": "pro-tier",
  "providerModel": "gemini-1.5-pro",
  "timeoutMs": 45000,
  "weight": 50,
  "priority": 2
}
```

## Những điểm còn có thể nâng cấp sau

1. Dashboard health hiện vẫn đơn giản, có thể tối ưu N+1 nếu account nhiều.
2. Circuit breaker full state `closed/open/half_open`.
3. Prompt template API dựa trên bảng `ai_prompt_templates`.
4. Response cache theo hash prompt.
5. JSON response format/schema validation.
6. OpenAI-compatible facade `/v1/chat/completions`.
7. Streaming response.
8. Tokenizer chính xác theo provider/model.

## Cảnh báo production

- Đổi `AdminAuth__ApiKeyHash`; dev key hiện là `change-me-admin-key`.
- Đổi `AiGateway__EncryptionKeyBase64` bằng key thật 32 bytes base64.
- Bật `AiGateway__RequireClientAuth=true` nếu service có nhiều caller/tenant.
- Không log full prompt/response nếu chưa có masking.
- Nếu đã có DB cũ từ bản trước, cần migrate thêm `route_code` cho `ai_model_routes` hoặc reset DB dev.
