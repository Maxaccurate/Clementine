# Build the Microsoft Store package (.msix) from the app bundle.
#   ./package-msix.ps1                  build the app, then package it
#   ./package-msix.ps1 -SkipBuild       package the existing outputs/ZestDrop
# Upload outputs/msix/ZestDrop_<version>_x64.msix in Partner Center; the Store signs it.
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'outputs/ZestDrop'
$work = Join-Path $root 'outputs/msix'

# The Store needs a four-part version whose last part is 0; take it from the project file.
[xml]$project = Get-Content -LiteralPath (Join-Path $root 'work/ZestDrop/ZestDrop.csproj')
$version = ([string]($project.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Project version '$version' must look like 1.2.3." }
$packageVersion = "$version.0"

if (-not $SkipBuild) {
    & (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE) { throw 'build.ps1 failed.' }
}
if (-not (Test-Path -LiteralPath (Join-Path $app 'ZestDrop.exe'))) { throw "No app bundle at $app. Run build.ps1 first." }

$sdk = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
    Where-Object { Test-Path (Join-Path $_.FullName 'x64\makeappx.exe') } | Sort-Object { [version]$_.Name } | Select-Object -Last 1
if (-not $sdk) { throw 'makeappx.exe not found. Install the Windows SDK (winget install Microsoft.WindowsSDK.10.0.26100).' }
$makeappx = Join-Path $sdk.FullName 'x64\makeappx.exe'
$makepri = Join-Path $sdk.FullName 'x64\makepri.exe'

# 1. Layout: real files only. robocopy follows the runtime junction, so its contents are copied.
$layout = Join-Path $work 'layout'
if (Test-Path -LiteralPath $layout) { Remove-Item -LiteralPath $layout -Recurse -Force }
New-Item -ItemType Directory -Force -Path $layout | Out-Null
# default-docx-template holds an unzipped docx whose "[Content_Types].xml" collides with the package's own reserved
# file of that name (makeappx error 0x8007007b); python-docx loads templates/default.docx instead and never reads it.
robocopy $app $layout /E /NFL /NDL /NJH /NJS /NP /XD source validation logs __pycache__ default-docx-template /XF *.pdb last-cli-result.json last-cli-result.json.progress events.jsonl | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the app failed (robocopy exit $LASTEXITCODE)." }
$links = Get-ChildItem -LiteralPath $layout -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue
if ($links) { throw "The layout still contains links: $($links.FullName -join ', ')" }
Copy-Item -LiteralPath (Join-Path $root 'packaging/Assets') -Destination (Join-Path $layout 'Assets') -Recurse -Force
(Get-Content -LiteralPath (Join-Path $root 'packaging/AppxManifest.xml') -Raw -Encoding UTF8).Replace('__VERSION__', $packageVersion) |
    Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Encoding UTF8

# 2. Resource index for the scaled logos. It is built from the assets alone: indexing the whole app would
#    turn .NET's per-language folders (de, zh-Hans, ...) into languages the package claims to support.
$pri = Join-Path $work 'pri'
if (Test-Path -LiteralPath $pri) { Remove-Item -LiteralPath $pri -Recurse -Force }
New-Item -ItemType Directory -Force -Path $pri | Out-Null
Copy-Item -LiteralPath (Join-Path $layout 'Assets') -Destination (Join-Path $pri 'Assets') -Recurse
Copy-Item -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Destination $pri
& $makepri createconfig /cf (Join-Path $pri 'priconfig.xml') /dq en-US /pv 10.0.0 /o | Out-Null
if ($LASTEXITCODE) { throw 'makepri createconfig failed.' }
& $makepri new /pr $pri /cf (Join-Path $pri 'priconfig.xml') /mn (Join-Path $pri 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if ($LASTEXITCODE) { throw 'makepri new failed.' }

# 3. Pack.
$package = Join-Path $work "ZestDrop_${packageVersion}_x64.msix"
& $makeappx pack /d $layout /p $package /o /h SHA256 | Out-Null
if ($LASTEXITCODE) { throw 'makeappx pack failed.' }
$size = [math]::Round((Get-Item -LiteralPath $package).Length / 1MB, 1)
Write-Output "Package: $package ($size MB, version $packageVersion)"
