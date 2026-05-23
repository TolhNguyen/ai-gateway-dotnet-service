-- Migration 004: Add default_model_code to user_account_keys
-- Allows users to configure a default model per API key/partner.

ALTER TABLE user_account_keys
    ADD COLUMN IF NOT EXISTS default_model_code TEXT;
