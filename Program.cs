using BillingGateway.Middelware;
using Microsoft.OpenApi.Models;
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
    });

builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Mobile Provider API Gateway",
        Version = "v1"
    });

    // JWT Bearer support
    var jwtScheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter 'Bearer {token}'"
    };
    c.AddSecurityDefinition("Bearer", jwtScheme);

    // Require JWT for endpoints that need authorization
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { jwtScheme, new string[] {} }
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
