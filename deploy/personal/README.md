# Deploy personale con aggiornamento automatico

Il ramo `personal` rappresenta l'istanza sempre attiva sulla tua macchina. Quando una PR di promozione viene unita in `personal`, `04-promote.yml` crea il tag `personal/vAAAA.MM.GG.N` e `05-deploy-personal.yml` pubblica una **pre-release GitHub** con i binari self-contained e `SHA256SUMS`. Gli script di questa cartella scaricano l'ultima pre-release `personal/v*`, verificano il checksum, sostituiscono il binario e riavviano il servizio.

Il repository è pubblico: il download è anonimo e non servono token.

## Layout sulla macchina

```
INSTALL_DIR/
  current/            versione in uso (contiene PrintServer.Host)
  previous/           versione precedente, per il rollback
  .version            tag installato
  update.sh|update.ps1
```

## Linux e macOS (`update.sh`)

Requisiti: `bash`, `curl`, `jq`, `tar`.

```bash
sudo mkdir -p /opt/printserver
sudo cp update.sh /opt/printserver/update.sh
sudo chmod +x /opt/printserver/update.sh
sudo /opt/printserver/update.sh
```

Pianificazione su Linux (systemd):

```bash
sudo cp printserver-update.service printserver-update.timer /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now printserver-update.timer
```

Pianificazione su macOS (launchd):

```bash
sudo cp com.printserver.update.plist /Library/LaunchDaemons/
sudo launchctl bootstrap system /Library/LaunchDaemons/com.printserver.update.plist
```

Variabili d'ambiente (opzionali): `INSTALL_DIR` (default `/opt/printserver`), `SERVICE_NAME` (unit systemd, default `printserver`), `LAUNCHD_LABEL` (default `com.printserver`), `HEALTH_URL` (es. `http://127.0.0.1:5000/health`: se impostato, dopo l'aggiornamento attende una risposta e in caso contrario torna alla versione precedente), `HEALTH_TIMEOUT` (secondi, default 30), `REPO`.

## Windows (`update.ps1`)

```powershell
New-Item -ItemType Directory -Force C:\ProgramData\PrintServer
Copy-Item update.ps1 C:\ProgramData\PrintServer\update.ps1

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -ExecutionPolicy Bypass -File C:\ProgramData\PrintServer\update.ps1'
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Hours 1)
Register-ScheduledTask -TaskName 'PrintServer update' -Action $action -Trigger $trigger -User 'SYSTEM' -RunLevel Highest
```

Parametri: `-InstallDir`, `-ServiceName` (default `PrintServer`), `-HealthUrl`, `-HealthTimeout`, `-Repo`.

## Rollback

```bash
sudo /opt/printserver/update.sh --rollback
```

```powershell
.\update.ps1 -Rollback
```

Se dopo un aggiornamento il controllo di salute fallisce, lo script torna da solo alla versione precedente e ricorda il tag fallito per non ritentarlo finché non esce una release più recente.

## Note

- Finché il servizio di sistema non esiste (feature `service-hosting`), gli script aggiornano i file e segnalano che il riavvio va fatto a mano.
- Se l'Environment `personal` ha un revisore obbligatorio, la pubblicazione della pre-release attende la tua approvazione.
