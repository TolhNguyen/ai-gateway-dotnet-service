-- Seed partners + models + routes.
-- No API keys here — users add them via the UI.

INSERT INTO ai_partners (code, name, status, adapter_code, base_url, health_check_model, weight, priority, quality_score)
VALUES
    ('gemini',      'Google Gemini',    'active', 'gemini',            'https://generativelanguage.googleapis.com', 'gemini-1.5-flash',                                   100, 1, 90),
    ('groq',        'Groq',             'active', 'openai_compatible', 'https://api.groq.com/openai',               'llama-3.1-8b-instant',                                90, 2, 80),
    ('mistral',     'Mistral AI',       'active', 'openai_compatible', 'https://api.mistral.ai',                    'mistral-small-latest',                                80, 3, 82),
    ('openrouter',  'OpenRouter',       'active', 'openrouter',        'https://openrouter.ai/api',                 'meta-llama/llama-3.2-3b-instruct:free',               70, 4, 75),
    ('fireworks',   'Fireworks AI',     'active', 'openai_compatible', 'https://api.fireworks.ai/inference',        'accounts/fireworks/models/llama-v3p1-8b-instruct',    75, 5, 78)
ON CONFLICT (code) DO NOTHING;

INSERT INTO ai_models (code, name, status, default_temperature, default_max_tokens, strategy, fallback_enabled, max_retry)
VALUES
    ('text-fast', 'Fast Text Model', 'active', 0.7, 1000, 'balanced', TRUE, 3),
    ('text-pro',  'Pro Text Model',  'active', 0.7, 2000, 'balanced', TRUE, 2)
ON CONFLICT (code) DO NOTHING;

-- text-fast routes
INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'flash', 'active', 'gemini-2.5-flash', 30000, 100, 1
FROM ai_models m, ai_partners p WHERE m.code='text-fast' AND p.code='gemini'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'llama-fast', 'active', 'llama-3.1-8b-instant', 20000, 80, 2
FROM ai_models m, ai_partners p WHERE m.code='text-fast' AND p.code='groq'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'small', 'active', 'mistral-small-latest', 30000, 70, 3
FROM ai_models m, ai_partners p WHERE m.code='text-fast' AND p.code='mistral'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'llama-free', 'active', 'meta-llama/llama-3.2-3b-instruct:free', 30000, 60, 4
FROM ai_models m, ai_partners p WHERE m.code='text-fast' AND p.code='openrouter'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

-- text-pro routes
INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'pro', 'active', 'gemini-2.5-pro', 60000, 100, 1
FROM ai_models m, ai_partners p WHERE m.code='text-pro' AND p.code='gemini'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'llama-70b', 'active', 'llama-3.3-70b-versatile', 30000, 80, 2
FROM ai_models m, ai_partners p WHERE m.code='text-pro' AND p.code='groq'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;

INSERT INTO ai_model_routes (model_id, partner_id, route_code, status, provider_model, timeout_ms, weight, priority)
SELECT m.id, p.id, 'large', 'active', 'mistral-large-latest', 45000, 70, 3
FROM ai_models m, ai_partners p WHERE m.code='text-pro' AND p.code='mistral'
ON CONFLICT (model_id, partner_id, route_code) DO NOTHING;
