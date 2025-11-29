using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Model;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Required for Swagger UI
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Memory cache for rate limiting
builder.Services.AddMemoryCache();

// Add YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromMemory(new[]
    {
        new RouteConfig
        {
            RouteId = "all_api",
            Match = new RouteMatch
            {
                Path = "/api/{**catch-all}"
            },
            ClusterId = "api_cluster"
        },
        new RouteConfig
        {
            RouteId = "swagger",
            Match = new RouteMatch
            {
                Path = "/swagger/v1/swagger.json"
            },
            ClusterId = "api_cluster"
        }
    },
    new[]
    {
        new ClusterConfig
        {
            ClusterId = "api_cluster",
            Destinations = new Dictionary<string, DestinationConfig>
            {
                { "destination1", new DestinationConfig { Address = "https://bill-pay-api.onrender.com/" } }
            }
        }
    });

var app = builder.Build();

app.UseRouting();

app.MapControllers(); // Swagger endpoints

// --- Logging + manual rate limiting middleware ---
app.Use(async (context, next) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var logFile = "gateway-logs.txt";

    var request = context.Request;
    var requestTime = DateTime.UtcNow;
    var method = request.Method;
    var path = request.Path + request.QueryString;
    var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    Console.WriteLine("----- Request -----");
    Console.WriteLine($"Timestamp: {requestTime:O}");
    Console.WriteLine($"Method: {method}");
    Console.WriteLine($"Path: {path}");
    Console.WriteLine($"Source IP: {sourceIp}");

    // Manual rate limiting for /query-bill
    if (context.Request.Path.Equals("/api/v1/MobileProviderApp/query-bill", StringComparison.OrdinalIgnoreCase))
    {
        var subscriberNo = context.Request.Query["subscriberNo"].FirstOrDefault() ?? "unknown";
        var cacheKey = $"rate_limit:{subscriberNo}:{DateTime.UtcNow:yyyyMMdd}";
        var memoryCache = app.Services.GetRequiredService<IMemoryCache>();

        var count = memoryCache.GetOrCreate(cacheKey, entry =>
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

        memoryCache.Set(cacheKey, count + 1);
    }

    await next();

    stopwatch.Stop();
    var response = context.Response;
    var statusCode = response.StatusCode;
    var latencyMs = stopwatch.ElapsedMilliseconds;

    Console.WriteLine("----- Response -----");
    Console.WriteLine($"Status code: {statusCode}");
    Console.WriteLine($"Latency: {latencyMs} ms");
    Console.WriteLine("-------------------");
});

// Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mobile Provider API v1");
});

// Reverse proxy (YARP)
app.MapReverseProxy();

app.Run();
