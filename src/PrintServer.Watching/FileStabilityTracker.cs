namespace PrintServer.Watching;

internal sealed class FileStabilityTracker(TimeProvider time, TimeSpan window)
{
    private readonly Dictionary<string, Observation> _observations = new(StringComparer.Ordinal);

    public bool HasPending => _observations.Values.Any(observation => !observation.Reported);

    public void Seed(IReadOnlyList<ScannedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var now = time.GetUtcNow();
        foreach (var file in files)
        {
            _observations[file.Entry.Path] = new Observation(file.Entry.Length, file.Entry.LastWriteUtc, now, true);
        }
    }

    public IReadOnlyList<ScannedFile> Update(IReadOnlyList<ScannedFile> current)
    {
        ArgumentNullException.ThrowIfNull(current);

        var now = time.GetUtcNow();
        var present = new HashSet<string>(current.Select(file => file.Entry.Path), StringComparer.Ordinal);
        foreach (var gone in _observations.Keys.Where(path => !present.Contains(path)).ToList())
        {
            _observations.Remove(gone);
        }

        var ready = new List<ScannedFile>();
        foreach (var file in current)
        {
            var entry = file.Entry;
            if (!_observations.TryGetValue(entry.Path, out var seen)
                || seen.Length != entry.Length
                || seen.LastWriteUtc != entry.LastWriteUtc)
            {
                _observations[entry.Path] = new Observation(entry.Length, entry.LastWriteUtc, now, false);
            }
            else if (!seen.Reported && now - seen.ChangedAt >= window)
            {
                _observations[entry.Path] = seen with { Reported = true };
                ready.Add(file);
            }
        }

        return ready;
    }

    private sealed record Observation(long Length, DateTimeOffset LastWriteUtc, DateTimeOffset ChangedAt, bool Reported);
}
