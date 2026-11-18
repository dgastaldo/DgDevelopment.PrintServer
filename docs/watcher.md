# Cartella osservata

Il server può osservare una cartella e rilevare i file da stampare. È spento finché non imposti `Watcher:Path`.

> Stato: il watcher pubblica i file pronti, ma la coda e il worker di stampa non ci sono ancora (voci `sqlite-job-queue` e `print-worker` della roadmap). Per ora serve a provare il rilevamento: ogni file riconosciuto compare nel log, e dopo `QueueCapacity` file non letti il watcher si ferma ad aspettare.

## Configurazione

In `appsettings.json`:

```json
{
  "Watcher": {
    "Path": "/var/printserver/inbox",
    "Extensions": [".pdf", ".ps"],
    "IgnoredFolders": ["processati", "errori"],
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
| `Extensions` | `.pdf`, `.ps` | Estensioni accettate, senza distinguere le maiuscole. Il punto iniziale è facoltativo. |
| `IgnoredFolders` | `processati`, `errori` | Sottocartelle da non guardare. |
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
  processati/              ignorata
  errori/                  ignorata
```

Le sottocartelle più in profondità non vengono guardate. Il nome della sottocartella è un dato non fidato: il watcher non verifica che la stampante esista, lo farà il worker.

**Stabilità.** Un file viene pubblicato solo quando dimensione e data di modifica non cambiano per `StabilityWindow`. Un file ancora in copia, o scritto a blocchi, non viene stampato a metà. Se un file sparisce prima di stabilizzarsi non viene pubblicato mai. Se viene riscritto dopo la pubblicazione, è pubblicato di nuovo dopo essersi stabilizzato.

**File scartati.** Oltre a quelli con un'estensione non ammessa, sono ignorati i nascosti e i temporanei: nomi che iniziano con `.` o `~`, e che finiscono con `.tmp`, `.part`, `.partial`, `.crdownload`, `.download`. Così un file copiato con un nome temporaneo e poi rinominato viene pubblicato una volta sola, con il nome finale.

**Eventi e polling.** Gli eventi del file system servono da segnale per rifare una scansione: non si ragiona sul singolo evento, quindi eventi duplicati o fuori ordine non causano problemi. La scansione completa avviene anche all'avvio e quando il file system segnala un errore (ad esempio un buffer di eventi pieno). Con `UsePolling` gli eventi sono spenti e si scansiona ogni `PollInterval`.

Se il watcher non si può creare (ad esempio per i limiti di inotify su Linux) il servizio passa da solo al polling e lo scrive nel log. Se la cartella non esiste, aspetta che compaia.

**Log.** Avvio e arresto, errori e ogni file rilevato sono scritti a livello Information, con il percorso e la stampante.

## Limiti noti

- **Cartelle di rete (SMB/NFS):** spesso non generano eventi. Senza `UsePolling` un file arrivato lì non viene rilevato fino al riavvio: su quelle cartelle va attivato.
- **Eventi persi:** senza `UsePolling`, un evento perso (su Linux o macOS) non viene recuperato finché non arriva un altro evento o un errore del watcher.
- **Formati:** il watcher non converte niente. Word e immagini restano fuori finché non ci saranno i convertitori. Lo spooler di Windows, inoltre, non converte i PDF: la stampante deve capirli da sola.
- **Sorgenti lente:** se un file resta fermo per più di `StabilityWindow` mentre è ancora in scrittura (ad esempio un trasferimento di rete lento), viene pubblicato troppo presto: in quel caso alza `StabilityWindow`.
