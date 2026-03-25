$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Join-Path $root "src"
$packRoot = Join-Path $root "pack"
$artifactDir = Join-Path $root "artifacts"
$testBuildDir = Join-Path $root "test_build"
$dllOut = Join-Path $artifactDir "Act4FinalAscent.dll"
$pckOut = Join-Path $artifactDir "Act4FinalAscent.pck"
$jsonOut = Join-Path $artifactDir "Act4FinalAscent.json"
$zipOut = Join-Path $artifactDir "Act4FinalAscent.zip"
$packBuildRoot = Join-Path $testBuildDir "pack_stage"
$legacyArtifactPaths = @(
    (Join-Path $artifactDir "Act4Placeholder.dll"),
    (Join-Path $artifactDir "Act4Placeholder.pck"),
    (Join-Path $artifactDir "Act4Placeholder.json"),
    (Join-Path $artifactDir "Act4Placeholder.zip")
)

function Convert-JsonObjectToOrderedHashtable {
    param(
        [Parameter(Mandatory = $true)]
        [object]$JsonObject
    )

    $ordered = [ordered]@{}
    foreach ($prop in $JsonObject.PSObject.Properties) {
        $ordered[$prop.Name] = $prop.Value
    }
    return $ordered
}

function Sync-LocalizationFallbackKeys {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LocalizationRoot
    )

    $englishRoot = Join-Path $LocalizationRoot "eng"
    if (-not (Test-Path $englishRoot)) {
        throw "Fallback locale root not found at $englishRoot"
    }

    $englishFiles = Get-ChildItem $englishRoot -File -Filter *.json
    $languageDirs = Get-ChildItem $LocalizationRoot -Directory | Where-Object { $_.Name -ne "eng" }

    foreach ($englishFile in $englishFiles) {
        $englishPath = $englishFile.FullName
        $englishRaw = Get-Content $englishPath -Raw
        $englishObj = $englishRaw | ConvertFrom-Json
        $englishMap = Convert-JsonObjectToOrderedHashtable -JsonObject $englishObj

        foreach ($languageDir in $languageDirs) {
            $targetPath = Join-Path $languageDir.FullName $englishFile.Name
            if (-not (Test-Path $targetPath)) {
                Copy-Item $englishPath $targetPath -Force
                Write-Output "Localization fallback: created $($languageDir.Name)/$($englishFile.Name) from eng baseline."
                continue
            }

            $targetObj = $null
            try {
                $targetRaw = Get-Content $targetPath -Raw
                $targetObj = $targetRaw | ConvertFrom-Json
            } catch {
                Copy-Item $englishPath $targetPath -Force
                Write-Output "Localization fallback: replaced invalid $($languageDir.Name)/$($englishFile.Name) with eng baseline."
                continue
            }

            $targetMap = Convert-JsonObjectToOrderedHashtable -JsonObject $targetObj
            $addedCount = 0
            foreach ($key in $englishMap.Keys) {
                if (-not $targetMap.Contains($key)) {
                    $targetMap[$key] = $englishMap[$key]
                    $addedCount++
                }
            }

            if ($addedCount -gt 0) {
                $jsonOut = $targetMap | ConvertTo-Json -Depth 16
                [System.IO.File]::WriteAllText($targetPath, $jsonOut + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
                Write-Output "Localization fallback: added $addedCount missing key(s) to $($languageDir.Name)/$($englishFile.Name)."
            }
        }
    }
}

