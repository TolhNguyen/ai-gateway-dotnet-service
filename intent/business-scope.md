# Business scope

What AI Gateway **is** responsible for:

- Authenticate consumers via JWT (web users) or Personal Access Token (backend integrations).
- Manage tenant-owned provider API keys with encryption at rest.
- Resolve internal model aliases to provider routes (admin-configurable).
- Select a viable provider route based on health, weight, priority, cooldown, and in-flight load.
- Enforce per-key rate limits (requests per minute / day) and token quotas (tokens per minute / day), atomically.
- Track inflight request counters per partner and per key for load-aware route selection.
- Cool down failing routes for an interval calibrated to the error type.
- Record success / failure / fallback / token / latency metrics in hourly buckets per user.
- Record error aggregates and sampled raw events for diagnostics.
- Run periodic health checks against tenant provider keys.
- Provide admin APIs for partner, model, and route configuration.
- Provide a built-in lightweight web UI for key management, token management, dashboards, and a generation playground.

The single-image deployment model (Postgres + Redis + .NET in one container, via supervisord) is part of the scope. The image is the unit of governance: it can be registered, deployed, versioned, and replaced as one entity in the Governance Center.
