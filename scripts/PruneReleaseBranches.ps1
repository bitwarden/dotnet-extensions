#!/usr/bin/env pwsh
<#

.PARAMETER DryRun

#>

[CmdletBinding()]
Param (
    [switch] $DryRun
)

$remoteName = "origin"

# lstrip=4 is saying to strip the 4 sections to the left `refs/remotes/origin/release` so that all we have left
# is the package name and version seperated by a forward slash
$releaseBranches = (git for-each-ref "refs/remotes/$remoteName/release/" --format="%(refname:lstrip=4)")

$groups = $releaseBranches | ForEach-Object {
    $parts = $_.Split("/")

    if ($parts.Count -ne 2) {
        Write-Output "Ignoring invalid release branch name: $_"
        return
    }

    $package = $parts[0]
    $version = $parts[1]

    return @{ Package = $parts[0]; Version = $parts[1] }
} | Group-Object -Property Package

foreach ($entry in $groups) {
    Write-Output "===== $($entry.Name) ====="

    $projectPath = "./extensions/$($entry.Name)/src/$($entry.Name).csproj"

    # It's possible for there to be release branches but not have a project checked into main
    # because you can start the release process from feature branches
    if (Test-Path -Path $projectPath) {
        $numOfMajorVersionsToKeep = (dotnet msbuild --getProperty:MajorVersionRetentionPolicy $projectPath)
        $numOfMinorVersionsToKeep = (dotnet msbuild --getProperty:MinorVersionRetentionPolicy $projectPath)
    } else {
        # The default retention policy is 2 major versions and 3 minor versions, since this project doesn't exist
        # on the main branch we will default them to even fewer versions as you likely won't be maintaining old
        # versions for something that hasn't been elevated to the main branch
        Write-Output "Project for $($entry.Name) not found, falling back to minimal retention policy"
        $numOfMajorVersionsToKeep = 1
        $numOfMinorVersionsToKeep = 1
    }

    $versions = $entry.Group | ForEach-Object { New-Object -TypeName System.Version -ArgumentList $_.Version }

    $versionsGroupedByMajor = $versions | Group-Object -Property Major | Sort-Object -Descending -Property Name

    $majorVersionsToDelete = $versionsGroupedByMajor | Select-Object -Skip $numOfMajorVersionsToKeep

    foreach ($majorVersionGroup in $majorVersionsToDelete) {
        Write-Output "Deleting all releases in major version $($majorVersionGroup.Name) because of $numOfMajorVersionsToKeep major version release policy"
        foreach ($versionToDelete in $majorVersionGroup.Group) {
            $fullBranchName = "release/$($entry.Name)/$($versionToDelete.Major).$($versionToDelete.Minor)"
            Write-Output "Deleting: $fullBranchName"
            if (-Not $DryRun) {
                git push --delete "$remoteName" "$fullBranchName"
            }
        }
    }

    $majorVersionsToKeep = $versionsGroupedByMajor | Select-Object -First $numOfMajorVersionsToKeep

    foreach ($majorVersionGroup in $majorVersionsToKeep) {
        Write-Output "Pruning all but $numOfMinorVersionsToKeep minor versions in major version $($majorVersionGroup.Name)"
        $oldMinorVersions = $majorVersionGroup.Group | Sort-Object -Descending | Select-Object -Skip $numOfMinorVersionsToKeep

        foreach ($versionToDelete in $oldMinorVersions) {
            $fullBranchName = "release/$($entry.Name)/$($versionToDelete.Major).$($versionToDelete.Minor)"
            Write-Output "Deleting: $fullBranchName"
            if (-Not $DryRun) {
                git push --delete "$remoteName" "$fullBranchName"
            }
        }
    }

    Write-Output "===== $($entry.Name) ====="
}
