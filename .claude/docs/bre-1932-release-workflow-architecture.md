# Release Workflow Architecture - BRE-1932 Migration

## Overview

This document explains the release workflow architecture after the BRE-1932 migration, which moved release orchestration from individual repos to a centralized deploy repo pattern.

## The Problem We Solved

### Original Architecture Issues
- Each repo (dotnet-extensions, etc.) managed its own releases
- Repos had GitHub App credentials to push to themselves
- **Security Risk**: Repos could modify their own protected `release/*` branches
- No centralized control or audit trail for releases
- Inconsistent release patterns across Bitwarden repos

### The Core Security Issue: Release Branch Protection
The critical requirement is: **`release/*` branches must be protected from modification by anyone except the deploy bot.**

Without centralization:
- dotnet-extensions workflows could push to `release/*` branches using GitHub App token
- Developers could accidentally or maliciously modify release branches
- No separation of duties between development and release processes

## New Architecture (After BRE-1932)

### Three-Repo Pattern

```
┌─────────────────────────────────────────────────────────────────┐
│                     dotnet-extensions                            │
│  - Developers work here                                         │
│  - Can only modify main and feature branches                    │
│  - CANNOT touch release/* branches                              │
│  - Triggers deploy repo for release operations                  │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼ (trigger-actions)
┌─────────────────────────────────────────────────────────────────┐
│                        deploy repo                               │
│  - Central orchestration for ALL Bitwarden releases             │
│  - Has GitHub App with cross-repo permissions                   │
│  - ONLY entity that can push to release/* branches              │
│  - Manages release branches, version bumps, GitHub releases     │
└─────────────────────────────────────────────────────────────────┘
                            │
                            ▼ (publishes to NuGet)
┌─────────────────────────────────────────────────────────────────┐
│                        NuGet.org                                 │
│  - Final published packages                                     │
└─────────────────────────────────────────────────────────────────┘
```

## Developer Walkthrough: How to Release

### Scenario 1: Starting a New Release (Creating Release Branch)

**What the developer does:**
1. Go to dotnet-extensions repo → Actions tab
2. Click "Start Release" workflow
3. Select the package to release (e.g., `Bitwarden.Core`)
4. Click "Run workflow"

**What happens behind the scenes:**
1. `start-release.yml` reads the current version from main (e.g., `1.2.0`)
2. Triggers deploy repo via `trigger-actions` with task: `cut-release-branch-dotnet-extensions`
3. Deploy repo's `trigger-actions.yml` receives the trigger and dispatches to `cut-release-branch-dotnet-extensions.yml`
4. Deploy bot creates `release/Bitwarden.Core/1.2` branch (major.minor only)
5. Deploy bot calls back to `dotnet-extensions/version-bump.yml` to bump main to `1.3.0`
6. Developer sees: Release branch created, main is now at next minor version

**Result:**
- ✅ Branch `release/Bitwarden.Core/1.2` exists and is at version `1.2.0`
- ✅ Main branch is now at version `1.3.0` (ready for next development)

---

### Scenario 2: Creating a Prerelease (Alpha/Beta/RC)

**What the developer does:**
1. Switch to the release branch (e.g., `release/Bitwarden.Core/1.2`)
2. Go to Actions tab → "Perform Prerelease" workflow
3. Click "Run workflow"

**What happens behind the scenes:**
1. `prerelease.yml` calls `pack-and-release.yml` with `prerelease: true`
2. Builds the package and uploads artifacts (`.nupkg`, `.snupkg`)
3. Triggers deploy repo via `trigger-actions` with task: `release-dotnet-extensions`
4. Deploy repo's `trigger-actions.yml` dispatches to `release-dotnet-extensions.yml`
5. Deploy bot downloads the artifacts
6. Deploy bot creates GitHub release with tag `Bitwarden.Core_v1.2.0-alpha.1`
7. Deploy bot publishes to NuGet.org (marked as prerelease)
8. Deploy bot calls back to `dotnet-extensions/version-bump.yml` with `type: prerelease`
9. Version on release branch bumps to `1.2.0-alpha.2` (ready for next prerelease)

**Result:**
- ✅ Package published to NuGet as `1.2.0-alpha.1` (prerelease)
- ✅ GitHub release created with tag `Bitwarden.Core_v1.2.0-alpha.1`
- ✅ Release branch now at `1.2.0-alpha.2` (ready for next iteration)

