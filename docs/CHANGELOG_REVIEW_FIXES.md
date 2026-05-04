# Review Fixes Changelog

Bản này sửa trực tiếp các vấn đề nghiêm trọng được review từ bản MVP trước.

## 1. Admin/dashboard/debug auth

Thêm:

```txt
Options/AdminAuthOptions.cs
Infrastructure/Security/AdminAuthMiddleware.cs
```

Bảo vệ:

```txt
/v1/admin/*
/v1/dashboard/*
/v1/debug/*
```

Dev key docker compose:

```txt
change-me-admin-key
```

## 2. Atomic quota/rate-limit

Sửa:

```txt
Infrastructure/Redis/RedisRateLimitStore.cs
Application/AiRouteSelector.cs
Application/AiGatewayService.cs
```

Bỏ TOCTOU `HasAccountQuotaAsync -> ReserveAccountUsageAsync`.

Thay bằng:

```txt
TryReserveAccountUsageAsync = Lua atomic check-and-reserve
CheckAndReserveClientAsync  = Lua atomic check-and-reserve
```

## 3. Token reserve/adjust

Thêm:

```txt
Application/TokenEstimator.cs
```

Flow mới:

```txt
reserve conservative tokens before call
adjust by actual usage after success
```

## 4. Config cache

Thêm:

```txt
Application/AiConfigService.cs
Infrastructure/Redis/RedisConfigCache.cs
```

Hot path đọc Redis cache trước, DB fallback.

Admin update sẽ invalidate cache.

## 5. Error events cap

Thêm:

```txt
Application/ErrorRecordingService.cs
```

`ai_error_aggregates` luôn update. `ai_error_events` bị cap cho lỗi noisy.

## 6. RouteCode

Sửa:

```txt
migrations/001_init.sql
migrations/002_upgrade_from_previous_mvp.sql
Domain/ConfigModels.cs
Contracts/AdminDtos.cs
Infrastructure/Database/AiConfigRepository.cs
```

Unique mới:

```txt
model_id + partner_id + route_code
```

## Chưa sửa trong bản này

Dashboard health vẫn là bản đơn giản, có thể N+1 nếu có rất nhiều partner/account. Vì dashboard chỉ admin dùng và refresh thủ công, để phase sau.
