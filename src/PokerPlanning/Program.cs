using PokerPlanning.Hubs;
using PokerPlanning.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
});
builder.Services.AddSingleton<RoomService>();

var app = builder.Build();

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
