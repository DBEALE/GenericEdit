# Starts backend API and frontend UI as tracked background processes.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$backendPath = Join-Path $root "backend\src\DatasetPlatform.Api"
$frontendPath = Join-Path $root "frontend"
$pidFilePath = Join-Path $root ".dev-processes.json"

# Always begin with a clean restart to avoid duplicate server instances.
& (Join-Path $root "stop-dev.ps1") | Out-Null

# Build before starting so --no-build is guaranteed to find up-to-date binaries.
Write-Host "Building backend..."
$buildResult = & dotnet build $backendPath --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
	Write-Error "Backend build failed. Aborting start."
	Write-Host $buildResult
	exit 1
}

$backendProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--no-build" -WorkingDirectory $backendPath -PassThru -WindowStyle Hidden
Start-Sleep -Seconds 1
$frontendProcess = Start-Process -FilePath "npm.cmd" -ArgumentList "start", "--", "--port", "4300" -WorkingDirectory $frontendPath -PassThru -WindowStyle Hidden

$processState = [PSCustomObject]@{
	backendPid = $backendProcess.Id
	frontendPid = $frontendProcess.Id
	startedAtUtc = [DateTime]::UtcNow.ToString("O")
}
$processState | ConvertTo-Json | Set-Content -Path $pidFilePath -Encoding UTF8

Write-Host ("Started backend (http://localhost:5201, PID {0}) and frontend (http://localhost:4300, PID {1})." -f $backendProcess.Id, $frontendProcess.Id)
