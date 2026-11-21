# Mini Print Server — Contesto del progetto

Riferimento per lo sviluppo: obiettivi, vincoli e scelte tecniche. Struttura rami e PR: vedi `BRANCHING.md`.

## Obiettivo

Un **mini print server** che inoltra lavori di stampa e gestisce le code.

1. Un file viene posizionato in una **cartella** monitorata.
2. Il server lo rileva, lo mette in **coda** e lo invia alla stampante.
3. Il sistema **tiene traccia** di cosa fa (stato di ogni lavoro, errori, storico).

## Vincoli

- **Multipiattaforma**: Windows, macOS, Ubuntu (Linux).
- **C#** con **ASP.NET Core 10** (`net10.0`).
- **Repository GitHub pubblico**, riutilizzabile da terzi.
- **Licenza**: AGPL-3.0.

## Architettura

### Livello intermedio per piattaforma (iniezione di dipendenze)

Watcher, coda e web UI non conoscono il sistema operativo; la parte di piattaforma sta dietro un'interfaccia scelta all'avvio.

```csharp
public interface IPrinterBackend
{
    Task<IReadOnlyList<PrinterInfo>> ListPrintersAsync(CancellationToken ct);
    Task<PrintResult> PrintAsync(PrintRequest request, CancellationToken ct);
    Task<JobStatus> GetJobStatusAsync(string backendJobId, CancellationToken ct);
}
```

```csharp
if (OperatingSystem.IsWindows())
    builder.Services.AddSingleton<IPrinterBackend, WindowsSpoolerBackend>();
else if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
    builder.Services.AddSingleton<IPrinterBackend, CupsBackend>();
```

| Backend | Piattaforme | Note |
|---|---|---|
| `CupsBackend` | macOS, Ubuntu | `lp`/`lpstat` via `Process` con `ArgumentList` (mai stringhe concatenate: i nomi file sono input non fidato). |
| `WindowsSpoolerBackend` | Windows | `[SupportedOSPlatform("windows")]`; `winspool.drv` o `System.Printing`. Evitare `ShellExecute` con verbo "print". |
| `IppBackend` | Tutte | Implementazione propria del protocollo IPP, senza librerie esterne; stampanti di rete e ripiego. Selezionabile da `appsettings.json`. |
| `FakePrinterBackend` | Test | Testa coda, tentativi e stati senza stampante. |

Gli stati dei backend sono normalizzati in un `JobStatus` comune (in coda, in stampa, completato, errore).

### Componenti

1. **Watcher della cartella** (`FileSystemWatcher` in un `BackgroundService`): attende stabilità di dimensione e data di modifica; filtra estensioni e temporanei; polling con `UsePolling` (eventi duplicati/persi, cartelle di rete SMB/NFS, limiti inotify), e in automatico se gli eventi non sono disponibili. Una sottocartella di primo livello = una stampante; sottocartelle `verticale`/`orizzontale`/`fronte`/`fronte-retro`/`fronte-retro-corto` impostano orientamento e fronte/retro. Dettagli in `docs/watcher.md`.
2. **Coda persistente** (`PrintServer.Queue`): SQLite + EF Core con migrazioni, tabella `jobs` (id, origine, nome, percorsi, hash SHA-256, stampante, formato, opzioni di stampa, stato, tentativi, errore, timestamp). Stati `in_attesa` → `in_conversione` → `in_stampa` → `completato` / `fallito` / `in_stallo`. Chiave del file: cartella, nome, data di creazione e hash; un duplicato attivo va in `duplicati/`, una nuova versione sostituisce il lavoro in attesa. Il file passa subito in `processing/`. Un lavoro in stampa da più di un'ora senza problemi segnalati dalla stampante va in `stallo/`. All'avvio la coda si riallinea chiedendo al backend; passaggio watcher → coda con `System.Threading.Channels`. Dettagli in `docs/queue.md`.
3. **Worker di stampa** (`JobProcessor` + `JobProcessingService`): un lavoro alla volta per stampante, stampanti in parallelo, via `IPrinterBackend`; 5 tentativi con attesa esponenziale (30 s, 1, 2, 4 min) poi `fallito`; sposta il file in `processati/` o `errori/`; stampante inesistente o file mancante falliscono subito; a ogni giro controlla lo stato dei lavori in stampa e lo stallo. Dettagli in `docs/queue.md`.
4. **Tracciamento e interfaccia**: log su file con rotazione + tabella `jobs` come storico; pagina web di stato (Minimal API + Blazor/Razor Pages) con SignalR; eventuale CLI (`stato`, `coda`, `riprova <id>`).

### Esecuzione e distribuzione

- Servizio: `AddWindowsService()`, `AddSystemd()`, `launchd` su macOS; file e script in `deploy/service`, descrizione in `docs/service.md`.
- `dotnet publish` self-contained single-file: `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`.
- Installer: `.deb`, `.pkg`/Homebrew, `.msi`.

### Punti delicati

- **Formati**: i PDF si inviano direttamente via IPP/CUPS; Word/immagini vanno convertiti. Su Windows può servire un renderer.
- **Più stampanti**: una cartella (o sottocartella) per stampante.
- **Firewall e permessi**: una porta in ascolto può generare avvisi su Windows/macOS.
- **Test**: su tre piattaforme e con stampanti diverse; i driver sono la parte imprevedibile.
- Analizzatore **CA1416** attivo per chiamate a API Windows fuori da controlli di piattaforma.

## Licenza

Codice pubblico sotto **AGPL-3.0** (`LICENSE`), che copre anche l'uso via rete (quindi l'interfaccia web).

- Verificare la licenza di ogni dipendenza (`THIRD-PARTY-NOTICES.md`); attenzione a librerie PDF commerciali e a strumenti GPL come SumatraPDF.

## Branch Strategy

Catena `develop → personal → docs → integration → main`, feature/fix → `develop` con PR + squash (1 approvazione), commit Conventional Commits, rilasci con release-please. Dettagli in `BRANCHING.md`.

## Roadmap

Ogni voce è un ramo `feature/*` → `develop`:

- [x] `repo-bootstrap` (questo documento, template)
- [x] `ci-pipeline` (workflow 01–09, release-please, una release per ambiente)
- [x] `solution-scaffold` — soluzione .NET 10 (Worker + web)
- [x] `printer-backend-abstraction` — `IPrinterBackend` + `FakePrinterBackend`
- [x] `folder-watcher` — stabilità file + polling di riserva
- [x] `sqlite-job-queue` — coda SQLite/EF Core, ripresa dopo riavvio
- [x] `print-worker` — retry, `processati/`/`errori/`
- [x] `ipp-backend`, [x] `cups-backend`, [x] `windows-spooler-backend`
- [x] `print-options` — orientamento e fronte/retro da sottocartelle (`orizzontale`, `fronte-retro`, …) fino ai backend
- [x] `printer-status` — problemi della stampante (carta, toner, inceppamento, sportello, errore, offline) dai tre backend
- [ ] `status-web-ui` — pagina di stato + SignalR
- [x] `service-hosting` — Windows Service / systemd / launchd
- [ ] `cli`
- [ ] `publish-and-installers`

## Note per AI / LLM

- Non aggiungere commenti nel codice se non richiesto.
- Seguire le convenzioni esistenti.
- Non introdurre dipendenze senza verificarne prima la licenza (compatibilità con AGPL-3.0).
- I `.csproj` devono usare `TargetFramework` `net10.0`.
- Mai committare configurazioni sensibili (connection string, chiavi).
- Nomi di file e percorsi sono input non fidato: mai concatenarli in comandi di shell.
