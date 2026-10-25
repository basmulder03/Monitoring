#!/usr/bin/env python3
"""Adds a nightly run of the budget tests to the history, writes the trend page, and fails on a regression.

usage: perf-trend.py <run.tsv> <results.json> <index.html> <commit-sha>

run.tsv is what the tests wrote through PerfResults (name, value, unit per line; a smaller value is always better).
A value is a regression when it is more than TWICE the median of the last WINDOW runs and also more than the noise floor of its unit above it,
so a shared runner's jitter on a tiny number does not fail the night. A measurement that was in earlier runs and is missing now fails as well.
"""
import datetime
import html
import json
import statistics
import sys

WINDOW = 7
KEEP = 365
# A median of one or two runs is just one runner's mood; judge only once there is some history.
MIN_RUNS = 3
FACTOR = 2.0
# What a value must exceed the median by, besides the factor: a jump from 1 ms to 3 ms on a shared runner is noise.
FLOOR = {"ms": 25.0, "us": 1.0, "MB": 5.0, "KB": 100.0, "B": 1.0}


def main(run_path, results_path, page_path, sha):
    values = {}
    for line in open(run_path, encoding="utf-8"):
        name, value, unit = line.rstrip("\n").split("\t")
        values[name] = {"value": float(value), "unit": unit}

    try:
        history = json.load(open(results_path, encoding="utf-8"))
    except FileNotFoundError:
        history = {"runs": []}
    previous = history["runs"][-WINDOW:]

    problems = []
    for name, now in sorted(values.items()):
        before = [r["values"][name]["value"] for r in previous if name in r["values"]]
        if not before:
            print(f"new   {name}: {now['value']:.3f} {now['unit']}")
            continue
        median = statistics.median(before)
        regressed = len(before) >= MIN_RUNS and now["value"] > median * FACTOR and now["value"] - median > FLOOR.get(now["unit"], 0)
        print(f"{'WORSE' if regressed else 'ok   '} {name}: {now['value']:.3f} {now['unit']} (median of {len(before)} runs: {median:.3f})")
        if regressed:
            problems.append(f"{name} is {now['value']:.3f} {now['unit']}, the median of the last runs is {median:.3f}")
    if previous:
        for name in sorted(set(previous[-1]["values"]) - set(values)):
            problems.append(f"{name} was measured by the last run and is missing now")

    history["runs"].append({"sha": sha, "date": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%d %H:%M"), "values": values, "regressions": problems})
    history["runs"] = history["runs"][-KEEP:]
    json.dump(history, open(results_path, "w", encoding="utf-8"), indent=1)
    open(page_path, "w", encoding="utf-8").write(page(history))

    for problem in problems:
        print(f"::error::{problem}")
    return 1 if problems else 0


def sparkline(series):
    width, height = 160, 28
    low, high = min(series), max(series)
    span = (high - low) or 1
    step = width / max(len(series) - 1, 1)
    points = " ".join(f"{i * step:.1f},{height - 2 - (v - low) / span * (height - 4):.1f}" for i, v in enumerate(series))
    return f'<svg width="{width}" height="{height}" role="img" aria-label="trend"><polyline points="{points}" fill="none" stroke="currentColor" stroke-width="1.5"/></svg>'


def page(history):
    runs = history["runs"]
    latest = runs[-1]
    rows = []
    for name in sorted(latest["values"]):
        series = [r["values"][name]["value"] for r in runs if name in r["values"]]
        unit = latest["values"][name]["unit"]
        rows.append(f"<tr><td>{html.escape(name)}</td><td class=n>{series[-1]:.3f} {unit}</td><td class=n>{statistics.median(series):.3f} {unit}</td><td>{sparkline(series)}</td></tr>")
    failed = "".join(f"<li>{html.escape(p)}</li>" for p in latest["regressions"])
    status = f'<p class=bad>Regressions in the last run:</p><ul>{failed}</ul>' if failed else "<p>The last run found no regression.</p>"
    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Plover speed budgets</title>
<style>
:root {{ --bg:#fff; --fg:#1b1f24; --line:#d0d7de; --bad:#b42318; }}
@media (prefers-color-scheme: dark) {{ :root {{ --bg:#0d1117; --fg:#e6edf3; --line:#30363d; --bad:#ff7b72; }} }}
body {{ background:var(--bg); color:var(--fg); font:16px/1.5 system-ui,sans-serif; margin:0 auto; max-width:60rem; padding:1rem; }}
table {{ border-collapse:collapse; width:100%; }} th,td {{ border-bottom:1px solid var(--line); padding:.4rem .6rem; text-align:left; }}
.n {{ font-variant-numeric:tabular-nums; text-align:right; }} .bad {{ color:var(--bad); font-weight:600; }}
.scroll {{ overflow-x:auto; }}
</style></head><body>
<h1>Speed budgets</h1>
<p>Last run {html.escape(latest['date'])} UTC, commit {html.escape(latest['sha'][:7])}; {len(runs)} runs kept. A smaller number is better.</p>
{status}
<div class=scroll><table><thead><tr><th>Measurement</th><th class=n>Last run</th><th class=n>Median</th><th>Trend</th></tr></thead><tbody>
{''.join(rows)}
</tbody></table></div>
</body></html>
"""


if __name__ == "__main__":
    sys.exit(main(*sys.argv[1:5]))
