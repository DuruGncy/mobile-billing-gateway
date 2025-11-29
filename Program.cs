using BillingGateway.Middelware;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Required for Swagger UI
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

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
    })
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(builderContext =>
    {
        builderContext.AddRequestTransform(async transformContext =>
        {
            if (transformContext.HttpContext.Request.Headers.TryGetValue("Authorization", out var auth))
            {
                transformContext.ProxyRequest.Headers.Add("Authorization", auth.ToString());
            }
        });
    });

var app = builder.Build();

app.UseRouting();

app.MapControllers(); // Swagger endpoints

app.UseGatewayMiddleware();

// Swagger UI — load the API's swagger.json directly from the upstream API
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("https://bill-pay-api.onrender.com/swagger/v1/swagger.json", "Mobile Provider API v1");
});

// Reverse proxy (YARP)
app.MapReverseProxy();

app.Run();
