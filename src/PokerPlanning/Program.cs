using PokerPlanning.Hubs;
using PokerPlanning.Services;

var builder = WebApplication.CreateBuilder(args);

// Structured console logging for HA addon log visibility
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.SignalR", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Connections", LogLevel.Warning);

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});
builder.Services.AddSingleton<RoomService>();

var app = builder.Build();

app.Logger.LogInformation("Planning Poker server starting");

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Prevent aggressive caching of JS/CSS
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
    }
});

app.MapHub<PokerHub>("/pokerhub");

// Background cleanup of disconnected players (every 60 seconds)
var cleanupTimer = new Timer(_ =>
{
    var roomService = app.Services.GetRequiredService<RoomService>();
    roomService.CleanupDisconnected();
}, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));

// Fallback: serve index.html for SPA routes
app.MapFallbackToFile("index.html");

app.Run();