function Get-Sts2DataDir {
    param([string]$ConfiguredPath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        if (Test-Path $ConfiguredPath) {
            return $ConfiguredPath
        }
        throw "STS2_GAME_DIR points to a missing path: $ConfiguredPath"
    }

    $candidateRoots = @()
    if ($env:ProgramFiles) { $candidateRoots += (Join-Path $env:ProgramFiles "Steam") }
    if (${env:ProgramFiles(x86)}) { $candidateRoots += (Join-Path ${env:ProgramFiles(x86)} "Steam") }
    foreach ($drive in Get-PSDrive -PSProvider FileSystem) {
        $candidateRoots += (Join-Path $drive.Root "Steam")
        $candidateRoots += (Join-Path $drive.Root "SteamLibrary")
    }

    $candidateRoots = $candidateRoots | Select-Object -Unique
    foreach ($steamRoot in $candidateRoots) {
        $candidate = Join-Path $steamRoot "steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64"
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Could not find Slay the Spire 2 automatically. Set STS2_GAME_DIR to the game's data_sts2_windows_x86_64 folder."
}

function Get-Sts2AssetRoot {
    param(
        [string]$ConfiguredPath,
        [string]$RepoRoot,
        [string]$GameDllRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPath)) {
        if (Test-Path $ConfiguredPath) {
            return $ConfiguredPath
        }
        throw "STS2_ASSET_ROOT points to a missing path: $ConfiguredPath"
    }

    $candidates = @(
        (Join-Path $RepoRoot "Slay the Spire 2"),
        (Join-Path (Split-Path -Parent $RepoRoot) "Slay the Spire 2"),
        (Join-Path (Split-Path -Parent (Split-Path -Parent $RepoRoot)) "Slay the Spire 2"),
        (Split-Path -Parent $GameDllRoot)
    ) | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate "animations\\monsters\\architect\\architect.skel")) {
            return $candidate
        }
    }

    throw "Could not find extracted game assets. Set STS2_ASSET_ROOT to a folder containing animations\\monsters\\architect\\architect.skel."
}

function Get-BundledArchitectSkeleton {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PackBuildRoot
    )

    $phaseSkeletons = @(
        (Join-Path $PackBuildRoot "animations\monsters\architect_phase2\architect.skel"),
        (Join-Path $PackBuildRoot "animations\monsters\architect_phase3\architect.skel")
    )

    foreach ($phaseSkeleton in $phaseSkeletons) {
        if (-not (Test-Path $phaseSkeleton)) {
            return $null
        }
    }

    return $phaseSkeletons
}

function Get-RemappedResourcePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ImportFilePath,
        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot
    )

    $match = [regex]::Match((Get-Content $ImportFilePath -Raw), 'path(?:\\.s3tc)?="(?<path>res://[^"]+)"')
    if (-not $match.Success) {
        throw "Could not resolve remapped resource path from $ImportFilePath"
    }

    $relative = $match.Groups["path"].Value.Substring("res://".Length).Replace("/", "\")
    return Join-Path $ProjectRoot $relative
}

New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null
New-Item -ItemType Directory -Force -Path $testBuildDir | Out-Null

foreach ($legacyArtifactPath in $legacyArtifactPaths) {
    if (Test-Path $legacyArtifactPath) {
        Remove-Item $legacyArtifactPath -Force
    }
}

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$sdkDir = $null
$sdkListOutput = & $dotnet --list-sdks 2>$null
if ($LASTEXITCODE -eq 0) {
    $sdkEntries = @()
    foreach ($sdkLine in $sdkListOutput) {
        if ($sdkLine -match '^(?<version>[^\s]+)\s+\[(?<root>[^\]]+)\]$') {
            $sdkEntries += [pscustomobject]@{
                Version = $matches["version"]
                Root = $matches["root"]
            }
        }
    }

    $latestSdk = $sdkEntries | Select-Object -Last 1
    if ($latestSdk) {
        $sdkPath = Join-Path $latestSdk.Root $latestSdk.Version
        if (Test-Path $sdkPath) {
            $sdkDir = Get-Item $sdkPath
        }
    }
}

if (-not $sdkDir) {
    $dotnetRoot = Split-Path -Parent $dotnet
    $fallbackSdkRoot = Join-Path $dotnetRoot "sdk"
    if (Test-Path $fallbackSdkRoot) {
        $sdkDir = Get-ChildItem $fallbackSdkRoot -Directory | Sort-Object Name -Descending | Select-Object -First 1
    }
}

if (-not $sdkDir) {
    throw "No .NET SDK found."
}

