-- Seed Anthropic Claude partner + model routes.
-- Uses current Claude API model IDs from Anthropic's model overview.

INSERT INTO ai_partners (code, name, status, adapter_code, base_url, health_check_model, weight, priority, quality_score)
VALUES
    ('claude', 'Anthropic Claude', 'active', 'claude', 'https://api.anthropic.com', 'claude-haiku-4-5-20251001', 85, 6, 85)
ON CONFLICT (code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'haiku', 'active', 'claude-haiku-4-5-20251001', 30000, 85, 6
FROM ai_models m, ai_partners p WHERE m.code='text-fast' AND p.code='claude'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'sonnet', 'active', 'claude-sonnet-4-6', 60000, 85, 4
FROM ai_models m, ai_partners p WHERE m.code='text-pro' AND p.code='claude'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;
