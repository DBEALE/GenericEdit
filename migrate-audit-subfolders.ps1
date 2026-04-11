<#
.SYNOPSIS
    Migrates flat audit JSON files into per-instance subfolders.

.DESCRIPTION
    New audit writes go to  audit/{dataset}/{instanceId}/{timestamp}_{action}_{auditId}.json
    Old flat writes landed at audit/{dataset}/{timestamp}_{action}_{auditId}.json

    This script reads every flat audit file, checks whether it carries a DatasetInstanceId,
    and if so moves it into the appropriate subfolder.  Files with no DatasetInstanceId
    (schema-level events) are left in place.  Already-migrated files are silently skipped.

.PARAMETER DataRoot
    Path to the blob storage root directory.  Defaults to the sibling "data" folder
    relative to this script's location (i.e. GenericEdit/data).
#>
param(
    [string]$DataRoot = (Join-Path $PSScriptRoot "data")
)

$auditRoot = Join-Path $DataRoot "audit"

if (-not (Test-Path $auditRoot)) {
    Write-Host "Audit directory not found: $auditRoot"
    exit 0
}

# Flat files live exactly two levels under the audit root: audit/{dataset}/{file}.json
# Migrated files live three levels deep:                   audit/{dataset}/{instanceId}/{file}.json
$flatFiles = Get-ChildItem -Path $auditRoot -Recurse -Filter "*.json" |
    Where-Object { $_.Directory.Parent.FullName -eq (Resolve-Path $auditRoot).Path }

$moved  = 0
$skipped = 0
$errors  = 0

foreach ($file in $flatFiles) {
    try {
        $json = Get-Content $file.FullName -Raw | ConvertFrom-Json

        # Only instance-scoped events need moving
        if (-not $json.DatasetInstanceId) {
            $skipped++
            continue
        }

        $instanceId  = $json.DatasetInstanceId.ToString().Replace("-", "").ToLower()
        $destDir     = Join-Path $file.Directory.FullName $instanceId
        $destFile    = Join-Path $destDir $file.Name

        if (Test-Path $destFile) {
            # Already migrated (same content exists at new path)
            Remove-Item $file.FullName -Force
            $moved++
            continue
        }

        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        Move-Item $file.FullName $destFile -Force
        $moved++
        Write-Verbose "Moved: $($file.Name) -> $instanceId\"
    }
    catch {
        Write-Warning "Failed to process $($file.FullName): $_"
        $errors++
    }
}

Write-Host "Migration complete. Moved: $moved  Skipped (no instanceId): $skipped  Errors: $errors"
