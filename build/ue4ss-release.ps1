#requires -Version 5.1
function Get-PbzUe4ssRelease([object]$Sources, [int]$TimeoutSeconds = 10) {
    $taskApiToken = $env:GITHUB_TOKEN
    if (-not $taskApiToken -and (Get-Command gh -ErrorAction SilentlyContinue)) {
        $taskPreviousExitCode = $global:LASTEXITCODE
        try {
            $taskCliToken = & gh auth token 2>$null
            if ($LASTEXITCODE -eq 0) { $taskApiToken = [string]$taskCliToken }
        }
        catch { $taskApiToken = $null }
        finally { $global:LASTEXITCODE = $taskPreviousExitCode }
    }
    foreach ($taskApi in $Sources.Api) {
        try {
            $taskApiHeaders = @{ 'User-Agent' = 'PBZKeyForge-build' }
            if ($taskApi.StartsWith('https://api.github.com/') -and $taskApiToken) {
                $taskApiHeaders.Authorization = 'Bearer ' + $taskApiToken
            }
            $taskReleases = Invoke-RestMethod -Uri $taskApi -Headers $taskApiHeaders -TimeoutSec $TimeoutSeconds
            foreach ($taskRelease in @($taskReleases | Where-Object { -not $_.draft -and -not $_.prerelease } | Sort-Object published_at -Descending)) {
                $taskAsset = $taskRelease.assets | Where-Object { $_.name -match '^phantombladezero_ue4ss_.*\.zip$' } | Select-Object -First 1
                if (-not $taskAsset) { continue }
                return [pscustomobject]@{
                    Metadata = [pscustomobject][ordered]@{
                        Provider = 'KintaroModding'; Version = [string]$taskRelease.tag_name
                        Source = [string]$taskRelease.html_url; Asset = [string]$taskAsset.name
                        Published = ([DateTimeOffset]$taskRelease.published_at).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
                        Download = [string]$taskAsset.browser_download_url; AssetId = [string]$taskAsset.id
                        Updated = ([DateTimeOffset]$taskAsset.updated_at).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
                    }
                    Notes = [string]$taskRelease.body
                }
            }
            throw 'No stable Kintaro Phantom Blade Zero runtime asset was found.'
        }
        catch { Write-Warning ("Release query failed via $(([Uri]$taskApi).Host): " + $_.Exception.Message) }
    }
    throw 'Unable to check the latest Kintaro PBZ release. Check network access and retry.'
}
