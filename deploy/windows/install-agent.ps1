<#
.SYNOPSIS
  Installs the monitoring agent as a Windows service that starts with the machine and is restarted when it stops.
.DESCRIPTION
  Run from an elevated PowerShell. The folder holds Monitoring.Agent.exe and its monitoring.json (see docs/configuration.md), which is read from
  there because a service starts in the system folder. Recovery: restart after 5 s, again after 5 s, then after 30 s, and the count is reset after a day
  without failures. A stop with a non-zero exit code counts as a failure too, so a configuration error is retried instead of leaving the server unmonitored.
  The folder is locked down: only SYSTEM and the administrators can change or read it (and -Account can read and run it), because it holds the program that
  the service runs and the token of the agent. -Account takes an account with a password, asked for, or a service account without one such as
  'NT SERVICE\MonitoringAgent' or 'NT AUTHORITY\LocalService'. Without -Account the service runs as SYSTEM.
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

# The folder holds the program that runs as SYSTEM and the token of the agent, so nobody else may change or read it. By SID, so the language of Windows does not matter:
# S-1-5-18 is SYSTEM and S-1-5-32-544 the administrators.
$folder = (Resolve-Path $Path).ProviderPath
$grants = @('*S-1-5-18:(OI)(CI)F', '*S-1-5-32-544:(OI)(CI)F')
icacls $folder /inheritance:r | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The rights on $folder could not be restricted." }
icacls $folder /grant:r @grants | Out-Null
if ($LASTEXITCODE -ne 0) { throw "The rights on $folder could not be set." }

$arguments = @{
  Name           = $Name
  BinaryPathName = "`"$exe`""
  DisplayName    = 'Monitoring agent'
  Description    = 'Reports the state of this server to the monitoring controller.'
  StartupType    = 'Automatic'
}
# A virtual or built-in service account has no password to ask for.
$withoutPassword = $Account -match '^NT (SERVICE|AUTHORITY)\\'
if ($Account -and -not $withoutPassword) { $arguments.Credential = Get-Credential -UserName $Account -Message "Password of $Account" }
New-Service @arguments | Out-Null
if ($withoutPassword) { sc.exe config $Name obj= $Account | Out-Null }
# The account can read and run what is in the folder, and change nothing. A virtual account only exists once its service does, so this comes after New-Service.
if ($Account) {
  icacls $folder /grant "${Account}:(OI)(CI)RX" | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "The rights on $folder could not be given to $Account; is it a real account?" }
}

sc.exe failure $Name reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
sc.exe failureflag $Name 1 | Out-Null

Start-Service -Name $Name
Write-Host "Installed and started the service $Name from $exe."
