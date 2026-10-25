# Contributing

Thank you for helping. The design in [docs/design.md](docs/design.md) is the reference; read "Code structure" first.

## How the code is organized

- Vertical slices. Each feature owns everything it needs under `Features/<Name>` of its executable, with a `Add<Name>` registration method. Slices use each other only through public types. `tests/Plover.Architecture.Tests` fails the build otherwise.
- Native AOT. No reflection, no assembly scanning, source-generated JSON. Trim and AOT warnings are errors.
- One type per file, `internal sealed` by default, XML documentation on public members and on internal ones whose purpose is not clear from the name.

## Tests

Every change comes with tests, and `dotnet test` must pass at every commit.

- Unit tests: one behavior each, written as Arrange, Act, Assert with the `// Arrange`, `// Act` and `// Assert` comments, and named for the behavior and the condition. Use Shouldly, one assertion per value. Time comes from `TimeProvider`.
- Anything involving several components goes in `tests/Plover.Component.Tests`.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/): `<type>(<scope>): <description>`, with the slice or project as scope, for example `feat(controller-alerting): ...`. One commit per coherent change, with its tests and documentation.

## Work items

Planned work is tracked as issues, one per slice, listed in the [roadmap](https://github.com/basmulder03/Plover/issues/61). Say on the issue when you pick one up.

## Documentation

The site (<https://basmulder03.github.io/Plover/>) is built from `docs` with VitePress. `cd docs && npm ci && npm run dev` runs it locally. The pages under Reference (code structure, wire protocol, HTTP API, dashboard components and icons) are generated from the source by `docs/scripts/generate.mjs`, so a change to the XML documentation of a message or the JSDoc of a component shows up on its own; write those comments for the reader of the site too. Diagrams are Mermaid in the Markdown. A dead link fails the build of a pull request.

## License

By contributing you agree that your contribution is licensed under the [Apache License 2.0](LICENSE), as the rest of the project.
