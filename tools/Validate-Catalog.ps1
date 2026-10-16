<#
 Checks that every winget id in Assets\catalog.json really exists.
 Run it in PowerShell (normal, not admin) before building / after editing the catalog:
     .\tools\Validate-Catalog.ps1
#>
$path = Join-Path $PSScriptRoot '..\Assets\catalog.json'
$catalog = Get-Content $path -Raw | ConvertFrom-Json
$bad = 0

foreach ($cat in $catalog.categories | Where-Object { $_.kind -eq 'app' }) {
    foreach ($item in $cat.items) {
        if (-not $item.id) { Write-Host ("[LINK] {0,-45} (download page, no winget id)" -f $item.name) -ForegroundColor Cyan; continue }
        $src = if ($item.source) { $item.source } else { 'winget' }
        winget show --id $item.id --exact --source $src --accept-source-agreements --disable-interactivity *> $null
        if ($LASTEXITCODE -eq 0) {
            Write-Host ("[ OK ] {0,-45} {1}" -f $item.name, $item.id) -ForegroundColor Green
        } else {
            Write-Host ("[MISS] {0,-45} {1}   -> try: winget search ""{2}""" -f $item.name, $item.id, $item.name) -ForegroundColor Red
            $bad++
        }
    }
}
if ($bad -eq 0) { Write-Host "`nAll ids found." -ForegroundColor Green } else { Write-Host "`n$bad id(s) need fixing in Assets\catalog.json" -ForegroundColor Yellow }
