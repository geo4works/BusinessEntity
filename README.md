## Business Entity Engine [RUS](README.ru.md)

Business Entity Engine is a self-deployable platform for building knowledge bases on business objects, where the core data model is built around a full graph of business entities.

## Current Features

The current application is an ASP.NET Core / Blazor Server system that includes:

- spaces as the top-level work context;
- folders, documents and rich documents as business entities;
- the ability to store infinite texts in rich documents (limited only by your database resources);
- the ability to upload images, video and audio and embed them into pages;
- authentication through Authentik;
- administration of users, roles, groups and access rights at the space level;
- deployment assets for Docker Compose;

## Philosophy of Business Objects

This system stores information in something called a business object. Business objects have commutativity, meaning they can be inserted into each other (embed) or form applications that exist inside business entities.

Applications can be part of the build, or they can be delivered inside plugins. The plugin and application mechanism is currently being developed.

## AI Policy

This product is 99% written through AI (windsurf, codex, claude), and the author plans to continue this way. The author is not a vibe coder and understands everything that happens in the code, all technologies and processes.

All instructions used in prompting are located in the Context folder.

## Project Status

Business Entity Engine is at the public incubation / preview stage.

At this stage, the platform intentionally remains as open as possible for practical feedback. You can install it, fork it, modify it, test it, use it for prototypes, write plugins and validate it in real scenarios.

Important preview-stage notes:

- APIs, storage layout, deployment scripts and migration mechanics may change.
- This is not yet a polished production distribution.
- Security, backup, storage and the access-rights model are still being hardened.
- Plugin and application mechanisms have not yet been developed.
- Before exposing an instance to the network, carefully review configuration and secrets.

## License

The source code and documentation in this repository are distributed under the MIT License. See [LICENSE](LICENSE).

### Attention!

At the public incubation stage, the current public branch of Business Entity Engine is distributed under the MIT License. After a stable version is released, the project may introduce other licensing options, including more closed commercial editions or distributions. At the same time, the last version published under the MIT License will be fixed as an MIT version and will remain available in that capacity forever.

The MIT License applies to code and documentation. It does not transfer rights to use the Business Entity name, logo, icons, visual identity, official project status, names of official releases, official domains or official distribution channels in a way that may mislead users.

See TRADEMARK.md for the brand and trademark policy.

## Brand Boundary

Forks and derived projects are welcome.

You may correctly write:

- "Fork of Business Entity Engine"
- "Powered by Business Entity Engine"
- "Compatible with Business Entity"
- "Unofficial plugin for Business Entity"
- "Built on Business Entity Engine"

You must not present a fork, service, hosting offering, package, organization or website as the official Business Entity without written permission from the brand owner.

## Repository Layout

```text
BusinessEntity/              Main ASP.NET Core / Blazor Server application
BlazorServerWebLogger/       Web logging service
BusinessEntity.Resources/    Shared resources
BusinessEntityStorage/       Local runtime storage, not part of the public release
Deployment/                  Release bundle files and install scripts
Context/                     Architecture notes and project policies
Powershell/                  Scripts for development and release bundle builds
docker-compose.yml           Development Docker Compose stack
CHANGELOG.md                 Changelog
LICENSE                      MIT license
TRADEMARK.md                 Brand and trademark policy
```

## Running From Source

Requirements:

- Docker-compatible runtime;
- Docker Compose plugin;
- .NET SDK 6.0 if the application is built or run outside Docker.

For a local development stack:

```powershell
./Powershell/Initialize-AuthentikEnv.ps1
docker network create docker-business-entity-common-bridge
docker compose up -d --build
```

The environment initializer preserves existing credentials and generates missing bootstrap
and OIDC client secrets. The Authentik worker creates the initial administrator and API
token. Before initializing users, the application waits for Authentik and configures the
`BusinessEntity` application/catalog group, `be-oidc` provider, callback URLs and group claims.
Initial logins are `akadmin` / `akadmin` and `admin` / `admin`. Existing `admin` passwords
and profiles are preserved. Re-running the initializer and restarting the stack reuses the
same credentials and Authentik objects.

When changing the application or Authentik ports, set `AUTHENTIK_REDIRECT_URIS` (first URL
is the application's callback) and `AUTHENTIK_BASE_URL_FOR_BROWSER` to the corresponding
public URLs. `ENSURE_AUTHENTIK_ON_STARTUP=false` disables OIDC provisioning for installations
managed externally; their Admin API token can be supplied as `AUTHENTIK_API_TOKEN`.

Default local endpoints:

- Business Entity: `http://localhost:7000`
- Authentik: `http://localhost:9000`
- Web Logger: `http://localhost:5080`

The root `docker-compose.yml` is development-oriented. Before public deployment, replace development secrets, review ports, review Authentik settings and use deployment assets from [Deployment](Deployment/README.md).

## Contributing

The repository is currently at an early public incubation stage. The main contribution channel is GitHub Issues.

We do not expect people to submit pull requests to this upstream repository. At this stage, the project needs feedback, not an external stream of branches and PRs. Forks for experiments are fine, but they should live as separate unofficial work unless explicitly agreed otherwise.

Issues are especially useful for:

- installation problems;
- deployment feedback;
- bug reports;
- security reports;
- feedback on storage and migration;
- feedback on the permission model;
- rich-document editing scenarios;
- experiments with plugins and mini-apps.

## Official Status

Only repositories, releases, packages, websites and services explicitly published by the Business Entity maintainers are considered official Business Entity distributions.

Forks and community builds must clearly identify themselves as unofficial.

## Additional Documents

- [TRADEMARK.md](TRADEMARK.md) - brand and trademark policy.
- [AUTHOR_NOTE.md](AUTHOR_NOTE.md) - author's note on project philosophy and development choices.
