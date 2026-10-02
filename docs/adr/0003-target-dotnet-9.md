# ADR-0003: Target .NET 9 instead of .NET 10

- **Status:** Accepted, revisit before the Azure deployment (M9)
- **Date:** 2026-10-03

## Context
The original spec asked for .NET 10 (LTS). The development laptop has SDK 9.0.308 installed, and the owner chose not to install another SDK because of disk space.

.NET 9 is an STS release. Microsoft extended STS support to 24 months, so .NET 9 support ends on **10 November 2026**. After that date it gets no security patches.

## Decision
Target `net9.0` with EF Core 9 and ASP.NET Core 9. `global.json` pins SDK `9.0.x` with `rollForward: latestFeature`.

## Consequences
- None of the features this project uses requires .NET 10: Minimal APIs, `TypedResults`, built-in OpenAPI, `TimeProvider`, EF Core complex types, compiled models and compiled queries.
- **Upgrading** means changing `<TargetFramework>` in `Directory.Build.props`, bumping the package versions in `Directory.Packages.props`, and updating the base images in the Dockerfile. CI runs on GitHub's runners, so it can build either version.
- In an interview, say this plainly: "We were on STS because of a tooling constraint, we documented the end-of-support date, and the upgrade is a one-line TFM change plus package bumps." Owning a known risk explicitly is a senior behaviour.
