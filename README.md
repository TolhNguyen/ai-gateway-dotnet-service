# AI Gateway v2

A self-contained, multi-tenant AI gateway. Postgres + Redis + the .NET 9 API all run inside a single Docker image — no `docker compose`, no external services.

## Quick start

```bash
# Build
docker build -t aigateway:latest .

# Run (mount a volume so data survives container restarts)
docker run -d --name aigateway \
  -p 8080:8080 \
  -v aigateway-pg:/var/lib/postgresql/data \
  -v aigateway-redis:/var/lib/redis \
  aigateway:latest
```

Open <http://localhost:8080/>. Sign in with the bootstrap admin:

| field    | default                    |
|----------|----------------------------|
| email    | `[email protected]`       |
| password | `ChangeMe!2026`            |

**Change this password immediately.** Override the defaults via env when starting the container:

```bash
docker run -d \
  -e AiGateway__BootstrapAdminEmail="[email protected]" \
  -e AiGateway__BootstrapAdminPassword="<strong password>" \
  -p 8080:8080 \
  -v aigateway-pg:/var/lib/postgresql/data \
  -v aigateway-redis:/var/lib/redis \
  aigateway:latest
```

For convenience, a `docker-compose.yml` is included that wraps these steps.

## What's inside the image

| process    | runs as   | priority |
|------------|-----------|----------|
| Postgres 15 | `postgres` | 10 |
| Redis 7     | `redis`    | 20 |
| .NET API    | `root`     | 30 |

`supervisord` keeps all three alive; `tini` is PID 1 for clean signal handling.

The first container start runs `initdb`, creates the `ai_gateway` user and database, then locks Postgres down to loopback-only access (`pg_hba.conf` allows only `127.0.0.1`).

## Multi-tenant model

```
┌────────────┐      ┌──────────────┐      ┌────────────────────┐
│  user A    │─────▶│ JWT or PAT   │─────▶│ Gateway picks      │
│  user B    │      │ identifies   │      │ candidate routes   │
│  user C    │      │ the tenant   │      │ from THIS user's   │
└────────────┘      └──────────────┘      │ saved API keys     │
                                          └────────────────────┘
```

* Sign-in returns a JWT (`Authorization: Bearer <jwt>`).
* For backend integrations, mint a **Personal Access Token** under *Access Tokens*. PATs use the same `Authorization: Bearer aigw_…` header.
* Every user manages their own pool of provider API keys under *My Keys*. Keys are stored AES-256-GCM encrypted; only the SHA-256 fingerprint is shown after save.
* When `/v1/ai/generate` is called, the gateway selects from **only** the calling user's active keys.

## Health checks

* A background worker pings every active key every `HealthCheckIntervalMinutes` (default 5) using each partner's cheapest `health_check_model`.
* Per-key status, last error, and latency surface in *My Keys*.
* Manual checks: click *Check* on a row, or `POST /v1/me/keys/{id}/health-check`.

## API surface

| method  | path                                  | auth      | purpose                                              |
|---------|---------------------------------------|-----------|------------------------------------------------------|
| POST    | `/v1/auth/register`                   | none      | Self-serve signup                                    |
| POST    | `/v1/auth/login`                      | none      | Returns JWT + user profile                           |
| GET     | `/v1/me`                              | JWT / PAT | Current profile                                      |
| GET/POST/DELETE | `/v1/me/tokens`               | JWT       | Manage personal access tokens                        |
| GET/POST/PUT/DELETE | `/v1/me/keys`             | JWT       | CRUD your provider API keys                          |
| POST    | `/v1/me/keys/{id}/health-check`       | JWT       | Run a one-off health probe                           |
| GET     | `/v1/me/keys/health`                  | JWT       | Live status of all keys (inflight + cooldown)        |
| GET     | `/v1/me/dashboard/overview`           | JWT       | Aggregated metrics                                   |
| GET     | `/v1/me/dashboard/{models|partners|account-keys}` | JWT | Grouped breakdowns                          |
| GET     | `/v1/me/dashboard/errors`             | JWT       | Recent errors                                        |
| POST    | `/v1/ai/generate`                     | JWT / PAT | Send a generation request                            |
| ...     | `/v1/admin/...`                       | admin     | Partner / model / route configuration                |
| GET     | `/health`, `/health/ready`            | none      | Liveness / readiness probes                          |

