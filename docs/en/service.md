[Italiano](../service.html) · **English**

# System service

The server can run as a service, started with the system and restarted if it stops: Windows Service, systemd on Linux, launchd on macOS. Started from a terminal it works as before.

The files are in `deploy/service`. The binary is installed first with the scripts in `deploy/personal` (the `current/` folder); these scripts install only the service.

## Machine configuration

The `appsettings.json` next to the binary is replaced at every update. The real settings therefore go in a machine file, outside `current/`, which the server reads at startup:

| System | File |
|---|---|
| Linux | `/etc/printserver/appsettings.json` |
| macOS | `/Library/Application Support/PrintServer/appsettings.json` |
| Windows | `%ProgramData%\PrintServer\appsettings.json` |

Another file can be chosen with the `PRINTSERVER_CONFIG` environment variable. The file is optional. It takes precedence over the `appsettings.json` next to the binary; environment variables and command-line arguments take precedence over it. An example is in `deploy/service/appsettings.example.json`; the scripts copy it if the file is not there:

```json
{
  "Urls": "http://127.0.0.1:5000",
  "Printing": { "Backend": "Auto" },
  "Watcher": { "Path": "/var/printserver/inbox" }
}
```

Without `Printing:Backend` the server uses the simulated backend (`Fake`). The [web page](webui.html) answers on the same address (`Urls`). See [backends](backends.html), [watched folder](watcher.html) and [queue](queue.html) for the other options.

## Linux (systemd)

```bash
sudo deploy/service/install-service.sh
```

The script creates the `printserver` system user, the configuration and the unit `/etc/systemd/system/printserver.service`, then enables and starts the service. The unit is of type `notify`: systemd considers the service ready when the server says so. The data folder `/var/lib/printserver` (the queue database) is created by systemd, owned by the service user.

**Permissions.** The service runs as `printserver`, not as root: the watched folder must be readable and writable by that user (the server creates `processing/`, `processed/` and the others inside it), for example `sudo install -d -o printserver /var/printserver/inbox`. To print with CUPS the user must be allowed to use the printers.

The script speaks English or Italian: it picks the language from the system's (`LANG`), or `--lang en|it` or the `PRINTSERVER_LANG` variable force it. Script options, as environment variables: `INSTALL_DIR` (default `/opt/printserver`), `SERVICE_NAME`, `SERVICE_USER`, `CONFIG_DIR`, `UNIT_DIR`; with `--no-start` it installs without starting.

```bash
systemctl status printserver
journalctl -u printserver -f
```

## macOS (launchd)

```bash
sudo deploy/service/install-service.sh
```

It installs `/Library/LaunchDaemons/com.printserver.plist` and loads it. The process runs as root and launchd restarts it if it stops (`KeepAlive`). The log is in `/var/log/printserver.log`.

```bash
sudo launchctl print system/com.printserver
```

## Windows

From PowerShell as administrator:

```powershell
.\deploy\service\install-service.ps1
```

It creates the `PrintServer` service with automatic start, pointing to `C:\ProgramData\PrintServer\current\PrintServer.Host.exe`, and sets automatic restart after a failure (the first and second time after 5 seconds, the third after 30). It runs as `LocalSystem`, which sees the printers installed on the computer; for another user use `-Credential`. Options: `-InstallDir`, `-ServiceName`, `-NoStart`, `-Language en|it` (otherwise `PRINTSERVER_LANG`, then the Windows display language). The log goes to the Windows event log.

## Updates and removal

The update scripts in `deploy/personal` (also in English or Italian, according to `PRINTSERVER_LANG` or the system language) restart the service (`SERVICE_NAME` / `-ServiceName` with the same name used here) and, with `HEALTH_URL`, go back to the previous version if it does not answer. The health address is `/health`.

To remove the service: `sudo systemctl disable --now printserver` and delete the unit; `sudo launchctl bootout system/com.printserver` and delete the plist; `Stop-Service PrintServer; sc.exe delete PrintServer`. The data (queue, configuration) is not touched.

## Known limits

- **Document conversion:** it needs LibreOffice on the machine and the service user must be able to run it (see [conversion](conversion.html)). The Snap version of LibreOffice may have file access restrictions: if there are problems, use a traditional package.
- **Only partly tested:** the service code and the scripts were checked for syntax and with local tests, not on a real systemd, launchd or Windows.
- **A single service user:** on Windows, network printers attached to a user's profile are not visible to `LocalSystem`.
- **Port and address:** `Urls` listens on `127.0.0.1` in the example; opening it to the whole network must be a deliberate choice (and done with a firewall), because for now there is no authentication.
