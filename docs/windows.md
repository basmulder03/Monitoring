# Plover Windows servers

On a Windows server one agent runs as a Windows service and takes the place of both the sidecar and the namespace agent of Kubernetes. The server is the scope. It reports to the same controller, with the same protocol, and the controller treats it like any other scope.

What it shows:

- The **services** you list, with their state and start type, a stopped service as unhealthy with the cause, how often it restarted, and the memory, threads and handles of its process.
- The **machine**: uptime, processor, memory, the fixed disks, and whether Windows waits for a restart. A nearly full disk makes the machine degraded and then unhealthy.
- What an **application** says about itself, when it uses the Plover client: version, health checks, metrics and calls, over a named pipe to the agent.

Details of every setting are in [configuration.md](configuration.md).

## What you need

- Windows Server 2019 or later, or Windows 10 or later, on x64.
- The published agent, `Plover.Agent.exe`, which is a single program.
- The address of the controller (`host:port`, port 9440 by default) and a token whose hash the controller lists under `Controller:AgentTokens`. An agent outside the controller's cluster reaches it through a TLS passthrough route and needs transport mode `tls` or `mtls`; see [openshift.md](openshift.md).
- Rights to install a service, which means an elevated PowerShell.

## The configuration file

Put `plover.json` next to the program. A service starts in the system folder, so this is where the agent looks for it.

```json
{
  "Agent": {
    "ScopeName": "srv-orders-01",
    "Token": "the enrollment token",
    "Controllers": ["plover.example.com:443"],
    "Labels": { "environment": "prod", "datacenter": "a", "role": "orders" },
    "Transport": { "Mode": "tls", "CaFile": "C:\\PloverAgent\\ca.crt" },
    "Discovery": {
      "Kind": "windows",
      "Windows": { "Services": ["OrdersApi", "OrdersWorker", "MSSQLSERVER"] }
    },
    "Sidecars": {
      "Pipe": { "Allow": ["NT SERVICE\\OrdersApi", "DOMAIN\\svc-orders"] }
    }
  }
}
```

`Pipe:Allow` lists the accounts the monitored applications run as. Without it only the system, the administrators, the agent's own account and the built-in Local Service and Network Service accounts can use the pipe, so a user who is not one of these cannot report as one of your applications. The accounts that are let in trust each other: the agent goes by the process id an application announces and does not check it against the process that opened the pipe, so give each monitored application an account of its own and keep the list short.

## Installing

By hand, from an elevated PowerShell:

```powershell
.\install-agent.ps1 -Path C:\PloverAgent
```

It installs the service `PloverAgent`, set to start with the machine and to be restarted when it stops (after 5 seconds, again after 5, then after 30), and starts it. Running it again replaces the service, which is how an upgrade of the program or the configuration takes effect. `-Uninstall` removes it. `-Account 'DOMAIN\svc-monitoring'` runs it as another account and asks for the password; a service account without a password, such as `-Account 'NT SERVICE\PloverAgent'`, needs no prompt. Without `-Account` the service runs as SYSTEM.

The installation also locks the folder down: only SYSTEM and the administrators can change or read it (the `-Account` account can read and run it). The folder holds the program that runs with those rights and the token of the agent, so a folder that other users can write to would let them run code as SYSTEM and read the token. Check the same when you install by hand in a folder of your own.

With Ansible, `deploy/windows/ansible/install-agent.yml` copies the program and the scripts, writes `plover.json` from variables and runs the installation:

```sh
ansible-playbook -i inventory.ini deploy/windows/ansible/install-agent.yml \
  -e agent_build=published/Agent -e controller_address=plover.example.com:443 \
  -e '{"agent_services": ["OrdersApi", "OrdersWorker"], "agent_labels": {"environment": "prod"}}' \
  -e @secrets.yml     # holds agent_token, in a vault
```

A controller outside a network you trust needs `agent_transport_mode: tls` (or `mtls`) and `agent_ca_src`, the authority that signed its certificate; the playbook copies it to the server and says so when the mode is `none`, because then the token and everything the agent reports travel in clear text. `agent_server_name` is the name in the certificate when it differs from the host of `controller_address`, `agent_cert_src` and `agent_key_src` are for `mtls`, and `agent_account` is passed to the installer, where only an account without a password works, because Ansible cannot answer a prompt.

The playbook needs the collections `ansible.windows` and a working WinRM connection to the hosts, which is yours to set up. It has passed Ansible's syntax check, but it has not been run against a Windows host; the script it runs has been tested on Windows 11.

## An application that uses the client

The application needs the Plover client in its code, like on Kubernetes, and nothing else: it finds the pipe `plover-agent` by itself. Its service has to be on the list under `Discovery:Windows:Services` and running, because the agent matches the application with its service by process id, and turns away any other process. The account the application runs as has to be allowed on the pipe (`Pipe:Allow`) unless it is Local System, Local Service or Network Service.

## Checking it

`deploy/windows/test-service.ps1 -Path <folder with the published agent>` installs the agent from a copy of the folder, checks that it runs, starts with the machine and is restarted after its process is killed, stops when asked, and removes it again. It is a good smoke test after a change of the program or the installation, and runs every night in CI.

In the dashboard the server shows up as a scope with the service `windows-host` (the machine itself), one instance for each listed service, and the agent as the service `plover-agent` with the instance `PloverAgent`, which is always listed. The agent writes to the Windows event log too (Application, source `PloverAgent`).

## When something is wrong

| What you see | Likely cause |
| --- | --- |
| The service stops at once and is started again and again | A mistake in `plover.json`, such as a missing `ScopeName`, token or controller. The exit code is 78 and the message is in the event log |
| The scope shows no services | The names under `Discovery:Windows:Services` do not match; they are compared by service name (not the display name) without regard to case, and a `*` at the end matches a prefix |
| An application does not show its health checks or metrics | Its service is not on the list or not running, its account is not under `Pipe:Allow`, or it runs in a process shared with other services, such as `svchost` |
| The scope says the agent is not connected | The agent cannot reach the controller: address, firewall, or transport mode |

## Not covered yet

- Clock skew is not measured, because the agent has no trusted time to compare with.
- The Native AOT build of the Windows program is made and checked by the nightly Windows job in CI; it has not been run by hand.
