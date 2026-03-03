# NuGet.org Publishing Setup Guide

This guide walks you through setting up automatic NuGet package publishing for Sorcha.Cryptography.

## Prerequisites

- A NuGet.org account
- Admin access to the GitHub repository
- The library must build and pass all tests

## Step 1: Create a NuGet.org Account

1. Go to [https://www.nuget.org](https://www.nuget.org)
2. Click **Sign in** or **Register** in the top right
3. Create an account or sign in with an existing Microsoft account

## Step 2: Generate a NuGet API Key

1. Sign in to [NuGet.org](https://www.nuget.org)
2. Click your username in the top right and select **API Keys**
3. Click **Create** to generate a new API key
4. Configure the API key:
   - **Key Name**: `Sorcha.Cryptography GitHub Actions` (or any descriptive name)
   - **Expiration**: Choose an appropriate expiration (recommended: 365 days)
   - **Select Scopes**: Choose **Push** and **Push new packages and package versions**
   - **Select Packages**:
     - Choose **Glob Pattern**
     - Enter: `Sorcha.Cryptography`
   - Click **Create**

5. **IMPORTANT**: Copy the API key immediately and store it securely. You won't be able to see it again!

## Step 3: Add API Key to GitHub Secrets

1. Go to your GitHub repository: `https://github.com/sorcha-platform/sorcha`
2. Click **Settings** (you need admin access)
3. In the left sidebar, expand **Secrets and variables** and click **Actions**
4. Click **New repository secret**
5. Configure the secret:
   - **Name**: `NUGET_API_KEY`
   - **Secret**: Paste the API key you copied from NuGet.org
6. Click **Add secret**

## Step 4: Create GitHub Environment (Optional but Recommended)

This adds an extra layer of protection by requiring approval before publishing.

1. In your repository settings, click **Environments** in the left sidebar
2. Click **New environment**
3. Name it: `nuget-production`
4. Configure protection rules (optional):
   - **Required reviewers**: Add team members who should approve releases
   - **Wait timer**: Add a delay before deployment (e.g., 5 minutes)
   - **Deployment branches**: Restrict to `main` or `master` branch only
5. Click **Save protection rules**

## Step 5: Verify the Workflow File

The workflow file has been created at [.github/workflows/cryptography-nuget.yml](../.github/workflows/cryptography-nuget.yml).

It will trigger when:
- Code is pushed to `main` or `master` branch
- Changes are made to files in `src/Common/Sorcha.Cryptography/` or `tests/Sorcha.Cryptography.Tests/`
- Pull requests are created (build and test only, no publish)
- Manually triggered via GitHub Actions UI

## Step 6: Update Package Version

Before publishing, ensure the version in [src/Common/Sorcha.Cryptography/Sorcha.Cryptography.csproj](../src/Common/Sorcha.Cryptography/Sorcha.Cryptography.csproj) is correct:

```xml
<Version>2.0.0</Version>
```

Follow [Semantic Versioning](https://semver.org/):
- **Major** (x.0.0): Breaking changes
- **Minor** (0.x.0): New features, backwards compatible
- **Patch** (0.0.x): Bug fixes, backwards compatible

## Step 7: Test the Pipeline

### Test Build and Test (without publishing)

1. Create a new branch:
   ```bash
   git checkout -b test-nuget-pipeline
   ```

2. Make a small change to the Sorcha.Cryptography project (e.g., update a comment)

3. Commit and push:
   ```bash
   git add .
   git commit -m "test: Verify NuGet pipeline configuration"
   git push origin test-nuget-pipeline
   ```

4. Create a Pull Request on GitHub

5. The workflow should run build and test jobs (but NOT publish)

### Test Publishing (to NuGet.org)

**WARNING**: This will publish a real package to NuGet.org!

1. Ensure the version number is what you want to publish
2. Merge the PR to `main` or `master` branch
3. The workflow will run and publish to NuGet.org if tests pass
4. Check the Actions tab in GitHub to monitor progress
5. After 5-10 minutes, verify the package appears at:
   - https://www.nuget.org/packages/Sorcha.Cryptography

## Step 8: Install and Use the Package

Once published, users can install it:

```bash
dotnet add package Sorcha.Cryptography
```

Or in a `.csproj` file:

```xml
<PackageReference Include="Sorcha.Cryptography" Version="2.0.0" />
```

## Workflow Features

The pipeline includes:

### 🔨 Build & Test
- Builds for .NET 10.0
- Runs all unit tests
- Generates code coverage reports
- Adds coverage summary to pull requests

### 📦 Packaging
- Creates NuGet package with proper metadata
- Includes XML documentation
- Generates symbol package (.snupkg) for debugging
- Includes Source Link for debugging into source code

### 🚀 Publishing
- Publishes to NuGet.org automatically on main branch
- Skips if version already exists (--skip-duplicate)
- Creates GitHub release with package files
- Uses protected environment for safety

## Troubleshooting

### "Package already exists" Error

If you see this error, it means the version number in the `.csproj` file has already been published. To fix:

1. Update the version number in `Sorcha.Cryptography.csproj`
2. Commit and push the change
3. The pipeline will publish the new version

### "Unauthorized" or "403 Forbidden" Error

This means the API key is invalid or doesn't have the right permissions:

1. Verify the `NUGET_API_KEY` secret is set correctly in GitHub
2. Check that the API key hasn't expired on NuGet.org
3. Ensure the API key has "Push" permissions for the `Sorcha.Cryptography` package

### Pipeline Not Triggering

The pipeline only triggers when files in these paths change:
- `src/Common/Sorcha.Cryptography/**`
- `tests/Sorcha.Cryptography.Tests/**`
- `.github/workflows/cryptography-nuget.yml`

If you need to trigger it manually:
1. Go to Actions tab in GitHub
2. Select "Sorcha.Cryptography NuGet Package" workflow
3. Click "Run workflow"

## Best Practices

1. **Version Management**: Always update the version number before merging to main
2. **Testing**: Ensure all tests pass locally before pushing
3. **Documentation**: Keep XML comments up to date
4. **Changelog**: Consider maintaining a CHANGELOG.md for the library
5. **API Key Security**: Rotate API keys regularly (at least annually)
6. **Review Process**: Use the GitHub environment protection to require approval for releases

## Security Considerations

- ✅ API key is stored as a GitHub secret (encrypted)
- ✅ API key is scoped to only this package
- ✅ Pipeline only runs on protected branches (main/master)
- ✅ Optional environment protection requires manual approval
- ✅ Source Link allows transparency into source code

## Next Steps

After setup is complete:

1. ☐ Test the pipeline with a PR
2. ☐ Publish the first version to NuGet.org
3. ☐ Add a badge to README.md showing the NuGet version
4. ☐ Set up API key expiration reminders
5. ☐ Document the package usage in the main README

## Resources

- [NuGet.org Documentation](https://docs.microsoft.com/en-us/nuget/)
- [GitHub Actions Documentation](https://docs.github.com/en/actions)
- [Semantic Versioning](https://semver.org/)
- [.NET Package Validation](https://learn.microsoft.com/en-us/dotnet/fundamentals/package-validation/)
