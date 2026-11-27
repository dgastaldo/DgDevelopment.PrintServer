[Italiano](../watcher.html) · **English**

# Watched folder

The server can watch a folder and detect the files to print. It is off until you set `Watcher:Path`.

> Status: the watcher publishes ready files, the [queue](queue.html) records them and the worker sends them to the printer. Without `Watcher:Path` none of this starts.

## Configuration

In `appsettings.json`:

```json
{
  "Watcher": {
    "Path": "/var/printserver/inbox",
    "Extensions": [".pdf", ".ps", ".docx", ".png"],
    "IgnoredFolders": ["processing", "processed", "errors", "stalled", "duplicates"],
    "StabilityWindow": "00:00:10",
    "UsePolling": false,
    "PollInterval": "00:00:30",
    "ProcessExistingFiles": true,
    "QueueCapacity": 256
  }
}
```

Or with environment variables, for example for a test: `Watcher__Path=/tmp/inbox Watcher__StabilityWindow=00:00:02`.

| Option | Default | Meaning |
|---|---|---|
| `Path` | none | Folder to watch. Without it the watcher does not start. |
| `Extensions` | PDF and PostScript, plus every format the server can convert (`.doc` `.docx` `.odt` `.rtf` `.txt` `.xls` `.xlsx` `.ods` `.ppt` `.pptx` `.odp` `.jpg` `.jpeg` `.png`) | Accepted extensions, case-insensitive. The leading dot is optional. If you set it, it replaces the list. |
| `IgnoredFolders` | `processing`, `processed`, `errors`, `stalled`, `duplicates` | Subfolders not to look at (the ones the queue manages). If you set it, include these too. |
| `StabilityWindow` | 10 seconds | How long size and modification time must stay unchanged before a file is considered complete. |
| `UsePolling` | `false` | Use only the periodic scan, without file system events. |
| `PollInterval` | 30 seconds | How often to scan with `UsePolling` (and when events are not available). |
| `ProcessExistingFiles` | `true` | Also consider the files already there at startup. |
| `QueueCapacity` | 256 | How many ready files can wait to be read before the watcher stops and waits. |

## How it works

**One folder per printer.** Files in the main folder have no printer. A file in a first-level subfolder takes the subfolder's name as its printer:

```
inbox/
  invoice.pdf              no printer (the default one)
  Office/
    label.pdf              printer "Office"
  processing/              ignored (managed by the queue)
  processed/               ignored
  errors/                  ignored
  stalled/                 ignored
  duplicates/              ignored
```

The subfolder name is untrusted data: the watcher does not check that the printer exists, the worker will.

**Orientation and duplex.** Inside a printer's folder, subfolders with a reserved name set how the files they contain are printed:

| Folder | Effect |
|---|---|
| `portrait` | portrait orientation |
| `landscape` | landscape orientation |
| `one-sided` | one side only |
| `two-sided-long-edge` | both sides, binding on the long edge |
| `two-sided-short-edge` | both sides, binding on the short edge |

```
inbox/
  Office/
    label.pdf                                   printer's default settings
    landscape/report.pdf                        landscape
    landscape/two-sided-long-edge/table.pdf     landscape, both sides
    two-sided-long-edge/landscape/table.pdf     the same, in any order
  landscape/                                    default printer, landscape
```

At most two folders can be nested (one for orientation, one for duplex); deeper ones are not looked at. If they contradict each other, the innermost wins. The reserved folders also apply in the main folder, for the default printer: for this reason a printer cannot be named like one of them. The queue's folders (`processed`, `errors`, …) are created inside the folder the file comes from, so also inside `landscape/`. A file without these folders uses the printer's default settings.

**Stability.** A file is published only when its size and modification time stay unchanged for `StabilityWindow`. A file that is still being copied, or written in blocks, is not printed half-way. If a file disappears before it stabilizes it is never published. If it is rewritten after publication, it is published again once it has stabilized.

**Discarded files.** Besides those with a disallowed extension, hidden and temporary files are ignored: names that start with `.` or `~`, and that end with `.tmp`, `.part`, `.partial`, `.crdownload`, `.download`. So a file copied under a temporary name and then renamed is published once, under its final name.

**Events and polling.** File system events only act as a signal to scan again: no reasoning is done on a single event, so duplicate or out-of-order events cause no problems. A full scan also happens at startup and when the file system reports an error (for example a full event buffer). With `UsePolling` events are off and a scan runs every `PollInterval`.

If the watcher cannot be created (for example because of inotify limits on Linux) the service switches to polling by itself and writes it to the log. If the folder does not exist, it waits for it to appear.

**Log.** Startup and shutdown, errors and every detected file are written at Information level, with the path and the printer.

## Known limits

- **Network folders (SMB/NFS):** they often do not generate events. Without `UsePolling` a file that arrives there is not detected until the restart: enable it on those folders.
- **Lost events:** without `UsePolling`, a lost event (on Linux or macOS) is not recovered until another event or a watcher error arrives.
- **Formats:** the watcher converts nothing: it queues the files and the [queue](conversion.html) converts them (documents need LibreOffice). The Windows spooler, moreover, does not convert PDFs: the printer must understand them by itself.
- **Slow sources:** if a file stays unchanged for more than `StabilityWindow` while it is still being written (for example a slow network transfer), it is published too early: in that case raise `StabilityWindow`.
