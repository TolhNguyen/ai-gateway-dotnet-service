# PostgreSQL Schema

Schema nằm ở `migrations/001_init.sql`.

## Config tables

```txt
ai_clients
ai_models
ai_partners
ai_accounts
ai_model_routes
ai_prompt_templates
```

`ai_model_routes` đã có `route_code`, vì vậy một `model + partner` có thể có nhiều route:

```sql
UNIQUE(model_id, partner_id, route_code)
```

## Metric/error tables

```txt
ai_request_metrics_hourly
ai_error_aggregates
ai_error_events
```

Success requests không insert từng event vào DB. Success chỉ ghi Redis metric buffer rồi worker flush aggregate theo giờ.

Error flow:

```txt
ai_error_aggregates  luôn update
ai_error_events      chỉ insert nếu qua error event cap
```

`ai_error_events` giữ mặc định 62 ngày. `CleanupWorker` xóa dữ liệu cũ.

## Security note

`ai_accounts.api_key_enc` là encrypted API key. Không lưu raw API key.

Admin API được bảo vệ bằng `AdminAuth` middleware, không lưu audit log vì hiện tại owner chỉ có một dev/admin.
