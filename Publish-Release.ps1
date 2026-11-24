$ErrorActionPreference = 'Stop'

$repo = $PSScriptRoot
$archiveName = 'DesctopNewYear-latest-win-x64.zip'
$releaseMutex = [Threading.Mutex]::new($false, 'Local\DesctopNewYear.PublishRelease')
$locked = $false
$buildRoot = $null
$pending = $null

try {
    try {
        $locked = $releaseMutex.WaitOne()
    }
    catch [Threading.AbandonedMutexException] {
        $locked = $true
    }

    $branch = git -C $repo symbolic-ref --quiet --short HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Check out the branch to publish first.' }
    $revision = git -C $repo rev-parse --verify HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Could not find the commit to publish.' }

    $tempRoot = (Get-Item -LiteralPath ([IO.Path]::GetTempPath())).FullName
    $unique = [Guid]::NewGuid().ToString('N')
    $buildRoot = Join-Path $tempRoot "DesctopNewYear-release-$unique"
    New-Item -ItemType Directory -Path $buildRoot | Out-Null

    $sourceZip = Join-Path $buildRoot 'source.zip'
    $checkout = Join-Path $buildRoot 'source'
    git -C $repo archive --format=zip "--output=$sourceZip" $revision
    if ($LASTEXITCODE -ne 0) { throw 'Could not export the release commit.' }
    Expand-Archive -LiteralPath $sourceZip -DestinationPath $checkout

    $project = Join-Path $checkout 'src/DesctopNewYear/DesctopNewYear.csproj'
    $package = Join-Path $buildRoot 'package'
    dotnet publish $project -c Release -r win-x64 --self-contained false -o $package --nologo `
        "-p:SourceRevisionId=$revision" -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }

    $executable = Get-Item -LiteralPath (Join-Path $package 'DesctopNewYear.exe')
    if (-not $executable.VersionInfo.ProductVersion.EndsWith("+$revision", [StringComparison]::Ordinal)) {
        throw 'The executable does not identify the release commit.'
    }
    $packed = Join-Path $buildRoot $archiveName
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $packed

    git -C $repo push origin "${revision}:refs/heads/$branch"
    if ($LASTEXITCODE -ne 0) { throw 'Git push failed. The last release archive is still in place.' }

    $release = Join-Path $repo 'release'
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    $latest = Join-Path $release $archiveName
    $pending = Join-Path $release ".$unique.tmp"
    Copy-Item -LiteralPath $packed -Destination $pending
    if ([IO.File]::Exists($latest)) {
        [IO.File]::Replace($pending, $latest, [NullString]::Value)
    }
    else {
        [IO.File]::Move($pending, $latest)
    }
    Write-Output "Release updated: $latest ($($executable.VersionInfo.ProductVersion))"
}
finally {
    if ($pending -and (Test-Path -LiteralPath $pending -PathType Leaf)) {
        Remove-Item -LiteralPath $pending -Force -ErrorAction SilentlyContinue
    }
    if ($buildRoot -and (Test-Path -LiteralPath $buildRoot -PathType Container)) {
        $resolvedBuild = Get-Item -LiteralPath $buildRoot
        if ($resolvedBuild.FullName -eq $buildRoot -and $resolvedBuild.Parent.FullName -eq $tempRoot -and
            -not ($resolvedBuild.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Remove-Item -LiteralPath $resolvedBuild.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    if ($locked) { $releaseMutex.ReleaseMutex() }
    $releaseMutex.Dispose()
}
