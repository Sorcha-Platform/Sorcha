# GitHub Actions Workflow Requirements

This document defines the rules, standards, and guidelines for all GitHub Actions workflows in the Sorcha project. All future workflow changes must adhere to these requirements.

## Core Principles

### 1. Efficiency First
- Workflows must only run when necessary
- Tests must be scoped to changed components and their dependents
- Artifacts must only be published when their source changes
- No redundant builds or deployments

### 2. Architectural Awareness
- All workflows must understand the project's architectural hierarchy
- Changes detection must follow dependency rules
- Test selection must be based on component relationships

### 3. Safety and Quality
- All tests must pass before publishing artifacts
- All artifacts must be published successfully before deployment
- No deployments without successful validation

### 4. Clear Communication
- Workflow outputs must be comprehensive and clear
- Summaries must explain what was tested/published and why
- Failures must provide actionable debugging information

## Workflow Architecture

### Current Workflow Structure

```
.github/workflows/
├── pr-validation.yml       # PRIMARY: PR testing (dependency-aware)
├── main-ci-cd.yml          # PRIMARY: Post-merge CI/CD (selective publishing)
├── codeql.yml              # SECURITY: CodeQL security scanning
└── release.yml             # RELEASES: Tag-based releases
```

### Workflow Responsibilities

#### PR Validation (`pr-validation.yml`)
**Trigger:** Pull requests to `main` or `master`

**Responsibilities:**
- Detect which components changed
- Run tests for changed components
- Run tests for direct dependents
- Block merge if critical tests fail

**Must NOT:**
- Publish any artifacts
- Deploy to any environment
- Run full solution build
- Test unchanged components (unless they depend on changes)

#### Main CI/CD (`main-ci-cd.yml`)
**Trigger:** Push to `main` or `master` (after PR merge)

**Responsibilities:**
- Run full solution build and test
- Detect changed components
- Publish NuGet packages for changed libraries
- Build and push container images for changed services
- Deploy to Azure after successful publishing

**Must NOT:**
- Skip tests before publishing
- Deploy without successful artifact publication
- Publish artifacts for unchanged components (unless forced)

#### CodeQL Security Scanning (`codeql.yml`)
**Trigger:** Push to main/develop, PRs to main, scheduled weekly

**Responsibilities:**
- Perform security analysis on C# and JavaScript code
- Report security vulnerabilities
- Independent of other workflows

**Must NOT:**
- Block CI/CD workflows
- Publish artifacts
- Deploy anything

#### Release (`release.yml`)
**Trigger:** Git tags matching `v*.*.*`

**Responsibilities:**
- Create GitHub releases
- Build and tag versioned artifacts
- Optionally publish to NuGet
- Push Docker images to GHCR with version tags

**Must NOT:**
- Run on regular pushes
- Deploy to Azure (handled by main-ci-cd)

## Architectural Hierarchy Rules

### Component Layers

```
Apps (Level 4)
  └── Depends on: Services, Core, Common

Services (Level 3)
  └── Depends on: Core, Common

Core (Level 2)
  └── Depends on: Common

Common (Level 1)
  └── Depends on: External packages only
```

### Current Component Classification

**Common (Level 1):**
- `Sorcha.Cryptography`
- `Sorcha.TransactionHandler`
- `Sorcha.Blueprint.Models`
- `Sorcha.ServiceDefaults`

**Core (Level 2):**
- `Sorcha.Blueprint.Fluent` (depends on: Blueprint.Models)
- `Sorcha.Blueprint.Schemas.Client`
- `Sorcha.Blueprint.Engine`

**Services (Level 3):**
- `Sorcha.Blueprint.Service` (depends on: Blueprint.Models)
- `Sorcha.ApiGateway`
- `Sorcha.Peer.Service`

**Apps (Level 4):**
- `Sorcha.AppHost`
- `Sorcha.Blueprint.Designer.Client` (depends on: Blueprint.Fluent, Blueprint.Models, Blueprint.Schemas.Client)

