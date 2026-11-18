# Branching Strategy — PrintServer

Modello ripreso da DgDevelopment.Identity (stato reale: catena a cinque rami, una release per ambiente con la versione di release-please).

## Branch Model

```
main ─────────── Produzione (release pubbliche)
  ▲
integration ──── Test cross-platform con stampanti reali, installer candidati
  ▲
docs ─────────── Documentazione + OpenAPI dell'API di stato
  ▲
personal ─────── Istanza sempre attiva sulla macchina personale
  ▲
develop ──────── Sviluppo attivo (base di tutti i feature/fix)
  ▲
feature/*  fix/*
```

| Branch | Scopo | Commit diretti | Riceve |
|---|---|---|---|
| `main` | Produzione | Mai | Merge da `integration` |
| `integration` | Test su Windows/macOS/Linux con stampanti reali | Mai | Merge da `docs` (avanti) o `main` (indietro) |
| `docs` | Documentazione e OpenAPI | Mai | Merge da `personal` (avanti) o `integration` (indietro) |
| `personal` | Deploy personale sempre attivo | Mai | Merge da `develop` (avanti) o `docs` (indietro) |
| `develop` | Sviluppo attivo | Solo PR + squash | `feature/*` e `fix/*` |

## Naming

`feature/<descrizione>` e `fix/<descrizione>`, kebab-case, in inglese (es. `feature/folder-watcher`, `fix/duplicate-print-on-restart`).

## Commit Convention (Conventional Commits)

```
<type>: <short description>
```

`feat`, `fix`, `docs`, `refactor`, `test`, `chore`, `perf`, `style`. Servono a release-please per calcolare versione e CHANGELOG.

## Pull Request Policy

- Ogni merge in `develop` passa da una **Pull Request** con **1 approvazione** e con tutti questi controlli verdi:
  - `build-test (ubuntu-latest)` e `build-test (windows-latest)`;
  - `dependency-audit` (non `dependency-audit-full`, che resta informativo: può segnalare vulnerabilità senza fix e non deve bloccare il merge);
  - `SonarCloud Code Analysis`, cioè il Quality Gate di SonarQube Cloud, alimentato dal job `sonar` con autenticazione OIDC (il job in sé ha `continue-on-error` e non è un requisito);
  - CodeQL: `Analyze (csharp)` e `Analyze (actions)`, più la regola sul code scanning, che blocca il merge se la PR introduce alert di sicurezza di gravità `high` o superiore, o altri alert di livello `error`.
- Il ramo della PR deve essere aggiornato con l'ultimo `develop` prima del merge (i controlli ripartono dopo l'aggiornamento).
- Il ruleset attiva anche la review automatica di Copilot a ogni push, bozze comprese. Gli amministratori possono aggirare le regole solo tramite PR, mai con push diretto.
- L'approvazione è automatica (`02-auto-approve`) per le PR del proprietario del repository non in bozza e per quelle di Dependabot di tipo patch o minor; un major di Dependabot richiede una review. Le PR di Dependabot e di release-please seguono per il resto le stesse regole.
- Il controllo su macOS gira solo dopo il merge (`03-develop-quality-gate`), non è richiesto e non blocca la promozione.
- Questi requisiti valgono **solo per `develop`**: `01-pr-quality-gate` parte solo sulle PR verso `develop`, quindi sulle PR di promozione (`personal`, `docs`, `integration`, `main`) non si richiede nessun controllo oltre a PR e approvazione.
- Metodo di merge: **squash** per feature e fix. Il ruleset ammette anche il merge commit, che serve solo ai backport degli hotfix verso `develop` (vedi Hotfix); il rebase non è ammesso. Il ramo viene eliminato dopo il merge.
- PR aperta subito (Draft se il lavoro è in corso); a lavoro finito `gh pr ready`.
- PR "stacked": se una feature dipende da un'altra in corso, il ramo parte dal padre e la PR punta al ramo padre.

## Promozione

`develop → personal → docs → integration → main`, sempre tramite PR (merge, non squash, per mantenere i rami allineati).

- `develop → personal`: PR aperta e unita a mano, con merge commit (`gh pr create --base personal --head develop`). `05-promote.yml` non scatta sui merge in `develop`.
- Dal merge in `personal` in poi `05-promote.yml` crea il tag di deploy e apre in automatico la PR verso il ramo successivo (riusa quella già aperta); ogni salto resta un'approvazione umana.
- Ogni ambiente ha la sua release GitHub, con la versione che release-please tiene in `.release-please-manifest.json` al commit promosso:

  | Ramo | Tag | Release |
  |---|---|---|
  | `personal` | `personal/vX.Y.Z` | pre-release con i binari (`06-deploy-personal`) |
  | `docs` | `docs/vX.Y.Z` | pre-release con le note di versione (`07-docs-publish`) |
  | `integration` | `integration/vX.Y.Z` | pre-release con i binari (`08-release-integration`) |
  | `main` | `vX.Y.Z` | release di produzione con i binari (`09-publish-binaries`) |

- Se il tag esiste già (nuova promozione senza una nuova versione di release-please) si aggiunge il numero di run come build metadata: `personal/vX.Y.Z+42`.

## Hotfix

1. `fix/<descrizione>` da `main`, PR verso `main`, squash.
2. Backport all'indietro con PR: `main → integration → docs → personal → develop`.

## Versioning

- Versioni semantiche dei componenti gestite da **release-please** (tag `server-vX.Y.Z`, ...) a partire dai Conventional Commits su `develop`; la Release PR è l'approvazione umana.
- Il tag `server-vX.Y.Z` di release-please marca solo la versione su `develop` e non pubblica nulla: la produzione si raggiunge solo con la promozione fino a `main`.