---

### Scenario 3: Creating a GA (General Availability) Release

**What the developer does:**
1. Switch to the release branch (e.g., `release/Bitwarden.Core/1.2`)
2. Go to Actions tab → "Perform Release" workflow
3. Click "Run workflow"

**What happens behind the scenes:**
1. `release.yml` calls `pack-and-release.yml` with `prerelease: false`
2. Builds the package and uploads artifacts
3. Triggers deploy repo via `trigger-actions` with task: `release-dotnet-extensions`
4. Deploy bot downloads the artifacts
5. Deploy bot creates GitHub release with tag `Bitwarden.Core_v1.2.0` (NOT marked as prerelease)
6. Deploy bot publishes to NuGet.org (stable release)
7. Deploy bot calls back to `dotnet-extensions/version-bump.yml` with `type: hotfix`
8. Version on release branch bumps to `1.2.1` (ready for hotfix if needed)

**Result:**
- ✅ Package published to NuGet as `1.2.0` (stable)
- ✅ GitHub release created with tag `Bitwarden.Core_v1.2.0`
- ✅ Release branch now at `1.2.1` (ready for hotfix releases)

---

### Cross-Repo Communication Flow

```
Developer Action (dotnet-extensions)
  │
  ├─ Clicks "Start Release" or "Perform Release/Prerelease"
  │
  ▼
dotnet-extensions workflow runs
  │
  ├─ start-release.yml OR pack-and-release.yml
  ├─ Reads version, builds package, uploads artifacts
  │
  ▼
Uses bitwarden/gh-actions/trigger-actions@main
  │
  ├─ Authenticates with Azure (OIDC)
  ├─ Creates deployment in deploy repo
  ├─ Passes data: {package, version, branch, run_id, prerelease}
  │
  ▼
deploy repo: .github/workflows/trigger-actions.yml
  │
  ├─ Receives deployment event
  ├─ Validates trigger came from dotnet-extensions main branch
  ├─ Dispatches to appropriate workflow based on task:
  │   • cut-release-branch-dotnet-extensions.yml
  │   • release-dotnet-extensions.yml
  │
  ▼
deploy repo workflow executes
  │
  ├─ Job 1: Main work (create branch, download artifacts, publish)
  ├─ Uses deploy bot GitHub App token (has special permissions)
  │
  ▼
dotnet-extensions runs version-bump.yml
  │
  ├─ Called by start-release.yml or pack-and-release.yml
  ├─ Checks out repo (persist-credentials: false - no bot token!)
  ├─ Runs PowerShell script to calculate version changes
  ├─ TODO (BRE-2027): Uploads modified .csproj files as artifacts
  ├─ Triggers deploy repo with task: version-bump-dotnet-extensions
  │
  ▼
deploy repo: version-bump-dotnet-extensions.yml
  │
  ├─ Gets GitHub App token from Azure Key Vault (deploy bot credentials)
  ├─ Checks out dotnet-extensions with bot token
  ├─ Downloads modified .csproj files from version-bump workflow
  ├─ TODO (BRE-2003): Commits and pushes changes
  └─ Only deploy bot can write to release/* branches!
```

## Workflow Details

### 1. Start Release (Cut Release Branch)

**Trigger**: Developer runs `start-release.yml` in dotnet-extensions

**Flow**:
```
dotnet-extensions/start-release.yml (2 jobs):
  Job 1: start-release
  ├─ Read current version from main branch
  └─ Trigger deploy repo → "cut-release-branch-dotnet-extensions"
      │
      ▼ deploy repo workflow:
      ├─ Clone dotnet-extensions (using deploy bot GitHub App token)
      ├─ Create release/{package}/{major.minor} branch from main
      └─ Push branch (only deploy bot can do this!)

  Job 2: bump-version (uses version-bump.yml)
  └─ Bump version on main branch to next GA version
      │
      ▼ version-bump.yml:
      ├─ Checkout (no credentials)
      ├─ Run PowerShell script (modifies .csproj)
      ├─ TODO (BRE-2027): Upload modified files
      └─ Trigger deploy → "version-bump-dotnet-extensions"
          │
          ▼ deploy repo workflow:
          ├─ Get bot credentials
          ├─ Checkout dotnet-extensions
          ├─ Download modified files
          └─ TODO (BRE-2003): Commit and push
```

