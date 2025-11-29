using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace BillingGateway.Middelware;

public class GatewayMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly string _jwtKey;
    private readonly string _jwtIssuer;
    private readonly string _jwtAudience;
    private readonly string _gatewaySecret;

    public GatewayMiddleware(RequestDelegate next, IMemoryCache cache, IConfiguration config)
    {
        _next = next;
        _cache = cache;
        _jwtKey = config["Jwt_Key"] ?? "!";
        _jwtIssuer = config["Jwt_Issuer"] ?? "";
        _jwtAudience = config["Jwt_Audience"] ?? "";
        _gatewaySecret = config["GatewaySecret"] ?? "super-secret";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var request = context.Request;
        var requestTime = DateTime.UtcNow;
        var method = request.Method;
        var path = request.Path + request.QueryString;
        var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
        var requestSize = request.ContentLength ?? 0;

        // --- Logging request ---
        Console.WriteLine("----- Request -----");
        Console.WriteLine($"Timestamp: {requestTime:O}");
        Console.WriteLine($"Method: {method}");
        Console.WriteLine($"Path: {path}");
        Console.WriteLine($"Source IP: {sourceIp}");
        Console.WriteLine($"Request size: {requestSize} bytes");
        Console.WriteLine($"Headers: {System.Text.Json.JsonSerializer.Serialize(headers)}");

        // --- JWT Authentication check ---
        bool isAuthenticated = false;
        if (request.Headers.TryGetValue("Authorization", out var authHeader) &&
            authHeader.ToString().StartsWith("Bearer "))
        {
            var token = authHeader.ToString().Substring("Bearer ".Length).Trim();
            var tokenHandler = new JwtSecurityTokenHandler();
            try
            {
                var key = Encoding.UTF8.GetBytes(_jwtKey);
                tokenHandler.ValidateToken(token, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateIssuerSigningKey = true,
                    ValidateLifetime = true,
                    ValidIssuer = _jwtIssuer,
                    ValidAudience = _jwtAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                }, out var validatedToken);

                isAuthenticated = true;
            }
            catch
            {
                isAuthenticated = false;
            }
        }

        Console.WriteLine($"Authentication succeeded: {isAuthenticated}");

        // --- Rate limiting example for /query-bill ---
        if (request.Path.StartsWithSegments("/api/v1/MobileProviderApp/query-bill"))
        {
            var subscriberNo = request.Query["subscriberNo"].FirstOrDefault() ?? "unknown";
            var cacheKey = $"rate_limit:{subscriberNo}:{DateTime.UtcNow:yyyyMMdd}";
            var count = _cache.GetOrCreate(cacheKey, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1);
                return 0;
            });

            if (count >= 3)
            {
                context.Response.StatusCode = 429;
                await context.Response.WriteAsync("Rate limit exceeded");
                return;
            }

            _cache.Set(cacheKey, count + 1);
        }

        // --- Gateway secret header for downstream API ---
        context.Request.Headers["X-Gateway-Secret"] = _gatewaySecret;

        // Call next middleware
        await _next(context);

        stopwatch.Stop();
        var response = context.Response;
        var latencyMs = stopwatch.ElapsedMilliseconds;

        // --- Logging response ---
        Console.WriteLine("----- Response -----");
        Console.WriteLine($"Status code: {response.StatusCode}");
        Console.WriteLine($"Latency: {latencyMs} ms");
        Console.WriteLine("-------------------");
    }
}

// --- Extension method for easy registration ---
public static class GatewayMiddlewareExtensions
{
    public static IApplicationBuilder UseGatewayMiddleware(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<GatewayMiddleware>();
    }
}

