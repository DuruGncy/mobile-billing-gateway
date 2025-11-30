# Mobile Billing API Gateway

Lightweight YARP-based API Gateway for the Mobile Billing system. It routes requests to the billing backend, enforces rate limits, injects a gateway secret header, and logs requests/responses. Built on .NET 9 and intended to run locally, in Docker, or on Render.com.

Live deployments
- Backend API: https://bill-pay-api.onrender.com
- Gateway: https://mobile-billing-gateway-2kqo.onrender.com

Quick summary
- Purpose: Provide routing, centralized logging, rate limiting and header injection for backend billing services.
- Tech: .NET 9, YARP, Serilog, IMemoryCache
- Key behavior: Limits `/query-bill` to 3 requests per subscriber per day, adds `X-Gateway-Secret` header for downstream validation, logs request/response metadata (method, path, headers, IP, sizes, status, latency).

Table of contents
- Features
- How it works (middleware flow)
- Configuration
- Run — Local, Docker, Render
- Examples (curl)
- Troubleshooting
- Contributing & License

Features
- Reverse proxy and routing via YARP
- Request/response logging with Serilog
- Rate limiting for `/query-bill` (3 requests per subscriber per day) using `IMemoryCache`
- Adds `X-Gateway-Secret` header to proxied requests (configurable)
- Centralized middleware in `GateWayMiddleware.cs` for logging, rate limiting and header injection
- Configurable via `appsettings.json` and environment variables

How it works (middleware flow)
1. Incoming request arrives at the gateway.
2. Gateway middleware:
   - Logs request metadata (timestamp, method, path, source IP, headers, request size).
   - If path is `/query-bill`, enforces per-subscriber rate limit (3/day). If exceeded, returns HTTP 429.
   - Injects `X-Gateway-Secret` header for downstream APIs (value comes from configuration).
3. Request is forwarded to the appropriate backend via YARP.
4. On response, gateway logs status code, response latency and response size, then forwards the response to the client.

If the per-subscriber daily limit is exceeded you will get HTTP 429 with a "Rate Limit Exceeded" message.

Notes about `X-Gateway-Secret`
- The gateway automatically injects `X-Gateway-Secret` into requests forwarded to downstream services. Configure the secret in `appsettings.json` or via `GatewaySecret` environment variable.
- Downstream services should validate this header for requests originating from the gateway.



