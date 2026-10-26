import { createElement } from "preact";
import type { ScopeSummary } from "../../api/contracts";
import { Icon, type IconName } from "../../components/Icon";
import { Status } from "../../components/Status";
import { SEVERITY, worstOf } from "../../format";
import { Tiles } from "./Tiles";

interface GroupsProps {
  scopes: readonly ScopeSummary[];
  /** The label names to group by, outermost first. */
  keys: readonly string[];
  /** Depth of the first group, which sets its heading level. */
  depth: number;
  now: number;
}

/** The label names that have an icon of their own. */
const KEY_ICONS: Record<string, IconName | undefined> = { environment: "environment", datacenter: "datacenter", site: "datacenter" };

/** Groups scopes by the first label, then by the next inside each group; scopes without the label go last. */
export function Groups({ scopes, keys, depth, now }: GroupsProps) {
  const [key, ...rest] = keys;
  if (key === undefined) return <Tiles scopes={scopes} now={now} />;

  const byValue = new Map<string | null, ScopeSummary[]>();
  for (const scope of scopes) {
    const value = scope.labels[key] || null;
    byValue.set(value, [...(byValue.get(value) ?? []), scope]);
  }

  const ordered = [...byValue].sort(([nameA, a], [nameB, b]) => {
    if (nameA === null || nameB === null) return nameA === null ? 1 : -1;
    const bySeverity = SEVERITY.indexOf(worstOf(b.map((s) => s.health))) - SEVERITY.indexOf(worstOf(a.map((s) => s.health)));
    return bySeverity !== 0 ? bySeverity : nameA.localeCompare(nameB);
  });

  return (
    <div class={`groups depth-${depth}`}>
      {ordered.map(([value, members]) => (
        <section class="group" key={value ?? "\u0000not set"}>
          {createElement(
            `h${Math.min(depth + 2, 6)}`,
            null,
            <Status health={worstOf(members.map((s) => s.health))} />,
            " ",
            ...(KEY_ICONS[key] ? [<Icon name={KEY_ICONS[key]} />] : []),
            ` ${key}: ${value ?? "not set"}`,
          )}
          <Groups scopes={members} keys={rest} depth={depth + 1} now={now} />
        </section>
      ))}
    </div>
  );
}
