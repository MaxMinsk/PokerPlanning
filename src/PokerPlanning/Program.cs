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
app.UseStaticFiles();

app.MapHub<PokerHub>("/pokerhub");

// Fallback: serve index.html for SPA routes
app.MapFallbackToFile("index.html");

app.Run();
