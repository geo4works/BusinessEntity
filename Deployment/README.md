# BusinessEntity Release Bundle

This folder contains the installation template for a packaged Business Entity release.

In simple terms: the application itself lives in the main project folders, but this
folder describes how to deliver it to another machine. The release builder
`Powershell/Build-ReleaseBundle.ps1` copies these files into a downloadable archive.
After that, a user can unpack the archive, run `install.ps1` or `install.bat`, and
get a local Docker-based Business Entity instance without manually assembling the
Compose stack.

This folder is useful for deployment and first installation. It is not required for
day-to-day local development from source.

The folder contains:

- `docker-compose.yml` - the Docker Compose stack used by the release bundle;
- `.env.example` - a template for local deployment settings and secrets;
- `install.ps1` / `install.bat` - first-run installer scripts;
- `deploy.ps1` - helper commands for status, logs, restart and stop;
- `scripts/bootstrap-initial-data.ps1` - initial data/bootstrap helper;
- `README.md` - this deployment note.

## Install

Windows:

```powershell
.\install.ps1
```

or:

```cmd
install.bat
```

The installer:

- creates `.env` from `.env.example`;
- generates missing secrets;
- creates local runtime folders;
- loads offline Docker images from `images/*.tar`, if present;
- creates the shared Docker network;
- runs `docker compose up -d`;
- waits for basic HTTP health checks.

Docker Desktop, Docker Engine, or another Docker-compatible runtime must already be installed.

## Operations

```powershell
.\deploy.ps1 status
.\deploy.ps1 logs -Service business-entity
.\deploy.ps1 restart
.\deploy.ps1 stop
```

The installer generates `AUTHENTIK_BOOTSTRAP_TOKEN` and `AUTHENTIK_CLIENT_SECRET` once
and preserves them on subsequent runs. The Authentik worker receives the bootstrap
credentials, while the application uses the same token for the Admin API. Application
startup waits for default Authentik flows and scope mappings, creates/updates the
`BusinessEntity` application/catalog group and OIDC provider, and then initializes users.

Initial application accounts are `akadmin` / `akadmin` and `admin` / `admin`.
The existing admin policy restores the technical `akadmin` password to `akadmin`.
An existing `admin` account retains its password and profile; missing group membership
and local role assignments are added. No passwords or tokens are stored in local user DTOs.

Keep the generated `.env` with this installation: changing the bootstrap token does not
rotate an already created Authentik token. For an existing Authentik with a different
management token, supply that token as `AUTHENTIK_API_TOKEN`. OIDC provisioning is enabled
by default through `ENSURE_AUTHENTIK_ON_STARTUP=true`; set it to `false` only for an
externally configured OIDC provider.

Set `AUTHENTIK_BASE_URL_FOR_BROWSER` and `AUTHENTIK_REDIRECT_URIS` to deployment URLs.
The first redirect URI is also used by the application during authorization-code login.
Startup fails visibly if required Authentik provisioning cannot complete; check application
logs instead of treating a running Authentik health endpoint as a completed installation.
