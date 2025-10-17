# Release process

## Start release

The release process for a package starts with the `start-release.yml` workflow where you choose
which package you want to start the release for. This will automatically create a release branch for
the package in the following format: `release/[Package]/X.Y` with only the major and minor version
of the packages current version. It will then create a PR bumping the minor version of the package
on the branch that the workflow was ran from. This workflow is expected to be ran from `main` but
can be ran from any branch in order to test it. The resulting version bump PR is expected to be
merged quickly so that `main` begins to point towards vNext.

## Prerelease

Once you have started a release, you can optionally do a prerelease, you do this by running the
`prerelease.yml` workflow. This workflow should be ran from a branch created via the
[Start release](#start-release) process. You select the release branch for the package and version
you want to do a prerelease for. A prerelease will publish the package to NuGet with the
`PreReleaseVersionLabel` and `PreReleaseVersionIteration` where if your label is `beta` and your
iteration is `2` you'd get a version like: `1.1.0-beta.1`. Following the publish it will create
a version bump PR into the release branch bumping the `PreReleaseVersionIteration`.

## Release

Once you have started a release you can do a full release by running the `release.yml`. This does
non-prerelease publish to NuGet and then creates a version bump PR to that release branch bumping
the patch version.

## Manual version bumps

There are two main type of version bumps that are not done automatically in this process major
versions and prerelease label. Major version bumps should be relatively in-frequent and should be
pre-planned. For this reason it's fine to have a manual PR do the bump. The idea is similar with
prerelease labels, it's generally a manual decision when you feel like you have enough stability
to move from an `alpha` to a `beta` onto an `rc`. It's also not expected that those kinds of
transitions will happen. It's likely enough even for a very important package to have a couple of
beta versions before a full release. A lot of packages can likely skip the prerelease process
altogether.

## Backporting

One giant benefit of this release process is the easy ability to support multiple versions of your
package at once. As long as you keep the `release/*` branch around you can quickly backport a fix
to that branch and do another release.

## New Packages

In order to start packaging and releasing a new package you need to first follow the folder format.
You package should get a folder in `extensions` corresponding to the full package ID. Then an
additional `src` package should be made inside. The source folder should contain the `csproj` file
for you package.

The following elements should be added to a `PropertyGroup` in your project file.

```xml
<VersionPrefix>1.0.0</VersionPrefix>
<PreReleaseVersionLabel>beta</PreReleaseVersionLabel>
<PreReleaseVersionIteration>1</PreReleaseVersionIteration>
<VersionSuffix Condition="'$(VersionSuffix)' == '' AND '$(IsPreRelease)' == 'true'">$(PreReleaseVersionLabel).$(PreReleaseVersionIteration)</VersionSuffix>
```

The values inside each of the elements can be whatever you want to start them off as. Then, add your
package ID to the workflow input in `start-release.yml`.
