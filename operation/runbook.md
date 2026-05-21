# Runbook — AI Gateway

## On-call rotation

- Tech Lead: lead.ai-gateway@example.com
- DevOps:    devops@example.com
- Slack:     `#ai-gateway-oncall`

## Common alerts

### Alert: `/health/ready` returns 503

**Meaning:** the container is up but a dependency (Postgres or Redis) is unreachable from inside the container.

**Triage:**
1. `docker logs aigateway | tail -200` — look for supervisord lines about postgres or redis.
2. `docker exec aigateway gosu postgres pg_isready` — Postgres up?
3. `docker exec aigateway redis-cli ping` — Redis up?
4. If only one of the three processes is restarting, the cause is usually disk full or OOM.

**Resolution:**
- Disk full: `docker exec aigateway df -h /var/lib/postgresql/data`. Roll up old `ai_error_events`; the cleanup worker prunes hourly but a backlog can accrue if it crashed.
- OOM: bump container memory; Postgres + Redis + .NET in one container has a baseline of ~512 MiB. 1 GiB is the comfortable floor.

### Alert: `error_rate > 20%` in last 15 minutes

**Triage:**
1. Open the portal → **Errors** tab. Group by `partnerCode`.
2. If a single partner dominates → that provider is having an outage. Verify against the provider's status page.
3. If a single `accountKeyCode` dominates and the partner is fine → that tenant has a key issue (revoked, quota exceeded, IP block).
4. If spread across partners → likely an internal issue. Check `audit_log` and recent deploys.

**Resolution:**
- Provider outage: confirm cooldown is engaged (`ai:cooldown:partner:<code>` in Redis). The route selector will skip until cooldown expires. Consider disabling the partner via `PATCH /v1/admin/partners/{code}/status` to status=`disabled` if outage is long.
- Tenant key issue: contact tenant; the per-key health record stores `last_health_error`.
- Internal: roll back the deploy, then diagnose.

### Alert: latency p95 > 5s

**Triage:**
1. Portal → **Usage** → **By partner** table. Avg latency by partner.
2. High latency on one partner only → upstream issue. Cooldown is health-based, not latency-based; consider lowering that partner's `weight` to deprioritize.
3. High latency across all partners → outbound network or DNS issue inside the container.

### Alert: bootstrap admin still has default password

**Triage:**
The default password is documented in `appsettings.json` and `README.md`. Anyone on the internet who knows the deployment URL can log in.

**Resolution:**
- Log in as the bootstrap admin.
- Change the password immediately.
- If the URL is internet-facing, rotate now and audit `audit_log` for any prior admin operations.

## Useful queries

### Postgres

```sql
-- Top noisy errors in the last hour
SELECT user_id, model_code, partner_code, error_type, count, last_message
FROM ai_error_aggregates
WHERE last_seen_at > NOW() - INTERVAL '1 hour'
ORDER BY count DESC
LIMIT 20;

-- Slowest 1% of recent requests (raw events are sampled)
SELECT request_id, partner_code, model_code, latency_ms, error_type
FROM ai_error_events
WHERE created_at > NOW() - INTERVAL '1 hour'
ORDER BY latency_ms DESC NULLS LAST
LIMIT 50;

-- Per-tenant usage today
SELECT user_id, SUM(total_count) AS reqs, SUM(total_tokens) AS toks
FROM ai_request_metrics_hourly
WHERE bucket_hour::date = CURRENT_DATE
GROUP BY user_id
ORDER BY toks DESC;
```

### Redis

```bash
# Show all keys currently in cooldown
docker exec aigateway redis-cli --scan --pattern 'ai:cooldown:*'

# Inflight counter for a partner
docker exec aigateway redis-cli GET 'ai:inflight:partner:gemini'

# Pending metric flush keys
docker exec aigateway redis-cli SMEMBERS 'ai:metric:index' | head -20
```

## Manual operations

### Force-reindex a tenant's keys cache

```bash
docker exec aigateway redis-cli --scan --pattern 'ai:config:user_keys:*' | \
  xargs -r docker exec aigateway redis-cli DEL
```

### Disable a partner without redeploying

```bash
curl -X PATCH https://ai-gateway.internal/v1/admin/partners/openrouter/status \
  -H "Authorization: Bearer <admin-jwt>" \
  -H "Content-Type: application/json" \
  -d '{"status":"disabled"}'
```

### Reset a stuck inflight counter (post-incident)

```bash
docker exec aigateway redis-cli DEL 'ai:inflight:partner:<code>'
docker exec aigateway redis-cli DEL 'ai:inflight:key:<id>'
```

The counters are bounded by 10-minute TTL and clamped at 0 in Lua, so a manual reset is only needed if a deliberate audit reveals drift.

## Escalation

- AIGW-RULE-001 / AIGW-RULE-002 incident (key leak or cross-tenant access) → page Domain Owner immediately. Treat as P0.
- Schema migration failure on deploy → roll back the image tag, do NOT run ad-hoc SQL.
- Postgres data directory corruption → restore from the volume backup; bootstrap secrets in `${PGDATA}/.aigateway.*` are part of the same volume.
