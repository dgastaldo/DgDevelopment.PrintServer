# Backend di stampa

Il backend si sceglie in `appsettings.json` con `Printing:Backend`.

| Valore | Cosa usa | Note |
|---|---|---|
| `Fake` | stampante simulata | predefinito finché non scegli altro; utile per prove |
| `Auto` | `WindowsSpooler` su Windows, `Cups` su macOS e Linux | |
| `Cups` | `lp` e `lpstat` di CUPS | macOS, Linux |
| `WindowsSpooler` | spooler di Windows (`winspool.drv`) | solo Windows; dati RAW, una copia per lavoro |
| `Ipp` | protocollo IPP via HTTP | stampanti di rete e ripiego su tutte le piattaforme |

## Esempi

CUPS, con percorsi opzionali:

```json
{
  "Printing": {
    "Backend": "Cups",
    "Cups": { "LpPath": "/usr/bin/lp", "LpstatPath": "/usr/bin/lpstat" }
  }
}
```

IPP, con l'elenco delle stampanti da usare (la prima è quella predefinita):

```json
{
  "Printing": {
    "Backend": "Ipp",
    "Ipp": {
      "UserName": "printserver",
      "RequestTimeout": "00:00:30",
      "Printers": [
        { "Name": "Ufficio", "Uri": "ipp://stampante.local/ipp/print" },
        { "Name": "Etichette", "Uri": "ipps://etichette.local:8631/ipp/print" }
      ]
    }
  }
}
```

Altre opzioni IPP: `DocumentFormat` (se omesso si deduce dall'estensione: PDF, JPEG, PNG, testo, altrimenti `application/octet-stream`) e `AllowInvalidCertificates` (`true` solo per stampanti con certificato autofirmato).

## Limiti noti

- **CUPS**: un lavoro che non è più in coda viene considerato completato, perché `lpstat` non distingue tra completato e annullato. Un lavoro su una stampante disabilitata resta in coda.
- **Spooler di Windows**: lo spooler non converte il documento; il file deve essere in un formato che la stampante capisce da sola. Una sola copia per lavoro; sono elencate solo le stampanti installate sul computer.
- **IPP**: il formato del documento deve essere accettato dalla stampante; un lavoro che la stampante non conosce più risulta completato.
