using PrintServer.Backends.Selection;
using PrintServer.Host;
using PrintServer.Queue;
using PrintServer.Watching;

var builder = WebApplication.CreateBuilder(args);

MachineConfiguration.Add(
    builder.Configuration,
    MachineConfiguration.Resolve(Environment.GetEnvironmentVariable(MachineConfiguration.EnvironmentVariable), HostPlatformDetector.Current()));

builder.Services.AddWindowsService(options => options.ServiceName = "PrintServer");
builder.Services.AddSystemd();

builder.Services.AddPrinterBackend(builder.Configuration);
builder.Services.AddJobQueue(builder.Configuration);
builder.Services.AddFolderWatcher(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

await app.RunAsync().ConfigureAwait(false);
