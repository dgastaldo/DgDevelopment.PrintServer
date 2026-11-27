[Italiano](../queue.html) · **English**

# Job queue

Every file the watcher detects becomes a job in a persistent queue (SQLite, EF Core). The queue starts together with the watcher: if `Watcher:Path` is not set it does not exist.

> Status: the queue records jobs, avoids duplicates, sends them to the printer with repeated attempts, moves files to `processed/` and `errors/`, stalls jobs that are stuck and resumes after a restart. Documents and images are converted to PDF first (see [conversion](conversion.html)).

## Configuration

```json
{
  "Queue": {
    "DatabasePath": "/var/lib/printserver/queue.db",
    "StallAfter": "01:00:00",
    "MaxAttempts": 5,
    "RetryBaseDelay": "00:00:30",
    "PollInterval": "00:00:05"
  }
}
```

| Option | Default | Meaning |
|---|---|---|
| `DatabasePath` | the platform's data folder | Path of the SQLite file. |
| `StallAfter` | 1 hour | How long a printing job with no problem reported by the printer is allowed to sit before it is considered stalled. |
| `MaxAttempts` | 5 | Print attempts per job (1 to 20), then `failed`. |
| `RetryBaseDelay` | 30 seconds | Wait after the first failed attempt; it doubles with every later attempt. |
| `PollInterval` | 5 seconds | How often the worker looks at the queue and asks the backends for the state of printing jobs. |

If `DatabasePath` is missing, the `queue.db` database goes to a system data folder if it is writable, otherwise to the user's:

| Platform | System folder | Fallback (user) |
|---|---|---|
| Linux | `/var/lib/printserver` | `~/.local/share/PrintServer` |
| macOS | `/Library/Application Support/PrintServer` | `~/.local/share/PrintServer` |
| Windows | `%ProgramData%\PrintServer` | `%LocalAppData%\PrintServer` |

At startup the database is created or upgraded with migrations and switched to WAL mode. Then recovery starts (see below).

## Job states

| State | Meaning |
|---|---|
| `pending` | Recorded, waiting for its turn. |
| `converting` | The file is being converted to PDF ([conversion](conversion.html)). |
| `printing` | Sent to the print backend. |
| `completed` | Printed. |
| `failed` | Not printable or replaced: the reason is saved with the job. |
| `stalled` | Stuck for too long without the printer reporting problems. |
| `cancelled` | Cancelled by the user while pending, converting or printing. |

Each job also keeps its printer, document format, hash, size, the file's creation date and the print options (orientation, duplex, copies). Orientation and duplex come from the `landscape`, `two-sided-long-edge` and similar folders (see [watched folder](watcher.html)); the copies are always one.

## Folders

Inside the folder the file comes from (the main one or a printer's subfolder) the server creates:

| Folder | Content |
|---|---|
| `processing/` | The files of active jobs, one per subfolder named after the job's identifier (with the converted PDF next to it, if any). The file is moved here at once, so the watcher does not read it again. |
| `processed/` | Completed jobs. |
| `errors/` | Failed jobs, or jobs replaced by a newer version. |
| `stalled/` | Stalled jobs. The file stays there whole: to retry, put it back in the watched folder. |
| `duplicates/` | Files that arrived while the same file was already queued. |
| `cancelled/` | Jobs cancelled by the user. |

The watcher ignores all of them.

**Upgrading from an earlier version.** The names used to be in Italian (`processati`, `errori`, `stallo`, `duplicati`, `annullati`; for the print options `verticale`, `orizzontale`, `fronte`, `fronte-retro`, `fronte-retro-corto`). The states in the database are renamed on their own at startup (migration `EnglishStateNames`), but the folders are not: before starting the new version rename the existing ones (or empty them), otherwise the watcher no longer recognizes them as queue folders and publishes their files again. The option folders (`orizzontale/` and the like) must be renamed the same way: files put in `orizzontale/` would be seen as files of a printer called "orizzontale".

## Duplicates and new versions

A file is identified by origin folder, name, creation date and the SHA-256 hash of its content.

- Same content and same creation date as an active job, or an older file: it is a duplicate and goes to `duplicates/`.
- Same name but different content or date: it is a new version. The previous job, if pending or converting, becomes `failed` (reason "replaced") and its file goes to `errors/`; the new job takes its place.
- A job that is already `printing` is not interrupted: it finishes, and the new version is queued separately. If the same content arrives while it is printing, it is a duplicate.
- A file that is already completed or failed can be put back in the folder and prints again: duplicates are only judged against active jobs.

## The print worker

Every `PollInterval` the worker does two things, in this order.

1. **Checks the `printing` jobs.** It asks the backend for the state: if it is completed the job becomes `completed` and the file goes to `processed/`; if it is in error it counts as a failed attempt; otherwise the stall rule applies (below). If the backend does not answer the job stays as it is.
2. **Sends the `pending` jobs**, oldest first. One job at a time per printer, different printers in parallel: a stuck printer does not block the others. A printer named by the subfolder is looked up case-insensitively; with no subfolder the default one is used. The job gets the number of copies from its options.

**Attempts.** If the backend refuses the job, raises an error or reports a print error, the job goes back to `pending` with the reason saved and starts again after `RetryBaseDelay × 2^(attempt-1)`: with the default values 30 s, 1 min, 2 min, 4 min. At the fifth failed attempt it becomes `failed` and the file goes to `errors/`.

**Errors without attempts.** These fail at once, without retrying: printer not found, no default printer, the job's file gone from `processing/`, options the backend cannot apply (Windows spooler), conversion failed or converter missing.

## Stall

A `printing` job older than `StallAfter` is evaluated like this:

1. If the printer is offline, out of paper or toner, jammed or in error, the job stays pending and the count restarts from that moment.
2. If the backend says it is completed or in error, the job takes that state (and the file goes to `processed/` or `errors/`).
3. Otherwise it becomes `stalled` and the file goes to `stalled/`.

The check happens at every worker round and when a file with the same name as a printing job arrives. The printer's problems are reported by the backends (see [Printer problems](backends.html#printer-problems)).

## After a restart

At startup the server realigns the jobs left half-way:

- `converting` goes back to `pending`.
- `printing` without a backend identifier goes back to `pending`. With the identifier the backend is asked: completed, in error, queued (goes back to `pending`) or still printing (stays as it is). If the backend does not answer the job stays `printing`.
- `pending` whose file is not in the `processing/` folder: if the file is still in its original place it is moved, otherwise the job is `failed` for a missing file.

The summary is written to the log.

## Known limits

- **SQLite and network folders:** the database must be on a local disk.
- **Self-contained binary:** SQLite's native library (`libe_sqlite3`) is published next to the executable and must be kept together with it. Publishing several times into the same folder makes the second publication remove it: use a clean folder.
- **Conversion:** it depends on LibreOffice for documents, see [conversion](conversion.html).
