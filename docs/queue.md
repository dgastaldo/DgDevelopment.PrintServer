# Coda dei lavori

Ogni file rilevato dal watcher diventa un lavoro in una coda persistente (SQLite, EF Core). La coda si accende da sola insieme al watcher: se `Watcher:Path` non è impostato non esiste.

> Stato: la coda registra i lavori, evita i doppioni, riprende dopo un riavvio e mette in stallo i lavori fermi. Il worker che li invia davvero alla stampante, con tentativi ripetuti e spostamento in `processati/` ed `errori/`, è la voce `print-worker` della roadmap.

## Configurazione

```json
{
  "Queue": {
    "DatabasePath": "/var/lib/printserver/queue.db",
    "StallAfter": "01:00:00"
  }
}
```

| Opzione | Predefinito | Significato |
|---|---|---|
| `DatabasePath` | cartella dati della piattaforma | Percorso del file SQLite. |
| `StallAfter` | 1 ora | Da quanto tempo un lavoro in stampa, senza problemi segnalati dalla stampante, è considerato in stallo. |

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
| `in_attesa` | Registrato, in attesa del suo turno. |
| `in_conversione` | Il file viene convertito in un formato adatto alla stampante (previsto, non ancora attivo). |
| `in_stampa` | Inviato al backend di stampa. |
| `completato` | Stampato. |
| `fallito` | Non stampabile o sostituito: il motivo è salvato col lavoro. |
| `in_stallo` | Fermo da troppo tempo senza che la stampante segnali problemi. |

Ogni lavoro conserva anche stampante, formato del documento, hash, dimensione, data di creazione del file e le opzioni di stampa (orientamento, fronte/retro, copie), oggi con i valori predefiniti.

## Cartelle

Dentro la cartella da cui arriva il file (la principale o la sottocartella di una stampante) il server crea:

| Cartella | Contenuto |
|---|---|
| `processing/` | I file dei lavori attivi, uno per sottocartella con l'identificativo del lavoro. Il file viene spostato qui subito, così il watcher non lo rilegge. |
| `processati/` | Lavori completati. |
| `errori/` | Lavori falliti o sostituiti da una versione più recente. |
| `stallo/` | Lavori messi in stallo. Il file resta lì per intero: se vuoi riprovare, rimettilo nella cartella osservata. |
| `duplicati/` | File arrivati mentre lo stesso file era già in coda. |

Tutte sono ignorate dal watcher.

## Doppioni e nuove versioni

Un file è identificato da cartella di origine, nome, data di creazione e hash SHA-256 del contenuto.

- Stesso contenuto e stessa data di creazione di un lavoro attivo, oppure file più vecchio: è un doppione e va in `duplicati/`.
- Stesso nome ma contenuto o data diversi: è una nuova versione. Il lavoro precedente in attesa o in conversione passa a `fallito` (motivo "sostituito") e il suo file va in `errori/`; il nuovo lavoro prende il suo posto.
- Un lavoro già `in_stampa` non viene interrotto: finisce, e la nuova versione viene accodata a parte. Se lo stesso contenuto arriva mentre sta stampando, è un doppione.
- Un file già completato o fallito può essere rimesso nella cartella e stampa di nuovo: i doppioni si valutano solo contro i lavori attivi.

## Stallo

Un lavoro `in_stampa` da più di `StallAfter` viene valutato così:

1. Se la stampante risulta offline, senza carta o toner, inceppata o con errore, il lavoro resta in attesa e il conteggio riparte da quel momento.
2. Se il backend dice che è completato o in errore, il lavoro prende quello stato (e il file va in `processati/` o `errori/`).
3. Altrimenti passa a `in_stallo` e il file va in `stallo/`.

Il controllo avviene quando arriva un file con lo stesso nome di un lavoro in stampa. La rilevazione dei problemi della stampante dipende dai backend (`printer-status` nella roadmap): finché non c'è, uno stallo è deciso solo dal tempo.

## Dopo un riavvio

All'avvio il server riallinea i lavori rimasti a metà:

- `in_conversione` torna `in_attesa`.
- `in_stampa` senza identificativo del backend torna `in_attesa`. Con l'identificativo si chiede al backend: completato, in errore, in coda (torna `in_attesa`) o ancora in stampa (resta com'è). Se il backend non risponde il lavoro resta `in_stampa`.
- `in_attesa` il cui file non è nella cartella `processing/`: se il file è ancora nella posizione originale viene spostato, altrimenti il lavoro è `fallito` per file mancante.

Il riepilogo è scritto nel log.

## Limiti noti

- **SQLite e cartelle di rete:** il database deve stare su un disco locale.
- **Binario self-contained:** la libreria nativa di SQLite (`libe_sqlite3`) viene pubblicata accanto all'eseguibile e va tenuta insieme a lui. Pubblicando più volte nella stessa cartella la seconda pubblicazione la rimuove: usa una cartella pulita.
- **Opzioni di stampa e conversione:** sono già nel modello (orientamento, fronte/retro, `in_conversione`) ma nessuna parte le usa ancora; arriveranno con `print-options` e `document-conversion`.
