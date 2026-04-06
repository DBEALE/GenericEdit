param(
    [string]$DataRoot = "data",
    [int]$YearsBack = 20,
    [int]$SnapshotsPerYear = 26,
    [int]$TemplatesPerDataset = 3,
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-StateToken {
    param([int]$State)

    switch ($State) {
        1 { return 'DRAFT' }
        2 { return 'PENDINGAPPROVAL' }
        3 { return 'OFFICIAL' }
        default { return 'DRAFT' }
    }
}

function Get-HeaderPropertyMap {
    param($Header)

    $map = @{}
    if ($null -eq $Header) {
        return $map
    }

    if ($Header -is [hashtable]) {
        foreach ($entry in $Header.GetEnumerator()) {
            $map[$entry.Key] = if ($null -eq $entry.Value) { '' } else { [string]$entry.Value }
        }

        return $map
    }

    foreach ($prop in $Header.PSObject.Properties) {
        $map[$prop.Name] = if ($null -eq $prop.Value) { '' } else { [string]$prop.Value }
    }

    return $map
}

function Get-HeaderHash {
    param($Header)

    $map = Get-HeaderPropertyMap -Header $Header
    $builder = New-Object System.Text.StringBuilder

    foreach ($key in ($map.Keys | Sort-Object)) {
        [void]$builder.Append($key.Trim().ToUpperInvariant())
        [void]$builder.Append([char]0)
        [void]$builder.Append($map[$key])
        [void]$builder.Append([char]0)
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($builder.ToString())
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
        return ([System.BitConverter]::ToString($hash) -replace '-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Ensure-Directory {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        [void](New-Item -ItemType Directory -Path $Path -Force)
    }
}

function Write-JsonFile {
    param(
        [string]$Path,
        $Value
    )

    Ensure-Directory -Path (Split-Path -Parent $Path)
    $json = $Value | ConvertTo-Json -Depth 100
    Set-Content -LiteralPath $Path -Value $json -Encoding utf8
}

$dataRootPath = Resolve-Path -LiteralPath $DataRoot
$instancesRoot = Join-Path $dataRootPath 'instances'

if (-not (Test-Path -LiteralPath $instancesRoot)) {
    throw "Instances folder not found: $instancesRoot"
}

$instanceFiles = Get-ChildItem -LiteralPath $instancesRoot -Recurse -File -Filter '*.json' |
    Where-Object { $_.Name -notlike '*.header.json' -and $_.DirectoryName -notmatch '\\headers(\\|$)' }

if ($instanceFiles.Count -eq 0) {
    throw "No instance JSON files found under $instancesRoot"
}

$sourceInstances = foreach ($file in $instanceFiles) {
    $model = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$model.datasetKey)) {
        continue
    }

    [pscustomobject]@{
        DatasetKey = ([string]$model.datasetKey).Trim().ToUpperInvariant()
        Model = $model
    }
}

$byDataset = $sourceInstances | Group-Object -Property DatasetKey
if ($byDataset.Count -eq 0) {
    throw 'No source instances could be parsed from datastore.'
}

$totalSnapshots = [Math]::Max(1, $YearsBack * $SnapshotsPerYear)
$stepDays = 365.25 / [Math]::Max(1, $SnapshotsPerYear)
$startDate = (Get-Date).Date.AddYears(-$YearsBack)
$dates = New-Object System.Collections.Generic.List[string]

for ($i = 0; $i -lt $totalSnapshots; $i++) {
    $dt = $startDate.AddDays([int][Math]::Round($i * $stepDays))
    $dates.Add($dt.ToString('yyyy-MM-dd'))
}

$uniqueDates = $dates | Select-Object -Unique

$generatedInstances = 0
$generatedHeaders = 0

foreach ($datasetGroup in $byDataset) {
    $datasetKey = [string]$datasetGroup.Name
    $datasetFolder = Join-Path $instancesRoot $datasetKey
    Ensure-Directory -Path $datasetFolder

    $templates = @($datasetGroup.Group | Select-Object -First ([Math]::Max(1, $TemplatesPerDataset)))
    $templateCount = $templates.Count

    for ($i = 0; $i -lt $uniqueDates.Count; $i++) {
        $template = $templates[$i % $templateCount].Model
        $clone = $template | ConvertTo-Json -Depth 100 | ConvertFrom-Json

        $newGuid = [guid]::NewGuid()
        $guidText = $newGuid.ToString('N')
        $dateToken = $uniqueDates[$i]

        $clone.id = $newGuid.ToString()
        $clone.datasetKey = $datasetKey
        $clone.asOfDate = $dateToken
        $clone.version = 1

        if ($clone.PSObject.Properties.Name -contains 'lastModifiedBy') {
            $clone.lastModifiedBy = 'historical-seeder'
        }

        if ($clone.PSObject.Properties.Name -contains 'lastModifiedAtUtc') {
            $clone.lastModifiedAtUtc = ([DateTimeOffset]::Parse("$dateToken`T12:00:00+00:00")).ToString('o')
        }

        if ($clone.PSObject.Properties.Name -contains 'createdBy' -and [string]::IsNullOrWhiteSpace([string]$clone.createdBy)) {
            $clone.createdBy = 'historical-seeder'
        }

        if ($clone.PSObject.Properties.Name -contains 'createdAtUtc' -and [string]::IsNullOrWhiteSpace([string]$clone.createdAtUtc)) {
            $clone.createdAtUtc = ([DateTimeOffset]::Parse("$dateToken`T12:00:00+00:00")).ToString('o')
        }

        if (-not ($clone.PSObject.Properties.Name -contains 'header')) {
            Add-Member -InputObject $clone -MemberType NoteProperty -Name header -Value (@{})
        }

        $stateInt = 1
        if ($clone.PSObject.Properties.Name -contains 'state') {
            try {
                $stateInt = [int]$clone.state
            }
            catch {
                $stateInt = 1
            }
        }

        $stateToken = Get-StateToken -State $stateInt

        $instancePath = Join-Path $datasetFolder "$guidText.json"
        $headerPath = Join-Path $datasetFolder (Join-Path 'headers' (Join-Path $stateToken (Join-Path $dateToken "$guidText.header.json")))

        $headerMap = Get-HeaderPropertyMap -Header $clone.header
        $headerIndex = [ordered]@{
            id = $newGuid.ToString()
            datasetKey = $datasetKey
            asOfDate = $dateToken
            state = $stateInt
            header = $headerMap
            headerHash = Get-HeaderHash -Header $clone.header
        }

        if (-not $WhatIf) {
            Write-JsonFile -Path $instancePath -Value $clone
            Write-JsonFile -Path $headerPath -Value $headerIndex
        }

        $generatedInstances++
        $generatedHeaders++
    }
}

Write-Output "datasets=$($byDataset.Count)"
Write-Output "datesPerDataset=$($uniqueDates.Count)"
Write-Output "generatedInstances=$generatedInstances"
Write-Output "generatedHeaders=$generatedHeaders"
Write-Output "dataRoot=$dataRootPath"
