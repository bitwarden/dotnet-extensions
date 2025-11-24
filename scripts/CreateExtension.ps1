#!/usr/bin/env pwsh
<#
.SYNOPSIS
Creates a new extension library with standard project structure.

.DESCRIPTION
Generates a new .NET extension library in the extensions/ directory with:
- Main class library project
- Test project with xUnit.v3
- README.md and PACKAGE.md stubs
- Automatic solution integration

.PARAMETER PackageName
The name of the package to create (e.g., Bitwarden.Server.Sdk.Caching)

#>
[CmdletBinding()]
Param (
    [ValidateNotNullOrEmpty()]
    [string] $RepoRoot = "${PSScriptRoot}/..",
    [Parameter(Mandatory=$True)]
    [string] $PackageName
)

$extensionsRoot = Join-Path $RepoRoot extensions $PackageName

$sourceRoot = Join-Path $extensionsRoot src

# Create main project package
dotnet new classlib --output $sourceRoot --name $PackageName

New-Item -Path $sourceRoot -Name README.md -Value "# $PackageName`n"
New-Item -Path $sourceRoot -Name PACKAGE.md -Value "# $PackageName`n"

# Add main project to solution
dotnet sln add $sourceRoot

$testRoot = Join-Path $extensionsRoot tests "$PackageName.Tests"

# Create stub unit test project
dotnet new xunit3 --output $testRoot

# Add test project to solution
dotnet sln add $testRoot

# Add main project reference to test project
dotnet add $testRoot reference $sourceRoot

Write-Output "Package can now be added to 'start-release.yml'"
