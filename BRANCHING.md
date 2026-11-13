# Branching Strategy — PrintServer

Modello ripreso da DgDevelopment.Identity (stato reale: catena a cinque rami, tag di deploy a data).

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

- Ogni merge in `develop` passa da una **Pull Request** con **1 approvazione**.
- Solo **squash merge**, ramo eliminato dopo il merge.
- PR aperta subito (Draft se il lavoro è in corso); a lavoro finito `gh pr ready`.
- PR "stacked": se una feature dipende da un'altra in corso, il ramo parte dal padre e la PR punta al ramo padre.
- Le PR esterne richiedono la firma del CLA (vedi `CLA.md`).

## Promozione

`develop → personal → docs → integration → main`, sempre tramite PR (merge, non squash, per mantenere i rami allineati).

- `develop → personal`: PR aperta e unita a mano.
- Dal merge in `personal` in poi `04-promote.yml` crea il tag di deploy e apre la PR verso il ramo successivo; ogni salto resta un'approvazione umana.
- Tag di deploy: `personal/vAAAA.MM.GG.N`, `docs/v…`, `integration/v…`, e `vAAAA.MM.GG.N` su `main` (N = numero di run).

## Hotfix

1. `fix/<descrizione>` da `main`, PR verso `main`, squash.
2. Backport all'indietro con PR: `main → integration → docs → personal → develop`.

## Versioning

- Versioni semantiche dei componenti gestite da **release-please** (tag `server-vX.Y.Z`, ...) a partire dai Conventional Commits su `develop`; la Release PR è l'approvazione umana.
- I tag di deploy (a data) sono marcatori di ambiente, distinti dalle versioni semantiche.
