# ADR-0005: Use the module's DbContext directly; no generic repository layer

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
Many .NET codebases wrap EF Core in `IRepository<T>` + `IUnitOfWork`. The usual reasons given are "testability" and "we might change ORM".

## Decision
Endpoints in a module use that module's `internal` DbContext directly:
1. load the aggregate (`FindAsync`, or a query);
2. call a domain method;
3. `SaveChangesAsync()`.

## Why
- **`DbContext` already *is* a unit of work, and `DbSet<T>` already *is* a repository.** A generic wrapper either hides useful EF features (projection, `AsNoTracking`, `Include`, compiled queries, which M7 needs) or re-exposes `IQueryable`, at which point it abstracts nothing.
- **"Testability" via mocked repositories tests the mock.** A mocked `IQueryable` doesn't translate to SQL, enforce unique indexes or detect concurrency conflicts. We test domain logic with plain unit tests (aggregates have no EF dependency) and data access against real SQL Server (Testcontainers).
- **"We might change ORM"** almost never happens. If it does, the module boundary already contains the blast radius: the DbContext is `internal`, so nothing outside the module can depend on it.

## When we would add a repository
We'd add a *specific* one (e.g. `IWorkOrderRepository.GetForUpdateAsync(id)`, not a generic `IRepository<T>`) when loading an aggregate becomes non-trivial (several `Include`s, a split query, filters) and is repeated across endpoints. That might happen in M4.

## Consequences
- **Coupling:** endpoint code depends on EF Core. That's acceptable inside a module.
- **Aggregate persistence:** aggregates must be EF-friendly. They need a private parameterless constructor and private setters. This is a small leak of persistence concerns into the domain model, accepted knowingly.
