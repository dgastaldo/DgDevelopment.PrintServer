# Conversione dei documenti

Il server stampa PDF e PostScript così come sono. Gli altri formati vengono prima convertiti in PDF, poi stampati come qualsiasi altro lavoro.

| Formati | Estensioni | Come |
|---|---|---|
| Word e OpenDocument testo | `.doc` `.docx` `.odt` `.rtf` `.txt` | LibreOffice |
| Fogli di calcolo | `.xls` `.xlsx` `.ods` | LibreOffice |
| Presentazioni | `.ppt` `.pptx` `.odp` | LibreOffice |
| Immagini | `.jpg` `.jpeg` `.png` | direttamente, una pagina A4 (PDFsharp) |
| PDF, PostScript | `.pdf` `.ps` | nessuna conversione |

Sono tutte estensioni accettate dal watcher per impostazione predefinita (vedi [cartella osservata](watcher.html)).

## LibreOffice

Per i documenti serve [LibreOffice](https://www.libreoffice.org) installato sulla stessa macchina. Il server lo lancia da riga di comando (`soffice --headless --convert-to pdf`) come programma esterno: non fa parte del server e non viene distribuito con esso. Lo si cerca nei percorsi abituali:

| Sistema | Percorsi cercati |
|---|---|
| Linux | `/usr/bin/soffice`, `/usr/bin/libreoffice`, `/usr/local/bin/soffice`, `/opt/libreoffice/program/soffice` |
| macOS | `/Applications/LibreOffice.app/Contents/MacOS/soffice` |
| Windows | `%ProgramFiles%\LibreOffice\program\soffice.exe` e la stessa in `%ProgramFiles(x86)%` |

Con un'installazione altrove (ad esempio la versione Snap o Flatpak) si imposta il percorso. Il `PATH` non viene consultato.

```json
{
  "Conversion": {
    "LibreOfficePath": "/opt/libreoffice24.8/program/soffice",
    "Timeout": "00:02:00"
  }
}
```

| Opzione | Predefinito | Significato |
|---|---|---|
| `LibreOfficePath` | cerca nei percorsi sopra | Percorso dell'eseguibile `soffice`. Se indicato, è l'unico usato. |
| `Timeout` | 2 minuti | Dopo questo tempo la conversione di un file viene interrotta e il lavoro fallisce. |

## Come funziona

1. Il lavoro entra in coda `in_attesa`. Se il formato non è stampabile, il worker lo porta a `in_conversione` (una conversione alla volta, nell'ordine di arrivo) prima di qualsiasi stampa.
2. Il PDF si crea accanto all'originale in `processing/`, con il suo nome più `.pdf` (`relazione.docx.pdf`). Per ogni conversione LibreOffice usa un profilo temporaneo, cancellato alla fine, così due installazioni o un profilo rovinato non si disturbano.
3. Finita la conversione il lavoro torna `in_attesa` e viene stampato il PDF, con il nome originale nel lavoro di stampa.
4. A stampa conclusa va in `processati/` (o `errori/`, o `stallo/`) **solo il file originale**: il PDF viene cancellato.

Se un lavoro `in_conversione` è fermo per un riavvio, riparte da capo.

## Quando la conversione fallisce

Il lavoro passa subito a `fallito`, senza tentativi, e il file originale va in `errori/` con il motivo salvato. Riprovare non cambierebbe niente per un file corrotto o protetto da password. I casi:

- LibreOffice non trovato: "LibreOffice was not found: install it or set Conversion:LibreOfficePath."
- LibreOffice termina con un errore o non produce il PDF (file danneggiato, protetto da password, formato non riconosciuto).
- Tempo scaduto (`Timeout`).
- Immagine illeggibile.
- Formato che nessun convertitore gestisce (ad esempio un'estensione aggiunta a `Watcher:Extensions`).

Per riprovare dopo aver installato LibreOffice basta rimettere il file nella cartella osservata.

## Limiti noti

- **Fedeltà:** è quella di LibreOffice. Documenti con font, macro o funzioni particolari di Office possono impaginarsi in modo un po' diverso da Word.
- **Fogli di calcolo:** vengono stampati con le impostazioni di pagina del file; fogli molto larghi possono occupare più pagine.
- **Immagini:** una pagina A4 per immagine, centrata e ridimensionata (orizzontale per le immagini più larghe che alte). Le opzioni di orientamento delle cartelle `verticale` e `orizzontale` si applicano alla stampa, non alla pagina creata.
- **Prima conversione lenta:** ogni conversione di LibreOffice richiede qualche secondo, anche per un file piccolo.
- **Servizio di sistema:** l'utente con cui gira il servizio deve poter eseguire LibreOffice e scrivere nella cartella osservata (vedi [servizio di sistema](service.html)).
