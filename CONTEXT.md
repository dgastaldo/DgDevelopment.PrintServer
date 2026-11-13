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
| `IppBackend` | Tutte | Libreria IPP (es. SharpIpp, da verificare); stampanti di rete e ripiego. Selezionabile da `appsettings.json`. |
| `FakePrinterBackend` | Test | Testa coda, tentativi e stati senza stampante. |

Gli stati dei backend sono normalizzati in un `JobStatus` comune (in coda, in stampa, completato, errore).

### Componenti

1. **Watcher della cartella** (`FileSystemWatcher` in un `BackgroundService`): attende stabilità di dimensione e data di modifica; filtra estensioni e temporanei; polling di riserva (eventi duplicati/persi, cartelle di rete SMB/NFS, limiti inotify).
2. **Coda persistente**: SQLite + EF Core, tabella `jobs` (id, percorso, hash, stampante, stato, tentativi, errore, timestamp creazione/inizio/fine). Stati `in_attesa` → `in_stampa` → `completato` / `fallito`. All'avvio i lavori `in_stampa` tornano in coda; l'hash evita doppie stampe; passaggio watcher → worker con `System.Threading.Channels`.
3. **Worker di stampa**: un lavoro alla volta (o uno per stampante) via `IPrinterBackend`; retry con attesa crescente (3 tentativi) poi `fallito`; sposta il file in `processati/` o `errori/`.
4. **Tracciamento e interfaccia**: log su file con rotazione + tabella `jobs` come storico; pagina web di stato (Minimal API + Blazor/Razor Pages) con SignalR; eventuale CLI (`stato`, `coda`, `riprova <id>`).

### Esecuzione e distribuzione

- Servizio: `UseWindowsService()`, `UseSystemd()`, `launchd` su macOS.
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
- [ ] `folder-watcher` — stabilità file + polling di riserva
- [ ] `sqlite-job-queue` — coda SQLite/EF Core, ripresa dopo riavvio
- [ ] `print-worker` — retry, `processati/`/`errori/`
- [ ] `ipp-backend`, [x] `cups-backend`, `windows-spooler-backend`
- [ ] `status-web-ui` — pagina di stato + SignalR
- [ ] `service-hosting` — Windows Service / systemd / launchd
- [ ] `cli`
- [ ] `publish-and-installers`

## Note per AI / LLM

- Non aggiungere commenti nel codice se non richiesto.
- Seguire le convenzioni esistenti.
- Non introdurre dipendenze senza verificarne prima la licenza (compatibilità con AGPL-3.0).
- I `.csproj` devono usare `TargetFramework` `net10.0`.
- Mai committare configurazioni sensibili (connection string, chiavi).
- Nomi di file e percorsi sono input non fidato: mai concatenarli in comandi di shell.
