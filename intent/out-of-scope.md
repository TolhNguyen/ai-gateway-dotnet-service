# Out of scope

What AI Gateway is explicitly **not** responsible for. If a feature lands here, it belongs in a different node.

| Concern | Why out of scope | Where it should live |
|---|---|---|
| Storing prompts and completions for analytics or training | This service is a routing layer, not a data lake. Storing content widens compliance scope (PII, contracts) and bloats Postgres. | A dedicated `ai-content-archive` node, if ever needed. |
| Vector storage / embeddings persistence | Different access pattern, different scale curve, different durability model. | A dedicated `vector-store` node. |
| Fine-tuning or model training | Workflow concern, not a routing concern. | A dedicated `model-ops` node. |
| Cost billing and invoicing | Aggregating usage into invoices is a billing concern. We expose usage metrics; we do not own pricing tables. | A future `billing` node. |
| Streaming response support | Planned, but not yet implemented. The current contract is single-response generation only. | Inside this service in a future release. |
| Provider key rotation policy enforcement | Currently advisory only. Rotation reminders / forced rotation belong in a governance overlay. | A future `secret-rotation` node or Governance Center policy. |
| Multi-region replication of Postgres / Redis | The single-image deployment is intentional. Multi-region is a Tier 3 concern. | Solved at infra layer when needed, not in this service. |
| Identity provider integration (SSO, OAuth-as-IdP) | Currently a self-managed user table with email + password. SSO is a future overlay. | A future `identity` node. |

Out-of-scope items are not blockers — they are **deliberate boundaries**. An AI agent must not "helpfully" add any of these capabilities. Adding them collapses the service responsibility model.
