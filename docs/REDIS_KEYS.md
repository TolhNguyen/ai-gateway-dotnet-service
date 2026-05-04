# Redis Keys

Redis là runtime store, không phải source of truth cho config. Config gốc nằm PostgreSQL, Redis chỉ cache và lưu counter tạm thời.

## Config cache

```txt
ai:config:client:{clientCode}
ai:config:model:{modelCode}
ai:config:route_candidates:{modelCode}
ai:config:index:route_candidates
```

TTL mặc định: `AiGateway:ConfigCacheSeconds`.

Admin update config sẽ invalidate cache.

## Client rate-limit

Atomic bằng Lua script trong `RedisRateLimitStore`.

```txt
ai:rate:client:{clientCode}:m:{yyyyMMddHHmm}
ai:rate:client:{clientCode}:d:{yyyyMMdd}
```

## Account quota

Atomic check-and-reserve bằng Lua script.

```txt
ai:quota:req:account:{accountCode}:model:{modelCode}:m:{yyyyMMddHHmm}
ai:quota:req:account:{accountCode}:model:{modelCode}:d:{yyyyMMdd}
ai:quota:tok:account:{accountCode}:model:{modelCode}:m:{yyyyMMddHHmm}
ai:quota:tok:account:{accountCode}:model:{modelCode}:d:{yyyyMMdd}
```

Token quota flow:

```txt
reserve conservative tokens before provider call
adjust delta when actual provider usage is available
```

## Cooldown

```txt
ai:cooldown:partner:{partnerCode}
ai:cooldown:account:{accountCode}
ai:cooldown:account:{accountCode}:model:{modelCode}
```

Cooldown được set khi provider trả `429`, `quota_exceeded`, `RESOURCE_EXHAUSTED`, hoặc có `Retry-After`.

## Inflight

```txt
ai:inflight:partner:{partnerCode}
ai:inflight:account:{accountCode}
```

## Metric buffer

```txt
ai:metric:index
ai:metric:h:{yyyyMMddHH}:client:{clientCode}:model:{modelCode}:partner:{partnerCode}:account:{accountCode}
```

Fields:

```txt
total
success
failed
fallback_success
tokens_in
tokens_out
tokens_total
latency_total_ms
latency_count
error_rate_limit
error_quota_exceeded
error_timeout
error_server_error
error_auth_error
error_permission_error
error_bad_response
error_unknown
```

`MetricFlushWorker` flush sang PostgreSQL.

## Error event cap

```txt
ai:error_event_cap:{yyyyMMddHH}:account:{accountCode}:type:{errorType}
```

Dùng để cap error events cho lỗi noisy như `rate_limit` và `quota_exceeded`.

## Response cache reserved key

Chưa dùng ở MVP, để dành phase sau:

```txt
ai:cache:response:{sha256}
```