### Dependency Testing Rules

When a component changes, tests must run for:

1. **The changed component itself**
2. **All direct dependents** (components that reference it)
3. **Critical integration tests** (if the component is in Common or Core)

**Examples:**

| Changed Component | Required Tests |
|-------------------|----------------|
| Sorcha.Cryptography | Cryptography.Tests + Peer.Service.Tests + Integration.Tests |
| Sorcha.Blueprint.Models | Models.Tests + Fluent.Tests + Service.Tests (any service using it) |
| Sorcha.Blueprint.Fluent | Fluent.Tests + Designer.Client tests + UI.E2E.Tests |
| Sorcha.ApiGateway | Gateway.Integration.Tests only |
| Sorcha.Blueprint.Designer.Client | UI.E2E.Tests + Integration.Tests only |

### Adding New Components

When adding a new component, update workflows:

1. **Identify the architectural level** (Common, Core, Services, or Apps)
2. **Add to change detection** in both `pr-validation.yml` and `main-ci-cd.yml`
3. **Add test job** if it's a publishable component
4. **Update dependency rules** to include it in dependent tests
5. **Document in README.md** under Architectural Hierarchy

## Publishing Rules

### NuGet Packages

**Eligible Components:**
- Only components in the `Common` or `Core` layers
- Must have a `.csproj` with `<IsPackable>true</IsPackable>` (or no setting, defaults to true)
- Must have proper package metadata (Version, Authors, Description)
- Must target net10.0

**Publishing Conditions:**
- Component source code has changed
- All tests pass
- Main CI/CD workflow only (not PR validation)
- Use `--skip-duplicate` to avoid errors on unchanged versions

**Required Configuration:**
```yaml
- name: Publish to NuGet.org
  run: |
    dotnet nuget push ./nupkg/*.nupkg \
      --api-key ${{ secrets.NUGET_API_KEY }} \
      --source https://api.nuget.org/v3/index.json \
      --skip-duplicate
```

### Container Images

**Eligible Components:**
- Services and Apps with Dockerfiles
- Must be production-ready services

**Publishing Conditions:**
- Service source code has changed, OR
- Dependencies have changed (e.g., Common/Core updates that affect service)
- All tests pass
- Images tagged with both `latest` and commit SHA

**Required Tags:**
```yaml
docker build -t $REGISTRY/service:${{ github.sha }} \
             -t $REGISTRY/service:latest \
             -f path/to/Dockerfile .
```

### Artifact Retention

- **Test Results:** 30 days
- **NuGet Packages:** 90 days
- **Coverage Reports:** 30 days
- **Container Images:** Managed by registry retention policies

## Environment and Secrets

### Required Secrets

| Secret Name | Purpose | Used By | Required |
|-------------|---------|---------|----------|
| `NUGET_API_KEY` | NuGet.org publishing | main-ci-cd.yml | Yes |
| `AZURE_CREDENTIALS` | Azure deployment | main-ci-cd.yml | Yes |
| `GITHUB_TOKEN` | Built-in, for releases | release.yml | Auto-provided |

### GitHub Environments

| Environment | Protection Rules | Used By |
|-------------|------------------|---------|
| `nuget-production` | Manual approval (optional) | main-ci-cd.yml publishing jobs |
| `azure-production` | Manual approval (optional) | main-ci-cd.yml deployment job |

### Environment Variables

Standard environment variables used across workflows:

```yaml
env:
  DOTNET_VERSION_10: '10.0.x'
  AZURE_RESOURCE_GROUP: sorcha
  AZURE_LOCATION: uksouth
  CONTAINER_REGISTRY: sorchaacr
```

**Rules:**
- Always use variables instead of hardcoded values
- Update in ALL workflows when changed
- Document any new variables in this file

## Workflow Standards

### Naming Conventions

**Workflow Names:**
- Use descriptive names: `PR Validation - Dependency-Aware Testing`
- Indicate purpose clearly
- Mark legacy workflows: `Name (Legacy - Manual Only)`

