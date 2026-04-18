using System.Text.Json.Serialization;
using BlazeCannon.Api.Hubs;
using BlazeCannon.Api.Services;
using BlazeCannon.Browser;
using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using BlazeCannon.Protocol;
using BlazeCannon.Proxy;
using BlazeCannon.Scanner;

var builder = WebApplication.CreateBuilder(args);

// Configure dual-port Kestrel: Api + MITM Proxy
var proxyPort = int.Parse(Environment.GetEnvironmentVariable("BLAZECANNON_PROXY_PORT") ?? "5001");
var uiPort = int.Parse(Environment.GetEnvironmentVariable("BLAZECANNON_UI_PORT") ?? "8080");

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(uiPort);    // BlazeCannon REST API + SignalR hub
    options.ListenAnyIP(proxyPort); // MITM Proxy
});

// REST + SignalR — camelCase JSON, enums as strings, byte[] stays base64 by default
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddSignalR()
    .AddJsonProtocol(o =>
    {
        o.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

// Permissive CORS so the Angular dev server can hit us during development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials());
});

// BlazeCannon services — unchanged business logic, reused as-is
builder.Services.AddSingleton<TargetConfig>();
builder.Services.AddSingleton<IProtocolDecoder, BlazorProtocolDecoder>();
builder.Services.AddSingleton<IProtocolEncoder, BlazorProtocolEncoder>();
builder.Services.AddSingleton<ITrafficProxy, BlazorInterceptingProxy>();
builder.Services.AddSingleton<IBrowserEngine, PlaywrightBrowserEngine>();
builder.Services.AddTransient<IVulnerabilityScanner, VulnerabilityScanner>();
builder.Services.AddTransient<EvidenceAnalyzer>();
builder.Services.AddTransient<TrafficRecorder>();

// MITM proxy + replay staging + broadcast bridge
builder.Services.AddSingleton<MitmProxyService>();
builder.Services.AddSingleton<IMitmProxy>(sp => sp.GetRequiredService<MitmProxyService>());
builder.Services.AddSingleton<ReplayStagingService>();
builder.Services.AddHostedService<TrafficBroadcastService>();

builder.Services.AddHttpClient("MitmProxy")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // Accept any cert for pen testing targets
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        AllowAutoRedirect = false
    });

var app = builder.Build();

// WebSockets must be enabled before the MITM middleware
app.UseWebSockets();

// Route by port: proxy traffic vs API traffic
app.UseWhen(
    context => context.Connection.LocalPort == proxyPort,
    proxyApp => proxyApp.UseMiddleware<MitmProxyMiddleware>()
);

app.UseRouting();
app.UseCors();

app.MapControllers();
app.MapHub<TrafficHub>("/hubs/traffic");

app.Run();
