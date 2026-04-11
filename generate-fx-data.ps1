# Generates historical FX_RATES instances spanning multiple years with varied
# region/entity combinations, realistic rate movements, and mixed states.
#
# Usage:  .\generate-fx-data.ps1 [-DataRoot <path>] [-StartYearMonth 2020-01] [-EndYearMonth 2026-03] [-WhatIf]

param(
    [string]$DataRoot      = (Join-Path $PSScriptRoot "data"),
    [string]$StartYearMonth = "2020-01",
    [string]$EndYearMonth   = "2026-03",
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─── Helpers ─────────────────────────────────────────────────────────────────

function Compute-HeaderHash($header) {
    $sb = New-Object System.Text.StringBuilder
    foreach ($key in ($header.Keys | Sort-Object { $_.ToUpperInvariant() })) {
        [void]$sb.Append($key.Trim().ToUpperInvariant())
        [void]$sb.Append([char]0)
        [void]$sb.Append($header[$key])
        [void]$sb.Append([char]0)
    }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($sb.ToString())
    $sha   = [System.Security.Cryptography.SHA256]::Create()
    try { return ([System.BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '') }
    finally { $sha.Dispose() }
}

function Write-Json($path, $obj) {
    $dir = Split-Path $path -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    $obj | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
}

# Formats a DateTimeOffset-style UTC string for a given date at a given hour:minute:second
function Fmt-Ts([string]$date, [int]$h, [int]$m, [int]$s) {
    $d = [datetime]::Parse($date)
    return ([DateTimeOffset]::new($d.Year, $d.Month, $d.Day, $h, $m, $s, 0, [TimeSpan]::Zero)).ToString('o')
}

# Audit filename timestamp: yyyyMMddTHHmmssfffffff
function Fmt-AuditTs([string]$date, [int]$h, [int]$m, [int]$s, [int]$frac) {
    $d = [datetime]::Parse($date)
    return "$($d.ToString('yyyyMMdd'))T$($h.ToString('D2'))$($m.ToString('D2'))$($s.ToString('D2'))$($frac.ToString('D7'))"
}

function Round-Rate([double]$r) {
    if     ($r -ge 10000) { return [Math]::Round($r, 0) }
    elseif ($r -ge 100)   { return [Math]::Round($r, 2) }
    elseif ($r -ge 1)     { return [Math]::Round($r, 4) }
    elseif ($r -ge 0.01)  { return [Math]::Round($r, 6) }
    else                  { return [Math]::Round($r, 8) }
}

# ─── Base rates (units of CCY per 1 USD) at 2022-01 ─────────────────────────

$basePerUsd = @{
    USD = 1.0;      EUR = 0.889;    GBP = 0.743;    JPY = 115.08;   CNY = 6.38
    CHF = 0.917;    AUD = 1.376;    CAD = 1.274;    SEK = 9.225;    NOK = 8.804
    PLN = 4.059;    DKK = 6.653;    HKD = 7.795;    SGD = 1.352;    KRW = 1196.0
    INR = 74.83;    TWD = 27.76;    NZD = 1.476;    AED = 3.672;    SAR = 3.751
    MXN = 20.53;    BRL = 5.648;    ILS = 3.135;    THB = 33.40;    ZAR = 15.90
}

# Long-term monthly drift per CCY (fraction per month relative to USD)
$drift = @{
    JPY = +0.004; TRY = +0.018; BRL = +0.003; MXN = +0.002; ZAR = +0.004
    CNY = +0.001; KRW = +0.002; INR = +0.001; THB = +0.001
    EUR = -0.001; GBP = -0.001; CHF = -0.001
}

# ─── Monthly rate with trend + sine-wave cycle ───────────────────────────────
# monthIdx = months since 2020-01 (Jan2020=0, Dec2025=71, Mar2026=74)

$baseMonthIdx = 24   # 2022-01 is month 24 from 2020-01

function Get-MonthRate([string]$ccy, [int]$monthIdx) {
    $base = $basePerUsd[$ccy]
    if ($null -eq $base) { throw "Unknown currency: $ccy" }

    # Long-term trend
    $d      = if ($drift.ContainsKey($ccy)) { $drift[$ccy] } else { 0.0 }
    $trend  = [Math]::Pow(1.0 + $d, $monthIdx - $baseMonthIdx)

    # Deterministic oscillation based on ccy name hash
    $ccyBytes = [System.Text.Encoding]::UTF8.GetBytes($ccy)
    $seed   = [int]($ccyBytes | Measure-Object -Sum | Select-Object -ExpandProperty Sum)
    $period = 18.0 + ($seed % 12)   # 18-30 month wave
    $phase  = ($seed % 100) / 100.0 * 2 * [Math]::PI
    $amp    = 0.03 + ($seed % 20) / 1000.0  # 3-5% amplitude
    $wave   = 1.0 + $amp * [Math]::Sin(2 * [Math]::PI * $monthIdx / $period + $phase)

    return $base * $trend * $wave
}

# Cross rate: how many units of B per 1 unit of A
function Get-CrossRate([string]$src, [string]$tgt, [int]$monthIdx) {
    $rSrc = Get-MonthRate -ccy $src -monthIdx $monthIdx
    $rTgt = Get-MonthRate -ccy $tgt -monthIdx $monthIdx
    return Round-Rate ($rTgt / $rSrc)
}

# ─── Region / entity configuration ───────────────────────────────────────────
# officialThrough: last month where state = Official (inclusive, "yyyy-MM")
# pendingThrough:  months after officialThrough up to here are PendingApproval
# Months after pendingThrough (up to EndYearMonth) are Draft

$configs = @(
    @{ Region="Europe";       Entity="HBEU"; Writer="eu-trader-1";  Approver="eu-approver"
       Currencies=@("EUR","USD","GBP","CHF","SEK","NOK","JPY","DKK")
       OfficialThrough="2025-08"; PendingThrough="2025-11" }

    @{ Region="Europe";       Entity="HBFR"; Writer="eu-trader-2";  Approver="eu-approver"
       Currencies=@("EUR","USD","GBP","CHF","PLN","NOK","JPY","DKK")
       OfficialThrough="2025-04"; PendingThrough="2025-09" }

    @{ Region="America";      Entity="HBUS"; Writer="us-trader-1";  Approver="us-approver"
       Currencies=@("USD","EUR","GBP","CAD","MXN","BRL","JPY","CHF")
       OfficialThrough="2025-09"; PendingThrough="2025-12" }

    @{ Region="America";      Entity="HUSI"; Writer="us-trader-2";  Approver="us-approver"
       Currencies=@("USD","EUR","GBP","CAD","MXN","NZD","AUD","BRL")
       OfficialThrough="2024-12"; PendingThrough="2025-06" }

    @{ Region="Asia Pacific";  Entity="HBAP"; Writer="ap-trader";    Approver="ap-approver"
       Currencies=@("JPY","USD","CNY","HKD","SGD","KRW","AUD","INR")
       OfficialThrough="2025-01"; PendingThrough="2025-07" }

    @{ Region="Middle East";   Entity="HBME"; Writer="me-trader";    Approver="me-approver"
       Currencies=@("AED","USD","EUR","GBP","SAR","JPY","INR","CHF")
       OfficialThrough="2024-09"; PendingThrough="2025-03" }
)

# ─── Month range ──────────────────────────────────────────────────────────────

function Parse-YM([string]$ym) {
    $parts = $ym.Split('-')
    return [pscustomobject]@{ Year=[int]$parts[0]; Month=[int]$parts[1] }
}

function Compare-YM($a, $b) {
    if ($a.Year -ne $b.Year) { return $a.Year - $b.Year }
    return $a.Month - $b.Month
}

function YM-To-Idx($ym) {
    return ($ym.Year - 2020) * 12 + ($ym.Month - 1)
}

$start = Parse-YM $StartYearMonth
$end   = Parse-YM $EndYearMonth

$months = @()
$cur    = $start
while ((Compare-YM $cur $end) -le 0) {
    $months += [pscustomobject]@{ Year=$cur.Year; Month=$cur.Month
        Label = "$($cur.Year)-$($cur.Month.ToString('D2'))"
        Date  = "$($cur.Year)-$($cur.Month.ToString('D2'))-01"
        Idx   = YM-To-Idx $cur }
    if ($cur.Month -eq 12) { $cur = [pscustomobject]@{Year=($cur.Year+1);Month=1} }
    else                   { $cur = [pscustomobject]@{Year=$cur.Year;Month=($cur.Month+1)} }
}

Write-Host "Generating $($months.Count) months x $($configs.Count) region/entity combos = $($months.Count * $configs.Count) instances..."

# ─── Paths ────────────────────────────────────────────────────────────────────

$DataRoot = Resolve-Path $DataRoot
$instancesRoot = Join-Path $DataRoot "instances"
$auditRoot     = Join-Path $DataRoot "audit"

# ─── Generate ─────────────────────────────────────────────────────────────────

$totalInstances = 0
$totalHeaders   = 0
$totalAudit     = 0

foreach ($cfg in $configs) {
    $officialCutoff = Parse-YM $cfg.OfficialThrough
    $pendingCutoff  = Parse-YM $cfg.PendingThrough
    $ccyList        = $cfg.Currencies

    foreach ($mo in $months) {
        # Determine state
        $cmpOfficial = Compare-YM ([pscustomobject]@{Year=[int]$mo.Label.Split('-')[0];Month=[int]$mo.Label.Split('-')[1]}) $officialCutoff
        $cmpPending  = Compare-YM ([pscustomobject]@{Year=[int]$mo.Label.Split('-')[0];Month=[int]$mo.Label.Split('-')[1]}) $pendingCutoff

        if     ($cmpOfficial -le 0) { $state = "Official" }
        elseif ($cmpPending  -le 0) { $state = "PendingApproval" }
        else                        { $state = "Draft" }

        $stateFolder = $state.ToUpperInvariant() -replace ' ', ''

        # Build cross-rate rows for all ordered pairs in ccyList
        $rows = @()
        for ($i = 0; $i -lt $ccyList.Count; $i++) {
            for ($j = 0; $j -lt $ccyList.Count; $j++) {
                if ($i -eq $j) { continue }
                $src  = $ccyList[$i]
                $tgt  = $ccyList[$j]
                $rate = Get-CrossRate -src $src -tgt $tgt -monthIdx $mo.Idx
                $rows += [ordered]@{ SourceCCY=$src; TargetCCY=$tgt; Rate=$rate }
            }
        }

        $instanceId    = [guid]::NewGuid()
        $auditCreateId = [guid]::NewGuid()
        $guidN         = $instanceId.ToString('N')
        $asOfDate      = $mo.Date
        $header        = [ordered]@{ Region=$cfg.Region; Entity=$cfg.Entity }
        $headerHash    = Compute-HeaderHash $header

        $createTs  = Fmt-Ts $asOfDate 9 0 0
        $modifyTs  = if ($state -ne "Draft") { Fmt-Ts $asOfDate 10 30 0 } else { $createTs }
        $signoffTs = if ($state -eq "Official") { Fmt-Ts $asOfDate 11 0 0 } else { $null }

        $version = switch ($state) { "Official" { 1 } "PendingApproval" { 2 } default { 1 } }
        $lastModBy = if ($state -ne "Draft") { $cfg.Writer } else { $cfg.Writer }

        # Instance file
        $instance = [ordered]@{
            id              = $instanceId.ToString()
            datasetKey      = "FX_RATES"
            asOfDate        = $asOfDate
            state           = $state
            version         = $version
            header          = $header
            rows            = $rows
            createdBy       = $cfg.Writer
            createdAtUtc    = $createTs
            lastModifiedBy  = $lastModBy
            lastModifiedAtUtc = $modifyTs
        }

        # Header index file
        $headerIndex = [ordered]@{
            id          = $instanceId.ToString()
            datasetKey  = "FX_RATES"
            asOfDate    = $asOfDate
            state       = $state
            header      = $header
            headerHash  = $headerHash
            version     = $version
            createdBy   = $cfg.Writer
            createdAtUtc = $createTs
            lastModifiedBy    = $lastModBy
            lastModifiedAtUtc = $modifyTs
        }

        # Audit: INSTANCE_CREATE
        $rowChanges = $rows | ForEach-Object {
            [ordered]@{
                operation    = "added"
                keyFields    = [ordered]@{ SourceCCY=$_['SourceCCY']; TargetCCY=$_['TargetCCY'] }
                sourceValues = $null
                targetValues = [ordered]@{ Rate=[string]$_['Rate'] }
            }
        }
        $auditCreate = [ordered]@{
            id               = $auditCreateId.ToString()
            occurredAtUtc    = $createTs
            userId           = $cfg.Writer
            action           = "INSTANCE_CREATE"
            datasetKey       = "FX_RATES"
            datasetInstanceId = $instanceId.ToString()
            rowChanges       = @($rowChanges)
        }

        # Paths
        $instancePath = Join-Path $instancesRoot "FX_RATES\$guidN.json"
        $headerPath   = Join-Path $instancesRoot "FX_RATES\headers\$stateFolder\$asOfDate\$guidN.header.json"
        $auditDir     = Join-Path $auditRoot "FX_RATES\$guidN"

        $createAuditTs = Fmt-AuditTs $asOfDate 9 0 0 0
        $auditCreatePath = Join-Path $auditDir "${createAuditTs}_INSTANCE_CREATE_$($auditCreateId.ToString('N')).json"

        if (-not $WhatIf) {
            Write-Json $instancePath   $instance
            Write-Json $headerPath     $headerIndex
            Write-Json $auditCreatePath $auditCreate
        }
        $totalInstances++
        $totalHeaders++
        $totalAudit++

        # Audit: INSTANCE_SIGNOFF (Official only)
        if ($state -eq "Official") {
            $auditSignoffId = [guid]::NewGuid()
            $signoffAuditTs = Fmt-AuditTs $asOfDate 11 0 0 0
            $auditSignoff = [ordered]@{
                id               = $auditSignoffId.ToString()
                occurredAtUtc    = $signoffTs
                userId           = $cfg.Approver
                action           = "INSTANCE_SIGNOFF"
                datasetKey       = "FX_RATES"
                datasetInstanceId = $instanceId.ToString()
                rowChanges       = @()
            }
            $auditSignoffPath = Join-Path $auditDir "${signoffAuditTs}_INSTANCE_SIGNOFF_$($auditSignoffId.ToString('N')).json"
            if (-not $WhatIf) { Write-Json $auditSignoffPath $auditSignoff }
            $totalAudit++
        }
    }

    Write-Host "  $($cfg.Region)/$($cfg.Entity): done ($($months.Count) months, OfficialThrough=$($cfg.OfficialThrough))"
}

Write-Host ""
Write-Host "Complete: $totalInstances instances, $totalHeaders headers, $totalAudit audit events"
if ($WhatIf) { Write-Host "(WhatIf mode - no files written)" }
