-- Update the health check model for Google Gemini partner
-- since gemini-1.5-flash is retired/deprecated in the Gemini API.
-- gemini-2.5-flash is the currently active and recommended GA model.

UPDATE ai_partners
SET health_check_model = 'gemini-2.5-flash'
WHERE code = 'gemini';
