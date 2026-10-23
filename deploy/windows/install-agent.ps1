<#
.SYNOPSIS
  Installs the monitoring agent as a Windows service that starts with the machine and is restarted when it stops.
.DESCRIPTION
  Run from an elevated PowerShell. The folder holds Monitoring.Agent.exe and its monitoring.json (see docs/configuration.md), which is read from
  there because a service starts in the system folder. Recovery: restart after 5 s, again after 5 s, then after 30 s, and the count is reset after a day
  without failures. A stop with a non-zero exit code counts as a failure too, so a configuration error is retried instead of leaving the server unmonitored.
.EXAMPLE
  .\install-agent.ps1 -Path C:\MonitoringAgent
.EXAMPLE
  .\install-agent.ps1 -Path C:\MonitoringAgent -Uninstall
#>
param(
  [Parameter(Mandatory)] [string] $Path,
  [string] $Name = 'MonitoringAgent',
  [string] $Account,
  [switch] $Uninstall
)
$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw 'Run this from an elevated PowerShell: installing a service needs administrator rights.'
}

$existing = Get-Service -Name $Name -ErrorAction SilentlyContinue
if ($existing) {
  if ($existing.Status -ne 'Stopped') { Stop-Service -Name $Name -Force }
  sc.exe delete $Name | Out-Null
  Write-Host "Removed the service $Name."
  # The Service Control Manager only forgets a deleted service once nothing holds it open.
  Start-Sleep -Seconds 2
}
if ($Uninstall) { return }

$exe = Join-Path (Resolve-Path $Path) 'Monitoring.Agent.exe'
if (-not (Test-Path $exe)) { throw "There is no Monitoring.Agent.exe in $Path." }

$arguments = @{
  Name           = $Name
  BinaryPathName = "`"$exe`""
  DisplayName    = 'Monitoring agent'
  Description    = 'Reports the state of this server to the monitoring controller.'
  StartupType    = 'Automatic'
}
if ($Account) { $arguments.Credential = Get-Credential -UserName $Account -Message "Password of $Account" }
New-Service @arguments | Out-Null

sc.exe failure $Name reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
sc.exe failureflag $Name 1 | Out-Null

Start-Service -Name $Name
Write-Host "Installed and started the service $Name from $exe."
