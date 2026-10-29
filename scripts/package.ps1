param(
    [ValidatePattern('^v\d+\.\d+\.\d+$')][string]$Version = 'v0.1.0',
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot '.local\releases' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$packageWork = Join-Path $projectRoot ('.local\package-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $packageWork 'payload'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -Test -Portable -OutputDirectory (Join-Path $packageWork 'build')
$build = Join-Path $packageWork 'build'
foreach ($item in @('DeskDots.exe','start.ps1','assets','integration','README.md')) {
    Copy-Item -LiteralPath (Join-Path $build $item) -Destination $payload -Recurse
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'INSTALL.md') -Destination $payload
New-Item -ItemType Directory -Path (Join-Path $payload 'skills') | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'skills\deskdots') -Destination (Join-Path $payload 'skills') -Recurse
Copy-Item -LiteralPath (Join-Path $projectRoot 'skills\deskdots\scripts\install.ps1') -Destination (Join-Path $payload 'install.ps1')
Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\install.cmd') -Destination $payload
$hashes = [ordered]@{}
Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($payload.Length + 1).Replace('\','/')
    $hashes[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
}
@{ version=$Version; repository='jeak01/deskdots'; files=$hashes } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'release.json') -Encoding UTF8
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip = Join-Path $OutputDirectory ('DeskDots-' + $Version + '-windows-x64.zip')
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip -Force
Copy-Item -LiteralPath (Join-Path $payload 'install.ps1') -Destination (Join-Path $OutputDirectory 'install.ps1') -Force
$sums = foreach ($name in @([IO.Path]::GetFileName($zip),'install.ps1')) {
    (Get-FileHash -LiteralPath (Join-Path $OutputDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $name
}
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), [string[]]$sums, [Text.UTF8Encoding]::new($false))
Write-Output ('Release assets: ' + $OutputDirectory)
