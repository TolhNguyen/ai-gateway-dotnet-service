-- =============================================================
-- AI Gateway v2 schema
-- =============================================================

-- ---- Schema migration tracker ------------------------------------------------
CREATE TABLE IF NOT EXISTS __schema_migrations (
    version    TEXT PRIMARY KEY,
    name       TEXT NOT NULL,
    checksum   TEXT NOT NULL,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ---- Users -------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS users (
    id            BIGSERIAL PRIMARY KEY,
    email         VARCHAR(255) UNIQUE NOT NULL,
    password_hash TEXT NOT NULL,
    role          VARCHAR(20) NOT NULL DEFAULT 'user',   -- 'user' | 'admin'
    status        VARCHAR(20) NOT NULL DEFAULT 'active', -- 'active' | 'disabled'
    display_name  VARCHAR(255),
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_users_status ON users(status);

-- ---- Personal Access Tokens --------------------------------------------------
CREATE TABLE IF NOT EXISTS user_personal_access_tokens (
    id            BIGSERIAL PRIMARY KEY,
    user_id       BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    name          VARCHAR(255) NOT NULL,
    token_hash    TEXT NOT NULL,            -- SHA-256 of raw token
    token_prefix  VARCHAR(32) NOT NULL,     -- first 8 chars (for display only)
    last_used_at  TIMESTAMPTZ,
    expires_at    TIMESTAMPTZ,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS idx_pat_token_hash ON user_personal_access_tokens(token_hash);
CREATE INDEX IF NOT EXISTS idx_pat_user ON user_personal_access_tokens(user_id);

-- ---- AI Partners (system-wide; admin manages) -------------------------------
CREATE TABLE IF NOT EXISTS ai_partners (
    id                       BIGSERIAL PRIMARY KEY,
    code                     VARCHAR(100) UNIQUE NOT NULL,
    name                     VARCHAR(255) NOT NULL,
    status                   VARCHAR(20) NOT NULL DEFAULT 'active',
    adapter_code             VARCHAR(100) NOT NULL,        -- gemini | openai_compatible | openrouter
    base_url                 TEXT NOT NULL,
    health_check_model       VARCHAR(255),                 -- provider model used for cheap health pings
    weight                   INT NOT NULL DEFAULT 100,
    priority                 INT NOT NULL DEFAULT 100,
    quality_score            INT NOT NULL DEFAULT 100,
    config                   JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ---- AI Models (system-wide alias; admin manages) ---------------------------
CREATE TABLE IF NOT EXISTS ai_models (
    id                   BIGSERIAL PRIMARY KEY,
    code                 VARCHAR(100) UNIQUE NOT NULL,   -- internal alias e.g. 'text-fast'
    name                 VARCHAR(255) NOT NULL,
    status               VARCHAR(20) NOT NULL DEFAULT 'active',
    default_temperature  NUMERIC(4,2) NOT NULL DEFAULT 0.7,
    default_max_tokens   INT NOT NULL DEFAULT 1000,
    strategy             VARCHAR(100) NOT NULL DEFAULT 'balanced',
    fallback_enabled     BOOLEAN NOT NULL DEFAULT TRUE,
    max_retry            INT NOT NULL DEFAULT 3,
    config               JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

-- ---- Model Routes (system-wide: which partner+providerModel for each model) -
CREATE TABLE IF NOT EXISTS ai_model_routes (
    id              BIGSERIAL PRIMARY KEY,
    model_id        BIGINT NOT NULL REFERENCES ai_models(id) ON DELETE CASCADE,
    partner_id      BIGINT NOT NULL REFERENCES ai_partners(id) ON DELETE CASCADE,
    route_code      VARCHAR(100) NOT NULL DEFAULT 'default',
    status          VARCHAR(20) NOT NULL DEFAULT 'active',
    provider_model  VARCHAR(255) NOT NULL,
    timeout_ms      INT NOT NULL DEFAULT 30000,
    weight          INT NOT NULL DEFAULT 100,
    priority        INT NOT NULL DEFAULT 100,
    config          JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(model_id, partner_id, route_code)
);
CREATE INDEX IF NOT EXISTS idx_routes_model ON ai_model_routes(model_id);
CREATE INDEX IF NOT EXISTS idx_routes_partner ON ai_model_routes(partner_id);

-- ---- User-owned API keys (per-tenant) ---------------------------------------
CREATE TABLE IF NOT EXISTS user_account_keys (
    id                     BIGSERIAL PRIMARY KEY,
    user_id                BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    partner_id             BIGINT NOT NULL REFERENCES ai_partners(id) ON DELETE CASCADE,
    code                   VARCHAR(100) NOT NULL,        -- user-defined slug, unique within user
    name                   VARCHAR(255),
    status                 VARCHAR(20) NOT NULL DEFAULT 'active',
    api_key_enc            TEXT NOT NULL,                -- AES-256-GCM ciphertext
    api_key_fingerprint    VARCHAR(64) NOT NULL,         -- SHA-256 hex of the raw key (for dedup detection)
    rpm_limit              INT,
    rpd_limit              INT,
    tpm_limit              INT,
    tpd_limit              INT,
    weight                 INT NOT NULL DEFAULT 100,
    priority               INT NOT NULL DEFAULT 100,
    last_health_check_at   TIMESTAMPTZ,
    last_health_status     VARCHAR(20),                  -- 'ok' | 'error' | 'unknown'
    last_health_error      TEXT,
    last_health_latency_ms INT,
    config                 JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at             TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, code)
);
CREATE INDEX IF NOT EXISTS idx_user_keys_user ON user_account_keys(user_id);
CREATE INDEX IF NOT EXISTS idx_user_keys_partner ON user_account_keys(partner_id);
CREATE INDEX IF NOT EXISTS idx_user_keys_status ON user_account_keys(status);

-- ---- Metrics per-user (hourly buckets, flushed from Redis) ------------------
CREATE TABLE IF NOT EXISTS ai_request_metrics_hourly (
    id                       BIGSERIAL PRIMARY KEY,
    bucket_hour              TIMESTAMPTZ NOT NULL,
    user_id                  BIGINT NOT NULL,
    model_code               VARCHAR(100) NOT NULL,
    partner_code             VARCHAR(100) NOT NULL,
    account_key_code         VARCHAR(100) NOT NULL,
    total_count              BIGINT NOT NULL DEFAULT 0,
    success_count            BIGINT NOT NULL DEFAULT 0,
    failed_count             BIGINT NOT NULL DEFAULT 0,
    fallback_success_count   BIGINT NOT NULL DEFAULT 0,
    input_tokens             BIGINT NOT NULL DEFAULT 0,
    output_tokens            BIGINT NOT NULL DEFAULT 0,
    total_tokens             BIGINT NOT NULL DEFAULT 0,
    latency_total_ms         BIGINT NOT NULL DEFAULT 0,
    latency_count            BIGINT NOT NULL DEFAULT 0,
    error_rate_limit         BIGINT NOT NULL DEFAULT 0,
    error_quota_exceeded     BIGINT NOT NULL DEFAULT 0,
    error_timeout            BIGINT NOT NULL DEFAULT 0,
    error_server_error       BIGINT NOT NULL DEFAULT 0,
    error_auth_error         BIGINT NOT NULL DEFAULT 0,
    error_permission_error   BIGINT NOT NULL DEFAULT 0,
    error_bad_response       BIGINT NOT NULL DEFAULT 0,
    error_unknown            BIGINT NOT NULL DEFAULT 0,
    created_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at               TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(bucket_hour, user_id, model_code, partner_code, account_key_code)
);
CREATE INDEX IF NOT EXISTS idx_metrics_user_bucket ON ai_request_metrics_hourly(user_id, bucket_hour DESC);
CREATE INDEX IF NOT EXISTS idx_metrics_bucket ON ai_request_metrics_hourly(bucket_hour);

-- ---- Error aggregates per-user ----------------------------------------------
CREATE TABLE IF NOT EXISTS ai_error_aggregates (
    id                 BIGSERIAL PRIMARY KEY,
    user_id            BIGINT NOT NULL,
    model_code         VARCHAR(100) NOT NULL,
    partner_code       VARCHAR(100) NOT NULL,
    account_key_code   VARCHAR(100) NOT NULL,
    error_type         VARCHAR(100) NOT NULL,
    error_code         VARCHAR(255) NOT NULL DEFAULT 'none',
    http_status        INT NOT NULL DEFAULT 0,
    count              BIGINT NOT NULL DEFAULT 0,
    first_seen_at      TIMESTAMPTZ,
    last_seen_at       TIMESTAMPTZ,
    last_message       TEXT,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE(user_id, model_code, partner_code, account_key_code, error_type, error_code, http_status)
);
CREATE INDEX IF NOT EXISTS idx_err_agg_user ON ai_error_aggregates(user_id, last_seen_at DESC);

-- ---- Error events (sampled / capped at runtime) -----------------------------
CREATE TABLE IF NOT EXISTS ai_error_events (
    id                 BIGSERIAL PRIMARY KEY,
    request_id         VARCHAR(100),
    user_id            BIGINT NOT NULL,
    model_code         VARCHAR(100) NOT NULL,
    partner_code       VARCHAR(100) NOT NULL,
    account_key_code   VARCHAR(100) NOT NULL,
    error_type         VARCHAR(100) NOT NULL,
    error_code         VARCHAR(255),
    http_status        INT,
    message            TEXT,
    latency_ms         INT,
    metadata           JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX IF NOT EXISTS idx_err_events_user_created ON ai_error_events(user_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_err_events_created ON ai_error_events(created_at DESC);
CREATE INDEX IF NOT EXISTS idx_err_events_request ON ai_error_events(request_id);
