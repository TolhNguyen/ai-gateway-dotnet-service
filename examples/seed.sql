INSERT INTO ai_clients (code, name, status, allowed_models)
VALUES ('product-service', 'Product Service', 'active', '["*"]'::jsonb)
ON CONFLICT (code) DO NOTHING;

INSERT INTO ai_models (code, name, status, default_temperature, default_max_tokens, strategy, fallback_enabled, max_retry)
VALUES ('text-fast', 'Fast Text Model', 'active', 0.7, 1000, 'balanced', true, 3)
ON CONFLICT (code) DO NOTHING;

INSERT INTO ai_partners (code, name, status, adapter_code, base_url, weight, priority, quality_score)
VALUES
  ('gemini', 'Google Gemini', 'active', 'gemini', 'https://generativelanguage.googleapis.com', 100, 1, 85),
  ('groq', 'Groq OpenAI-Compatible', 'active', 'openai_compatible', 'https://api.groq.com/openai', 80, 2, 75)
ON CONFLICT (code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'default', 'active', 'gemini-1.5-flash', 30000, 100, 1
FROM ai_models m CROSS JOIN ai_partners p
WHERE m.code = 'text-fast' AND p.code = 'gemini'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'default', 'active', 'llama-3.1-8b-instant', 20000, 80, 2
FROM ai_models m CROSS JOIN ai_partners p
WHERE m.code = 'text-fast' AND p.code = 'groq'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

-- Accounts should be created through POST /v1/admin/partners/{partnerCode}/accounts
-- so the API key is encrypted with AiGateway:EncryptionKeyBase64.
