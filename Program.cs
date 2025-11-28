using AspNetCoreRateLimit;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

// Add Ocelot configuration
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);

builder.Services.AddOcelot(builder.Configuration);

builder.Services.AddSwaggerGen();
builder.Services.AddSwaggerForOcelot(builder.Configuration);

builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests = false;
    options.HttpStatusCode = 429;
    options.RealIpHeader = "X-Real-IP";
    options.ClientIdHeader = "X-ClientId"; // This will be our subscriberNo
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "/v1/MobileProviderApp/query-bill", // Only limit this endpoint
            Period = "1d",
            Limit = 3
        }
    };
});
builder.Services.AddInMemoryRateLimiting();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

var app = builder.Build();

app.UseRouting();

//logging middleware
app.Use(async (context, next) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var logFile = "gateway-logs.txt";

    // Request-level logging
    var request = context.Request;
    var requestTime = DateTime.UtcNow;
    var method = request.Method;
    var path = request.Path + request.QueryString;
    var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
    var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    // Read request body size
    long? requestSize = null;
    if (request.ContentLength.HasValue)
        requestSize = request.ContentLength.Value;

    // Authentication success (optional if you use JWT)
    var authSucceeded = context.User?.Identity?.IsAuthenticated ?? false;

    Console.WriteLine("----- Request -----");
    Console.WriteLine($"Timestamp: {requestTime:O}");
    Console.WriteLine($"Method: {method}");
    Console.WriteLine($"Path: {path}");
    Console.WriteLine($"Source IP: {sourceIp}");
    Console.WriteLine($"Headers: {System.Text.Json.JsonSerializer.Serialize(headers)}");
    Console.WriteLine($"Request size: {requestSize} bytes");
    Console.WriteLine($"Auth succeeded: {authSucceeded}");

    await File.AppendAllTextAsync(logFile, $"Request: {method} {path} from {sourceIp} at {requestTime:O}\n");

    // Add header for Rate Limit
    if (context.Request.Path.StartsWithSegments("/v1/MobileProviderApp/query-bill"))
    {
        var subscriberNo = context.Request.Query["subscriberNo"].FirstOrDefault() ?? "unknown";
        context.Request.Headers["X-ClientId"] = subscriberNo;

    }

    // Call downstream
    await next();

    // Response-level logging
    stopwatch.Stop();
    var response = context.Response;
    var statusCode = response.StatusCode;
    var responseSize = response.ContentLength ?? -1;
    var latencyMs = stopwatch.ElapsedMilliseconds;

    Console.WriteLine("----- Response -----");
    Console.WriteLine($"Status code: {statusCode}");
    Console.WriteLine($"Latency: {latencyMs} ms");
    Console.WriteLine($"Response size: {responseSize} bytes");

    Console.WriteLine("-------------------");

    await File.AppendAllTextAsync(logFile, $"Response: {statusCode} in {latencyMs} ms, {responseSize} bytes\n\n");


});


app.UseSwagger();
app.UseSwaggerForOcelotUI();

// Enable rate limiting
app.UseIpRateLimiting();

await app.UseOcelot();

app.Run();
