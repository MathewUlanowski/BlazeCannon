using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using BlazeCannon.Protocol;
using BlazeCannon.Proxy;
using BlazeCannon.Scanner;
using BlazeCannon.Browser;

var builder = WebApplication.CreateBuilder(args);

// Configure dual-port Kestrel: UI + MITM Proxy
var proxyPort = int.Parse(Environment.GetEnvironmentVariable("BLAZECANNON_PROXY_PORT") ?? "5001");
var uiPort = int.Parse(Environment.GetEnvironmentVariable("BLAZECANNON_UI_PORT") ?? "8080");

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(uiPort);    // BlazeCannon UI
    options.ListenAnyIP(proxyPort); // MITM Proxy
});

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// BlazeCannon services
builder.Services.AddSingleton<TargetConfig>();
builder.Services.AddSingleton<IProtocolDecoder, BlazorProtocolDecoder>();
builder.Services.AddSingleton<IProtocolEncoder, BlazorProtocolEncoder>();
builder.Services.AddSingleton<ITrafficProxy, BlazorInterceptingProxy>();
builder.Services.AddSingleton<IBrowserEngine, PlaywrightBrowserEngine>();
builder.Services.AddTransient<IVulnerabilityScanner, VulnerabilityScanner>();
builder.Services.AddTransient<EvidenceAnalyzer>();
builder.Services.AddTransient<TrafficRecorder>();
builder.Services.AddSingleton<BlazeCannon.App.Services.AppStateService>();

// MITM Proxy services
builder.Services.AddSingleton<MitmProxyService>();
builder.Services.AddSingleton<IMitmProxy>(sp => sp.GetRequiredService<MitmProxyService>());
builder.Services.AddHttpClient("MitmProxy")
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        // Accept any cert for pen testing targets
        ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        AllowAutoRedirect = false
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// WebSockets must be enabled before the MITM middleware
app.UseWebSockets();

// Route by port: proxy traffic vs UI traffic
app.UseWhen(
    context => context.Connection.LocalPort == proxyPort,
    proxyApp => proxyApp.UseMiddleware<MitmProxyMiddleware>()
);

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
