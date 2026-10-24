param([string]$SourceRoot = (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if ($SourceRoot -eq $projectRoot) { $SourceRoot = Split-Path $projectRoot -Parent }
$catalog = Get-Content -LiteralPath (Join-Path $projectRoot 'assets/art/origami/catalog.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$changed = @()
foreach ($asset in $catalog.assets) {
    foreach ($component in $asset.components.PSObject.Properties.Value) {
        $source = Join-Path $SourceRoot $component.source
        if (-not (Test-Path -LiteralPath $source)) { $changed += [pscustomobject]@{Asset=$asset.id;Source=$component.source;State='missing'}; continue }
        if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant() -ne $component.sourceSha256) {
            $changed += [pscustomobject]@{Asset=$asset.id;Source=$component.source;State='changed'}
        }
    }
}
$changed | ConvertTo-Json -Depth 4
if ($changed.Count -eq 0) { Write-Output 'No source changes.' }
# Deliberately read-only. Publishing follows export, validation, review, and a normal git push.
