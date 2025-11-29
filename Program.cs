using AspNetCoreRateLimit;
using Microsoft.Extensions.Caching.Memory;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Required for SwaggerForOcelot
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer(); // adds IApiDescriptionGroupCollectionProvider
builder.Services.AddSwaggerGen(); // must be here even if gateway has no controllers

// Add Ocelot configuration
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
builder.Services.AddOcelot(builder.Configuration);

// Swagger for Ocelot
builder.Services.AddSwaggerForOcelot(builder.Configuration);

// Memory cache for manual rate limiting
builder.Services.AddMemoryCache();

var app = builder.Build();

app.UseRouting();

app.MapControllers(); // register controllers / swagger endpoints

// --- Logging + Manual rate-limiting middleware ---
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

    // --- Manual memory-based rate limiting for /query-bill only ---
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
            return; // stop pipeline
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

// Swagger UI for Ocelot
app.UseSwaggerForOcelotUI(opt =>
{
    opt.PathToSwaggerGenerator = "/swagger/docs";
});

// Ocelot routing (must be last)
await app.UseOcelot();

app.Run();
