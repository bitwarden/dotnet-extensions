#!/usr/bin/env pwsh
<#

.PARAMETER RepoRoot

#>

[CmdletBinding()]
Param (
    [ValidateNotNullOrEmpty()]
    [string] $RepoRoot = "${PSScriptRoot}/..",
    [Parameter(Mandatory=$True)]
    [string] $PackageName,
    [Parameter(Mandatory=$True)]
    [string] $BumpType
)

$csprojPath = Join-Path $RepoRoot extensions $PackageName src "${PackageName}.csproj"

$csproj = New-Object xml
$csproj.PreserveWhitespace = $true
$csproj.Load($csprojPath)
$propertyGroup = ($csproj | Select-Xml "Project/PropertyGroup/VersionPrefix").Node.ParentNode
$versionPrefix = $propertyGroup.VersionPrefix
$currentVersion = New-Object -TypeName System.Version -ArgumentList $versionPrefix

$showPreRelease = $false

Switch ($BumpType)
{
    "ga" {
        # Bump the minor version and reset the build and prerelease iteration
        $newVersion = New-Object -TypeName System.Version -ArgumentList ($currentVersion.Major, ($currentVersion.Minor+1), 0)
        $newPreReleaseIteration = 1
    }
    "prerelease" {
        # Keep the same version and bump prerelease iteration
        $newVersion = $currentVersion
        $newPreReleaseIteration = ([int]$propertyGroup.PreReleaseVersionIteration + 1)
        $showPreRelease = $true
    }
    "hotfix" {
        # Bump just the build number and reset the prerelease iteration
        $newVersion = New-Object -TypeName System.Version -ArgumentList ($currentVersion.Major, $currentVersion.Minor, ($currentVersion.Build + 1))
        $newPreReleaseIteration = 1
    }
}

if ($showPreRelease)
{
    $label = $propertyGroup.PreReleaseVersionLabel;
    Write-Output "$newVersion-$label.$newPreReleaseIteration"
}
else
{
    Write-Output "$newVersion"
}

$propertyGroup.VersionPrefix = $newVersion.ToString()
$propertyGroup.PreReleaseVersionIteration = $newPreReleaseIteration
$csproj.Save($csprojPath)
