using BlazeCannon.Core.Interfaces;
using BlazeCannon.Core.Models;
using BlazeCannon.Protocol;
using BlazeCannon.Proxy;
using BlazeCannon.Scanner;
using BlazeCannon.Browser;

var builder = WebApplication.CreateBuilder(args);

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

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
