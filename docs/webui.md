# Pagina web di stato

Il server ha una pagina web (Blazor Server, in italiano) per vedere la coda e gestirla: stato dei lavori, stampanti con i loro problemi, dettaglio di ogni lavoro, e per chi ha il permesso riprovare, annullare e aggiungere file. Si aggiorna da sola ogni pochi secondi.

| Pagina | Cosa mostra | Permesso |
|---|---|---|
| `/` Stato | Quanti lavori per stato, stampanti con carta/toner/errori, ultimi lavori | vedere |
| `/jobs` Lavori | Elenco con filtro per stato e per nome file, pagine da 25 | vedere (azioni: gestire) |
| `/jobs/{id}` | Tutti i dati di un lavoro: errore, tentativi, orari, cartella, file | vedere (azioni: gestire) |
| `/add` Aggiungi | Carica uno o più file scegliendo stampante, orientamento e fronte/retro | aggiungere |

Le azioni sono **Riprova** (un lavoro fallito, in stallo o annullato torna in coda, vedi [coda](queue.html)), **Annulla** (un lavoro in attesa, in conversione o in stampa; se è già alla stampante viene tolto anche da lì) e **Aggiungi**. Un file aggiunto finisce nella cartella osservata (quella della stampante e delle opzioni scelte, vedi [cartella osservata](watcher.html)) e il watcher lo accoda come qualsiasi altro.

## Come si accede

Ci sono due modi, secondo la configurazione.

### Con DgDevelopment.Identity (OIDC)

Se imposti l'indirizzo e il client del provider, la pagina richiede l'accesso con [DgDevelopment.Identity](https://github.com/dgastaldo/DgDevelopment.Identity) (flusso con codice e PKCE). Gli utenti non autenticati vengono mandati al provider; chi non ha nessun permesso riceve un errore 403.

```json
{
  "WebUi": {
    "Authority": "https://identity.example.com",
    "ClientId": "0b1c...-client-guid",
    "ClientSecret": "....",
    "RequireHttpsMetadata": true
  }
}
```

Il segreto non va nel file di configurazione versionato: mettilo nel file di configurazione della macchina (vedi [servizio di sistema](service.html)) o nella variabile d'ambiente `WebUi__ClientSecret`.

**Cosa registrare in Identity**

1. Un client **riservato** (confidential) per PrintServer, con il suo segreto, su una piattaforma che inserisce i permessi nel token (`IdentityManaged`).
2. Gli indirizzi di ritorno: `https://<host>/signin-oidc` e, per l'uscita, `https://<host>/signout-callback-oidc`.
3. I tre permessi (nomi modificabili, vedi sotto) da assegnare a utenti, ruoli o gruppi:

| Permesso | Predefinito | Cosa consente |
|---|---|---|
| Vedere | `printserver.view` | Entrare e vedere stato, elenco e dettaglio |
| Gestire | `printserver.manage` | Riprovare e annullare i lavori |
| Aggiungere | `printserver.add` | Aggiungere file dalla pagina |

Basta uno dei tre per entrare nella pagina; ogni azione richiede il suo permesso (anche se qualcuno prova a chiamarla senza il pulsante). Assegna quindi `view` insieme agli altri.

I permessi si leggono dal claim `permissions` del token di accesso e restano quelli del momento dell'accesso: se li cambi, l'utente li vede al prossimo accesso (la sessione dura 8 ore). Il nome del claim e dei permessi si cambia con `PermissionClaim`, `ViewPermission`, `ManagePermission` e `AddPermission`.

### Senza provider: solo lettura, solo da questa macchina

Se `Authority` e `ClientId` mancano, la pagina è accessibile **solo da chi si collega dalla macchina stessa** (indirizzo 127.0.0.1 o ::1), senza accesso e senza azioni: si può vedere ma non cambiare niente, e la barra in alto lo dice. Chi arriva da un altro indirizzo riceve un errore 403.

Non mettere un reverse proxy sulla stessa macchina davanti al server in questa modalità: le sue richieste arriverebbero da 127.0.0.1 e passerebbero come locali. Con un proxy configura il provider OIDC.

## Opzioni

| Opzione | Predefinito | Significato |
|---|---|---|
| `Enabled` | `true` | Se `false` la pagina non esiste (restano `/health` e il resto). |
| `Authority` | nessuna | Indirizzo del provider OIDC (senza barra finale). |
| `ClientId`, `ClientSecret` | nessuno | Client registrato nel provider. |
| `RequireHttpsMetadata` | `true` | Pretende HTTPS per leggere la configurazione del provider; mettilo a `false` solo per prove su HTTP. |
| `PermissionClaim` | `permissions` | Nome del claim con i permessi nel token di accesso. |
| `ViewPermission`, `ManagePermission`, `AddPermission` | `printserver.view`, `printserver.manage`, `printserver.add` | Nomi dei tre permessi. |
| `SessionLifetime` | 8 ore | Durata della sessione dopo l'accesso. |
| `RefreshInterval` | 3 secondi | Ogni quanto la pagina si aggiorna. |

Le aggiunte dalla pagina hanno un limite fisso di 25 MB per file (non configurabile); la pagina lo indica accanto al campo di caricamento. File più grandi vanno copiati direttamente nella cartella osservata.

## HTTPS e indirizzo

Il server ascolta dove dice `Urls` (vedi [servizio di sistema](service.html)); l'esempio usa `http://127.0.0.1:5000`. Per usare la pagina da altri computer metti davanti un proxy con HTTPS e passa gli header di inoltro (variabile `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`): gli indirizzi di ritorno del provider si costruiscono da quelli della richiesta. Su HTTP semplice l'accesso funziona ma password e sessione viaggiano in chiaro.

## Limiti noti

- **Non provato con un'istanza reale di DgDevelopment.Identity:** l'accesso è stato provato con un provider OIDC finto (codice, PKCE, nonce, permessi nel token) e leggendo il codice del provider. Cose da controllare al primo uso: che gli indirizzi di ritorno siano registrati, che l'emittente sia uguale a `Authority` e che i permessi arrivino nel token di accesso.
- **Annullare dalla stampante:** dipende dal backend (CUPS, IPP, spooler). Se non risponde, il lavoro risulta annullato nella coda e compare un avviso: controlla la coda della stampante.
- **Una sola istanza:** la pagina legge il database della coda; con più istanze del server sullo stesso database non è provata.
