**Italiano** · [English](en/queue.html)

# Coda dei lavori

Ogni file rilevato dal watcher diventa un lavoro in una coda persistente (SQLite, EF Core). La coda si accende da sola insieme al watcher: se `Watcher:Path` non è impostato non esiste.

> Stato: la coda registra i lavori, evita i doppioni, li invia alla stampante con tentativi ripetuti, sposta i file in `processed/` ed `errors/`, mette in stallo i lavori fermi e riprende dopo un riavvio. Converte prima in PDF i documenti e le immagini (vedi [conversione](conversion.html)).

## Configurazione

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

| Opzione | Predefinito | Significato |
|---|---|---|
| `DatabasePath` | cartella dati della piattaforma | Percorso del file SQLite. |
| `StallAfter` | 1 ora | Da quanto tempo un lavoro in stampa, senza problemi segnalati dalla stampante, è considerato in stallo. |
| `MaxAttempts` | 5 | Tentativi di stampa per lavoro (da 1 a 20), poi `failed`. |
| `RetryBaseDelay` | 30 secondi | Attesa dopo il primo tentativo fallito; raddoppia a ogni tentativo successivo. |
| `PollInterval` | 5 secondi | Ogni quanto il worker guarda la coda e chiede ai backend lo stato dei lavori in stampa. |

Se `DatabasePath` manca, il database `queue.db` va in una cartella dati di sistema, se è scrivibile, altrimenti in quella dell'utente:

| Piattaforma | Cartella di sistema | Ripiego (utente) |
|---|---|---|
| Linux | `/var/lib/printserver` | `~/.local/share/PrintServer` |
| macOS | `/Library/Application Support/PrintServer` | `~/.local/share/PrintServer` |
| Windows | `%ProgramData%\PrintServer` | `%LocalAppData%\PrintServer` |

All'avvio il database viene creato o aggiornato con le migrazioni e passa in modalità WAL. Poi parte il recupero (vedi sotto).

## Stati di un lavoro

| Stato | Significato |
|---|---|
| `pending` | Registrato, in attesa del suo turno. |
| `converting` | Il file viene convertito in PDF ([conversione](conversion.html)). |
| `printing` | Inviato al backend di stampa. |
| `completed` | Stampato. |
| `failed` | Non stampabile o sostituito: il motivo è salvato col lavoro. |
| `stalled` | Fermo da troppo tempo senza che la stampante segnali problemi. |
| `cancelled` | Annullato dall'utente mentre era in attesa, in conversione o in stampa. |

Ogni lavoro conserva anche stampante, formato del documento, hash, dimensione, data di creazione del file e le opzioni di stampa (orientamento, fronte/retro, copie). Orientamento e fronte/retro vengono dalle cartelle `landscape`, `two-sided-long-edge` e simili (vedi [cartella osservata](watcher.html)); le copie sono sempre una.

## Cartelle

Dentro la cartella da cui arriva il file (la principale o la sottocartella di una stampante) il server crea:

| Cartella | Contenuto |
|---|---|
| `processing/` | I file dei lavori attivi, uno per sottocartella con l'identificativo del lavoro (con l'eventuale PDF convertito accanto). Il file viene spostato qui subito, così il watcher non lo rilegge. |
| `processed/` | Lavori completati. |
| `errors/` | Lavori falliti o sostituiti da una versione più recente. |
| `stalled/` | Lavori messi in stallo. Il file resta lì per intero: se vuoi riprovare, rimettilo nella cartella osservata. |
| `duplicates/` | File arrivati mentre lo stesso file era già in coda. |
| `cancelled/` | Lavori annullati dall'utente. |

Tutte sono ignorate dal watcher.

**Aggiornare da una versione precedente.** I nomi erano in italiano (`processati`, `errori`, `stallo`, `duplicati`, `annullati`; per le opzioni di stampa `verticale`, `orizzontale`, `fronte`, `fronte-retro`, `fronte-retro-corto`). Gli stati nel database si rinominano da soli all'avvio (migrazione `EnglishStateNames`), ma le cartelle no: prima di avviare la nuova versione rinomina quelle esistenti (o svuotale), altrimenti il watcher non le riconosce più come cartelle della coda e ne ripubblica i file. Le cartelle delle opzioni (`orizzontale/` e simili) vanno rinominate allo stesso modo: i file messi in `orizzontale/` verrebbero visti come file di una stampante chiamata "orizzontale".

