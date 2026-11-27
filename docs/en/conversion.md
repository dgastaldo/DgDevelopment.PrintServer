[Italiano](../conversion.html) · **English**

# Document conversion

The server prints PDF and PostScript as they are. Other formats are converted to PDF first, then printed like any other job.

| Formats | Extensions | How |
|---|---|---|
| Word and OpenDocument text | `.doc` `.docx` `.odt` `.rtf` `.txt` | LibreOffice |
| Spreadsheets | `.xls` `.xlsx` `.ods` | LibreOffice |
| Presentations | `.ppt` `.pptx` `.odp` | LibreOffice |
| Images | `.jpg` `.jpeg` `.png` | directly, one A4 page (PDFsharp) |
| PDF, PostScript | `.pdf` `.ps` | no conversion |

These are all extensions the watcher accepts by default (see [watched folder](watcher.html)).

## LibreOffice

Documents need [LibreOffice](https://www.libreoffice.org) installed on the same machine. The server runs it from the command line (`soffice --headless --convert-to pdf`) as an external program: it is not part of the server and is not shipped with it. It is looked for in the usual places:

| System | Paths searched |
|---|---|
| Linux | `/usr/bin/soffice`, `/usr/bin/libreoffice`, `/usr/local/bin/soffice`, `/opt/libreoffice/program/soffice` |
| macOS | `/Applications/LibreOffice.app/Contents/MacOS/soffice` |
| Windows | `%ProgramFiles%\LibreOffice\program\soffice.exe` and the same under `%ProgramFiles(x86)%` |

With an installation elsewhere (for example the Snap or Flatpak version) set the path. The `PATH` is not consulted.

```json
{
  "Conversion": {
    "LibreOfficePath": "/opt/libreoffice24.8/program/soffice",
    "Timeout": "00:02:00"
  }
}
```

| Option | Default | Meaning |
|---|---|---|
| `LibreOfficePath` | search the paths above | Path of the `soffice` executable. If set, it is the only one used. |
| `Timeout` | 2 minutes | After this time the conversion of a file is interrupted and the job fails. |

## How it works

1. The job enters the queue as `pending`. If the format cannot be printed, the worker moves it to `converting` (one conversion at a time, in order of arrival) before any printing.
2. The PDF is created next to the original in `processing/`, with its name plus `.pdf` (`report.docx.pdf`). For every conversion LibreOffice uses a temporary profile, deleted at the end, so two installations or a damaged profile do not get in each other's way.
3. When the conversion is done the job goes back to `pending` and the PDF is printed, with the original name in the print job.
4. When printing ends, **only the original file** goes to `processed/` (or `errors/`, or `stalled/`): the PDF is deleted.

If a `converting` job is interrupted by a restart, it starts over.

## When conversion fails

The job goes to `failed` at once, without attempts, and the original file goes to `errors/` with the reason saved. Retrying would change nothing for a corrupt or password-protected file. The cases:

- LibreOffice not found: "LibreOffice was not found: install it or set Conversion:LibreOfficePath."
- LibreOffice exits with an error or does not produce the PDF (damaged file, password-protected, unrecognized format).
- Timeout (`Timeout`).
- Unreadable image.
- A format no converter handles (for example an extension added to `Watcher:Extensions`).

To retry after installing LibreOffice just put the file back in the watched folder.

## Known limits

- **Fidelity:** it is LibreOffice's. Documents with fonts, macros or Office-specific features may paginate a little differently from Word.
- **Spreadsheets:** they are printed with the file's page settings; very wide sheets may take several pages.
- **Images:** one A4 page per image, centered and resized (landscape for images wider than tall). The orientation options of the `portrait` and `landscape` folders apply to printing, not to the page that is created.
- **Slow first conversion:** every LibreOffice conversion takes a few seconds, even for a small file.
- **System service:** the user the service runs as must be able to run LibreOffice and write to the watched folder (see [system service](service.html)).