**Key Files**:
- `dotnet-extensions/.github/workflows/start-release.yml` (triggers deploy, calls version-bump)
- `deploy/.github/workflows/cut-release-branch-dotnet-extensions.yml` (creates branch)
- `dotnet-extensions/.github/workflows/version-bump.yml` (calculates version changes, no credentials)
- `deploy/.github/workflows/version-bump-dotnet-extensions.yml` (commits version changes)

**Why deploy must do this**:
- Only deploy bot has permission to create `release/*` branches
- dotnet-extensions is locked out of `release/*` branches by branch protection rules

### 2. Pack and Release

**Trigger**: Workflow runs automatically when on a `release/*` branch, or manual dispatch

**Flow**:
```
dotnet-extensions/pack-and-release.yml (2 jobs):
  Job 1: release
  ├─ Parse package name from branch (release/{package}/{version})
  ├─ dotnet build --configuration Release
  ├─ Upload artifacts (*.nupkg, *.snupkg)
  └─ Trigger deploy repo → "release-dotnet-extensions"
      │
      ▼ deploy repo workflow:
      ├─ Download artifacts from dotnet-extensions
      ├─ Create GitHub release in dotnet-extensions repo
      └─ Publish to NuGet.org

  Job 2: bump-version (uses version-bump.yml)
  └─ Bump version on the release branch
      ├─ type: "prerelease" if prerelease, "hotfix" if GA
      │
      ▼ version-bump.yml:
      ├─ Checkout (no credentials)
      ├─ Run PowerShell script (modifies .csproj)
      ├─ TODO (BRE-2027): Upload modified files
      └─ Trigger deploy → "version-bump-dotnet-extensions"
          │
          ▼ deploy repo workflow:
          ├─ Get bot credentials
          ├─ Checkout dotnet-extensions
          ├─ Download modified files
          └─ TODO (BRE-2003): Commit and push
```

**Key Files**:
- `dotnet-extensions/.github/workflows/pack-and-release.yml` (packs, triggers deploy, calls version-bump)
- `dotnet-extensions/.github/workflows/prerelease.yml` (calls pack-and-release with prerelease flag)
- `dotnet-extensions/.github/workflows/release.yml` (calls pack-and-release for GA release)
- `deploy/.github/workflows/release-dotnet-extensions.yml` (publishes to NuGet, creates release)
- `dotnet-extensions/.github/workflows/version-bump.yml` (calculates version changes, no credentials)
- `deploy/.github/workflows/version-bump-dotnet-extensions.yml` (commits version changes)

**Why deploy must do this**:
- Version bump happens ON the release branch (only deploy bot can write to it)
- Centralized NuGet publishing with proper credentials
- Consistent release creation across all repos

## Branch Protection Configuration

### dotnet-extensions Branch Protection Rules

**`main` branch**:
- Protected from force push
- Requires PR reviews
- CI checks must pass
- Deploy bot can push (for version bumps)

**`release/*` pattern**:
- **CRITICAL**: Only deploy bot can push
- All other users (including admins) blocked
- No PR bypasses allowed
- Deploy bot uses GitHub App token for authentication

### Why This Matters

Without these protections:
- A compromised workflow could push malicious code to a release branch
- Developers could accidentally modify released versions
- No audit trail for release modifications

With deploy bot exclusivity:
- All release branch operations go through deploy repo (audit trail)
- Separation of duties: dev vs. release
- Reduced attack surface

## What Moved Where

### Stays in dotnet-extensions
- ✅ `version-bump.yml` workflow (but refactored - no credentials, only calculates changes)
- ✅ Build and pack logic
- ✅ Test workflows
- ✅ Main development workflows
- ✅ `start-release.yml` and `pack-and-release.yml` (now call version-bump.yml directly)

### Moved to deploy repo
- ✅ Release branch creation logic
- ✅ GitHub release creation
- ✅ NuGet publishing
- ✅ Release orchestration
- ✅ Version bump commit logic (new: `version-bump-dotnet-extensions.yml`)

### Removed from dotnet-extensions
- ❌ Direct release branch creation (now via deploy)
- ❌ GitHub release creation steps
- ❌ Direct NuGet publishing
- ❌ DevOps dispatch (replaced with deploy trigger)
- ❌ GitHub App credential access in version-bump.yml (security improvement!)

## Security Benefits

### 1. Release Branch Protection
- Only deploy bot can modify `release/*` branches
- Prevents unauthorized changes to release code
- Clear separation between development and release

