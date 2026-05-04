# Architecture

AI Gateway Service là service độc lập để nhiều service khác gọi AI qua một API thống nhất.

```txt
Caller Service
  -> POST /v1/ai/generate
  -> AI Gateway
       -> Client auth
       -> Config cache Redis / PostgreSQL
       -> Route selector
       -> Atomic quota reserve Redis Lua
       -> Partner adapter
       -> Metrics/error recording
  -> Gemini/Groq/OpenRouter/...
```

## Control plane

PostgreSQL lưu config chuẩn:

```txt
ai_clients
ai_models
ai_partners
ai_accounts
ai_model_routes
ai_prompt_templates
```

Admin API thay đổi config và invalidate Redis config cache.

## Data plane

Runtime request dùng Redis nhiều nhất:

```txt
config cache
client rate-limit
account quota
cooldown
inflight
metric buffer
error event cap
```

## Hot path

Hot path không đọc DB trực tiếp nếu cache còn hạn:

```txt
ClientAuthService -> AiConfigService -> Redis cache -> DB fallback
AiGatewayService  -> AiConfigService -> Redis cache -> DB fallback
AiRouteSelector   -> AiConfigService -> Redis cache -> DB fallback
```

## Quota correctness

Quota dùng Lua atomic trong `RedisRateLimitStore` để tránh TOCTOU.

Không còn pattern:

```txt
check quota
reserve quota sau
```

Thay bằng:

```txt
TryReserveAccountUsageAsync = atomic check + reserve
```

## Token quota

Trước provider call, gateway reserve token bảo thủ. Sau success, nếu provider trả actual usage, gateway adjust delta.

## Error storage

```txt
Success request: Redis metric buffer only
Error request: Redis metric + DB aggregate + capped DB event
```

Không lưu full prompt/response mặc định.

## Dashboard

Dashboard dùng HTML/CSS/JS thuần. Static dashboard public nhưng data API `/v1/dashboard/*` được bảo vệ bằng admin auth.
