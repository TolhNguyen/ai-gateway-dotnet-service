-- Optional migration if you already ran the previous MVP schema.
-- For a fresh dev DB, use migrations/001_init.sql only.

ALTER TABLE ai_model_routes
ADD COLUMN IF NOT EXISTS route_code VARCHAR(100) NOT NULL DEFAULT 'default';

DO $$
BEGIN
    IF EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'ai_model_routes_model_id_partner_id_key'
    ) THEN
        ALTER TABLE ai_model_routes
        DROP CONSTRAINT ai_model_routes_model_id_partner_id_key;
    END IF;
END $$;

CREATE UNIQUE INDEX IF NOT EXISTS ux_ai_model_routes_model_partner_route_code
ON ai_model_routes(model_id, partner_id, route_code);