### 2. Reduced Attack Surface
- dotnet-extensions workflows cannot modify their own release branches
- GitHub App credentials centralized in deploy repo only
- **New improvement**: dotnet-extensions workflows cannot even ACCESS bot credentials
  - version-bump.yml only calculates changes and uploads artifacts
  - Deploy repo downloads artifacts and commits with bot credentials
- Limits blast radius if a workflow is compromised

### 3. Audit Trail
- All release operations go through deploy repo
- Centralized logging and monitoring
- Easy to audit who triggered what release

### 4. Consistency
- All Bitwarden repos follow same release pattern
- Easier to maintain and improve release process
- Single place to add approval gates, notifications, etc.

## Common Operations

### Starting a New Release

1. Navigate to dotnet-extensions repo
2. Actions → Start Release workflow
3. Select package to release
4. Deploy bot creates `release/{package}/{major.minor}` branch
5. Main branch version is bumped automatically

### Creating a Prerelease (alpha, beta, rc)

1. Navigate to `release/*` branch
2. Actions → Perform Prerelease workflow
3. Deploy bot handles build, publish, version bump

### Creating a GA Release

1. Navigate to `release/*` branch
2. Actions → Perform Release workflow
3. Deploy bot handles build, publish, version bump, GitHub release

### Manual Version Bumps

**On main branch** (for major bumps or prerelease label changes):
1. Manually edit version in `.csproj` files
2. Create PR to main
3. Automated version bumps handle minor/patch after release

**On release branch**:
- ❌ Cannot do manually (deploy bot only)
- ✅ Automatic after each release via deploy bot

## Trigger-Actions Pattern

The migration uses `bitwarden/gh-actions/trigger-actions@main` for cross-repo triggers.

### How It Works

```yaml
- name: Trigger deploy repo
  uses: bitwarden/gh-actions/trigger-actions@main
  with:
    azure_subscription_id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
    azure_tenant_id: ${{ secrets.AZURE_TENANT_ID }}
    azure_client_id: ${{ secrets.AZURE_CLIENT_ID }}
    task: cut-release-branch-dotnet-extensions
    data: '{"package": "Bitwarden.Core", "version": "1.2.3"}'
```

### Why Not workflow_dispatch?

- `workflow_dispatch` requires GitHub token with workflow permissions
- `trigger-actions` uses Azure credentials to queue tasks in deploy repo
- More secure: no GitHub token exposure
- Better for cross-org triggers if needed

## Troubleshooting

### "Failed to push to release branch"

**Cause**: Deploy bot doesn't have permission to push to `release/*` branches

**Fix**: Check dotnet-extensions branch protection rules, ensure deploy bot GitHub App is allowed

### "Version bump failed"

**Cause**: Version-bump workflow in dotnet-extensions failed when called by deploy

**Fix**: Check version-bump.yml workflow runs in dotnet-extensions, verify Azure Key Vault access

### "Artifacts not found"

**Cause**: Deploy repo couldn't download artifacts from dotnet-extensions

**Fix**: Check `upload-artifact` and `download-artifact` are using same artifact name, verify run_id passed correctly

### "Cannot create release branch - already exists"

**Cause**: Release branch already exists from previous release

**Fix**: This is expected for patch releases. Deploy bot should push to existing branch, not create new one

## Future Enhancements

### Potential Improvements

1. **Remove version-bump.yml from individual repos**
   - Move all version bumping logic to deploy repo
   - Even more centralized control

2. **Release Approval Gates**
   - Add manual approval step in deploy repo before publishing
   - Notifications to Slack/Teams

3. **Automated Changelog Generation**
   - Deploy bot generates changelog from commits
   - Includes in GitHub release notes

4. **Rollback Capability**
   - Deploy bot can unpublish/delist from NuGet if needed
   - Automated rollback procedures

## Related Documents

- `/Users/bbiete/github/dotnet-extensions/.claude/CLAUDE.md` - Codebase overview
- Branch protection settings: GitHub repo → Settings → Branches
- Deploy repo workflows: `bitwarden/deploy/.github/workflows/`

## Key Takeaway

**The entire migration exists to enable release branch protection.** By giving deploy bot exclusive write access to `release/*` branches, we ensure that release code cannot be modified except through controlled, audited deploy workflows. This is the security foundation that makes the complexity worthwhile.
