# Stops backend/frontend dev server processes for this workspace.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$pidFilePath = Join-Path $root ".dev-processes.json"
$stopped = New-Object System.Collections.Generic.HashSet[int]

function Stop-ProcessTreeIfRunning {
  param([int]$ProcessId)

  if ($ProcessId -le 0 -or $stopped.Contains($ProcessId)) {
    return
  }

  $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
  if ($null -eq $process) {
    return
  }

  taskkill /PID $ProcessId /T /F 2>$null | Out-Null
  $null = $stopped.Add($ProcessId)
}

# First, stop tracked processes from the previous start.
if (Test-Path $pidFilePath) {
  try {
    $tracked = Get-Content -Path $pidFilePath -Raw | ConvertFrom-Json
    if ($null -ne $tracked.backendPid) {
      Stop-ProcessTreeIfRunning -ProcessId ([int]$tracked.backendPid)
    }

    if ($null -ne $tracked.frontendPid) {
      Stop-ProcessTreeIfRunning -ProcessId ([int]$tracked.frontendPid)
    }
  }
  catch {
    # Ignore malformed pid file and continue with discovery-based cleanup.
  }

  Remove-Item -Path $pidFilePath -Force -ErrorAction SilentlyContinue
}

# Then stop processes listening on known dev ports.
foreach ($port in @(5201, 4300)) {
  Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Stop-ProcessTreeIfRunning -ProcessId $_ }
}

# Finally, stop any matching command-line processes that may not currently own the target ports.
Get-CimInstance Win32_Process |
  Where-Object {
    ($_.Name -match 'dotnet(.exe)?|DatasetPlatform.Api(.exe)?|powershell(.exe)?|pwsh(.exe)?' -and $_.CommandLine -match 'DatasetPlatform.Api|dotnet\s+run') -or
    ($_.Name -match 'node(.exe)?|npm(.cmd)?|cmd(.exe)?|powershell(.exe)?|pwsh(.exe)?' -and $_.CommandLine -match 'ng\s+serve|npm\s+start|--port\s+4300')
  } |
  ForEach-Object { Stop-ProcessTreeIfRunning -ProcessId $_.ProcessId }

Write-Host ("Stopped {0} backend/frontend dev process(es)." -f $stopped.Count)
