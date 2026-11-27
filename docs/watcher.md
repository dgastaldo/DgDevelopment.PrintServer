**Italiano** · [English](en/watcher.html)

# Cartella osservata

Il server può osservare una cartella e rilevare i file da stampare. È spento finché non imposti `Watcher:Path`.

> Stato: il watcher pubblica i file pronti, la [coda](queue.html) li registra e il worker li invia alla stampante. Senza `Watcher:Path` non parte niente di questo.

## Configurazione

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

Oppure con variabili d'ambiente, ad esempio per una prova: `Watcher__Path=/tmp/inbox Watcher__StabilityWindow=00:00:02`.

| Opzione | Predefinito | Significato |
|---|---|---|
| `Path` | nessuno | Cartella da osservare. Senza, il watcher non parte. |
| `Extensions` | PDF e PostScript, più tutti i formati che il server sa convertire (`.doc` `.docx` `.odt` `.rtf` `.txt` `.xls` `.xlsx` `.ods` `.ppt` `.pptx` `.odp` `.jpg` `.jpeg` `.png`) | Estensioni accettate, senza distinguere le maiuscole. Il punto iniziale è facoltativo. Se lo imposti, sostituisce l'elenco. |
| `IgnoredFolders` | `processing`, `processed`, `errors`, `stalled`, `duplicates` | Sottocartelle da non guardare (quelle gestite dalla coda). Se lo imposti, includi anche queste. |
| `StabilityWindow` | 10 secondi | Quanto tempo dimensione e data di modifica devono restare invariate prima che un file sia considerato completo. |
| `UsePolling` | `false` | Usa solo la scansione periodica, senza gli eventi del file system. |
| `PollInterval` | 30 secondi | Ogni quanto scansionare con `UsePolling` (e quando gli eventi non sono disponibili). |
| `ProcessExistingFiles` | `true` | Considera anche i file già presenti all'avvio. |
| `QueueCapacity` | 256 | Quanti file pronti possono attendere di essere letti prima che il watcher si fermi ad aspettare. |

## Come funziona

**Una cartella per stampante.** I file nella cartella principale non hanno una stampante. Un file in una sottocartella di primo livello prende il nome della sottocartella come stampante:

```
inbox/
  fattura.pdf              senza stampante (quella predefinita)
  Ufficio/
    etichetta.pdf          stampante "Ufficio"
  processing/              ignorata (gestita dalla coda)
  processed/               ignorata
  errors/                  ignorata
  stalled/                 ignorata
  duplicates/              ignorata
```

Il nome della sottocartella è un dato non fidato: il watcher non verifica che la stampante esista, lo farà il worker.

**Orientamento e fronte/retro.** Dentro la cartella di una stampante, sottocartelle con un nome riservato impostano come stampare i file che contengono:

| Cartella | Effetto |
|---|---|
| `portrait` | orientamento verticale |
| `landscape` | orientamento orizzontale |
| `one-sided` | solo fronte |
| `two-sided-long-edge` | fronte e retro, rilegatura sul lato lungo |
| `two-sided-short-edge` | fronte e retro, rilegatura sul lato corto |

```
inbox/
  Ufficio/
    etichetta.pdf                       impostazioni predefinite della stampante
    landscape/relazione.pdf                      orizzontale
    landscape/two-sided-long-edge/tabella.pdf    orizzontale, fronte e retro
    two-sided-long-edge/landscape/tabella.pdf    lo stesso, in qualsiasi ordine
  landscape/                                     stampante predefinita, orizzontale
```

Si possono annidare al massimo due cartelle (una per orientamento, una per fronte/retro); più in profondità non si guarda. Se si contraddicono, vince la più interna. Le cartelle riservate valgono anche nella cartella principale, per la stampante predefinita: per questo una stampante non può chiamarsi come una di esse. Le cartelle della coda (`processed`, `errors`, …) si creano dentro la cartella da cui arriva il file, quindi anche dentro `landscape/`. Un file senza queste cartelle usa le impostazioni predefinite della stampante.

**Stabilità.** Un file viene pubblicato solo quando dimensione e data di modifica non cambiano per `StabilityWindow`. Un file ancora in copia, o scritto a blocchi, non viene stampato a metà. Se un file sparisce prima di stabilizzarsi non viene pubblicato mai. Se viene riscritto dopo la pubblicazione, è pubblicato di nuovo dopo essersi stabilizzato.

**File scartati.** Oltre a quelli con un'estensione non ammessa, sono ignorati i nascosti e i temporanei: nomi che iniziano con `.` o `~`, e che finiscono con `.tmp`, `.part`, `.partial`, `.crdownload`, `.download`. Così un file copiato con un nome temporaneo e poi rinominato viene pubblicato una volta sola, con il nome finale.

**Eventi e polling.** Gli eventi del file system servono da segnale per rifare una scansione: non si ragiona sul singolo evento, quindi eventi duplicati o fuori ordine non causano problemi. La scansione completa avviene anche all'avvio e quando il file system segnala un errore (ad esempio un buffer di eventi pieno). Con `UsePolling` gli eventi sono spenti e si scansiona ogni `PollInterval`.

Se il watcher non si può creare (ad esempio per i limiti di inotify su Linux) il servizio passa da solo al polling e lo scrive nel log. Se la cartella non esiste, aspetta che compaia.

**Log.** Avvio e arresto, errori e ogni file rilevato sono scritti a livello Information, con il percorso e la stampante.

## Limiti noti

- **Cartelle di rete (SMB/NFS):** spesso non generano eventi. Senza `UsePolling` un file arrivato lì non viene rilevato fino al riavvio: su quelle cartelle va attivato.
- **Eventi persi:** senza `UsePolling`, un evento perso (su Linux o macOS) non viene recuperato finché non arriva un altro evento o un errore del watcher.
- **Formati:** il watcher non converte niente: accoda i file e li converte la [coda](conversion.html) (per i documenti serve LibreOffice). Lo spooler di Windows, inoltre, non converte i PDF: la stampante deve capirli da sola.
- **Sorgenti lente:** se un file resta fermo per più di `StabilityWindow` mentre è ancora in scrittura (ad esempio un trasferimento di rete lento), viene pubblicato troppo presto: in quel caso alza `StabilityWindow`.