## Doppioni e nuove versioni

Un file è identificato da cartella di origine, nome, data di creazione e hash SHA-256 del contenuto.

- Stesso contenuto e stessa data di creazione di un lavoro attivo, oppure file più vecchio: è un doppione e va in `duplicates/`.
- Stesso nome ma contenuto o data diversi: è una nuova versione. Il lavoro precedente in attesa o in conversione passa a `failed` (motivo "sostituito") e il suo file va in `errors/`; il nuovo lavoro prende il suo posto.
- Un lavoro già `printing` non viene interrotto: finisce, e la nuova versione viene accodata a parte. Se lo stesso contenuto arriva mentre sta stampando, è un doppione.
- Un file già completato o fallito può essere rimesso nella cartella e stampa di nuovo: i doppioni si valutano solo contro i lavori attivi.

## Il worker di stampa

Ogni `PollInterval` il worker fa due cose, in quest'ordine.

1. **Controlla i lavori `printing`.** Chiede al backend lo stato: se è completato il lavoro passa a `completed` e il file a `processed/`; se è in errore conta come tentativo fallito; altrimenti applica la regola dello stallo (sotto). Se il backend non risponde il lavoro resta com'è.
2. **Invia i lavori `pending`**, dal più vecchio. Un lavoro alla volta per stampante, stampanti diverse in parallelo: una stampante ferma non blocca le altre. Una stampante nominata dalla sottocartella si cerca senza distinguere le maiuscole; senza sottocartella si usa quella predefinita. Il lavoro riceve il numero di copie delle sue opzioni.

**Tentativi.** Se il backend rifiuta il lavoro, genera un errore o segnala un errore di stampa, il lavoro torna `pending` con il motivo salvato e riparte dopo `RetryBaseDelay × 2^(tentativo-1)`: con i valori predefiniti 30 s, 1 min, 2 min, 4 min. Al quinto tentativo fallito diventa `failed` e il file va in `errors/`.

**Errori senza tentativi.** Falliscono subito, senza riprovare: stampante non trovata, nessuna stampante predefinita, file del lavoro sparito da `processing/`, opzioni che il backend non può applicare (spooler di Windows), conversione fallita o convertitore mancante.

## Stallo

Un lavoro `printing` da più di `StallAfter` viene valutato così:

1. Se la stampante risulta offline, senza carta o toner, inceppata o con errore, il lavoro resta in attesa e il conteggio riparte da quel momento.
2. Se il backend dice che è completato o in errore, il lavoro prende quello stato (e il file va in `processed/` o `errors/`).
3. Altrimenti passa a `stalled` e il file va in `stalled/`.

Il controllo avviene a ogni giro del worker e quando arriva un file con lo stesso nome di un lavoro in stampa. I problemi della stampante li riportano i backend (vedi [Problemi della stampante](backends.html#problemi-della-stampante)).

## Dopo un riavvio

All'avvio il server riallinea i lavori rimasti a metà:

- `converting` torna `pending`.
- `printing` senza identificativo del backend torna `pending`. Con l'identificativo si chiede al backend: completato, in errore, in coda (torna `pending`) o ancora in stampa (resta com'è). Se il backend non risponde il lavoro resta `printing`.
- `pending` il cui file non è nella cartella `processing/`: se il file è ancora nella posizione originale viene spostato, altrimenti il lavoro è `failed` per file mancante.

Il riepilogo è scritto nel log.

## Limiti noti

- **SQLite e cartelle di rete:** il database deve stare su un disco locale.
- **Binario self-contained:** la libreria nativa di SQLite (`libe_sqlite3`) viene pubblicata accanto all'eseguibile e va tenuta insieme a lui. Pubblicando più volte nella stessa cartella la seconda pubblicazione la rimuove: usa una cartella pulita.
- **Conversione:** dipende da LibreOffice per i documenti, vedi [conversione](conversion.html).
