# AI Gateway

AI Gateway is a self-contained, multi-tenant AI Gateway and an **AI Capability Node** inside a larger **Microservice Node Governance Platform**.

It provides a unified access layer for AI providers, manages tenant-level API keys, handles provider routing and fallback, tracks usage metrics, and gives other services a stable internal AI API.

This service can run independently, but its main design direction is to become a governed node service that can be registered, monitored, versioned, deployed, and reused inside the Governance Center architecture.

---

## Table of Contents

- [Product Vision](#product-vision)
- [Position in the Microservice Node Governance Platform](#position-in-the-microservice-node-governance-platform)
- [Why this project exists](#why-this-project-exists)
- [Core Goals](#core-goals)
- [Key Advantages](#key-advantages)
- [What makes this source valuable](#what-makes-this-source-valuable)
- [Architecture Overview](#architecture-overview)
- [What's inside the image](#whats-inside-the-image)
- [Quick Start](#quick-start)
- [Run with custom bootstrap admin](#run-with-custom-bootstrap-admin)
- [Run with docker-compose](#run-with-docker-compose)
- [First Usage Flow](#first-usage-flow)
- [Multi-tenant Model](#multi-tenant-model)
- [Supported Providers and Default Models](#supported-providers-and-default-models)
- [API Usage](#api-usage)
- [API Surface](#api-surface)
- [Observability and Metrics](#observability-and-metrics)
- [Routing and Fallback Strategy](#routing-and-fallback-strategy)
- [Health Checks](#health-checks)
- [Security Model](#security-model)
- [Configuration Reference](#configuration-reference)
- [Adding a New Partner](#adding-a-new-partner)
- [How this Node fits Governance Center](#how-this-node-fits-governance-center)
- [Bug Fixes vs v1](#bug-fixes-vs-v1)
- [Roadmap](#roadmap)

---

## Product Vision

AI Gateway is designed to become a lightweight, self-hosted control layer for AI usage across teams, products, internal tools, and microservice-based systems.

Instead of integrating directly with each AI provider one by one, applications call a single unified gateway. The gateway is responsible for authentication, provider routing, API key isolation, fallback, usage tracking, rate limiting, health monitoring, and operational visibility.

The long-term vision is to help teams build AI-powered systems that are:

- Easier to manage
- Safer to operate
- Less dependent on a single AI vendor
- Easier to observe
- Easier for humans and AI agents to understand
- Ready to become part of a governed microservice ecosystem

AI Gateway is not only an API wrapper. It is an infrastructure service that can become the standard AI access node for a larger platform.

---

## Position in the Microservice Node Governance Platform

AI Gateway is designed as one **node service** inside a larger **Microservice Node Governance Platform**.

In this architecture, each microservice is treated as an independent node with a clear responsibility, lifecycle, deployment boundary, governance metadata, and operational visibility.

AI Gateway plays the role of the:

> **AI Capability Node**

This node is responsible for managing how internal systems access AI providers.

It is not intended to be only a standalone demo API. It is a reusable infrastructure node that can be registered, monitored, deployed, versioned, and governed by the central Governance Center.

---

## Role of this Node Service

Within the microservice governance model, AI Gateway is responsible for:

- Providing a unified AI API for other services.
- Managing tenant-level AI provider keys.
- Routing AI requests to different providers.
- Handling fallback when one provider fails.
- Applying rate limit and quota control.
- Recording AI usage metrics.
- Tracking provider health and error patterns.
- Reducing direct dependency between business services and external AI vendors.
- Becoming a reusable AI infrastructure capability for the whole platform.

Other services in the platform do not need to integrate directly with Gemini, Groq, Mistral, OpenRouter, Fireworks, OpenAI-compatible APIs, or future private model APIs.

They only communicate with this AI Gateway node.

---

## Why this project exists

Modern AI applications often depend on multiple providers such as Gemini, Groq, Mistral, OpenRouter, Fireworks, OpenAI-compatible endpoints, or private model APIs.

As the number of AI features grows, direct integration becomes harder to maintain:

- Each product must manage its own provider keys.
- Switching or adding AI providers requires code changes.
- Rate limits and quota failures are difficult to control globally.
- Usage cost and error visibility are scattered.
- Multi-tenant isolation is easy to get wrong.
- Fallback logic is often duplicated across services.
- Business services become tightly coupled to external AI vendors.
- AI-related errors are difficult to trace across the system.

AI Gateway solves this by placing a centralized gateway between applications and AI providers.

Applications only need to call one API. The gateway handles the provider complexity behind the scenes.

---

## Core Goals

### 1. Unified AI access

Provide a single API endpoint for applications to call AI models without caring which provider is used behind the scenes.

Client services call internal model aliases such as:

- `text-fast`
- `text-pro`

The gateway decides which actual provider model should be used.

### 2. Multi-tenant key isolation

Each user or tenant manages their own provider API keys.

When a request is made, the gateway only selects from the active keys that belong to the calling user.

This prevents one tenant from accidentally using another tenant's credentials.

### 3. Provider flexibility

Support multiple AI providers through adapter-based routing.

This makes it easier to:

- Add a new provider
- Disable a provider
- Change provider priority
- Replace provider models
- Route traffic between different model backends
- Reduce vendor lock-in

### 4. Reliable AI routing

Route requests based on configured model aliases, provider priority, weight, health status, cooldown status, and available account keys.

The gateway can choose the best available route at runtime.

### 5. Fallback and resilience

If one provider or account key fails due to rate limits, quota issues, authentication errors, timeout, or server errors, the gateway can try another available route.

This helps production AI features avoid breaking immediately when one provider has an issue.

### 6. Operational visibility

Track request volume, success rate, failed requests, fallback usage, token usage, latency, and error categories by:

- Model
- Provider
- Account key
- User
- Time bucket

This allows teams to understand how AI is being consumed and where failures happen.

### 7. Self-contained deployment

Run the API, PostgreSQL, and Redis inside one Docker image.

This is useful for:

- Local development
- Internal deployment
- Demo environments
- Proof-of-concept projects
- Lightweight infrastructure nodes

---

## Key Advantages

### 1. One gateway, many AI providers

AI Gateway allows applications to consume AI through one internal API instead of directly integrating with every provider.

This makes product code cleaner and reduces vendor lock-in.

### 2. Tenant-safe API key management

Provider API keys are owned by each user or tenant.

Keys are encrypted before storage, and only a fingerprint or masked value is displayed after saving.

### 3. Built-in fallback strategy

The gateway can retry another available route when a provider fails.

This improves reliability for production AI features where a single provider outage or quota error should not immediately break the user experience.

### 4. Centralized rate limit and quota control

Instead of spreading rate-limit logic across multiple services, AI Gateway centralizes:

- Usage reservation
- Token estimation
- Inflight tracking
- Account-level limits
- Cooldown handling
- Quota protection

### 5. Better observability for AI usage

The system records metrics such as:

- Total requests
- Successful requests
- Failed requests
- Fallback success
- Input tokens
- Output tokens
- Total tokens
- Average latency
- Error categories

This gives teams a clearer view of AI usage.

### 6. Simple deployment model

The project is packaged as a self-contained Docker image with:

- PostgreSQL
- Redis
- .NET API

This makes it easy to run locally, deploy for internal teams, or use as a proof-of-concept without preparing external infrastructure first.

### 7. Admin-configurable models and routes

Admins can manage:

- AI partners
- Internal model aliases
- Provider routes
- Provider model mapping
- Route priority
- Route weight
- Route status

Client applications can call stable internal model names while the actual provider model can change behind the gateway.

---

## What makes this source valuable

This source is not only a demo API. It already includes the foundation of an AI platform control layer:

- Authentication with JWT and Personal Access Tokens.
- Per-user provider API key management.
- Encrypted API key storage.
- Multi-provider route configuration.
- Model aliasing.
- Provider fallback.
- Redis-based rate limit and cooldown handling.
- PostgreSQL-backed metrics and error aggregation.
- Health checks for provider keys.
- Admin management APIs.
- Lightweight built-in web UI.
- Docker-first self-contained deployment.

Because of this, the project can be used as a base for:

- A larger internal AI platform.
- A gateway for microservices.
- A commercial AI infrastructure product.
- A governed AI node inside the Microservice Node Governance Platform.
- A reference implementation for AI routing and fallback.

---

## Architecture Overview

```text
┌──────────────────────┐
│ Business Services    │
│ Internal Tools       │
│ AI Agents            │
│ Product Features     │
└──────────┬───────────┘
           │
           │ Unified AI API
           ▼
┌────────────────────────────────────────────┐
│ AI Gateway                              │
│                                            │
│ - Auth JWT / PAT                           │
│ - Tenant API key management                │
│ - Model alias resolution                   │
│ - Provider route selection                 │
│ - Rate limit / quota control               │
│ - Fallback / retry                         │
│ - Metrics / error tracking                 │
│ - Health checks                            │
└──────────┬─────────────────────────────────┘
           │
           │ Adapter-based provider calls
           ▼
┌────────────────────────────────────────────┐
│ AI Providers                               │
│                                            │
│ - Gemini                                   │
│ - Groq                                     │
│ - Mistral                                  │
│ - OpenRouter                               │
│ - Fireworks                                │
│ - OpenAI-compatible APIs                   │
│ - Future private/local model APIs          │
└────────────────────────────────────────────┘
```

---

## What's inside the image

Postgres + Redis + the .NET 9 API all run inside a single Docker image.

No external Postgres.
No external Redis.
No separate docker-compose requirement for the basic setup.

| Process     | Runtime role                         |
|-------------|--------------------------------------|
| Postgres 15 | Durable storage                      |
| Redis       | Rate limit, cooldown, metric buffer  |
| .NET 9 API  | Gateway API and web UI               |

`supervisord` keeps all three processes alive.

`tini` is used as PID 1 for clean signal handling.

The first container start runs `initdb`, creates the app database, then locks Postgres down to loopback-only access.

---

## Quick Start

### Build

```bash
docker build -t aigateway:latest .
```

### Run

```bash
docker run -d --name aigateway \
  -p 8080:8080 \
  -v aigateway-pg:/var/lib/postgresql/data \
  -v aigateway-redis:/var/lib/redis \
  aigateway:latest
```

Open:

```text
http://localhost:8080/
```

Sign in with the bootstrap admin:

| Field    | Default             |
|----------|---------------------|
| Email    | `admin@example.com` |
| Password | `ChangeMe!2026`     |

Change this password immediately after first login.

---

## Run with custom bootstrap admin

```bash
docker run -d --name aigateway \
  -e AiGateway__BootstrapAdminEmail="admin@your-company.com" \
  -e AiGateway__BootstrapAdminPassword="<strong password>" \
  -p 8080:8080 \
  -v aigateway-pg:/var/lib/postgresql/data \
  -v aigateway-redis:/var/lib/redis \
  aigateway:latest
```

---

## Run with docker-compose

A `docker-compose.yml` file is included for convenience.

```bash
docker compose up -d --build
```

---

## First Usage Flow

After starting the service:

1. Open the web UI.
2. Login with the bootstrap admin account.
3. Change the default password.
4. Configure AI partners and model routes if needed.
5. Create a normal user account.
6. Login as that user.
7. Add provider API keys under **My Keys**.
8. Run health check for each key.
9. Call `/v1/ai/generate` using JWT or Personal Access Token.
10. Monitor usage in the dashboard.

---

## Multi-tenant Model

```text
┌────────────┐      ┌──────────────┐      ┌────────────────────┐
│  user A    │─────▶│ JWT or PAT   │─────▶│ Gateway picks      │
│  user B    │      │ identifies   │      │ candidate routes   │
│  user C    │      │ the tenant   │      │ from THIS user's   │
└────────────┘      └──────────────┘      │ saved API keys     │
                                          └────────────────────┘
```

- Sign-in returns a JWT.
- Backend integrations can use Personal Access Tokens.
- PATs use the same `Authorization: Bearer <token>` header.
- Every user manages their own pool of provider API keys under **My Keys**.
- Keys are stored encrypted.
- Only a SHA-256 fingerprint/masked value is shown after save.
- When `/v1/ai/generate` is called, the gateway selects from only the calling user's active keys.

---

## Supported Providers and Default Models

The initial seed includes provider definitions and model routes for:

| Provider     | Adapter type          |
|--------------|-----------------------|
| Gemini       | Native Gemini adapter |
| Groq         | OpenAI-compatible     |
| Mistral      | OpenAI-compatible     |
| OpenRouter   | OpenRouter adapter    |
| Fireworks AI | OpenAI-compatible     |

Default internal model aliases:

| Internal model | Purpose                            |
|----------------|------------------------------------|
| `text-fast`    | Fast, low-latency text generation  |
| `text-pro`     | Higher-capability text generation  |

The actual provider model behind each alias can be changed by admin route configuration.

---

## API Usage

### 1. Login

```bash
curl -X POST http://localhost:8080/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@example.com",
    "password": "ChangeMe!2026"
  }'
```

Response example:

```json
{
  "accessToken": "<jwt>",
  "tokenType": "Bearer",
  "expiresInSeconds": 3600,
  "user": {
    "id": 1,
    "email": "admin@example.com",
    "role": "admin",
    "status": "active"
  }
}
```

### 2. Add a provider API key

```bash
curl -X POST http://localhost:8080/v1/me/keys \
  -H "Authorization: Bearer <jwt>" \
  -H "Content-Type: application/json" \
  -d '{
    "code": "my-gemini-key",
    "partnerCode": "gemini",
    "apiKey": "<provider-api-key>",
    "name": "My Gemini Key",
    "rpmLimit": 60,
    "rpdLimit": 1000,
    "tpmLimit": 100000,
    "tpdLimit": 1000000,
    "weight": 100,
    "priority": 100
  }'
```

### 3. Run a health check

```bash
curl -X POST http://localhost:8080/v1/me/keys/1/health-check \
  -H "Authorization: Bearer <jwt>"
```

### 4. Generate text

```bash
curl -X POST http://localhost:8080/v1/ai/generate \
  -H "Authorization: Bearer <jwt-or-pat>" \
  -H "Content-Type: application/json" \
  -d '{
    "model": "text-fast",
    "systemPrompt": "You are a helpful assistant.",
    "prompt": "Write a short introduction about AI Gateway.",
    "temperature": 0.7,
    "maxTokens": 500,
    "debug": true
  }'
```

Response example:

```json
{
  "success": true,
  "requestId": "81c7ad87cd474c359b89532fb3b44bc6",
  "model": "text-fast",
  "content": "AI Gateway is a unified access layer for AI providers...",
  "usage": {
    "inputTokens": 41,
    "outputTokens": 136,
    "totalTokens": 177
  },
  "latencyMs": 2519,
  "routing": {
    "partnerCode": "gemini",
    "accountKeyCode": "my-gemini-key",
    "routeCode": "flash",
    "providerModel": "gemini-2.5-flash",
    "fallbackUsed": false,
    "retryCount": 0
  }
}
```

---

## API Surface

| Method | Path                                                   | Auth      | Purpose                                  |
|--------|--------------------------------------------------------|-----------|------------------------------------------|
| POST   | `/v1/auth/register`                                    | none      | Self-serve signup                        |
| POST   | `/v1/auth/login`                                       | none      | Returns JWT and user profile             |
| GET    | `/v1/me`                                               | JWT / PAT | Current profile                          |
| GET    | `/v1/me/tokens`                                        | JWT       | List personal access tokens              |
| POST   | `/v1/me/tokens`                                        | JWT       | Create a personal access token           |
| DELETE | `/v1/me/tokens/{id}`                                   | JWT       | Revoke a personal access token           |
| GET    | `/v1/me/keys`                                          | JWT       | List provider API keys                   |
| POST   | `/v1/me/keys`                                          | JWT       | Create provider API key                  |
| PUT    | `/v1/me/keys/{id}`                                     | JWT       | Update provider API key                  |
| DELETE | `/v1/me/keys/{id}`                                     | JWT       | Delete provider API key                  |
| POST   | `/v1/me/keys/{id}/health-check`                        | JWT       | Run one-off health probe                 |
| GET    | `/v1/me/keys/health`                                   | JWT       | Live status of keys                      |
| GET    | `/v1/me/dashboard/overview`                            | JWT       | Aggregated metrics                       |
| GET    | `/v1/me/dashboard/models`                              | JWT       | Metrics grouped by model                 |
| GET    | `/v1/me/dashboard/partners`                            | JWT       | Metrics grouped by partner               |
| GET    | `/v1/me/dashboard/account-keys`                        | JWT       | Metrics grouped by account key           |
| GET    | `/v1/me/dashboard/errors`                              | JWT       | Recent error aggregates                  |
| POST   | `/v1/ai/generate`                                      | JWT / PAT | Send a generation request                |
| GET    | `/v1/admin/partners`                                   | admin     | List AI partners                         |
| PUT    | `/v1/admin/partners/{code}`                            | admin     | Upsert partner                           |
| PATCH  | `/v1/admin/partners/{code}/status`                     | admin     | Update partner status                    |
| GET    | `/v1/admin/models`                                     | admin     | List internal models                     |
| PUT    | `/v1/admin/models/{code}`                              | admin     | Upsert internal model                    |
| PATCH  | `/v1/admin/models/{code}/status`                       | admin     | Update model status                      |
| GET    | `/v1/admin/models/{code}/routes`                       | admin     | List routes for a model                  |
| PUT    | `/v1/admin/models/{code}/routes`                       | admin     | Upsert model route                       |
| DELETE | `/v1/admin/models/routes/{id}`                         | admin     | Delete model route                       |
| GET    | `/health`                                              | none      | Liveness probe                           |
| GET    | `/health/ready`                                        | none      | Readiness probe                          |

---

## Observability and Metrics

AI Gateway records metrics into hourly buckets.

Metrics include:

- Total requests
- Success count
- Failed count
- Fallback success count
- Input tokens
- Output tokens
- Total tokens
- Total latency
- Average latency
- Rate-limit errors
- Quota-exceeded errors
- Timeout errors
- Server errors
- Auth errors
- Permission errors
- Bad response errors
- Unknown errors

Metrics can be viewed through the dashboard APIs and the built-in UI.

This makes the gateway useful not only as a runtime service, but also as a governance and monitoring point for AI usage.

---

## Routing and Fallback Strategy

The routing flow is:

1. Resolve the requested internal model alias.
2. Load available active routes for that model.
3. Filter routes by the current user's active provider keys.
4. Exclude keys currently in cooldown.
5. Score candidates by weight, priority, provider quality, and current inflight usage.
6. Reserve estimated token usage and rate-limit quota.
7. Call the selected provider.
8. If the call succeeds, record success metrics.
9. If the call fails, record error metrics and try the next candidate if fallback is allowed.
10. Return the successful response or final error response.

Cooldown is applied for noisy or recoverable errors such as:

- `rate_limit`
- `quota_exceeded`
- `auth_error`
- `permission_error`
- `server_error`

This avoids repeatedly calling unhealthy routes.

---

## Health Checks

A background worker periodically checks active provider keys.

Default interval:

```text
AiGateway:HealthCheckIntervalMinutes = 5
```

Each key is checked using the partner's configured `health_check_model`.

The result is stored per key:

- Last health status
- Last error
- Last latency
- Last check time

Manual health checks are also available through:

```text
POST /v1/me/keys/{id}/health-check
```

---

## Security Model

AI Gateway includes several security protections:

### Authentication

- JWT for web users.
- Personal Access Tokens for backend integrations.
- Admin APIs require the `admin` role.

### API key protection

- Provider API keys are encrypted at rest.
- Only masked fingerprints are displayed.
- Raw API keys are never returned after creation.

### Tenant isolation

- Each request is tied to the authenticated user.
- Route candidates are selected only from that user's active keys.
- One user cannot use another user's provider keys.

### Request control

- Request body size is limited.
- Contracts use validation attributes.
- Invalid requests return automatic 400 responses.

### Runtime safety

- Postgres is loopback-only inside the container.
- Redis is bound to loopback.
- Secrets can be provided through environment variables.
- AES and JWT secrets are generated on first run if not provided.

---

## Configuration Reference

All keys live under the `AiGateway` section and can be overridden by environment variables using the `AiGateway__<Key>` format.

| Key                                | Default              | Meaning                                             |
|------------------------------------|----------------------|-----------------------------------------------------|
| `EncryptionKeyBase64`              | auto-generated       | AES-256 key for user API key at-rest encryption     |
| `BootstrapAdminEmail`              | `admin@example.com`  | Bootstrap admin email                               |
| `BootstrapAdminPassword`           | `ChangeMe!2026`      | Bootstrap admin password                            |
| `MetricFlushSeconds`               | `30`                 | Redis to Postgres metric flush cadence              |
| `ErrorEventsRetentionDays`         | `62`                 | Error event retention                               |
| `ConfigCacheSeconds`               | `60`                 | Redis config-cache TTL                              |
| `ErrorEventsMaxPerKeyTypeHour`     | `20`                 | Raw event cap per key, type, and hour               |
| `DefaultReservedOutputTokens`      | `1000`               | Output token reservation when client omits value    |
| `ExposeRoutingWhenDebug`           | `true`               | Expose routing info when request debug is true      |
| `MaxRequestBodyBytes`              | `1048576`            | Kestrel request body size cap                       |
| `HealthCheckIntervalMinutes`       | `5`                  | Background health check interval                    |
| `HealthCheckTimeoutMs`             | `10000`              | Per-key health probe timeout                        |

JWT configuration:

| Key                   | Default        | Meaning                    |
|-----------------------|----------------|----------------------------|
| `Jwt__Issuer`         | `ai-gateway`   | JWT issuer                 |
| `Jwt__Audience`       | `ai-gateway`   | JWT audience               |
| `Jwt__SecretBase64`   | auto-generated | JWT signing secret         |
| `Jwt__AccessTokenMinutes` | `60`       | JWT expiry in minutes      |

OpenRouter configuration:

| Key                       | Meaning                    |
|---------------------------|----------------------------|
| `OpenRouter__AppReferer`  | Referer sent to OpenRouter |
| `OpenRouter__AppTitle`    | App title for OpenRouter   |

The `Jwt__SecretBase64` and `AiGateway__EncryptionKeyBase64` values are generated on first run and persisted under `${PGDATA}/.aigateway.*` so they survive container restarts.

---

## Adding a New Partner

Adding a partner requires writing an adapter.

Recommended flow:

1. Implement `IAiPartnerClient`.
2. Follow the existing adapters:
   - `GeminiClient`
   - `OpenAiCompatibleClient`
   - `OpenRouterClient`
3. Register the adapter in `Program.cs`.
4. Insert a row into `ai_partners` with the matching `adapter_code`.
5. Add model routes under the admin route configuration.
6. Let users add their personal API key under **My Keys**.

This design keeps provider-specific integration inside adapter code while keeping runtime route configuration flexible.

---

## How this Node fits Governance Center

AI Gateway can be treated as a governed microservice node in the larger Governance Center model.

In that model, every node should have:

- A clear responsibility.
- A stable API boundary.
- A deployment unit.
- Runtime configuration.
- Health checks.
- Observability.
- Documentation.
- Upgrade path.
- Metadata for humans and AI agents.

AI Gateway already satisfies many of these requirements:

| Governance requirement | AI Gateway support |
|------------------------|-----------------------|
| Clear responsibility   | AI provider access and routing |
| Stable API boundary    | `/v1/ai/generate`, `/v1/me`, `/v1/admin` |
| Deployment unit        | Docker image |
| Data ownership         | Own PostgreSQL schema |
| Runtime cache          | Redis |
| Health check           | `/health`, `/health/ready`, provider key health checks |
| Observability          | Metrics dashboard and error aggregates |
| Security               | JWT, PAT, encrypted provider keys |
| Admin governance       | Partner, model, and route configuration |

This means the source can act as a reference node for the broader platform.

Future Governance Center integration can include:

- `scpa.yaml` or service metadata file.
- `catalog-info.yaml` for service catalog registration.
- Service ownership metadata.
- Dependency map.
- Deployment policy.
- Runtime SLO definition.
- Template upgrade tracking.
- AI-readable HTML documentation.
- Automatic registration into the central Governance Center.

---

## Relationship with the SCPA / Microservice Node Idea

This service is a practical implementation of the SCPA-style idea:

> A microservice should not only contain code. It should also contain enough structure, documentation, configuration, and operational metadata so both humans and AI agents can understand its purpose, responsibility, and place in the larger system.

AI Gateway demonstrates this concept through a real service:

- It has a focused responsibility.
- It owns a specific platform capability.
- It exposes clear APIs.
- It manages its own data.
- It includes deployment configuration.
- It has health checks.
- It has operational metrics.
- It can be extended by adapters.
- It can be governed by a central platform.

In the future, this node can be connected to the Governance Center so BA, PM, PO, Dev, DevOps, and AI agents can understand:

- What this service does.
- Which systems depend on it.
- Which providers it connects to.
- Which routes and models are active.
- How healthy the node is.
- What errors are happening.
- How much AI usage each tenant consumes.

This makes AI Gateway not just an API service, but a meaningful platform node.

---

## Bug Fixes vs v1

These were the hard bugs from the MVP audit, all fixed in this version:

1. **Token-usage adjustment after key expiry**

   Lua now checks `EXISTS` before `INCRBY`, uses `KEEPTTL`, and clamps at 0. This avoids persistent negative-value keys that can break quota for the whole bucket.

2. **Inflight decrement issue**

   The same `EXISTS` + `KEEPTTL` + clamp pattern is used. Load-balancing scores stay accurate after long-running requests.

3. **MigrationRunner re-runs all SQL on every start**

   Migrations are now tracked in `__schema_migrations` with SHA-256 checksums.

4. **Hardcoded OpenRouter referer**

   OpenRouter referer and title are now bound to `OpenRouterOptions`.

5. **No request validation**

   Contracts use `DataAnnotations`, and `[ApiController]` returns automatic 400 responses.

6. **Vietnamese / UTF-8 token under-estimation**

   `TokenEstimator` uses `max(chars/2, bytes/3.5)`.

7. **Cache stampede**

   `RedisConfigCache.GetOrSetAsync` uses per-key `SemaphoreSlim` to ensure single-flight loads.

8. **Encrypted API keys cached in Redis**

   Removed. Per-user keys load from Postgres on demand.

9. **Metric flush re-flushes old buckets forever**

   Each bucket has a dirty marker. The marker is cleared after flush, and old buckets are pruned.

10. **No correlation ID**

    `CorrelationIdMiddleware` reads or generates `X-Request-Id` and adds a log scope.

11. **No body size limit**

    Kestrel `MaxRequestBodySize` is set from `AiGateway:MaxRequestBodyBytes`.

---

## Roadmap

Possible next steps:

### Platform governance

- Add `scpa.yaml` metadata.
- Add `catalog-info.yaml` for service catalog registration.
- Add owner, lifecycle, domain, and dependency metadata.
- Register this node into Governance Center.
- Generate AI-readable service documentation.

### Runtime and deployment

- Support external Postgres and Redis for production.
- Add Kubernetes manifests or Helm chart.
- Add CI/CD pipeline template.
- Add backup and restore guide.
- Add zero-downtime migration strategy.

### AI gateway capabilities

- Add streaming response support.
- Add chat-completion style message endpoint.
- Add embedding endpoint.
- Add image-generation endpoint.
- Add provider cost tracking.
- Add budget policy per tenant.
- Add prompt template management.
- Add feature-level usage tracking.

### Admin and governance UI

- Improve partner/model/route admin screens.
- Add usage charts.
- Add route simulation.
- Add tenant usage report.
- Add provider reliability report.
- Add cost estimation dashboard.

### Security and compliance

- Add audit logs.
- Add role-based permissions beyond admin/user.
- Add API key rotation reminders.
- Add secret scanning in CI.
- Add tenant-level access policy.

---

## Recommended README positioning

This repository should be introduced as:

> **AI Gateway is a governed AI Capability Node for a Microservice Node Governance Platform. It centralizes AI provider access, tenant key isolation, routing, fallback, quota control, health monitoring, and usage observability behind one internal API.**

This positioning makes the source stronger than a normal demo project.

It shows that the project is both:

1. A usable AI Gateway service.
2. A concrete node in a larger microservice governance architecture.

---

## License

Add your project license here.

