#!/usr/bin/env pwsh
<#

.PARAMETER PackageName

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
