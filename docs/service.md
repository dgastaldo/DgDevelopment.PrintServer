# Servizio di sistema

Il server può girare come servizio, avviato col sistema e riavviato se si ferma: Windows Service, systemd su Linux, launchd su macOS. Lanciato da terminale funziona come prima.

I file sono in `deploy/service`. Il binario si installa prima con gli script di `deploy/personal` (cartella `current/`); questi script installano solo il servizio.

## Configurazione della macchina

L'`appsettings.json` che sta accanto al binario viene sostituito a ogni aggiornamento. Le impostazioni vere vanno quindi in un file della macchina, fuori da `current/`, che il server legge all'avvio:

| Sistema | File |
|---|---|
| Linux | `/etc/printserver/appsettings.json` |
| macOS | `/Library/Application Support/PrintServer/appsettings.json` |
| Windows | `%ProgramData%\PrintServer\appsettings.json` |

Si può scegliere un altro file con la variabile d'ambiente `PRINTSERVER_CONFIG`. Il file è facoltativo. Ha la precedenza sull'`appsettings.json` accanto al binario; variabili d'ambiente e argomenti della riga di comando hanno la precedenza su di lui. Un esempio è in `deploy/service/appsettings.example.json`; gli script lo copiano se il file non c'è:

```json
{
  "Urls": "http://127.0.0.1:5000",
  "Printing": { "Backend": "Auto" },
  "Watcher": { "Path": "/var/printserver/inbox" }
}
```

Senza `Printing:Backend` il server usa il backend simulato (`Fake`). La [pagina web](webui.html) risponde sullo stesso indirizzo (`Urls`). Vedi [backend](backends.html), [cartella osservata](watcher.html) e [coda](queue.html) per le altre opzioni.

## Linux (systemd)

```bash
sudo deploy/service/install-service.sh
```

Lo script crea l'utente di sistema `printserver`, la configurazione, l'unit `/etc/systemd/system/printserver.service`, poi abilita e avvia il servizio. L'unit è di tipo `notify`: systemd considera il servizio pronto quando il server lo comunica. La cartella dati `/var/lib/printserver` (database della coda) la crea systemd, di proprietà dell'utente del servizio.

**Permessi.** Il servizio gira come `printserver`, non come root: la cartella osservata deve essere leggibile e scrivibile da quell'utente (il server crea al suo interno `processing/`, `processed/` e le altre), ad esempio `sudo install -d -o printserver /var/printserver/inbox`. Per stampare con CUPS l'utente deve poter usare le stampanti.

Opzioni dello script, come variabili d'ambiente: `INSTALL_DIR` (default `/opt/printserver`), `SERVICE_NAME`, `SERVICE_USER`, `CONFIG_DIR`, `UNIT_DIR`; con `--no-start` installa senza avviare.

```bash
systemctl status printserver
journalctl -u printserver -f
```

## macOS (launchd)

```bash
sudo deploy/service/install-service.sh
```

Installa `/Library/LaunchDaemons/com.printserver.plist` e lo carica. Il processo gira come root e launchd lo riavvia se si ferma (`KeepAlive`). Il log è in `/var/log/printserver.log`.

```bash
sudo launchctl print system/com.printserver
```

## Windows

Da PowerShell come amministratore:

```powershell
.\deploy\service\install-service.ps1
```

Crea il servizio `PrintServer` con avvio automatico, che punta a `C:\ProgramData\PrintServer\current\PrintServer.Host.exe`, e imposta il riavvio automatico dopo un errore (la prima e la seconda volta dopo 5 secondi, la terza dopo 30). Gira come `LocalSystem`, che vede le stampanti installate sul computer; per un altro utente usa `-Credential`. Opzioni: `-InstallDir`, `-ServiceName`, `-NoStart`. Il log va nel registro eventi di Windows.

## Aggiornamenti e rimozione

Gli script di aggiornamento in `deploy/personal` riavviano il servizio (`SERVICE_NAME` / `-ServiceName` con lo stesso nome usato qui) e, con `HEALTH_URL`, tornano alla versione precedente se non risponde. L'indirizzo di salute è `/health`.

Per rimuovere il servizio: `sudo systemctl disable --now printserver` e cancellare l'unit; `sudo launchctl bootout system/com.printserver` e cancellare il plist; `Stop-Service PrintServer; sc.exe delete PrintServer`. I dati (coda, configurazione) non vengono toccati.

## Limiti noti

- **Conversione dei documenti:** richiede LibreOffice sulla macchina e che l'utente del servizio possa eseguirlo (vedi [conversione](conversion.html)). La versione Snap di LibreOffice può avere restrizioni di accesso ai file: se ci sono problemi, usa un pacchetto tradizionale.
- **Provato solo in parte:** il codice del servizio e gli script sono stati verificati per sintassi e con prove locali, non su un systemd, un launchd o un Windows reali.
- **Un solo utente di servizio:** su Windows le stampanti di rete collegate al profilo di un utente non sono visibili a `LocalSystem`.
- **Porta e indirizzo:** `Urls` ascolta su `127.0.0.1` nell'esempio; aprirla a tutta la rete va fatto di proposito (e con un firewall), perché per ora non c'è autenticazione.
