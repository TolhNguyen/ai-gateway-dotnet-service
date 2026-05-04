# Roadmap

## Đã xử lý trong bản hiện tại

- Admin/dashboard/debug auth.
- Redis Lua atomic client rate-limit/account quota.
- Token reserve + actual usage adjustment.
- Redis config cache cho hot path.
- Error event cap.
- `routeCode` trong `ai_model_routes`.

## Nên làm tiếp

1. Tối ưu dashboard health nếu số account lớn.
2. Circuit breaker full state `closed/open/half_open`.
3. Prompt template API.
4. Response cache.
5. JSON schema response validation.
6. OpenAI-compatible facade `/v1/chat/completions`.
7. Streaming.
8. Tokenizer chính xác theo provider/model.
9. Admin UI chỉnh config nếu cần, vẫn có thể giữ HTML/CSS/JS thuần.
