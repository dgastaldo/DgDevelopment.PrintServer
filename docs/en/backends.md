[Italiano](../backends.html) · **English**

# Printer backends

Choose the backend in `appsettings.json` with `Printing:Backend`.

| Value | What it uses | Notes |
|---|---|---|
| `Fake` | simulated printer | default until you choose another; handy for tests |
| `Auto` | `WindowsSpooler` on Windows, `Cups` on macOS and Linux | |
| `Cups` | CUPS `lp` and `lpstat` | macOS, Linux |
| `WindowsSpooler` | Windows spooler (`winspool.drv`) | Windows only; RAW data, one copy per job |
| `Ipp` | IPP protocol over HTTP | network printers, and a fallback on every platform |

## Examples

CUPS, with optional paths:

```json
{
  "Printing": {
    "Backend": "Cups",
    "Cups": { "LpPath": "/usr/bin/lp", "LpstatPath": "/usr/bin/lpstat" }
  }
}
```

IPP, with the list of printers to use (the first one is the default):

```json
{
  "Printing": {
    "Backend": "Ipp",
    "Ipp": {
      "UserName": "printserver",
      "RequestTimeout": "00:00:30",
      "Printers": [
        { "Name": "Office", "Uri": "ipp://printer.local/ipp/print" },
        { "Name": "Labels", "Uri": "ipps://labels.local:8631/ipp/print" }
      ]
    }
  }
}
```

Other IPP options: `DocumentFormat` (if omitted it is inferred from the extension: PDF, JPEG, PNG, text, otherwise `application/octet-stream`) and `TrustedCertificateThumbprints`: a list of SHA-256 thumbprints (hexadecimal, colons allowed) of certificates that are accepted even though they are not valid, for printers with a self-signed certificate. The certificate must match one of the thumbprints; there is no option to accept any certificate.

## Orientation and duplex

The worker passes the job's orientation and duplex mode to the backend (taken from the folders described in [watched folder](watcher.html)):

| Backend | Orientation | Duplex |
|---|---|---|
| CUPS | `-o orientation-requested=3` (portrait) or `4` (landscape) | `-o sides=one-sided`, `two-sided-long-edge`, `two-sided-short-edge` |
| IPP | `orientation-requested` attribute (3 or 4) | `sides` attribute (same values) |
| Windows spooler | not applicable | not applicable |

The Windows spooler sends RAW data and cannot enforce these options: a job that asks for them fails immediately, with the reason saved, and the file goes to `errors/`. Put the file in the printer folder without `landscape`, `two-sided-long-edge` and the like. If you ask for nothing, the printer keeps the driver's default settings. If a printer ignores an option, the server cannot tell.

## Cancelling jobs

A job that has already been sent to the printer can be cancelled from the server; each backend removes it from its own queue:

| Backend | How |
|---|---|
| CUPS | `cancel <id>` (path in `Printing:Cups:CancelPath`, default `/usr/bin/cancel`) |
| IPP | `Cancel-Job` operation |
| Windows spooler | `SetJob` with the delete command |
| `Fake` | forgets the job |

If the job has already finished or the printer no longer knows it, cancelling still succeeds. If the printer does not answer, the job is still cancelled in the server's queue and a warning appears: check the printer's own queue.

## Printer problems

Besides name and availability, each backend reports the problems that keep a printer from printing. The worker uses them to decide whether a job that has been stuck for more than an hour is stalled (see [queue](queue.html)): while a problem is active the job stays pending.

| Problem | IPP and CUPS (`printer-state-reasons`) | Windows spooler |
|---|---|---|
| Offline | `offline`, `shutdown` (also as a warning); a printer that does not answer or does not accept jobs | paused, offline, not available, unknown server |
| Out of paper | `media-empty`, `media-needed` | out of paper or paper problem |
| Out of toner | `toner-empty`, `marker-supply-empty` | no toner |
| Paper jam | `media-jam` | paper jam |
| Door open | `door-open`, `cover-open`, `interlock-open` | door open |
| Error | any other reason ending in `-error`, for example `spool-area-full-error` | error, output bin full, out of memory, user intervention |

With IPP and CUPS, reasons ending in `-warning` or `-report` (such as `toner-low-warning`) are warnings and do not block; the exception is `offline-report`, which means an offline printer. On CUPS the reasons are read from `lpstat -l -p` (the `Alerts:` line). The `Fake` backend can simulate a problem with `Printing:Fake:Issues` (for example `{ "Fake Printer": "PaperOut" }`).

## Known limits

- **CUPS**: a job that is no longer in the queue is considered completed, because `lpstat` does not tell completed from cancelled. A job on a disabled printer stays in the queue.
- **Windows spooler**: the spooler does not convert the document; the file must be in a format the printer understands by itself. One copy per job; only printers installed on the computer are listed.
- **Printer problems**: they depend on what the printer or driver declares; some do not report running out of paper or toner. In that case, after an hour without progress, the job is stalled.
- **IPP**: the document format must be accepted by the printer; a job the printer no longer knows is reported as completed.
