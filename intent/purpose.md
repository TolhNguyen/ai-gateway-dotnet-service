# Purpose

AI Gateway is the **AI Capability Node** of the Microservice Node Governance Platform.

It is the only path through which any service in the platform consumes external AI providers (Gemini, Groq, Mistral, OpenRouter, Fireworks, OpenAI-compatible endpoints, and future private model APIs). It centralizes:

- **Tenant isolation** — every user owns their own provider API keys.
- **Provider key encryption** — keys are AES-256-GCM at rest; raw keys are never returned after creation.
- **Routing and fallback** — internal model aliases (`text-fast`, `text-pro`) resolve to provider routes; a failing route triggers an attempt at the next viable candidate.
- **Rate limit and quota enforcement** — atomic check-and-reserve via Lua-scripted Redis ops.
- **Operational visibility** — hourly usage metrics, error aggregates, and per-key health.

Business services do not integrate with vendors directly. They consume one stable internal API: `POST /v1/ai/generate`.
