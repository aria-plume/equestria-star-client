param([string]$GodotPath = '', [switch]$SkipBuild)
$ErrorActionPreference='Stop'
Set-Location -LiteralPath $PSScriptRoot
if (-not $GodotPath) {
    $localEngine=Join-Path $PSScriptRoot '.art-tools/Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64.exe'
    if (Test-Path -LiteralPath $localEngine) { $GodotPath=$localEngine }
    else { throw '请通过 -GodotPath 指定 Godot 4.6.2 .NET 可执行文件。' }
}
if (-not $SkipBuild) {
    & $GodotPath --headless --editor --path . --import
    if ($LASTEXITCODE -ne 0) {throw '资源导入失败'}
    dotnet build --nologo
    if ($LASTEXITCODE -ne 0) {throw 'C#构建失败'}
}
& $GodotPath --path . res://scenes/art/OrigamiGallery.tscn
