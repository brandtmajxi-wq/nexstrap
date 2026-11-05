<#
  Builds the current source as the DEV Launcher and puts it in %LocalAppData%\Programs\Nexstrap Dev.
  Run it after every change, before any public release. Nothing here is published.
  If the dev copy is open it keeps running, and the new build starts next time it's opened.
  Usage: powershell -File tools/update-dev.ps1 [-SkipTests]
#>
param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$project = Join-Path $root 'src\Nexstrap\Nexstrap.csproj'
$local = [Environment]::GetFolderPath('LocalApplicationData')
$folder = Join-Path $local 'Programs\Nexstrap Dev'
$exe = Join-Path $folder 'Nexstrap Dev.exe'
$data = Join-Path $local 'Nexstrap Dev'
$publicData = Join-Path $local 'Nexstrap'

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'Nexstrap.slnx') --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; the dev copy was not changed.' }
}

$publish = Join-Path $root 'artifacts\dev'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $project -c Dev -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed; the dev copy was not changed.' }

New-Item -ItemType Directory -Force $folder | Out-Null
Get-ChildItem $folder -Filter '*.old' -ErrorAction SilentlyContinue | ForEach-Object {
    try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { } # still running; goes next time
}
# Windows can't overwrite a running exe, but it can rename one.
if (Test-Path $exe) { Move-Item $exe "$exe.$([DateTime]::Now.Ticks).old" }
Copy-Item (Join-Path $publish 'Nexstrap.exe') $exe
(Get-Item $exe).LastWriteTime = Get-Date
Remove-Item $publish -Recurse -Force

# First run only: start from a copy of your setup. After that the two copies are separate.
if (-not (Test-Path $data)) {
    New-Item -ItemType Directory -Force $data | Out-Null
    foreach ($item in 'Settings.json', 'FastFlags.json', 'History.json', 'Themes') {
        $source = Join-Path $publicData $item
        if (Test-Path $source) { Copy-Item $source $data -Recurse }
    }
}

$shortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Nexstrap DEV.lnk'
$link = (New-Object -ComObject WScript.Shell).CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $folder
$link.Description = 'Nexstrap DEV Launcher'
$link.Save()

$running = @(Get-Process 'Nexstrap Dev' -ErrorAction SilentlyContinue).Count -gt 0
Write-Host "DEV Launcher updated: $exe"
if ($running) { Write-Host 'It is open now, so restart it to get this build.' }
