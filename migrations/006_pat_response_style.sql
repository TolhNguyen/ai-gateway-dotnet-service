ALTER TABLE user_personal_access_tokens
ADD COLUMN IF NOT EXISTS response_style VARCHAR(30) NOT NULL DEFAULT 'normal';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'chk_pat_response_style'
    ) THEN
        ALTER TABLE user_personal_access_tokens
        ADD CONSTRAINT chk_pat_response_style
        CHECK (response_style IN ('normal', 'caveman'));
    END IF;
END $$;
