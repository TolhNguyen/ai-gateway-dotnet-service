# API

## Public

```txt
GET /health
POST /v1/ai/generate
```

## Admin protected

Các endpoint sau cần header:

```http
X-Admin-Key: <admin-key>
```

hoặc:

```http
Authorization: Bearer <admin-key>
```

Protected paths:

```txt
/v1/admin/*
/v1/dashboard/*
/v1/debug/*
```

Dev key trong docker compose:

```txt
change-me-admin-key
```

## Generate

```http
POST /v1/ai/generate
```

```json
{
  "model": "text-fast",
  "systemPrompt": "Bạn là trợ lý viết nội dung bán hàng.",
  "prompt": "Viết mô tả sản phẩm áo thun nam cotton.",
  "debug": true,
  "clientCode": "product-service",
  "featureCode": "product_description"
}
```

## Admin model route

```http
POST /v1/admin/models/{modelCode}/routes
```

```json
{
  "partnerCode": "gemini",
  "routeCode": "default",
  "providerModel": "gemini-1.5-flash",
  "timeoutMs": 30000,
  "weight": 100,
  "priority": 1
}
```

`routeCode` cho phép nhiều route cùng model + partner.

## Dashboard

```txt
GET /v1/dashboard/overview
GET /v1/dashboard/models
GET /v1/dashboard/partners
GET /v1/dashboard/accounts
GET /v1/dashboard/clients
GET /v1/dashboard/errors
GET /v1/dashboard/health
```

Dashboard static:

```txt
/dashboard/index.html
```

Static HTML/CSS/JS không chứa data nhạy cảm. Data API vẫn cần admin key.
