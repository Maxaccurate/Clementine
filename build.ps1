param([switch]$SkipRuntimeCheck)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskOutput = Join-Path $taskRoot 'outputs/ZestDrop'

if (-not $SkipRuntimeCheck) {
    foreach ($taskFile in @('runtime/python/python.exe', 'runtime/ffmpeg/ffmpeg.exe', 'runtime/ffmpeg/ffprobe.exe', 'runtime/7zip/7z.exe', 'runtime/7zip/7z.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $taskOutput $taskFile))) {
            throw "Missing $taskFile. Run python work/prepare-runtime.py first, or copy runtime from a complete build."
        }
    }
}

dotnet publish (Join-Path $taskRoot 'work/ZestDrop/ZestDrop.csproj') -c Release -r win-x64 --self-contained true -o $taskOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'work/ZestDrop/backend') -Destination $taskOutput -Recurse -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/usage.md') -Destination (Join-Path $taskOutput '使用说明.md') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/dependencies.json') -Destination (Join-Path $taskOutput '依赖清单.json') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination (Join-Path $taskOutput 'LICENSE') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'docs/validation') -Destination $taskOutput -Recurse -Force
$taskSource = Join-Path $taskOutput 'source'
New-Item -ItemType Directory -Force -Path $taskSource | Out-Null
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'work/ZestDrop') -File | Where-Object {$_.Extension -in @('.cs', '.csproj', '.manifest')} | Copy-Item -Destination $taskSource -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'work/ZestDrop/backend') -Destination $taskSource -Recurse -Force
Write-Output "Built: $taskOutput/ZestDrop.exe"
