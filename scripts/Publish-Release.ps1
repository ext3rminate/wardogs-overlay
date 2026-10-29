[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$')]
    [string] $Version,

    [string] $OutputDirectory,
    [switch] $FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $projectRoot 'WardogsOverlay/WardogsOverlay.csproj'
[xml] $project = Get-Content -LiteralPath $projectFile -Raw
$projectVersion = [string] $project.Project.PropertyGroup.Version | Where-Object { $_ }
if ($projectVersion -ne $Version) {
    throw "Requested version $Version differs from project version $projectVersion. Update the project before tagging."
}

if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$packageName = "WardogsOverlay-v$Version-win-x64"
$packageDirectory = Join-Path $OutputDirectory $packageName
if (Test-Path -LiteralPath $packageDirectory) {
    throw "Output already exists: $packageDirectory. Use a new output directory to avoid including stale files."
}

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
dotnet publish $projectFile -c Release -r win-x64 --self-contained $selfContained `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false `
    -o $packageDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

if (-not $FrameworkDependent) {
    $assetsFile = Join-Path $projectRoot 'WardogsOverlay/obj/project.assets.json'
    $restoreAssets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
    $runtimePacks = foreach ($framework in $restoreAssets.project.frameworks.PSObject.Properties.Value) {
        $framework.downloadDependencies | Where-Object { $_.name -match '^Microsoft\.(NETCore|WindowsDesktop)\.App\.Runtime\.win-x64$' }
    }
    foreach ($runtimePack in $runtimePacks) {
        $runtimeVersion = $runtimePack.version.Trim('[', ']').Split(',')[0].Trim()
        foreach ($folder in $restoreAssets.packageFolders.PSObject.Properties.Name) {
            $packagePath = Join-Path $folder "$($runtimePack.name.ToLowerInvariant())/$runtimeVersion"
            if (-not (Test-Path -LiteralPath $packagePath)) { continue }
            $noticeName = "$($runtimePack.name)-$runtimeVersion"
            foreach ($notice in Get-ChildItem -LiteralPath $packagePath -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' }) {
                $noticePath = Join-Path $packageDirectory "licenses/$noticeName-$($notice.Name)"
                Copy-Item -LiteralPath $notice.FullName -Destination $noticePath
            }
            break
        }
    }
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/USER_GUIDE.ru.txt') -Destination (Join-Path $packageDirectory 'README.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $packageDirectory
if ($FrameworkDependent) {
    $runtimeNote = 'This build requires .NET 10 Desktop Runtime (Windows x64).'
} else {
    $runtimeNote = 'Self-contained Windows x64 build. .NET Runtime is included; no separate .NET installation is required.'
}
Set-Content -LiteralPath (Join-Path $packageDirectory 'BUILD.txt') -Value "Wardogs Overlay $Version`r`n$runtimeNote" -Encoding utf8

$archivePath = Join-Path $OutputDirectory "$packageName.zip"
Compress-Archive -Path "$packageDirectory/*" -DestinationPath $archivePath -CompressionLevel Optimal
$exePath = Join-Path $OutputDirectory "$packageName.exe"
Copy-Item -LiteralPath (Join-Path $packageDirectory 'WardogsOverlay.exe') -Destination $exePath
$hashes = foreach ($asset in @($archivePath, $exePath)) {
    $hash = (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($asset))"
}
$hashes | Set-Content -LiteralPath (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii
Write-Host "Release assets: $OutputDirectory"
