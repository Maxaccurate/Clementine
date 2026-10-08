param([string]$Language = 'auto', [string]$Out, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Images)
# Text recognition with the OCR engine that ships with Windows 10/11 (Windows.Media.Ocr). Nothing is downloaded or bundled.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
[void][Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
[void][Windows.Storage.Streams.IRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
[void][Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.BitmapDecoder, Windows.Foundation, ContentType = WindowsRuntime]
[void][Windows.Graphics.Imaging.SoftwareBitmap, Windows.Foundation, ContentType = WindowsRuntime]
[void][Windows.Globalization.Language, Windows.Globalization, ContentType = WindowsRuntime]
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
function Await($operation, [Type]$type) { $task = $asTask.MakeGenericMethod($type).Invoke($null, @($operation)); [void]$task.Wait(-1); $task.Result }
$engine = $null
if ($Language -and $Language -ne 'auto') { $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage([Windows.Globalization.Language]::new($Language)) }
else { $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages() }
if (-not $engine) { [IO.File]::WriteAllText($Out, '{"error":"noengine"}', (New-Object Text.UTF8Encoding($false))); exit 0 }
$pages = @()
foreach ($path in $Images) {
    $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
    $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
    $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
    $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
    $result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
    $lines = @()
    foreach ($line in $result.Lines) {
        $x1 = [double]::MaxValue; $y1 = [double]::MaxValue; $x2 = 0.0; $y2 = 0.0
        foreach ($word in $line.Words) {
            $r = $word.BoundingRect
            if ($r.X -lt $x1) { $x1 = $r.X }; if ($r.Y -lt $y1) { $y1 = $r.Y }
            if ($r.X + $r.Width -gt $x2) { $x2 = $r.X + $r.Width }; if ($r.Y + $r.Height -gt $y2) { $y2 = $r.Y + $r.Height }
        }
        $lines += [pscustomobject]@{ text = $line.Text; x = $x1; y = $y1; w = $x2 - $x1; h = $y2 - $y1 }
    }
    $pages += [pscustomobject]@{ width = $bitmap.PixelWidth; height = $bitmap.PixelHeight; lines = $lines }
    $stream.Dispose()
}
[IO.File]::WriteAllText($Out, (ConvertTo-Json -InputObject @($pages) -Depth 6 -Compress), (New-Object Text.UTF8Encoding($false)))
