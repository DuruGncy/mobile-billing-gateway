using AspNetCoreRateLimit;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Add controllers for Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHttpClient("swaggerClient");

// Add Ocelot configuration
builder.Configuration.AddJsonFile("ocelot.json", optional: false, reloadOnChange: true);
builder.Services.AddOcelot(builder.Configuration);

// Rate limiting
builder.Services.AddMemoryCache();
builder.Services.AddInMemoryRateLimiting();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests = false;
    options.HttpStatusCode = 429;
    options.ClientIdHeader = "X-ClientId"; // We will use subscriberNo
    options.GeneralRules = new List<RateLimitRule>
    {
        new RateLimitRule
        {
            Endpoint = "/mobile/v1/query-bill",
            Period = "1d",
            Limit = 3
        }
    };
});
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

var app = builder.Build();

app.UseRouting();

// Logging middleware
app.Use(async (context, next) =>
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();

    var logFile = "gateway-logs.txt";

    var request = context.Request;
    var requestTime = DateTime.UtcNow;
    var method = request.Method;
    var path = request.Path + request.QueryString;
    var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
    var sourceIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    var requestSize = request.ContentLength ?? 0;
    var authSucceeded = context.User?.Identity?.IsAuthenticated ?? false;


    Console.WriteLine("----- Request -----");
    Console.WriteLine($"Timestamp: {requestTime:O}");
    Console.WriteLine($"Method: {method}");
    Console.WriteLine($"Path: {path}");
    Console.WriteLine($"Source IP: {sourceIp}");
    Console.WriteLine($"Headers: {System.Text.Json.JsonSerializer.Serialize(headers)}");
    Console.WriteLine($"Request size: {requestSize} bytes");
    Console.WriteLine($"Auth succeeded: {authSucceeded}");

    await File.AppendAllTextAsync(logFile, $"[{requestTime:O}] Request: {method} {path} from {sourceIp}, Auth: {authSucceeded}, Size: {requestSize} bytes\n");

    // Add X-ClientId header for rate limiting
    if (context.Request.Path.StartsWithSegments("/mobile/v1/query-bill"))
    {
        var subscriberNo = context.Request.Query["subscriberNo"].FirstOrDefault() ?? "unknown";
        context.Request.Headers["X-ClientId"] = subscriberNo;
    }

    await next();

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

    await File.AppendAllTextAsync(logFile, $"[{DateTime.UtcNow:O}] Response: {statusCode}, Size: {responseSize} bytes, Latency: {latencyMs} ms\n\n");
});

// Rate limiting
app.UseIpRateLimiting();

// Swagger UI
app.MapGet("/swagger/v1/swagger.json", async (IHttpClientFactory httpClientFactory) =>
{
    var client = httpClientFactory.CreateClient("swaggerClient");

    var json = await client.GetStringAsync(
        "https://bill-pay-api.onrender.com/swagger/v1/swagger.json"
    );

    return Results.Content(json, "application/json");
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Mobile Billing Gateway v1");
});

// Map controllers (required for Swagger)
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});

// Ocelot routing
await app.UseOcelot();

app.Run();
