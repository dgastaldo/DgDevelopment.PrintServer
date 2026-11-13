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

- Ogni merge in `develop` passa da una **Pull Request** con **1 approvazione** e con i controlli `build-test (ubuntu-latest)` e `build-test (windows-latest)` verdi. Il controllo su macOS gira solo dopo il merge (`03-develop-quality-gate`).
- Solo **squash merge**, ramo eliminato dopo il merge.
- PR aperta subito (Draft se il lavoro è in corso); a lavoro finito `gh pr ready`.
- PR "stacked": se una feature dipende da un'altra in corso, il ramo parte dal padre e la PR punta al ramo padre.

## Promozione

`develop → personal → docs → integration → main`, sempre tramite PR (merge, non squash, per mantenere i rami allineati).

- `develop → personal`: PR aperta e unita a mano.
- Dal merge in `personal` in poi `05-promote.yml` crea il tag di deploy e apre la PR verso il ramo successivo; ogni salto resta un'approvazione umana.
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
