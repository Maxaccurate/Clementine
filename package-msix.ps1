# Build the Microsoft Store package (.msix) from the app bundle.
#   ./package-msix.ps1                  build the app, then package it
#   ./package-msix.ps1 -SkipBuild       package the existing outputs/ZestDrop
# Upload outputs/msix/ZestDrop_<version>_x64.msix in Partner Center; the Store signs it.
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$app = Join-Path $root 'outputs/ZestDrop'
$work = Join-Path $root 'outputs/msix'

# The only deletions in this script. The target is resolved, printed and checked to be exactly
# <repo>\outputs\msix\<layout|pri> before anything is removed, so a wrong variable can never wipe a parent folder.
function Remove-WorkFolder([string]$name) {
    if ($name -notin 'layout', 'pri') { throw "Refusing to delete '$name'." }
    $expected = [System.IO.Path]::GetFullPath((Join-Path (Join-Path (Join-Path $root 'outputs') 'msix') $name))
    $parent = [System.IO.Path]::GetFullPath((Join-Path (Join-Path $root 'outputs') 'msix'))
    if ((Split-Path -Parent $expected) -ne $parent.TrimEnd('\') -or (Split-Path -Leaf $expected) -ne $name) { throw "Unexpected delete target: $expected" }
    if (Test-Path -LiteralPath $expected) {
        Write-Host "Removing old $expected"
        Remove-Item -LiteralPath $expected -Recurse -Force
    }
    return $expected
}

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
$layout = Remove-WorkFolder 'layout'
New-Item -ItemType Directory -Force -Path $layout | Out-Null
# default-docx-template holds an unzipped docx whose "[Content_Types].xml" collides with the package's own reserved
# file of that name (makeappx error 0x8007007b); python-docx loads templates/default.docx instead and never reads it.
$appAssets = Join-Path $app 'assets'
# /XD with the full path skips only the app's top-level assets folder (a bare name would skip every folder with it).
robocopy $app $layout /E /NFL /NDL /NJH /NJS /NP /XD source validation logs __pycache__ default-docx-template $appAssets /XF *.pdb last-cli-result.json last-cli-result.json.progress events.jsonl | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the app failed (robocopy exit $LASTEXITCODE)." }
# The app bundle has its own "assets" folder (fonts), and Windows ignores letter case in folder names, so it is the same
# folder as the package's "Assets" (the logos the manifest points to). Merge both into one folder spelled exactly
# "Assets", as the manifest does; copying the logo folder into a lowercase "assets" would leave the paths mismatched
# (or nest it as Assets\Assets), and the Store install would find no logos.
$logoTarget = Join-Path $layout 'Assets'
New-Item -ItemType Directory -Force -Path $logoTarget | Out-Null
if (Test-Path -LiteralPath $appAssets) {
    robocopy $appAssets $logoTarget /E /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the app's assets failed (robocopy exit $LASTEXITCODE)." }
}
Get-ChildItem -LiteralPath (Join-Path $root 'packaging/Assets') -File | Copy-Item -Destination $logoTarget -Force
$links = Get-ChildItem -LiteralPath $layout -Recurse -Force -Attributes ReparsePoint -ErrorAction SilentlyContinue
if ($links) { throw "The layout still contains links: $($links.FullName -join ', ')" }
(Get-Content -LiteralPath (Join-Path $root 'packaging/AppxManifest.xml') -Raw -Encoding UTF8).Replace('__VERSION__', $packageVersion) |
    Set-Content -LiteralPath (Join-Path $layout 'AppxManifest.xml') -Encoding UTF8

# 2. Resource index for the scaled logos. It is built from the assets alone: indexing the whole app would
#    turn .NET's per-language folders (de, zh-Hans, ...) into languages the package claims to support.
$pri = Remove-WorkFolder 'pri'
New-Item -ItemType Directory -Force -Path $pri | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'packaging/Assets') -Destination (Join-Path $pri 'Assets') -Recurse
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
