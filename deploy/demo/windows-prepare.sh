#!/usr/bin/env bash
# Makes what the Windows VM of the live demo (--Windows=true) needs: the agent published for Windows, the installer, a key for ssh, the configuration of the agent
# (it reports to the controller of the kind cluster, through a node port) and the script that Windows runs after its own installation.
# Needs the dotnet SDK on the PATH (a plain publish: Native AOT cannot cross-compile to Windows, and the VM does not care).
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
out="$root/artifacts/demo/windows"
node="$(docker inspect plover-demo-control-plane -f '{{(index .NetworkSettings.Networks "kind").IPAddress}}')"
mkdir -p "$out/shared" "$out/oem"

dotnet publish "$root/src/Plover.Agent" -c Release -r win-x64 --self-contained -p:PublishAot=false -p:PublishSingleFile=true -o "$out/shared" >/dev/null
cp "$root/deploy/windows/install-agent.ps1" "$out/shared/"
[ -f "$out/id_ed25519" ] || ssh-keygen -q -t ed25519 -N '' -f "$out/id_ed25519"
cp "$out/id_ed25519.pub" "$out/shared/authorized_keys"

cat > "$out/shared/plover.json" <<JSON
{
  "Agent": {
    "ScopeName": "windows-server-1",
    "Token": "$WINDOWS_TOKEN",
    "Controllers": ["$node:30440", "$node:30441"],
    "Labels": { "environment": "demo", "datacenter": "vm" },
    "Discovery": { "Kind": "windows", "Windows": { "Services": ["Spooler", "W32Time"] } }
  }
}
JSON

# Runs once, as an administrator, when Windows has been installed (dockur/windows: the folder /oem). \\host.lan\Data is the folder /shared.
cat > "$out/oem/install.bat" <<'BAT'
@echo off
mkdir C:\PloverAgent
xcopy /Y /E \\host.lan\Data\* C:\PloverAgent\
sc config W32Time start= auto
net start W32Time
powershell -NoProfile -ExecutionPolicy Bypass -Command "Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0; Set-Service sshd -StartupType Automatic; Start-Service sshd; Copy-Item C:\PloverAgent\authorized_keys C:\ProgramData\ssh\administrators_authorized_keys -Force; icacls C:\ProgramData\ssh\administrators_authorized_keys /inheritance:r /grant 'SYSTEM:F' 'BUILTIN\Administrators:F'; New-NetFirewallRule -Name sshd -DisplayName sshd -Protocol TCP -LocalPort 22 -Action Allow -ErrorAction SilentlyContinue"
rem The evaluation ISO of Windows Server is not patched, and .NET 10 stops there with "Your Windows doesn't fully support CET" unless the program opts out of shadow stacks.
powershell -NoProfile -Command "Set-ProcessMitigation -Name Plover.Agent.exe -Disable UserShadowStack,UserShadowStackStrictMode"
powershell -NoProfile -ExecutionPolicy Bypass -File C:\PloverAgent\install-agent.ps1 -Path C:\PloverAgent
BAT
echo "prepared $out"
