# About the reference

The pages in this section are **generated from the source code every time the site is built**, so they cannot drift from what the code does. The explanation in [Design](design) says why; these pages say exactly what.

| Page | Built from | Use it to |
| --- | --- | --- |
| [Code structure](reference/structure) | The projects (`.csproj` references), the feature slices and their `Add…` registration, the alert rules | See how the projects depend on each other and what each feature adds |
| [Wire protocol](reference/protocol) | The XML documentation of `Monitoring.Protocol` | Look up a message, a field, an enum value or a report |
| [HTTP API](reference/api) | Every `MapGet`, `MapPost` and the like in the services | Find a route and the feature that serves it |
| [Dashboard components and icons](reference/ui) | The Preact components and the SVG icon sprite | See which components and icons exist before adding another |

To read the pages in your editor or change what they say, edit the XML documentation or the JSDoc comment in the code; `docs/scripts/generate.mjs` is the only other part. `npm run generate` in `docs` writes them to `docs/reference` (not committed).
