# ADR 0001: Extract requisition ownership before switching its page

Status: accepted design; implementation pending.

## Context

The application must demonstrate a real migration from a functioning Razor Pages monolith while the monolith continues serving unmigrated pages. Changing the frontend alone would leave the business boundary inside the monolith. Moving traffic between independent writable stores would risk divergent records.

## Decision

Use YARP as one public entry point. Extract the bounded requisition feature into an ASP.NET Core API, then introduce React for that feature's route. Dashboard and notes remain Razor Pages.

Keep one PostgreSQL database. Preserve existing requisition records through a controlled schema-and-permission expand-contract transition. The API becomes their exclusive logical owner; the monolith runtime loses direct feature access. A separate migration role controls schema changes.

Retain a Razor requisition page that delegates to the API after extraction. Page selection can then change independently of data ownership. UI rollback returns to that adapter, not to the original database writer.

Use compatible server authentication cookies and a persistent shared data-protection keyring. Enforce API owner authorization and antiforgery validation regardless of which page invokes an operation.

## Alternatives considered

| Alternative | Reason not selected |
| --- | --- |
| Rewrite the entire application | Removes the coexistence boundary the application needs to demonstrate. |
| Replace only the page with React | Does not extract requisition business ownership. |
| Dual-write independent databases | Adds reconciliation and partial-failure behavior without a product need. |
| Give both runtimes lasting direct feature access | Leaves ownership ambiguous and permits bypassing API rules. |
| Roll back backend writes with a routing flag | Cannot establish data compatibility or fence the previous writer. |

## Consequences

Both page implementations depend on the extracted API. The database remains shared infrastructure, while schema privileges enforce the application boundary. The ownership transition may require brief feature-write quiescence; uninterrupted writes and automatic backend rollback are not claimed. Shared authentication keys create a deliberate trust relationship between server applications.

## Verification required

Preserve records created by the original monolith; verify them through the extracted API and both pages. Deny direct feature writes by the former runtime role. Exercise UI switching after new API writes, stale edits, cross-owner access, malformed requests, antiforgery rejection, API outage and ordinary restart. Interrupt the ownership transition in isolated state and verify the documented recovery path before claiming cutover safety.

## Sources

- [Microsoft: Strangler Fig pattern](https://learn.microsoft.com/en-us/azure/architecture/patterns/strangler-fig)
- [YARP configuration](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-files?view=aspnetcore-10.0)
- [PostgreSQL schemas and privileges](https://www.postgresql.org/docs/18/ddl-schemas.html)
- [ASP.NET Core shared cookies](https://learn.microsoft.com/en-us/aspnet/core/security/cookie-sharing?view=aspnetcore-10.0)
- [ASP.NET Core antiforgery](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)
