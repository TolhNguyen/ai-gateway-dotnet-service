CREATE TABLE IF NOT EXISTS ai_clients (
    id BIGSERIAL PRIMARY KEY,
    code VARCHAR(100) UNIQUE NOT NULL,
    name VARCHAR(255) NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'active',
    api_key_hash VARCHAR(255) NULL,
    rpm_limit INT NULL,
    rpd_limit INT NULL,
    allowed_models JSONB NOT NULL DEFAULT '[]'::jsonb,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_models (
    id BIGSERIAL PRIMARY KEY,
    code VARCHAR(100) UNIQUE NOT NULL,
    name VARCHAR(255) NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'active',
    default_temperature NUMERIC(4,2) DEFAULT 0.7,
    default_max_tokens INT DEFAULT 1000,
    strategy VARCHAR(100) NOT NULL DEFAULT 'balanced',
    fallback_enabled BOOLEAN DEFAULT TRUE,
    max_retry INT DEFAULT 3,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_partners (
    id BIGSERIAL PRIMARY KEY,
    code VARCHAR(100) UNIQUE NOT NULL,
    name VARCHAR(255) NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'active',
    adapter_code VARCHAR(100) NOT NULL,
    base_url TEXT NOT NULL,
    weight INT DEFAULT 100,
    priority INT DEFAULT 100,
    quality_score INT DEFAULT 100,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_accounts (
    id BIGSERIAL PRIMARY KEY,
    partner_id BIGINT NOT NULL REFERENCES ai_partners(id) ON DELETE CASCADE,
    code VARCHAR(100) UNIQUE NOT NULL,
    name VARCHAR(255) NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'active',
    account_ref VARCHAR(255) NULL,
    api_key_enc TEXT NULL,
    api_key_ref TEXT NULL,
    rpm_limit INT NULL,
    rpd_limit INT NULL,
    tpm_limit INT NULL,
    tpd_limit INT NULL,
    weight INT DEFAULT 100,
    priority INT DEFAULT 100,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_ai_accounts_partner_id ON ai_accounts(partner_id);
CREATE INDEX IF NOT EXISTS idx_ai_accounts_status ON ai_accounts(status);

CREATE TABLE IF NOT EXISTS ai_model_routes (
    id BIGSERIAL PRIMARY KEY,
    model_id BIGINT NOT NULL REFERENCES ai_models(id) ON DELETE CASCADE,
    partner_id BIGINT NOT NULL REFERENCES ai_partners(id) ON DELETE CASCADE,
    route_code VARCHAR(100) NOT NULL DEFAULT 'default',
    status VARCHAR(50) NOT NULL DEFAULT 'active',
    provider_model VARCHAR(255) NOT NULL,
    timeout_ms INT DEFAULT 30000,
    weight INT DEFAULT 100,
    priority INT DEFAULT 100,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(model_id, partner_id, route_code)
);

CREATE INDEX IF NOT EXISTS idx_ai_model_routes_model_id ON ai_model_routes(model_id);
CREATE INDEX IF NOT EXISTS idx_ai_model_routes_partner_id ON ai_model_routes(partner_id);
CREATE INDEX IF NOT EXISTS idx_ai_model_routes_status ON ai_model_routes(status);

CREATE TABLE IF NOT EXISTS ai_prompt_templates (
    id BIGSERIAL PRIMARY KEY,
    code VARCHAR(100) UNIQUE NOT NULL,
    name VARCHAR(255) NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'active',
    model_code VARCHAR(100) NULL,
    system_prompt TEXT NULL,
    user_prompt_template TEXT NOT NULL,
    version INT DEFAULT 1,
    config JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS ai_request_metrics_hourly (
    id BIGSERIAL PRIMARY KEY,
    bucket_hour TIMESTAMPTZ NOT NULL,
    client_code VARCHAR(100) NOT NULL DEFAULT 'anonymous',
    model_code VARCHAR(100) NOT NULL,
    partner_code VARCHAR(100) NOT NULL,
    account_code VARCHAR(100) NOT NULL,
    total_count BIGINT DEFAULT 0,
    success_count BIGINT DEFAULT 0,
    failed_count BIGINT DEFAULT 0,
    fallback_success_count BIGINT DEFAULT 0,
    input_tokens BIGINT DEFAULT 0,
    output_tokens BIGINT DEFAULT 0,
    total_tokens BIGINT DEFAULT 0,
    latency_total_ms BIGINT DEFAULT 0,
    latency_count BIGINT DEFAULT 0,
    error_rate_limit BIGINT DEFAULT 0,
    error_quota_exceeded BIGINT DEFAULT 0,
    error_timeout BIGINT DEFAULT 0,
    error_server_error BIGINT DEFAULT 0,
    error_auth_error BIGINT DEFAULT 0,
    error_permission_error BIGINT DEFAULT 0,
    error_bad_response BIGINT DEFAULT 0,
    error_unknown BIGINT DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(bucket_hour, client_code, model_code, partner_code, account_code)
);

CREATE INDEX IF NOT EXISTS idx_ai_metrics_hourly_bucket ON ai_request_metrics_hourly(bucket_hour);
CREATE INDEX IF NOT EXISTS idx_ai_metrics_hourly_model ON ai_request_metrics_hourly(model_code, bucket_hour);
CREATE INDEX IF NOT EXISTS idx_ai_metrics_hourly_partner ON ai_request_metrics_hourly(partner_code, bucket_hour);

CREATE TABLE IF NOT EXISTS ai_error_aggregates (
    id BIGSERIAL PRIMARY KEY,
    client_code VARCHAR(100) NOT NULL DEFAULT 'anonymous',
    model_code VARCHAR(100) NOT NULL,
    partner_code VARCHAR(100) NOT NULL,
    account_code VARCHAR(100) NOT NULL,
    error_type VARCHAR(100) NOT NULL,
    error_code VARCHAR(255) NOT NULL DEFAULT 'none',
    http_status INT NOT NULL DEFAULT 0,
    count BIGINT DEFAULT 0,
    first_seen_at TIMESTAMPTZ NULL,
    last_seen_at TIMESTAMPTZ NULL,
    last_message TEXT NULL,
    created_at TIMESTAMPTZ DEFAULT NOW(),
    updated_at TIMESTAMPTZ DEFAULT NOW(),
    UNIQUE(client_code, model_code, partner_code, account_code, error_type, error_code, http_status)
);

CREATE INDEX IF NOT EXISTS idx_ai_error_aggregates_last_seen ON ai_error_aggregates(last_seen_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_error_aggregates_account ON ai_error_aggregates(account_code, last_seen_at DESC);

CREATE TABLE IF NOT EXISTS ai_error_events (
    id BIGSERIAL PRIMARY KEY,
    request_id VARCHAR(100) NULL,
    client_code VARCHAR(100) NOT NULL DEFAULT 'anonymous',
    model_code VARCHAR(100) NOT NULL,
    partner_code VARCHAR(100) NOT NULL,
    account_code VARCHAR(100) NOT NULL,
    error_type VARCHAR(100) NOT NULL,
    error_code VARCHAR(255) NULL,
    http_status INT NULL,
    message TEXT NULL,
    latency_ms INT NULL,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_ai_error_events_created ON ai_error_events(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_error_events_request ON ai_error_events(request_id);
