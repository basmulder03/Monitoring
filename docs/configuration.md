# Configuration reference

The controller and the agent read their bootstrap settings from the same sources. Anything not set keeps its default.

## Sources

From lowest to highest priority, so a later source overrides an earlier one:

1. A JSON file. By default `monitoring.json` in the current directory; set `MONITORING_CONFIG_FILE` to use another path. The file is optional, and comments are allowed.
2. Environment variables starting with `MONITORING_`. A nested setting uses a double underscore: `Agent:ScopeName` becomes `MONITORING_Agent__ScopeName`.
3. Command line arguments: `--Agent:ScopeName=shop`.

A list can be written as a JSON array, as indexed variables (`MONITORING_Agent__Controllers__0=...`), or as one comma-separated value (`MONITORING_Agent__Controllers=a:9440,b:9440`).
Durations are written as `hh:mm:ss`, for example `00:00:30`; fractions of a second use a dot (`00:00:00.250`).

Logging is controlled by the usual `Logging:LogLevel` settings, for example `MONITORING_Logging__LogLevel__Default=Debug`.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Stopped normally |
| 1 | Could not start, for example the port is already in use |
| 78 | A setting is missing or invalid; the message names the setting |

## Controller

| Setting | Default | Meaning |
| --- | --- | --- |
| `Controller:Listen:Address` | `0.0.0.0` | IP address agents connect to |
| `Controller:Listen:Port` | `9440` | TCP port agents connect to |
| `Controller:Replicas` | none | The individual `host:port` address of every controller replica, handed to agents so they connect to all of them. List every replica, including this one: agents follow exactly this list |
| `Controller:AgentTokens` | required | The accepted agent tokens, see below. At least one |
| `Controller:AgentLink:HelloTimeout` | `00:00:10` | How long a new connection may stay silent before it must say hello |
| `Controller:Registry:GoneRetention` | `00:10:00` | How long a removed instance stays visible as Gone |
| `Controller:Registry:PurgeInterval` | `00:01:00` | How often expired Gone instances are dropped |
| `Controller:Alerting:StartupGrace` | `00:10:00` | After startup no alert rule is evaluated for this long, so agents can send their snapshots first |
| `Controller:Alerting:InstanceUnhealthyWait` | `00:00:30` | How long an instance must stay Unhealthy before its alert fires |
| `Controller:Alerting:ServiceDownWait` | `00:00:00` | How long a service must have no instance able to serve before its alert fires |
| `Controller:Alerting:EvaluationInterval` | `00:00:05` | How often the alert rules run |

### Agent tokens

The controller never needs a list of agents. It holds the hashes of the tokens it accepts, and agents register themselves under the scope name they announce.

| Entry | Meaning |
| --- | --- |
| `Scope: "*"` | An enrollment token: any agent that holds it registers under whatever scope name it announces. One such token is enough for a whole fleet. Anyone with the token can claim any scope name |
| `Scope: "shop"` | A token bound to one scope: its holders can only report as `shop`. Use it where a team should not be able to report as another team |

Several entries may share a scope, so a new token can be introduced before the old one is removed.
The hash is the SHA-256 of the token as lower-case hexadecimal text: `printf '%s' 'the-token' | sha256sum`.

```json
{
  "Controller": {
    "Listen": { "Port": 9440 },
    "Replicas": ["controller-0.example:9440", "controller-1.example:9440"],
    "AgentTokens": [
      { "Scope": "*", "TokenHash": "<sha-256 of the enrollment token>" }
    ]
  }
}
```

Without transport security the token travels in clear text. Use it only on a network you trust; TLS comes in a later version.

## Agent

| Setting | Default | Meaning |
| --- | --- | --- |
| `Agent:ScopeName` | required | The scope this agent reports as, for example the namespace or the server name. It registers itself under this name |
| `Agent:Token` | required | The token the controller holds a hash of. Prefer an environment variable or a mounted secret over the JSON file |
| `Agent:Controllers` | required | The bootstrap controller address as `host:port`, plus optional fallbacks. The controller answers with every replica's address, and the agent then connects to all of them |
| `Agent:Id` | machine name | Id of this agent |
| `Agent:Version` | none | Version of this agent, shown by the controller |
| `Agent:Labels:<name>` | none | Free-form labels such as `env` and `site`, sent with the hello |
| `Agent:Reconnect:MinDelay` | `00:00:01` | Shortest wait before reconnecting to a replica |
| `Agent:Reconnect:MaxDelay` | `00:00:30` | Longest wait; repeated failures grow the wait up to this |
| `Agent:ConfigReplyTimeout` | `00:00:10` | How long to wait for the controller's answer to the hello |
| `Agent:SnapshotInterval` | `00:05:00` | How often a full snapshot is sent to repair drift |

```json
{
  "Agent": {
    "ScopeName": "orders-platform",
    "Controllers": ["controller.example:9440"],
    "Labels": { "env": "prod", "site": "a" }
  }
}
```

with the token in the environment: `MONITORING_Agent__Token=...`.