## Bug fixes vs v1

These were the hard bugs from the MVP audit, all fixed here:

1. **Token-usage adjustment after key expiry**: Lua now checks `EXISTS` before `INCRBY`, uses `KEEPTTL`, and clamps at 0 — no more persistent negative-value keys that break quota for the whole bucket.
2. **Inflight DECR**: same `EXISTS`+`KEEPTTL`+clamp pattern; load-balancing scores stay accurate after long-running requests.
3. **MigrationRunner re-runs all SQL on every start**: now tracked in `__schema_migrations` with SHA-256 checksums.
4. **Hardcoded `your-org` OpenRouter referer**: bound to `OpenRouterOptions` (`AppReferer`, `AppTitle`).
5. **No request validation**: every contract uses `DataAnnotations`; `[ApiController]` returns 400s automatically.
6. **Vietnamese / UTF-8 token under-estimation**: `TokenEstimator` uses `max(chars/2, bytes/3.5)`.
7. **Cache stampede**: `RedisConfigCache.GetOrSetAsync` uses per-key `SemaphoreSlim` to ensure single-flight loads.
8. **Encrypted API keys cached in Redis**: removed. Per-user keys load from Postgres on demand (cheap with the join).
9. **Metric flush re-flushes old buckets forever**: each bucket has a `_dirty` marker; cleared after flush, buckets older than 2h are pruned.
10. **No correlation ID**: `CorrelationIdMiddleware` reads / generates `X-Request-Id` and adds a log scope.
11. **No body size limit**: Kestrel `MaxRequestBodySize` set from `AiGateway:MaxRequestBodyBytes` (default 1 MB).

## Adding a new partner

Adding a partner requires writing an adapter (consistent with what you asked for — partner integration stays in code):

1. Implement `IAiPartnerClient` (see `GeminiClient`, `OpenAiCompatibleClient`, `OpenRouterClient`).
2. Register it in `Program.cs`:
   ```csharp
   builder.Services.AddSingleton<IAiPartnerClient, MyClient>();
   ```
3. Insert a row into `ai_partners` with the matching `adapter_code` — either via the admin UI or via SQL migration.

Users can then add their personal API key for that partner under *My Keys*.

## Configuration reference

All keys live under the `AiGateway` section (override via `AiGateway__<Key>` env var).

| key                                | default              | meaning                                              |
|------------------------------------|----------------------|------------------------------------------------------|
| `EncryptionKeyBase64`              | auto-generated       | AES-256 key for user API key at-rest encryption      |
| `BootstrapAdminEmail/Password`     | admin defaults       | Created only when the users table is empty           |
| `MetricFlushSeconds`               | 30                   | Redis → Postgres metric flush cadence                |
| `ErrorEventsRetentionDays`         | 62                   | `ai_error_events` retention                          |
| `ConfigCacheSeconds`               | 60                   | Redis config-cache TTL                               |
| `ErrorEventsMaxPerKeyTypeHour`     | 20                   | Cap raw event inserts per (key, type, hour)          |
| `DefaultReservedOutputTokens`      | 1000                 | Output token reservation when client omits           |
| `MaxRequestBodyBytes`              | 1 048 576            | Kestrel body size cap                                |
| `HealthCheckIntervalMinutes`       | 5                    | Background sweep cadence                             |
| `HealthCheckTimeoutMs`             | 10 000               | Per-key health probe timeout                         |

The `Jwt__SecretBase64` and `AiGateway__EncryptionKeyBase64` are generated on first run and persisted under `${PGDATA}/.aigateway.*` so they survive container restarts.
