<#
.SYNOPSIS
  Installs the published agent as a Windows service on this machine and checks what the installation promises, then removes it again.
.DESCRIPTION
  Run from an elevated PowerShell with the folder that holds the published Plover.Agent.exe. It checks that the service starts, is set to start with
  the machine, is restarted after its process is killed, stops when asked, and is removed by the uninstall. Exits with 1 and says what failed otherwise.
.EXAMPLE
  .\test-service.ps1 -Path .\published\Agent
#>
param([Parameter(Mandatory)] [string] $Path)
$ErrorActionPreference = 'Stop'
$name = 'PloverAgent'
$installer = Join-Path $PSScriptRoot 'install-agent.ps1'
$failures = 0

function Check($what, [bool] $ok, $detail = '') {
  if ($ok) { Write-Host "ok: $what" } else { Write-Host "FAILED: $what $detail"; $script:failures++ }
}
function ProcessIdOf { (Get-CimInstance Win32_Service -Filter "Name='$name'").ProcessId }

$dir = Join-Path ([IO.Path]::GetTempPath()) "plover-agent-test-$([Guid]::NewGuid().ToString('N'))"
try {
  Copy-Item (Resolve-Path $Path) $dir -Recurse
  # Read from next to the program, as a service has to; a missing file would stop the agent with a configuration error.
  '{ "Agent": { "ScopeName": "server-1", "Token": "t", "Controllers": ["127.0.0.1:1"], "Discovery": { "Kind": "windows", "Windows": { "Services": ["EventLog"] } } } }' |
    Out-File (Join-Path $dir 'plover.json') -Encoding ascii

  & $installer -Path $dir
  Start-Sleep -Seconds 4
  $service = Get-Service $name
  Check 'the service is running' ($service.Status -eq 'Running') "(it is $($service.Status))"
  Check 'it starts with the machine' ($service.StartType -eq 'Automatic') "(it is $($service.StartType))"
  $failure = (sc.exe qfailure $name) -join ' '
  Check 'it is restarted when it fails' ($failure -match 'RESTART -- Delay = 5000') $failure

  $first = ProcessIdOf
  Stop-Process -Id $first -Force
  Start-Sleep -Seconds 12
  $second = ProcessIdOf
  Check 'it is restarted after its process was killed' ($second -ne 0 -and $second -ne $first) "(process $first, then $second)"
  Check 'it is running again' ((Get-Service $name).Status -eq 'Running')

  Stop-Service $name
  Check 'it stops when asked' ((Get-Service $name).Status -eq 'Stopped')
} finally {
  if (Get-Service $name -ErrorAction SilentlyContinue) { & $installer -Path $dir -Uninstall }
  Check 'it is gone after the uninstall' ($null -eq (Get-Service $name -ErrorAction SilentlyContinue))
  Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures -gt 0) { Write-Host "$failures check(s) failed"; exit 1 }
Write-Host 'The service works.'
