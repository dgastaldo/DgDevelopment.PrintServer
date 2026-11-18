using PrintServer.Backends.Selection;
using PrintServer.Watching;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPrinterBackend(builder.Configuration);
builder.Services.AddFolderWatcher(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync().ConfigureAwait(false);
