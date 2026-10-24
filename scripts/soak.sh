#!/usr/bin/env bash
# Runs the demo host (a real controller with three made-up agents that play an incident again and again) for a number of minutes, with dashboards that connect and
# leave all the time, and samples its memory, threads and open files. It fails when memory keeps growing after a warm-up, which is what a leak looks like and what a test
# that measures one moment cannot see.
#
#   scripts/soak.sh [minutes] [sample-seconds]        defaults: 120 minutes, a sample every 30 seconds
#
# Needs the .NET SDK and Node.js (the demo builds the dashboard), curl and python3. Writes soak-samples.csv in the current directory.
set -euo pipefail
export LC_ALL=C   # decimal points, whatever the language of the machine

minutes=${1:-120}
every=${2:-30}
port=5081
here=$(cd "$(dirname "$0")/.." && pwd)

dotnet build "$here/samples/Monitoring.Demo" --configuration Release -v quiet >/dev/null
demo=$(ls "$here"/artifacts/*/bin/Monitoring.Demo/release/Monitoring.Demo 2>/dev/null | head -1)
[ -x "$demo" ] || { echo "The demo host was not built." >&2; exit 2; }

"$demo" "$port" >soak-demo.log 2>&1 &
pid=$!
clients=()
cleanup() {
  kill "${clients[@]}" 2>/dev/null || true
  kill "$pid" 2>/dev/null || true
  sleep 2
  kill -9 "$pid" 2>/dev/null || true   # it stops slowly, and the port has to be free for the next run
}
trap cleanup EXIT

for _ in $(seq 1 30); do curl -fsS "http://127.0.0.1:$port/healthz" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:$port/healthz" >/dev/null || { echo "The demo host did not start." >&2; cat soak-demo.log >&2; exit 2; }

start_clients() {
  clients=()
  for _ in $(seq 1 20); do
    curl -sN "http://127.0.0.1:$port/api/events" >/dev/null 2>&1 &
    clients+=($!)
  done
}
start_clients

echo "seconds,rss_mb,threads,open_files" >soak-samples.csv
end=$((SECONDS + minutes * 60))
next_churn=$((SECONDS + 300))
while [ $SECONDS -lt $end ]; do
  kill -0 "$pid" 2>/dev/null || { echo "The demo host stopped." >&2; tail -20 soak-demo.log >&2; exit 1; }
  rss=$(awk '/VmRSS/ {printf "%.1f", $2/1024}' "/proc/$pid/status")
  threads=$(awk '/Threads/ {print $2}' "/proc/$pid/status")
  files=$(ls "/proc/$pid/fd" | wc -l)
  echo "$SECONDS,$rss,$threads,$files" | tee -a soak-samples.csv
  # Pages open and close all the time in real life: every five minutes all of them leave and new ones come.
  if [ $SECONDS -ge $next_churn ]; then
    kill "${clients[@]}" 2>/dev/null || true
    start_clients
    next_churn=$((SECONDS + 300))
  fi
  # Other things the dashboard asks for.
  for path in /api/overview /api/scopes/shop /api/topology /api/packages /api/vulnerabilities /api/alerts/history /api/system; do curl -fsS "http://127.0.0.1:$port$path" >/dev/null || true; done
  sleep "$every"
done

python3 - <<'PY'
import csv, statistics, sys
rows = list(csv.DictReader(open('soak-samples.csv')))
if len(rows) < 10:
    print('Too few samples to judge:', len(rows)); sys.exit(0)
def window(a, b):
    part = rows[int(len(rows) * a):int(len(rows) * b)]
    return statistics.median(float(r['rss_mb']) for r in part)
early, late = window(0.2, 0.4), window(0.8, 1.0)
print(f"memory: {early:.1f} MB between 20% and 40% of the run, {late:.1f} MB over the last 20% ({late / early:.2f}x)")
for column in ('threads', 'open_files'):
    first, last = statistics.median(float(r[column]) for r in rows[len(rows) // 5:2 * len(rows) // 5]), statistics.median(float(r[column]) for r in rows[-len(rows) // 5:])
    print(f"{column}: {first:.0f} then {last:.0f}")
# Memory that grows by a quarter and by more than 25 MB after the warm-up is not noise.
if late > early * 1.25 and late - early > 25:
    print('::error::Memory kept growing: that looks like a leak.'); sys.exit(1)
print('Memory is steady.')
PY
