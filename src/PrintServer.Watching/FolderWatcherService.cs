using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PrintServer.Core.Watching;

namespace PrintServer.Watching;

public sealed partial class FolderWatcherService : BackgroundService
{
    private static readonly TimeSpan MinTick = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxTick = TimeSpan.FromSeconds(1);

    private readonly WatcherOptions _options;
    private readonly Channel<DetectedFile> _channel;
    private readonly IFileSystem _fileSystem;
    private readonly IFolderChangeNotifier _notifier;
    private readonly ILogger<FolderWatcherService> _logger;
    private readonly FolderScanner _scanner;
    private readonly FileStabilityTracker _tracker;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly string _root;
    private readonly TimeSpan _tick;
    private IDisposable? _subscription;
    private bool _pollingOnly;
    private bool _missingLogged;
    private int _signalled;

    public FolderWatcherService(
        IOptions<WatcherOptions> options,
        Channel<DetectedFile> channel,
        IFileSystem fileSystem,
        IFolderChangeNotifier notifier,
        TimeProvider time,
        ILogger<FolderWatcherService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(notifier);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _channel = channel;
        _fileSystem = fileSystem;
        _notifier = notifier;
        _logger = logger;
        _root = System.IO.Path.GetFullPath(_options.Path ?? throw new InvalidOperationException("Watcher:Path is required."));
        _scanner = new FolderScanner(fileSystem, new FileFilter(_options), _options);
        _tracker = new FileStabilityTracker(time, _options.StabilityWindow);
        _tick = TimeSpan.FromTicks(Math.Clamp((_options.StabilityWindow / 4).Ticks, MinTick.Ticks, MaxTick.Ticks));
        _pollingOnly = _options.UsePolling;
    }

    public override void Dispose()
    {
        _subscription?.Dispose();
        _signal.Dispose();
        base.Dispose();
        GC.SuppressFinalize(this);
    }

    internal async Task ScanOnceAsync(bool initial, CancellationToken ct)
    {
        var files = _scanner.Scan(_root);
        if (initial && !_options.ProcessExistingFiles)
        {
            _tracker.Seed(files);
            return;
        }

        foreach (var file in _tracker.Update(files))
        {
            LogDetected(file.Entry.Path, file.PrinterName);
            await _channel.Writer
                .WriteAsync(new DetectedFile(file.Entry.Path, file.PrinterName, file.Entry.Length, file.Entry.CreatedUtc), ct)
                .ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(_root, _pollingOnly);
        try
        {
            var initial = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                EnsureWatcher();
                await ScanOnceAsync(initial, stoppingToken).ConfigureAwait(false);
                initial = false;

                if (await _signal.WaitAsync(NextWait(), stoppingToken).ConfigureAwait(false))
                {
                    Interlocked.Exchange(ref _signalled, 0);
                }
            }
        }
        finally
        {
            _subscription?.Dispose();
            _subscription = null;
            LogStopped();
        }
    }

    private TimeSpan NextWait()
    {
        if (_tracker.HasPending)
        {
            return _tick;
        }

        return _pollingOnly || _subscription is null ? _options.PollInterval : Timeout.InfiniteTimeSpan;
    }

    private void EnsureWatcher()
    {
        if (!_fileSystem.DirectoryExists(_root))
        {
            _subscription?.Dispose();
            _subscription = null;
            if (!_missingLogged)
            {
                _missingLogged = true;
                LogFolderMissing(_root);
            }

            return;
        }

        _missingLogged = false;
        if (_pollingOnly || _subscription is not null)
        {
            return;
        }

        try
        {
            _subscription = _notifier.Watch(_root, Notify);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _pollingOnly = true;
            LogWatcherUnavailable(ex, _root);
        }
    }

    private void Notify()
    {
        if (Interlocked.Exchange(ref _signalled, 1) == 0)
        {
            _signal.Release();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Watching '{Path}' (polling only: {PollingOnly}).")]
    private partial void LogStarted(string path, bool pollingOnly);

    [LoggerMessage(Level = LogLevel.Information, Message = "Detected '{Path}' for printer '{Printer}'.")]
    private partial void LogDetected(string path, string? printer);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The watched folder '{Path}' does not exist.")]
    private partial void LogFolderMissing(string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "File system events are not available for '{Path}', falling back to polling.")]
    private partial void LogWatcherUnavailable(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Folder watcher stopped.")]
    private partial void LogStopped();
}