**Job Names:**
- Use action-oriented names: `Test Cryptography + Dependents`
- Indicate what will be built/tested/published
- Be specific about scope

**Step Names:**
- Start with action verb: `Build`, `Test`, `Publish`, `Deploy`
- Be concise but descriptive
- Use consistent terminology

### Code Quality

**All workflows must:**
- Use pinned action versions (e.g., `@v4`, not `@main`)
- Include error handling (`continue-on-error` where appropriate)
- Use `if` conditions to skip unnecessary jobs
- Upload artifacts for debugging
- Generate workflow summaries

**Example:**
```yaml
- name: Test Component
  run: dotnet test tests/Component.Tests --logger "trx"

- name: Upload test results
  uses: actions/upload-artifact@v4
  if: always()
  with:
    name: component-test-results
    path: '**/*.trx'
```

### Performance Optimization

**Required optimizations:**
- Use `needs` to define job dependencies
- Run independent jobs in parallel
- Skip jobs when not needed using `if` conditions
- Use caching for dependencies (where applicable)
- Minimize Docker image layers

**Example:**
```yaml
jobs:
  test-cryptography:
    if: needs.detect-changes.outputs.cryptography == 'true'
    # Only runs if cryptography changed

  publish-cryptography:
    needs: [detect-changes, full-build-and-test]
    if: |
      needs.full-build-and-test.result == 'success' &&
      needs.detect-changes.outputs.cryptography == 'true'
    # Only publishes if tests passed AND component changed
```

## Change Detection

### File Path Patterns

Change detection must use consistent patterns:

```yaml
# Common layer
src/Common/Sorcha.Cryptography
src/Common/Sorcha.TransactionHandler
src/Common/Sorcha.Blueprint.Models
src/Common/Sorcha.ServiceDefaults

# Core layer
src/Core/Sorcha.Blueprint.Fluent
src/Core/Sorcha.Blueprint.Schemas.Client
src/Core/Sorcha.Blueprint.Engine

# Services layer
src/Services/Sorcha.Blueprint.Service
src/Services/Sorcha.ApiGateway
src/Services/Sorcha.Peer.Service

# Apps layer
src/Apps/Sorcha.AppHost
src/Apps/UI/Sorcha.Blueprint.Designer.Client
```

### Detection Implementation

Use Git diff for change detection:

```bash
# For PRs - compare against base branch
CHANGED_FILES=$(git diff --name-only origin/${{ github.base_ref }}...HEAD)

# For pushes - compare commits
CHANGED_FILES=$(git diff --name-only ${{ github.event.before }} ${{ github.sha }})

# Check for component changes
echo "cryptography=$(echo "$CHANGED_FILES" | grep -q "src/Common/Sorcha.Cryptography" && echo "true" || echo "false")" >> $GITHUB_OUTPUT
```

**Important:** Always test change detection logic thoroughly!

## Testing Requirements

### Test Execution Rules

**Required for PR Validation:**
- Unit tests for changed components: MUST pass
- Integration tests for critical dependents: MUST pass
- Service tests: CAN fail (use `|| true`)
- E2E tests: CAN fail (use `|| true`)

**Required for Main CI/CD:**
- ALL unit tests: MUST pass
- Integration tests: MUST pass (or workflow fails)
- E2E tests: SHOULD pass (can be optional)

### Test Output

All test runs must:
- Generate TRX files for test results
- Collect code coverage (XPlat Code Coverage)
- Upload results as artifacts
- Report summary in GitHub Actions summary

```yaml
- name: Run tests
  run: |
    dotnet test path/to/Tests.csproj \
      --configuration Release \
      --logger "trx;LogFileName=test-results.trx" \
      --collect:"XPlat Code Coverage"

- name: Upload test results
  uses: actions/upload-artifact@v4
  if: always()
  with:
    name: test-results
    path: '**/*.trx'
```

## Deployment Rules

