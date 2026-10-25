import { useState } from "preact/hooks";
import type { PackageCatalog, PackageEntry } from "../../api/contracts";
import { plural } from "../../format";
import { useDisclosure } from "../../state/OpenState";
import { useJson } from "../../state/useJson";
import { VulnerabilitiesSection } from "./VulnerabilitiesSection";

function PackageRow({ entry }: { entry: PackageEntry }) {
  const differs = entry.versions.length > 1;
  // A package used in several versions is the thing to look at, so it starts open.
  const disclosure = useDisclosure(`package:${entry.name}`, differs);
  const violates = entry.violation !== null;

  return (
    <details class="package" {...disclosure}>
      <summary>
        <strong>{entry.name}</strong>
        <span class={differs ? "differs" : "muted"}>{differs ? `${entry.versions.length} versions in use` : entry.versions[0]?.version}</span>
        {violates && <span class="violation">Not aligned: {entry.violation}</span>}
      </summary>
      <table class="instances">
        <thead>
          <tr>
            <th>Version</th>
            <th>Used by</th>
          </tr>
        </thead>
        <tbody>
          {entry.versions.map((version) => (
            <tr key={version.version}>
              <td>{version.version}</td>
              <td>
                {version.usedBy
                  .map((usage) => `${usage.scope}/${usage.service} (${plural(usage.instances, "instance", "instances")}${usage.direct ? "" : ", indirect"})`)
                  .join(", ")}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </details>
  );
}

/** What the services are built from, with search, and the bill of materials to download. */
export function PackagesView({ refresh }: { refresh: unknown }) {
  const loaded = useJson<PackageCatalog>("/api/packages", refresh);
  const [search, setSearch] = useState("");

  if (loaded.state === "loading") return <p class="muted">Loading…</p>;
  if (loaded.state !== "found") return <p class="muted">The packages could not be loaded.</p>;

  const catalog = loaded.value;
  const needle = search.trim().toLowerCase();
  const shown = catalog.packages.filter((entry) => entry.name.toLowerCase().includes(needle));
  const violations = catalog.packages.filter((entry) => entry.violation !== null).length;

  return (
    <div>
      <h2>Packages</h2>
      <VulnerabilitiesSection refresh={refresh} />
      {catalog.packages.length === 0 ? (
        <p class="muted">No service has reported what it is built from yet.</p>
      ) : (
        <>
          <p class="muted">
            {plural(catalog.packages.length, "package", "packages")} in {plural(catalog.services, "service", "services")}.
            {catalog.servicesWithoutList > 0 && ` ${plural(catalog.servicesWithoutList, "service has", "services have")} not reported its packages.`}{" "}
            <a href="/api/sbom" download="landscape.cdx.json">Download the bill of materials (CycloneDX)</a>
          </p>
          {violations > 0 && <p class="notice">{plural(violations, "package is", "packages are")} not aligned as the configuration requires.</p>}
          <div class="filter">
            <label for="package-search">Search packages </label>
            <input id="package-search" type="search" value={search} onInput={(event) => setSearch(event.currentTarget.value)} />
          </div>
          {shown.length === 0 && <p class="muted">No package matches the search.</p>}
          {shown.map((entry) => (
            <PackageRow key={entry.name} entry={entry} />
          ))}
        </>
      )}
    </div>
  );
}
