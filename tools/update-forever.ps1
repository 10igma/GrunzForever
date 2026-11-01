<#
.SYNOPSIS
    Vollautomatisches Forensik-, Extraktions-, Diff- und Release-Skript fuer GrunzForever (wowf / Classic Forever).

.DESCRIPTION
    1. Prueft den lokalen WoW: Classic Forever Client (_forever_ / Produkt wowf):
       .build.info, Wow.exe, DBCache.bin, CASC-Archive und optional Blizzards Live-EU-TACT-Endpoint.
    2. Sichert bei Intra-Build-Updates (gleiche Build-Version, z. B. CASC-Streaming oder DBCache-Hotfix-Welle)
       automatisch einen temporaeren SQLite-Snapshot vor dem Ueberschreiben.
    3. Baut und testet GrunzForever.slnx (Release) und fuehrt die Extraktion inkl. --sync-repo-data aus.
    4. Erzeugt automatisch Build-zu-Build-Diffs (<old>_to_<new>.md/.json) oder Intra-Build-Hotfix-Diffs
       (<ver>_hotfix_<timestamp>.md/.json, falls tatsaechliche Daten-Deltas vorliegen).
    5. Packt optional Release-ZIPs (-PackageRelease) oder veroeffentlicht direkt ein GitHub-Release (-PublishRelease vX.Y.Z).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1 -PackageRelease

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File tools/update-forever.ps1 -PublishRelease v0.2.0
#>
[CmdletBinding()]
param(
    [string]$WowDir = "",
    [switch]$SkipTactCheck,
    [switch]$AllDb2,
    [switch]$SkipTests,
    [switch]$PackageRelease,
    [string]$PublishRelease = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot

try {
    # -------------------------------------------------------------------------
    # 1. Lokales WoW-Forever-Verzeichnis aufloesen
    # -------------------------------------------------------------------------
    $resolvedWowDir = $WowDir
    if ([string]::IsNullOrWhiteSpace($resolvedWowDir)) {
        $resolvedWowDir = $env:WOW_FOREVER_DIR
    }
    if ([string]::IsNullOrWhiteSpace($resolvedWowDir)) {
        foreach ($drive in [System.IO.DriveInfo]::GetDrives() | Where-Object { $_.IsReady }) {
            $root = $drive.RootDirectory.FullName
            $candidates = @(
                (Join-Path $root "Games\World of Warcraft\_forever_"),
                (Join-Path $root "World of Warcraft\_forever_"),
                (Join-Path $root "Program Files (x86)\World of Warcraft\_forever_"),
                (Join-Path $root "Program Files\World of Warcraft\_forever_")
            )
            foreach ($cand in $candidates) {
                if (Test-Path (Join-Path $cand ".flavor.info")) {
                    $resolvedWowDir = $cand
                    break
                }
            }
            if (-not [string]::IsNullOrWhiteSpace($resolvedWowDir)) { break }
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolvedWowDir) -or -not (Test-Path $resolvedWowDir)) {
        throw "WoW: Classic Forever Ordner (_forever_) nicht gefunden. Bitte -WowDir oder WOW_FOREVER_DIR angeben."
    }
    $resolvedWowDir = (Resolve-Path $resolvedWowDir).Path
    $wowRoot = Split-Path -Parent $resolvedWowDir

    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host " GrunzForever Update-Pipeline" -ForegroundColor Cyan
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host "Repo-Pfad : $repoRoot"
    Write-Host "WoW-Pfad  : $resolvedWowDir"

    # -------------------------------------------------------------------------
    # 2. Forensik-Check: .build.info, Wow.exe, DBCache.bin, CASC & EU-TACT
    # -------------------------------------------------------------------------
    $buildInfoPath = Join-Path $wowRoot ".build.info"
    if (-not (Test-Path $buildInfoPath)) {
        throw ".build.info nicht gefunden unter: $buildInfoPath"
    }

    $biLines = Get-Content $buildInfoPath -Encoding UTF8 | Where-Object { $_.Trim().Length -gt 0 }
    $headers = ($biLines[0] -split '\|') | ForEach-Object { ($_ -split '!')[0].Trim() }
    $localVersion = ""
    $localBuildKey = ""
    $localCdnKey = ""

    for ($i = 1; $i -lt $biLines.Count; $i++) {
        $cols = $biLines[$i] -split '\|'
        $rowMap = @{}
        for ($c = 0; $c -lt [Math]::Min($headers.Count, $cols.Count); $c++) {
            $rowMap[$headers[$c]] = $cols[$c].Trim()
        }
        if ($rowMap["Product"] -eq "wowf") {
            $localVersion = $rowMap["Version"]
            $localBuildKey = $rowMap["Build Key"]
            $localCdnKey = $rowMap["CDN Key"]
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($localVersion)) {
        throw "Produkt 'wowf' wurde in $buildInfoPath nicht gefunden."
    }

    $wowExePath = Join-Path $resolvedWowDir "Wow.exe"
    $wowExeVer = if (Test-Path $wowExePath) { (Get-Item $wowExePath).VersionInfo.FileVersion } else { "n/a" }
    $wowExeTime = if (Test-Path $wowExePath) { (Get-Item $wowExePath).LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") } else { "n/a" }

    $dbCachePath = Join-Path $resolvedWowDir "Cache\ADB\enUS\DBCache.bin"
    $dbCacheInfo = if (Test-Path $dbCachePath) {
        $f = Get-Item $dbCachePath
        "{0:N2} MB (Stand: {1})" -f ($f.Length / 1MB), $f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
    } else {
        "nicht vorhanden"
    }

    $cascDataDir = Join-Path $wowRoot "Data\data"
    $cascStats = if (Test-Path $cascDataDir) {
        $idxFiles = @(Get-ChildItem $cascDataDir -Filter "*.idx" -ErrorAction SilentlyContinue)
        $datFiles = @(Get-ChildItem $cascDataDir -Filter "data.*" -ErrorAction SilentlyContinue)
        $totalGb = (($datFiles | Measure-Object -Property Length -Sum).Sum) / 1GB
        $latestWrite = ($idxFiles + $datFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
        "{0} Archive ({1:N2} GB), {2} .idx, letzter Schreibzugriff: {3}" -f $datFiles.Count, $totalGb, $idxFiles.Count, $latestWrite.ToString("yyyy-MM-dd HH:mm:ss")
    } else {
        "n/a"
    }

    Write-Host ""
    Write-Host "[1/5] Client-Forensik:" -ForegroundColor Yellow
    Write-Host "  Produkt / Version : wowf $localVersion"
    Write-Host "  BuildKey / CDNKey : $localBuildKey / $localCdnKey"
    Write-Host "  Wow.exe           : $wowExeVer ($wowExeTime)"
    Write-Host "  DBCache.bin       : $dbCacheInfo"
    Write-Host "  CASC Data\data    : $cascStats"

    if (-not $SkipTactCheck) {
        try {
            $tactResp = Invoke-WebRequest -Uri "http://eu.patch.battle.net:1119/wowf/versions" -UseBasicParsing -TimeoutSec 5
            $tactLines = ($tactResp.Content -split "`r?`n") | Where-Object { $_ -and -not $_.StartsWith("#") }
            $tactHeaders = ($tactLines[0] -split '\|') | ForEach-Object { ($_ -split '!')[0].Trim() }
            $euVer = ""
            $euBuildCfg = ""
            for ($t = 1; $t -lt $tactLines.Count; $t++) {
                $tCols = $tactLines[$t] -split '\|'
                $tMap = @{}
                for ($c = 0; $c -lt [Math]::Min($tactHeaders.Count, $tCols.Count); $c++) {
                    $tMap[$tactHeaders[$c]] = $tCols[$c].Trim()
                }
                if ($tMap["Region"] -eq "eu") {
                    $euVer = $tMap["VersionsName"]
                    $euBuildCfg = $tMap["BuildConfig"]
                    break
                }
            }
            if ($euVer -eq $localVersion -and $euBuildCfg -eq $localBuildKey) {
                Write-Host "  EU-TACT-Endpoint  : SYNCHRON ($euVer | $euBuildCfg)" -ForegroundColor Green
            } elseif ($euVer) {
                Write-Host "  EU-TACT-Endpoint  : ABWEICHUNG! Live-EU ist $euVer ($euBuildCfg), lokal ist $localVersion ($localBuildKey)." -ForegroundColor Red
                Write-Host "                      Tipp: Battle.net Launcher kurz starten/patchen lassen!" -ForegroundColor Red
            }
        } catch {
            Write-Host "  EU-TACT-Endpoint  : Offline / Timeout (wird uebersprungen)" -ForegroundColor DarkGray
        }
    }

    # -------------------------------------------------------------------------
    # 3. Baseline-SQLite-Datenbank ermitteln (inkl. Intra-Build-Snapshot)
    # -------------------------------------------------------------------------
    $exportsDir = Join-Path $repoRoot "Exports"
    New-Item -ItemType Directory -Force -Path $exportsDir | Out-Null

    $existingDbs = @(Get-ChildItem $exportsDir -Filter "wow_wowf_*.sqlite" -ErrorAction SilentlyContinue |
        Where-Object { -not $_.Name.StartsWith("_baseline_") } |
        Sort-Object LastWriteTime -Descending)

    $baselineDbPath = ""
    $baselineVer = ""
    $tempSnapshotPath = ""

    if ($existingDbs.Count -gt 0) {
        $latestDb = $existingDbs[0]
        if ($latestDb.Name -match '^wow_wowf_(.+)\.sqlite$') {
            $baselineVer = $Matches[1]
        }

        if ($baselineVer -eq $localVersion) {
            $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
            $tempSnapshotPath = Join-Path $exportsDir "_baseline_${localVersion}_${stamp}.sqlite"
            Copy-Item $latestDb.FullName $tempSnapshotPath -Force
            $baselineDbPath = $tempSnapshotPath
            Write-Host ""
            Write-Host "[2/5] Baseline-Sicherung: Intra-Build-Snapshot angelegt ($($latestDb.Name))" -ForegroundColor Yellow
        } else {
            $baselineDbPath = $latestDb.FullName
            Write-Host ""
            Write-Host "[2/5] Baseline erkannt  : $($latestDb.Name) (Build-Wechsel $baselineVer -> $localVersion)" -ForegroundColor Yellow
        }
    } else {
        Write-Host ""
        Write-Host "[2/5] Keine vorherige SQLite-Baseline in Exports\ gefunden (Erst-Extraktion)." -ForegroundColor Yellow
    }

    # -------------------------------------------------------------------------
    # 4. Build, Test & Extraktion (--sync-repo-data)
    # -------------------------------------------------------------------------
    Write-Host ""
    Write-Host "[3/5] Kompilieren, Testen & CASC/DBCache-Extraktion..." -ForegroundColor Yellow

    & dotnet build "GrunzForever.slnx" -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "dotnet build fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

    if (-not $SkipTests) {
        & dotnet test "GrunzForever.slnx" -c Release --no-build --nologo -v minimal
        if ($LASTEXITCODE -ne 0) { throw "dotnet test fehlgeschlagen (Exit-Code $LASTEXITCODE)." }
    }

    $cliArgs = @("run", "--project", "src/GrunzForever.Cli", "-c", "Release", "--no-build", "--", "--wow-dir", $resolvedWowDir, "--sync-repo-data")
    if ($AllDb2) {
        $cliArgs += "--all-db2"
    }

    & dotnet @cliArgs
    if ($LASTEXITCODE -ne 0) { throw "GrunzForever.Cli Extraktion fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

    $newDbPath = Join-Path $exportsDir "wow_wowf_${localVersion}.sqlite"
    if (-not (Test-Path $newDbPath)) {
        throw "Erwartete SQLite-Datenbank wurde nicht erzeugt: $newDbPath"
    }

    # -------------------------------------------------------------------------
    # 5. Automatischer Diff-Bericht (Build-zu-Build oder Intra-Build-Hotfix)
    # -------------------------------------------------------------------------
    $generatedDiffMd = ""
    $generatedDiffJson = ""

    Write-Host ""
    Write-Host "[4/5] Delta-Analyse (WowBuildDiffer)..." -ForegroundColor Yellow

    if (-not [string]::IsNullOrWhiteSpace($baselineDbPath) -and (Test-Path $baselineDbPath)) {
        $diffsDir = Join-Path $repoRoot "docs\diffs"
        New-Item -ItemType Directory -Force -Path $diffsDir | Out-Null

        if ($baselineVer -ne $localVersion) {
            $outMd = Join-Path $diffsDir "${baselineVer}_to_${localVersion}.md"
            $outJson = Join-Path $diffsDir "${baselineVer}_to_${localVersion}.json"

            & dotnet run --project src/GrunzForever.Cli -c Release --no-build -- --diff $baselineDbPath $newDbPath --out-md $outMd --out-json $outJson
            if ($LASTEXITCODE -ne 0) { throw "WowBuildDiffer fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

            $generatedDiffMd = $outMd
            $generatedDiffJson = $outJson
        } else {
            $tmpMd = Join-Path $exportsDir "_temp_intra_diff.md"
            $tmpJson = Join-Path $exportsDir "_temp_intra_diff.json"

            & dotnet run --project src/GrunzForever.Cli -c Release --no-build -- --diff $baselineDbPath $newDbPath --out-md $tmpMd --out-json $tmpJson
            if ($LASTEXITCODE -ne 0) { throw "WowBuildDiffer (Intra-Build) fehlgeschlagen (Exit-Code $LASTEXITCODE)." }

            $diffObj = Get-Content $tmpJson -Raw -Encoding UTF8 | ConvertFrom-Json
            $changeCount =
                @($diffObj.AddedTables).Count +
                @($diffObj.RemovedTables).Count +
                @($diffObj.ChangedTables).Count +
                @($diffObj.NewHotfixWaves).Count +
                @($diffObj.AddedItems).Count +
                @($diffObj.RemovedItems).Count +
                @($diffObj.ModifiedItems).Count +
                @($diffObj.AddedSpells).Count +
                @($diffObj.RemovedSpells).Count +
                @($diffObj.ModifiedSpells).Count +
                @($diffObj.AddedTitles).Count

            if ($changeCount -gt 0) {
                $stamp = Get-Date -Format "yyyyMMdd-HHmm"
                $outMd = Join-Path $diffsDir "${localVersion}_hotfix_${stamp}.md"
                $outJson = Join-Path $diffsDir "${localVersion}_hotfix_${stamp}.json"
                Move-Item $tmpMd $outMd -Force
                Move-Item $tmpJson $outJson -Force
                $generatedDiffMd = $outMd
                $generatedDiffJson = $outJson
                Write-Host "  Intra-Build-Aenderungen erkannt ($changeCount Delta-Eintraege) -> $outMd" -ForegroundColor Green
            } else {
                Remove-Item $tmpMd -Force -ErrorAction SilentlyContinue
                Remove-Item $tmpJson -Force -ErrorAction SilentlyContinue
                Write-Host "  Keine DB2- oder Hotfix-Aenderungen gegenueber dem letzten Stand ($localVersion)." -ForegroundColor Green
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($tempSnapshotPath) -and (Test-Path $tempSnapshotPath)) {
        Remove-Item $tempSnapshotPath -Force -ErrorAction SilentlyContinue
    }

    # -------------------------------------------------------------------------
    # 6. Optional: Release-ZIPs packen & GitHub-Release veroeffentlichen
    # -------------------------------------------------------------------------
    $shouldPackage = $PackageRelease.IsPresent -or (-not [string]::IsNullOrWhiteSpace($PublishRelease))
    $sqliteZipPath = ""
    $jsonZipPath = ""

    if ($shouldPackage) {
        Write-Host ""
        Write-Host "[5/5] Release-Artefakte komprimieren..." -ForegroundColor Yellow

        Add-Type -AssemblyName System.IO.Compression
        Add-Type -AssemblyName System.IO.Compression.FileSystem

        $artDir = Join-Path $repoRoot "artifacts"
        New-Item -ItemType Directory -Force -Path $artDir | Out-Null

        $sqliteZipPath = Join-Path $artDir "wow_wowf_${localVersion}_sqlite.zip"
        if (Test-Path $sqliteZipPath) { Remove-Item $sqliteZipPath -Force }
        $fs = [System.IO.File]::Open($sqliteZipPath, [System.IO.FileMode]::Create)
        try {
            $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create)
            try {
                [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $zip,
                    $newDbPath,
                    "wow_wowf_${localVersion}.sqlite",
                    [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
            } finally {
                $zip.Dispose()
            }
        } finally {
            $fs.Dispose()
        }

        $jsonSrcDir = Join-Path $exportsDir "json"
        $jsonZipPath = Join-Path $artDir "wow_wowf_${localVersion}_json_bundle.zip"
        if (Test-Path $jsonZipPath) { Remove-Item $jsonZipPath -Force }
        [System.IO.Compression.ZipFile]::CreateFromDirectory(
            $jsonSrcDir,
            $jsonZipPath,
            [System.IO.Compression.CompressionLevel]::Optimal,
            $false)

        $sqMb = (Get-Item $sqliteZipPath).Length / 1MB
        $jsMb = (Get-Item $jsonZipPath).Length / 1MB
        Write-Host ("  SQLite-ZIP : {0} ({1:N2} MB)" -f $sqliteZipPath, $sqMb) -ForegroundColor Green
        Write-Host ("  JSON-ZIP   : {0} ({1:N2} MB)" -f $jsonZipPath, $jsMb) -ForegroundColor Green

        if (-not [string]::IsNullOrWhiteSpace($PublishRelease)) {
            $dirty = @(git status --porcelain)
            if ($dirty.Count -gt 0) {
                throw "Arbeitsverzeichnis hat uncommittete Aenderungen. Bitte zuerst README/CHANGELOG pruefen und committen, bevor -PublishRelease ausgefuehrt wird."
            }

            $existingTag = git tag -l $PublishRelease
            if ([string]::IsNullOrWhiteSpace($existingTag)) {
                & git tag -a $PublishRelease -m "GrunzForever $PublishRelease - Classic Forever Build $localVersion"
                if ($LASTEXITCODE -ne 0) { throw "git tag fehlgeschlagen." }
            }

            & git push origin main
            if ($LASTEXITCODE -ne 0) { throw "git push main fehlgeschlagen." }
            & git push origin $PublishRelease
            if ($LASTEXITCODE -ne 0) { throw "git push tag $PublishRelease fehlgeschlagen." }

            $notesArgs = @()
            if (-not [string]::IsNullOrWhiteSpace($generatedDiffMd) -and (Test-Path $generatedDiffMd)) {
                $notesArgs = @("--notes-file", $generatedDiffMd)
            } else {
                $notesArgs = @("--generate-notes")
            }

            & gh release create $PublishRelease $sqliteZipPath $jsonZipPath --title "GrunzForever $PublishRelease - Classic Forever Build $localVersion" @notesArgs
            if ($LASTEXITCODE -ne 0) { throw "gh release create fehlgeschlagen." }
        }
    } else {
        Write-Host ""
        Write-Host "[5/5] Release-Packaging uebersprungen (nutze -PackageRelease oder -PublishRelease vX.Y.Z)." -ForegroundColor DarkGray
    }

    # -------------------------------------------------------------------------
    # Zusammenfassung
    # -------------------------------------------------------------------------
    Write-Host ""
    Write-Host "======================================================================" -ForegroundColor Cyan
    Write-Host " Update-Lauf erfolgreich abgeschlossen (wowf $localVersion)" -ForegroundColor Cyan
    Write-Host "======================================================================" -ForegroundColor Cyan
    if (-not [string]::IsNullOrWhiteSpace($generatedDiffMd)) {
        Write-Host "Diff-Changelog : $generatedDiffMd" -ForegroundColor Green
        Write-Host "Diff-JSON      : $generatedDiffJson" -ForegroundColor Green
    }
    Write-Host ""
    Write-Host "Geaenderte Dateien im Git-Working-Tree:" -ForegroundColor Yellow
    & git status --short
} finally {
    if ($tempSnapshotPath -and (Test-Path $tempSnapshotPath)) {
        Remove-Item $tempSnapshotPath -Force -ErrorAction SilentlyContinue
    }
    Pop-Location
}