### Azure Deployment

**Trigger Conditions:**
- Only from `main-ci-cd.yml`
- Only after successful test run
- Only after successful artifact publishing
- Can be manually triggered for emergency deployments

**Deployment Steps:**
1. Deploy base infrastructure (if needed)
2. Build and push container images (only changed ones)
3. Deploy Container Apps with new images
4. Report deployment URLs

**Safety Requirements:**
- Infrastructure deployment must be idempotent
- Use `continue-on-error: true` for infrastructure that may already exist
- Always verify image exists before deploying
- Report all deployment URLs in summary

### Rollback Procedures

If deployment fails:
1. Check workflow logs for specific error
2. Verify Azure credentials are valid
3. Manually deploy using legacy `azure-deploy.yml` (workflow_dispatch)
4. If needed, push previous image tags to ACR and redeploy

## Maintenance

### Regular Updates

**Monthly:**
- Review and update action versions
- Check for security advisories
- Review workflow run times and costs

**Quarterly:**
- Review test coverage and skip patterns
- Validate change detection accuracy
- Update documentation for any architecture changes

**As Needed:**
- Add new components to workflows
- Adjust testing rules based on flakiness
- Optimize slow jobs

### Workflow Modifications

**Before modifying a workflow:**
1. Review this document
2. Understand the architectural hierarchy
3. Test changes in a feature branch
4. Verify change detection works correctly
5. Update documentation

**When adding new workflow files:**
1. Document purpose in `README.md`
2. Add to the workflow list in this document
3. Ensure it doesn't conflict with existing workflows
4. Use consistent naming and structure

## Prohibited Practices

### DO NOT:

❌ Run full test suite on every PR (use dependency-aware testing)
❌ Publish artifacts from PR workflows
❌ Deploy to production from PRs
❌ Skip tests before publishing
❌ Hardcode secrets or credentials
❌ Use `latest` tags without also using commit SHAs
❌ Create workflows that duplicate existing functionality
❌ Modify workflows without updating documentation
❌ Use outdated or unversioned actions
❌ Ignore workflow failures without investigation

### ALWAYS:

✅ Use change detection to minimize work
✅ Run tests before publishing
✅ Publish with version tags and commit SHAs
✅ Upload test results and artifacts
✅ Generate workflow summaries
✅ Handle errors gracefully
✅ Document workflow changes
✅ Test workflow changes before merging
✅ Follow architectural hierarchy rules
✅ Keep workflows maintainable and readable

## Troubleshooting Guide

### Common Issues

**Issue: Change detection not working**
- Check file paths in grep patterns
- Verify git diff command is correct
- Test with `git diff --name-only` locally
- Ensure fetch-depth is 0 for full history

**Issue: Tests failing in CI but passing locally**
- Check .NET version differences
- Verify all dependencies are restored
- Check for environment-specific configuration
- Review test output artifacts

**Issue: Publishing skipped unexpectedly**
- Check detect-changes job output
- Verify component path matches exactly
- Review job `if` conditions
- Check for previous failed runs blocking job

**Issue: Deployment fails**
- Verify Azure credentials are valid and not expired
- Ensure infrastructure exists
- Check ACR login succeeds
- Verify Dockerfile paths are correct
- Check Azure subscription has capacity

### Getting Help

1. Review workflow logs in GitHub Actions
2. Check this requirements document
3. Review component dependencies
4. Verify secrets and environment configuration
5. Test locally with `act` (if possible)
6. Open an issue with:
   - Workflow run link
   - Error messages
   - What was expected vs. actual behavior

## Version History

| Version | Date | Changes |
|---------|------|---------|
| 1.0 | 2025-01-13 | Initial workflow requirements established |

## Approval Process

Changes to this document require:
- Review by at least one maintainer
- Verification that existing workflows comply
- Update to all affected workflows
- Update to README.md if user-facing changes

---

**Last Updated:** 2025-01-13
**Next Review:** 2025-04-13 (Quarterly)
