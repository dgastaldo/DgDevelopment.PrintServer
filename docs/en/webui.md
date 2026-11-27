[Italiano](../webui.html) · **English**

# Status web page

The server has a web page (Blazor Server, in English or Italian) to see the queue and manage it: job states, printers with their problems, the detail of every job, and, for those who have the permission, retry, cancel and add files. It refreshes itself every few seconds.

| Page | What it shows | Permission |
|---|---|---|
| `/` Status | How many jobs per state, printers with paper/toner/errors, latest jobs | view |
| `/jobs` Jobs | List with filter by state and file name, pages of 25 | view (actions: manage) |
| `/jobs/{id}` | All the data of a job: error, attempts, times, folder, file | view (actions: manage) |
| `/add` Add | Upload one or more files choosing printer, orientation and duplex | add |

The actions are **Retry** (a failed, stalled or cancelled job goes back to the queue, see [queue](queue.html)), **Cancel** (a pending, converting or printing job; if it is already at the printer it is removed from there too) and **Add**. An added file ends up in the watched folder (the one for the chosen printer and options, see [watched folder](watcher.html)) and the watcher queues it like any other.

## How to sign in

There are two ways, depending on the configuration.

### With DgDevelopment.Identity (OIDC)

If you set the provider's address and client, the page requires sign-in with [DgDevelopment.Identity](https://github.com/dgastaldo/DgDevelopment.Identity) (code flow with PKCE). Unauthenticated users are sent to the provider; whoever has no permission gets a 403 error.

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

The secret does not belong in the versioned configuration file: put it in the machine's configuration file (see [system service](service.html)) or in the `WebUi__ClientSecret` environment variable.

**What to register in Identity**

1. A **confidential** client for PrintServer, with its secret, on a platform that puts permissions in the token (`IdentityManaged`).
2. The return addresses: `https://<host>/signin-oidc` and, for sign-out, `https://<host>/signout-callback-oidc`.
3. The three permissions (names can be changed, see below) to assign to users, roles or groups:

| Permission | Default | What it allows |
|---|---|---|
| View | `printserver.view` | Enter and see status, list and detail |
| Manage | `printserver.manage` | Retry and cancel jobs |
| Add | `printserver.add` | Add files from the page |

Any one of the three is enough to enter the page; every action requires its own permission (even if someone tries to call it without the button). Assign `view` together with the others.

Permissions are read from the `permissions` claim of the access token and stay what they were at sign-in: if you change them, the user sees them at the next sign-in (the session lasts 8 hours). The name of the claim and of the permissions is changed with `PermissionClaim`, `ViewPermission`, `ManagePermission` and `AddPermission`.

### Without a provider: read-only, from this machine only

If `Authority` and `ClientId` are missing, the page is reachable **only by whoever connects from the machine itself** (address 127.0.0.1 or ::1), with no sign-in and no actions: you can look but not change anything, and the bar at the top says so. Whoever arrives from another address gets a 403 error.

Do not put a reverse proxy on the same machine in front of the server in this mode: its requests would come from 127.0.0.1 and pass as local. With a proxy, configure the OIDC provider.

## Options

| Option | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | If `false` the page does not exist (`/health` and the rest remain). |
| `Authority` | none | Address of the OIDC provider (no trailing slash). |
| `ClientId`, `ClientSecret` | none | Client registered in the provider. |
| `RequireHttpsMetadata` | `true` | Requires HTTPS to read the provider's configuration; set it to `false` only for tests over HTTP. |
| `PermissionClaim` | `permissions` | Name of the claim with the permissions in the access token. |
| `ViewPermission`, `ManagePermission`, `AddPermission` | `printserver.view`, `printserver.manage`, `printserver.add` | Names of the three permissions. |
| `SessionLifetime` | 8 hours | How long the session lasts after sign-in. |
| `RefreshInterval` | 3 seconds | How often the page refreshes. |

Files added from the page have a fixed limit of 25 MB each (not configurable); the page states it next to the upload field. Larger files must be copied straight into the watched folder.

## Language

The page is available in **English** (the default) and **Italian**. The language is chosen automatically from the browser's (`Accept-Language`); the **EN** and **IT** links at the top right fix it and remember it for a year in a cookie (`.AspNetCore.Culture`). Unknown languages fall back to English.

The texts live in `src/PrintServer.WebUi/Resources/Strings.resx` (English) and `Strings.it.resx` (Italian). The keys are in English (`State.Pending`, `Action.Retry`, …), so even a text that has not been translated yet stays understandable. To add a language: a new `Strings.<code>.resx` with the same keys (a test checks that the keys match) and the code in `WebUiServiceCollectionExtensions.Languages`.

## HTTPS and address

The server listens where `Urls` says (see [system service](service.html)); the example uses `http://127.0.0.1:5000`. To use the page from other computers put a proxy with HTTPS in front and pass the forwarding headers (the `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` variable): the provider's return addresses are built from the request's. Over plain HTTP sign-in works but the password and the session travel in the clear.

## Known limits

- **Not tested with a real DgDevelopment.Identity instance:** sign-in was tested with a fake OIDC provider (code, PKCE, nonce, permissions in the token) and by reading the provider's code. Things to check on first use: that the return addresses are registered, that the issuer equals `Authority` and that the permissions arrive in the access token.
- **Cancelling at the printer:** it depends on the backend (CUPS, IPP, spooler). If it does not answer, the job is cancelled in the queue and a warning appears: check the printer's queue.
- **A single instance:** the page reads the queue database; with several server instances on the same database it is untested.
