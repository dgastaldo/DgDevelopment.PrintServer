using Microsoft.Extensions.Options;
using PrintServer.Core.Printing;
using PrintServer.Core.Queue;
using PrintServer.Core.Watching;

namespace PrintServer.Watching;

public sealed class FolderJobSubmitter : IJobSubmitter
{
    private const int MaxFileNameLength = 200;
    private const string PartialSuffix = ".part";

    private readonly string _root;
    private readonly FileFilter _filter;
    private readonly IReadOnlyList<string> _extensions;

    public FolderJobSubmitter(IOptions<WatcherOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        _root = Path.GetFullPath(value.Path ?? throw new InvalidOperationException("The watched folder is not configured."));
        _filter = new FileFilter(value);
        _extensions = [.. (value.Extensions ?? DocumentFormats.Extensions).Select(NormalizeExtension)];
    }

    public IReadOnlyList<string> AcceptedExtensions => _extensions;

    public async Task<SubmissionResult> SubmitAsync(string fileName, Stream content, string? printerName, PrintOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(options);

        var problem = Validate(fileName, printerName, options);
        if (problem is not null)
        {
            return SubmissionResult.Failure(problem);
        }

        var directory = Destination(printerName, options);
        var target = Path.Combine(directory, fileName);
        var partial = target + PartialSuffix;
        try
        {
            Directory.CreateDirectory(directory);
            if (File.Exists(target))
            {
                return SubmissionResult.Failure("A file with the same name is already waiting in the folder.");
            }

            using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true))
            {
                await content.CopyToAsync(destination, ct).ConfigureAwait(false);
            }

            File.Move(partial, target, overwrite: false);
            return SubmissionResult.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeletePartial(partial);
            return SubmissionResult.Failure($"The file could not be saved: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            DeletePartial(partial);
            throw;
        }
    }

    private static string NormalizeExtension(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private static void DeletePartial(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover partial file is ignored by the watcher, so there is nothing more to do.
        }
    }

    private string? Validate(string fileName, string? printerName, PrintOptions options)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Length > MaxFileNameLength
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal))
        {
            return "The file name is not valid.";
        }

        if (!_filter.Accepts(fileName))
        {
            return $"Files of this type are not accepted (accepted: {string.Join(", ", _extensions)}).";
        }

        if (options.Copies != 1)
        {
            return "Only one copy per file can be requested.";
        }

        return printerName is not null && !IsUsableFolderName(printerName)
            ? "The printer name cannot be used as a folder name."
            : null;
    }

    private static bool IsUsableFolderName(string name) =>
        name.Length is > 0 and <= MaxFileNameLength
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name.Trim() == name
        && name is not ("." or "..")
        && !QueueFolders.All.Contains(name, StringComparer.OrdinalIgnoreCase)
        && !PrintOptionFolders.TryApply(name, PrintOptions.Default, out _);

    private string Destination(string? printerName, PrintOptions options)
    {
        var parts = new List<string> { _root };
        if (printerName is not null)
        {
            parts.Add(printerName);
        }

        if (PrintOptionFolders.FolderFor(options.Orientation) is { } orientation)
        {
            parts.Add(orientation);
        }

        if (PrintOptionFolders.FolderFor(options.Duplex) is { } duplex)
        {
            parts.Add(duplex);
        }

        return Path.Combine([.. parts]);
    }
}
