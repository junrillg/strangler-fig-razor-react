# Strangler Fig with Razor Pages and React

An ASP.NET Core Razor Pages application that keeps its dashboard and notes while its work-item feature moves into an ASP.NET Core API and React.

**Status: proposed architecture.** This revision contains design documents only. The application, migration, local runtime and rollback behavior are not implemented or verified yet.

```mermaid
flowchart LR
    Browser[Browser] --> Gateway[YARP gateway: one origin]
    Gateway --> Legacy[Razor Pages: dashboard and notes]
    Gateway --> React[React: work items]
    Gateway --> API[Work-item API]
    Legacy -->|Work-item adapter after extraction| API
    Legacy --> LegacyData[(PostgreSQL: monolith schema)]
    React -->|Same-origin requests through gateway| API
    API --> FeatureData[(PostgreSQL: work-item schema)]
```

The diagram shows the intended extracted state. Both schemas belong to the same PostgreSQL database. React's API arrow represents a browser request through YARP, not a direct public API listener.

The migration separates two changes: moving business and data ownership into the API, then switching the work-item page from Razor to React. The Razor work-item page remains an API client after extraction, so changing the page route does not restore an obsolete database writer.

- [Design and request flow](docs/design.md)
- [Boundary and rollback decision](docs/decisions/0001-strangler-boundary.md)
- [Technical sources and decisions](docs/research.md)

Startup and operating commands will be documented when the corresponding runtime exists. This architecture does not claim uninterrupted feature writes during ownership transfer or production deployment readiness.
