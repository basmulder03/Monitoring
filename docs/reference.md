# About the reference

The pages in this section are generated from the source code every time the site is built, so they match what the code does. [Design](design) explains the reasons behind it.

| Page | Built from | Use it to |
| --- | --- | --- |
| [Code structure](reference/structure) | The projects (`.csproj` references), the feature slices and their `Add…` registration, the alert rules | See how the projects depend on each other and what each feature adds |
| [Wire protocol](reference/protocol) | The XML documentation of `Plover.Protocol` | Look up a message, a field, an enum value or a report |
| [HTTP API](reference/api) | Every `MapGet`, `MapPost` and the like in the services | Find a route and the feature that serves it |
| [Dashboard components and icons](reference/ui) | The Preact components and the SVG icon sprite | See which components and icons exist before adding another |

To change what a page says, edit the XML documentation or the JSDoc comment in the code. `npm run generate` in `docs` writes the pages to `docs/reference`, which is not committed.
