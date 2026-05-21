# AGENTS.md — instructions for AI agents on AI Gateway

You are working on **ai-gateway**, a SCPA Service Node. This service is the
AI Capability Node of a Microservice Node Governance Platform — it sits
between every business service and every external AI provider. A regression
here breaks every AI feature in the platform.

## Before you propose ANY change

Read these in order. The SCP-as-MCP Server should have already loaded them
into context for you. If not, read them now:

1. `intent/business-rules.yaml` — the six rules below are inviolable.
2. `intent/out-of-scope.md` — list of things this service deliberately does NOT do.
3. `ai/do-not-touch.yaml` — files you must not modify.
4. `contracts/openapi.yaml` — the wire contract. Changes here are breaking unless explicitly additive.

## Hard rules — never violate

The IDs map to `intent/business-rules.yaml`. Reference these IDs in commit
messages and test names when your change relates to one.

| Rule    | What it means for your edits |
|---------|------------------------------|
| AIGW-RULE-001 | Provider API keys are AES-256-GCM at rest. Never log raw keys. Never return raw keys in responses. Never write a code path that emits anything but the SHA-256 fingerprint mask. |
| AIGW-RULE-002 | Every database read of `user_account_keys` MUST filter by `user_id`. Never add a query that returns keys across users. The repository is the enforcement layer, not the service layer. |
| AIGW-RULE-003 | Rate limit and token quota checks MUST be atomic. Do not split the Lua scripts in `RedisRateLimitStore` into multiple round trips. Do not "simplify" them. |
| AIGW-RULE-004 | Fallback is bounded by `model.FallbackEnabled` AND `model.MaxRetry` AND candidate availability. Do not loosen any of the three checks. |
| AIGW-RULE-005 | The bootstrap admin warning in `Program.cs` is intentional. Do not silence it. |
| AIGW-RULE-006 | Do not introduce a Redis cache of `user_account_keys`. Encrypted ciphertext does not belong in Redis. |

## Files you MUST NOT modify

These are listed in `ai/do-not-touch.yaml`. Summary:

- `migrations/**` — applied SQL is immutable. New schema goes in a new file.
- `src/AiGateway.Api/Infrastructure/Redis/RedisRateLimitStore.cs` — the Lua scripts carry hard-won bug fixes (token-usage adjustment after key expiry, inflight clamp-at-zero, `KEEPTTL`). Edits here regress production behavior. New rate-limit features → new file + new method.
- `src/AiGateway.Api/Infrastructure/Security/AesGcmSecretProtector.cs` — changes break decryption of every existing encrypted key. New crypto algorithms → versioned `ApiKeyEncV2` column + migration.
- `src/AiGateway.Api/Infrastructure/Security/SecretProtector.cs` — the `ISecretProtector` interface signature must not change. Stored ciphertext depends on it.

If you believe a change in one of these files is necessary, open a separate
PR labelled `do-not-touch-change`. It routes for explicit human review per
`human_review_required_for` in `scp.yaml`.

## How to make changes

- Add new endpoints under `Controllers/`. Mirror the existing controller
  style: thin controllers calling a service in `Application/`.
- Add new tables via a new migration file in `migrations/`. Never edit an
  applied migration — `MigrationRunner` checksums them and a mismatch is
  logged loudly.
- New AI providers: implement `IAiPartnerClient`, register in `Program.cs`,
  insert a row in `ai_partners` via a seed migration. Do NOT edit existing
  partner clients to "extend" them — write a new adapter.
- New business rule discovered: add it to `intent/business-rules.yaml` and
  open a PR. Intent Layer changes require BA / PO / Domain Owner approval.
- Code style: match what's already in the file. Comments explain *why*, not
  *what*. The codebase has a deliberate "why-comment" style — preserve it.

## How to test

- `dotnet test` runs the full suite.
- For changes touching `RedisRateLimitStore`, write a concurrency test that
  fires N parallel requests at the same key and asserts the total reserved
  count does not exceed the limit.
- For changes touching the route selector, write a test that confirms
  cooldown-blocked candidates are excluded.
- Reference business rule IDs in test names:
  e.g. `ReserveAccountUsage_RespectsRpmLimit_AIGW_RULE_003`.

## When you reply in a PR review

- Cite the source: business rule IDs, ADR numbers, or specific file:line
  references. Vague claims ("this is safer") are not acceptable.
- If you find a conflict between intent and implementation, flag it as a
  Gap Detection finding. Do not silently align code to the wrong source.

## When you don't know

Ask. The MCP server's `scpa_get_node_overview` and `scpa_find_rule` tools
exist for this. The Governance Center can also surface ADRs and prior PRs
that touched the same code.