$csc = Join-Path $sdkDir.FullName "Roslyn\bincore\csc.dll"
$godotCandidates = @(
    $env:GODOT,
    "C:\Program Files\Godot\Godot_v4.5-stable_mono_win64_console.exe",
    "C:\Program Files\Godot\Godot_v4.5-stable_mono_win64\Godot_v4.5-stable_mono_win64_console.exe",
    "C:\Program Files\Godot\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe",
    "C:\Tools\Godot\Godot_v4.5-stable_mono_win64_console.exe"
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

$godot = $godotCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $godot) {
    $godotCmd = Get-Command godot -ErrorAction SilentlyContinue
    if ($godotCmd) {
        $godot = $godotCmd.Source
    }
}
if (-not $godot) {
    $downloadedGodot = Get-ChildItem (Join-Path $HOME "Downloads") -Recurse -Filter "Godot*_console.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -ExpandProperty FullName -First 1
    if ($downloadedGodot) {
        $godot = $downloadedGodot
    }
}
if (-not $godot) {
    throw "Godot console executable not found. Set GODOT or install Godot 4.5 Mono."
}

$packProject = Join-Path $packRoot "project.godot"
if (-not (Test-Path $packProject)) {
    throw "Pack project.godot not found at $packProject"
}

$gameDllRoot = Get-Sts2DataDir -ConfiguredPath $env:STS2_GAME_DIR
Write-Output "Using game DLLs from: $gameDllRoot"
$refArgs = Get-ChildItem $gameDllRoot -Filter *.dll |
    Where-Object {
        try {
            [Reflection.AssemblyName]::GetAssemblyName($_.FullName) | Out-Null
            $true
        } catch {
            $false
        }
    } |
    ForEach-Object { "/r:`"$($_.FullName)`"" }

$srcArgs = Get-ChildItem $projectRoot -Recurse -Filter *.cs |
    Where-Object {
        $_.FullName -notmatch '\\obj\\' -and
        $_.FullName -notmatch '\\bin\\'
    } |
    ForEach-Object { "`"$($_.FullName)`"" }

$cscResponseFile = Join-Path $testBuildDir "csc_args.rsp"
[System.IO.File]::WriteAllLines(
    $cscResponseFile,
    @(
        "/out:`"$dllOut`""
        $refArgs
        $srcArgs
    ),
    [System.Text.UTF8Encoding]::new($false)
)

& $dotnet $csc /noconfig /nostdlib+ /target:library /langversion:preview /nullable:enable /unsafe /optimize+ "@$cscResponseFile"
if ($LASTEXITCODE -ne 0) {
    throw "C# compile failed."
}

$packGodotDir = Join-Path $packBuildRoot ".godot"
if (Test-Path $packBuildRoot) {
    Remove-Item $packBuildRoot -Recurse -Force
}
Copy-Item $packRoot $packBuildRoot -Recurse -Force

$legacyPackName = "Act4Placeholder"
$artifactPackName = "Act4FinalAscent"
$legacyPackResourceRoot = Join-Path $packBuildRoot $legacyPackName
$artifactPackResourceRoot = Join-Path $packBuildRoot $artifactPackName
$legacyLocalizationRoot = Join-Path $legacyPackResourceRoot "localization"
$artifactLocalizationRoot = Join-Path $artifactPackResourceRoot "localization"

if (-not (Test-Path $legacyLocalizationRoot)) {
    throw "Localization root not found at $legacyLocalizationRoot"
}

New-Item -ItemType Directory -Force -Path $artifactLocalizationRoot | Out-Null
Copy-Item (Join-Path $legacyLocalizationRoot "*") $artifactLocalizationRoot -Recurse -Force
Sync-LocalizationFallbackKeys -LocalizationRoot $artifactLocalizationRoot

$legacyModImage = Join-Path $legacyPackResourceRoot "mod_image.png"
if (Test-Path $legacyModImage) {
    Copy-Item $legacyModImage (Join-Path $artifactPackResourceRoot "mod_image.png") -Force
}

$legacyModImageImport = Join-Path $legacyPackResourceRoot "mod_image.png.import"
if (Test-Path $legacyModImageImport) {
    Copy-Item $legacyModImageImport (Join-Path $artifactPackResourceRoot "mod_image.png.import") -Force
}

if (Test-Path $packGodotDir) {
    Remove-Item $packGodotDir -Recurse -Force
}

& $godot --headless --editor --quit --path $packBuildRoot
if ($LASTEXITCODE -ne 0) {
    throw "Godot import failed."
}

$importedDir = Join-Path $packBuildRoot ".godot\imported"
New-Item -ItemType Directory -Force -Path $importedDir | Out-Null

$bundledArchitectSkeletons = Get-BundledArchitectSkeleton -PackBuildRoot $packBuildRoot
if ($bundledArchitectSkeletons) {
    Copy-Item $bundledArchitectSkeletons[0] (Join-Path $importedDir "architect_phase2_architect.skel.spskel") -Force
    Copy-Item $bundledArchitectSkeletons[1] (Join-Path $importedDir "architect_phase3_architect.skel.spskel") -Force
    Write-Output "Using bundled Architect skeleton files from the repo."
} else {
    $gameRoot = Get-Sts2AssetRoot -ConfiguredPath $env:STS2_ASSET_ROOT -RepoRoot $root -GameDllRoot $gameDllRoot

    $baseArchitectSkelImported = $null
    try {
        $candidate = Get-RemappedResourcePath -ImportFilePath (Join-Path $gameRoot "animations\monsters\architect\architect.skel.import") -ProjectRoot $gameRoot
        if (Test-Path $candidate) { $baseArchitectSkelImported = $candidate }
    } catch {}

    if (-not $baseArchitectSkelImported) {
        $baseArchitectSkelImported = Join-Path $gameRoot "animations\monsters\architect\architect.skel"
        if (-not (Test-Path $baseArchitectSkelImported)) { throw "architect.skel not found in game source" }
        Write-Output "Note: using raw architect.skel (Godot import cache not present)"
    }
    Copy-Item $baseArchitectSkelImported (Join-Path $importedDir "architect_phase2_architect.skel.spskel") -Force
    Copy-Item $baseArchitectSkelImported (Join-Path $importedDir "architect_phase3_architect.skel.spskel") -Force
}

$phaseAtlasTargets = @(
    @{
        SourceAtlas = Join-Path $packBuildRoot "animations\monsters\architect_phase2\architect.atlas"
        SourcePath = "res://animations/monsters/architect_phase2/architect.atlas"
        Output = Join-Path $importedDir "architect_phase2_architect.atlas.spatlas"
    },
    @{
        SourceAtlas = Join-Path $packBuildRoot "animations\monsters\architect_phase3\architect.atlas"
        SourcePath = "res://animations/monsters/architect_phase3/architect.atlas"
        Output = Join-Path $importedDir "architect_phase3_architect.atlas.spatlas"
    }
)

foreach ($phaseAtlasTarget in $phaseAtlasTargets) {
    if (-not (Test-Path $phaseAtlasTarget.SourceAtlas)) {
        throw "Expected atlas file not found: $($phaseAtlasTarget.SourceAtlas)"
    }

    $atlasPayload = @{
        atlas_data = (Get-Content $phaseAtlasTarget.SourceAtlas -Raw).TrimEnd("`r", "`n")
        normal_texture_prefix = "n"
        source_path = $phaseAtlasTarget.SourcePath
        specular_texture_prefix = "s"
    } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText($phaseAtlasTarget.Output, $atlasPayload, [System.Text.UTF8Encoding]::new($false))
}

if (Test-Path $pckOut) {
    Remove-Item $pckOut -Force
}

& $godot --headless --script (Join-Path $root "pack_mod.gd") -- $packBuildRoot $pckOut
if ($LASTEXITCODE -ne 0) {
    throw "PCK pack failed."
}

Copy-Item (Join-Path $root "Act4FinalAscent.json") $jsonOut -Force

if (Test-Path $zipOut) {
    Remove-Item $zipOut -Force
}

Compress-Archive -Path $dllOut, $pckOut, $jsonOut -DestinationPath $zipOut -CompressionLevel Optimal

Write-Output "Built:"
Write-Output $dllOut
Write-Output $pckOut
Write-Output $zipOut
