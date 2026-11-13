using PrintServer.Backends.Selection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddPrinterBackend(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
