param([string]$Version)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
$previousAppData = $env:APPDATA
try {
    # Keep NuGet configuration local, including under restricted build accounts.
    $env:APPDATA = Join-Path $PSScriptRoot '.build-profile'
    dotnet restore --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    $versionArgs = @()
    if ($Version) {
        if ($Version -notmatch '^\d+\.\d+(\.\d+)?$') { throw 'Version must be major.minor or major.minor.patch.' }
        $parts = $Version.Split('.')
        $assemblyVersion = ($parts + (@('0') * (4 - $parts.Count))) -join '.'
        $versionArgs = @("-p:Version=$Version", "-p:InformationalVersion=$Version", "-p:AssemblyVersion=$assemblyVersion", "-p:FileVersion=$assemblyVersion")
    }
    dotnet publish -c Release -o publish --no-restore @versionArgs
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
finally {
    $env:APPDATA = $previousAppData
    Pop-Location
}
