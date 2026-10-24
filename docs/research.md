# Technical research

Official sources checked on 24–25 October 2026. The current implementation is the Razor Pages monolith; API extraction and React routing remain planned. Upstream documentation explains supported mechanisms. Runtime behavior comes from the application source and its executable checks, rather than from documentation alone.

## Current implementation

| Source | Documented behavior | Application decision |
| --- | --- | --- |
| [.NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) | Microsoft publishes the .NET 10 SDK and ASP.NET Core runtime releases. | Pin the SDK and runtime for reproducible container builds. |
| [YARP configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files?view=aspnetcore-10.0) and [HTTP client configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/http-client-config?view=aspnetcore-10.0) | Routes select destination clusters; each cluster has an HTTP forwarding client with configurable connection limits. | Use one application origin, a monolith fallback and a bounded upstream connection pool. |
| [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0) and [Kestrel limits](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options?view=aspnetcore-10.0) | Native concurrency admission and connection limits constrain different resources. | Bound admitted requests without a waiting queue and configure finite server connections; do not treat these values as throughput evidence. |
| [ASP.NET Core static assets](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/static-files?view=aspnetcore-10.0) | Built assets have dedicated endpoints, content-based ETags and cache headers. | Serve shared CSS and the self-hosted font from the gateway before its proxy fallback. Preserve native caching only for registered public assets. |
| [ASP.NET Core cookie authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0) and [antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0) | Cookie authentication establishes the caller; automatically attached cookies require antiforgery protection on mutations. | Use server-issued sessions, participant and role checks, native form tokens and explicit JSON antiforgery headers. |
| [Npgsql basic usage](https://www.npgsql.org/doc/basic-usage.html) | A data source manages PostgreSQL connections and pooling; parameterized commands separate values from SQL. | Use Npgsql directly with a bounded pool, parameterized queries and short transactions. |
| [Scalar ASP.NET Core integration](https://scalar.com/products/api-references/integrations/aspnetcore/integration) | Scalar can render Microsoft-generated OpenAPI and offers a packaged script and configurable reference options. | Keep inspection authenticated and local, use same-origin URLs, and disable external font, proxy, agent and telemetry behavior. |
| [Adminer](https://www.adminer.org/en/) and [release 6.2.0](https://github.com/vrana/adminer/releases/tag/v6.2.0) | Adminer supports PostgreSQL schema and record inspection through native database authentication. | Use the checksum-pinned release with a fixed read-only database login. The viewer has no mounted database password. |
| [OpenObserve OTLP ingestion](https://openobserve.ai/docs/ingestion/logs/otlp/) and [OSS source](https://github.com/openobserve/openobserve/tree/v1.1.2) | OpenObserve accepts OTLP logs and provides a searchable self-hosted log interface. | Export bounded allowlisted request summaries over OTLP/HTTP using an ingestion-only credential; keep the viewer outside the request dependency path. |
| [PostgreSQL locale support](https://www.postgresql.org/docs/18/locale.html) and [collation support](https://www.postgresql.org/docs/18/collation.html) | The built-in `C.UTF-8` locale orders by Unicode code points and has defined Unicode character behavior. | Initialize the local database with the built-in provider and `C.UTF-8`; do not assume language-specific dictionary ordering or reuse a database directory across incompatible libc environments. |

### Repository pins

| Component | Pinned version | Canonical declaration |
| --- | --- | --- |
| .NET SDK | 10.0.402 | [global.json](../global.json), [Dockerfile](../Dockerfile) |
| ASP.NET Core runtime | 10.0.13 | [Dockerfile](../Dockerfile) |
| YARP | 2.3.0 | [Directory.Packages.props](../Directory.Packages.props) |
| Npgsql | 10.0.3 | [Directory.Packages.props](../Directory.Packages.props) |
| OpenTelemetry hosting and OTLP exporter | 1.20.1 | [Directory.Packages.props](../Directory.Packages.props) |
| Microsoft.AspNetCore.OpenApi | 10.0.13 | [Directory.Packages.props](../Directory.Packages.props) |
| Scalar.AspNetCore | 2.18.3 | [Directory.Packages.props](../Directory.Packages.props) |
| xUnit v3 | 4.0.2 | [Directory.Packages.props](../Directory.Packages.props) |
| PostgreSQL | 18.6, Alpine 3.24 | [PostgreSQL image](../infrastructure/postgres.Dockerfile) |
| Adminer application | 6.2.0 | [Viewer image](../infrastructure/adminer.Dockerfile) |
| OpenObserve OSS | 1.1.2 | [compose.yaml](../compose.yaml) |

Container base digests and narrowly pinned distribution package updates are recorded in their Dockerfiles. The Adminer image uses the pinned official standalone base plus the checksum-verified upstream 6.2.0 application. Source Sans 3 is self-hosted with an immutable source and license in its [provenance record](../src/Gateway/wwwroot/assets/SourceSans3-PROVENANCE.md). Dependency lock files and native Microsoft.Testing.Platform execution are part of the .NET build; no VSTest compatibility layer is used.

## Planned extraction

| Source | Documented behavior | Planned decision |
| --- | --- | --- |
| [Microsoft: Strangler Fig pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/strangler-fig) | A facade directs requests between existing and replacement functionality; cross-system dependencies need an explicit adapter. | Extract requisition behavior while dashboard and notes remain Razor Pages. |
| [ASP.NET Core cookie sharing](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing?view=aspnetcore-10.0) | Shared tickets require compatible authentication schemes, application name, cookie settings and data-protection keys. | Share the trusted server authentication boundary while retaining independent role and participant checks. |
| [PostgreSQL schemas](https://www.postgresql.org/docs/18/ddl-schemas.html), [ALTER TABLE](https://www.postgresql.org/docs/18/sql-altertable.html), [REVOKE](https://www.postgresql.org/docs/18/sql-revoke.html) and [locking](https://www.postgresql.org/docs/18/explicit-locking.html) | Schemas share a database; table schema changes and access revocation are supported, and conflicting operations can wait on locks. | Preserve existing records with a controlled schema-and-permission transition and bounded feature-write drain. Verify old-writer fencing directly. |
| [React: add to an existing project](https://react.dev/learn/add-react-to-an-existing-project) | React can serve a dedicated route within an existing application. | Introduce a requisition route and retain native full-page navigation to Razor. |
| [Vite production build](https://vite.dev/guide/build) | Vite builds static assets and supports a nested public base path. | Serve compiled feature assets at the configured route; do not add a public development server to the runtime. |

## Alternatives

A full rewrite would remove the coexistence boundary. Embedding React in every Razor page would distribute the migration across the whole site. Separate browser origins would add cross-origin configuration without helping this feature. Dual writes would require reconciliation and partial-failure handling.

The planned extraction keeps one PostgreSQL database and moves logical ownership through schema and privilege changes. Physical database separation remains outside this scope. Bounded synchronous requisition operations require no queue, object storage, AI integration or cloud service.
