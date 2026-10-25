import { useState } from "preact/hooks";

const DURATIONS = [
  { minutes: 60, label: "1 hour" },
  { minutes: 240, label: "4 hours" },
  { minutes: 1440, label: "1 day" },
  { minutes: 10080, label: "7 days" },
];

/** Asks how long and why an alert is silenced. Silencing always has an end, so a forgotten silence cannot hide an alert for ever. */
export function SilenceForm({ rule, subject, onSilence }: { rule: string; subject: string; onSilence(body: { rule: string; subjectPrefix: string; minutes: number; reason: string }): Promise<void> }) {
  const [minutes, setMinutes] = useState(60);
  const [reason, setReason] = useState("");
  return (
    <form
      class="silence-form"
      onSubmit={(event) => {
        event.preventDefault();
        void onSilence({ rule, subjectPrefix: subject, minutes, reason: reason.trim() });
      }}
    >
      <label>
        Silence for{" "}
        <select value={minutes} onChange={(event) => setMinutes(Number(event.currentTarget.value))}>
          {DURATIONS.map((d) => (
            <option key={d.minutes} value={d.minutes}>
              {d.label}
            </option>
          ))}
        </select>
      </label>
      <label>
        Reason{" "}
        <input type="text" value={reason} maxLength={200} onInput={(event) => setReason(event.currentTarget.value)} />
      </label>
      <button type="submit" class="action">
        Silence {subject}
      </button>
    </form>
  );
}
