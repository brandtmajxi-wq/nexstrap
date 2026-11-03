<#
  Builds a Nexstrap release package the self-updater accepts (see docs/RELEASING.md):
    artifacts/release-v<version>/Nexstrap.exe
    artifacts/release-v<version>/SHA256SUMS.txt
    artifacts/release-v<version>/RELEASE_NOTES.md
  Usage: powershell -File tools/package-release.ps1 [-SkipTests]
#>
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\Nexstrap\Nexstrap.csproj'

[xml]$csproj = Get-Content $project
$version = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'No <Version> in Nexstrap.csproj' }
Write-Host "Packaging Nexstrap $version"

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'Nexstrap.slnx') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not packaging.' }
}

$out = Join-Path $root "artifacts\release-v$version"
$publish = Join-Path $out 'publish'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $publish | Out-Null

dotnet publish $project -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$exe = Join-Path $out 'Nexstrap.exe'
Move-Item (Join-Path $publish 'Nexstrap.exe') $exe
Remove-Item $publish -Recurse -Force

# Same checks the updater performs on the user's PC.
$info = (Get-Item $exe).VersionInfo
if ($info.ProductName -ne 'Nexstrap') { throw "ProductName is '$($info.ProductName)'" }
$fileVersion = "$($info.FileMajorPart).$($info.FileMinorPart).$($info.FileBuildPart)"
if ($fileVersion -ne $version) { throw "File version $fileVersion doesn't match $version" }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
[System.IO.File]::WriteAllText((Join-Path $out 'SHA256SUMS.txt'), "$hash  Nexstrap.exe`n", (New-Object System.Text.UTF8Encoding $false))

# This version's CHANGELOG section becomes the release description.
$changelog = [System.IO.File]::ReadAllText((Join-Path $root 'CHANGELOG.md'))
$section = [regex]::Match($changelog, "(?ms)^## \[$([regex]::Escape($version))\].*?(?=^## \[|^\[\d|\z)").Value.Trim()
if (-not $section) { Write-Warning "CHANGELOG.md has no section for $version"; $section = "Nexstrap $version" }
[System.IO.File]::WriteAllText((Join-Path $out 'RELEASE_NOTES.md'), $section + "`n", (New-Object System.Text.UTF8Encoding $false))

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $out"
Write-Host "  Nexstrap.exe     $size MB"
Write-Host "  SHA256           $hash"
Write-Host "Publish as tag v$version with Nexstrap.exe and SHA256SUMS.txt attached."
