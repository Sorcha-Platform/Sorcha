# AI-Generated Code Documentation Policy

**Version:** 1.0
**Created:** 2025-11-17
**Status:** Active
**Related:** [constitution.md](constitution.md), [CONTRIBUTING.md](../CONTRIBUTING.md)

---

## Purpose

This policy establishes mandatory documentation requirements for all automated and AI-generated code contributions to the Sorcha project. The goal is to maintain comprehensive, up-to-date documentation that accurately reflects the current state of the codebase.

## Scope

This policy applies to:
- AI-assisted code generation (e.g., GitHub Copilot, Claude Code, ChatGPT)
- Automated code scaffolding and generation tools
- Code templates and generators
- Any code not written entirely by human developers

## Core Principle

**When code is automatically generated, documentation must be automatically updated.**

No AI-generated code contribution is considered complete until all relevant documentation has been reviewed and updated to reflect the changes.

---

## Required Documentation Updates

When any automated or AI-generated code is created or modified, the following documentation MUST be updated:

### 1. README Files

**What to Update:**
- **Project README** (`/README.md`) - Update if:
  - New services or major components are added
  - Architecture changes significantly
  - New features affect the project overview
  - Development status changes

- **Component READMEs** (e.g., `src/Services/*/README.md`) - Update if:
  - New APIs or endpoints are added
  - Configuration requirements change
  - Usage patterns are modified
  - Dependencies are added or changed

**Required Sections:**
- Overview/Description
- Features list
- Usage examples (if API changes)
- Configuration (if new settings added)
- Dependencies (if packages added)

### 2. Documentation Files (`docs/`)

**What to Update:**
- **Architecture Documentation** (`docs/reference/architecture.md`) - Update if:
  - New services or components are added
  - Service interactions change
  - Data flow patterns are modified

- **API Reference** (`docs/api-reference.md`) - Update if:
  - New endpoints are added
  - Request/response formats change
  - Authentication requirements change

- **Development Status** (`docs/reference/development-status.md`) - Update if:
  - Component completion percentages change
  - Major milestones are reached
  - New features are implemented

**Additional Documentation:**
- Getting Started guides (if setup process changes)
- Deployment guides (if deployment changes)
- Integration guides (if integration patterns change)
- Testing documentation (if test coverage changes significantly)

### 3. Spec-Kit Documentation (`.specify/`)

**What to Update:**

#### 3.1 MASTER-TASKS.md
- **REQUIRED** - Update task status when code is generated for a task:
  - Change status from 📋 Not Started → 🚧 In Progress → ✅ Complete
  - Update completion dates
  - Update task counts and percentages
  - Add notes about implementation decisions
  - Update effort estimates if they differ from actuals

#### 3.2 MASTER-PLAN.md
- Update if:
  - Phase completion status changes
  - Major milestones are achieved
  - Timeline adjustments are needed
  - Risks materialize or are resolved

#### 3.3 Service Specifications (`.specify/specs/`)
- Update the relevant service specification if:
  - APIs are added or modified
  - Data models change
  - Integration patterns change
  - Technical decisions deviate from spec

#### 3.4 Task Files (`.specify/tasks/`)
- Update individual task files if they contain:
  - Implementation details that changed during development
  - Acceptance criteria that need adjustment
  - Dependencies that were discovered

#### 3.5 constitution.md
- Update if:
  - New architectural patterns are established
  - New standards or principles are adopted
  - Security practices change

### 4. Status Files

**What to Update:**
- **Component Status Files** (e.g., `tests/*/STATUS.md`, `docs/*-status.md`)
  - Update completion percentages
  - Document what's implemented vs. planned
  - Note any deviations from original design
  - Update test coverage statistics
  - List known issues or limitations

### 5. Code-Level Documentation

**What to Update:**
- **XML Documentation Comments**
  - All public classes, methods, and properties
  - Parameters and return values
  - Exceptions that may be thrown
  - Usage examples for complex APIs

- **OpenAPI Documentation**
  - Auto-generated OpenAPI specs must be verified
  - Ensure all endpoints are documented
  - Request/response models must have descriptions
  - Examples must be provided for complex payloads

- **Inline Comments**
  - Complex algorithms must have explanatory comments
  - Design decisions should be documented
  - Workarounds or technical debt should be noted

### 6. Project Tracking

**What to Update:**
- **GitHub Issues/PRs**
  - Reference related issues in commit messages
  - Update issue status when code is merged
  - Document implementation notes in issue comments

- **Changelog** (if exists)
  - Add entry for significant features
  - Note breaking changes
  - Document bug fixes

---

## Documentation Update Workflow

### Step 1: Generate Code
When AI generates code, document the following:
- What was generated
- Why it was generated (which task/requirement)
- Any assumptions made
- Any deviations from original specifications

### Step 2: Identify Affected Documentation
Review the checklist above and identify which documentation files need updates.

### Step 3: Update Documentation
For each identified document:
1. Read the current content
2. Identify sections that need updates
3. Make precise, accurate updates
4. Ensure consistency with other documentation
5. Verify examples and code snippets still work

### Step 4: Verify Documentation
Before committing:
- [ ] All links work correctly
- [ ] Code examples compile/run
- [ ] Version numbers are updated
- [ ] Dates are current
- [ ] Status indicators are accurate
- [ ] Cross-references are consistent

### Step 5: Commit Documentation with Code
- Documentation updates should be included in the same PR as code changes
- Commit message should mention documentation updates
- PR description should list documentation files updated

---

## AI-Specific Requirements

### For AI Coding Assistants (Claude Code, Copilot, etc.)

When an AI assistant generates code, it MUST:

1. **Identify Documentation Impact**
   - Analyze which documentation files are affected
   - List them explicitly before making changes

2. **Update Documentation Proactively**
   - Don't wait to be asked
   - Update documentation as part of the code generation task
   - Treat documentation updates as mandatory, not optional

3. **Verify Accuracy**
   - Ensure documentation accurately reflects the generated code
   - Update examples to match new APIs
   - Verify technical details are correct

4. **Maintain Consistency**
   - Keep terminology consistent across all docs
   - Update cross-references
   - Maintain the project's documentation style

5. **Flag Incomplete Documentation**
   - If documentation cannot be fully updated, create a TODO
   - Document what needs to be completed
   - Assign to appropriate owner

### For Human Developers Using AI Tools

When using AI tools to generate code, you are responsible for:

1. **Reviewing AI-Generated Documentation**
   - Don't blindly accept AI documentation updates
   - Verify technical accuracy
   - Ensure it matches project style

2. **Completing Documentation**
   - If AI didn't update all required docs, you must complete them
   - Don't merge PRs with incomplete documentation

3. **Quality Assurance**
   - Ensure documentation is helpful to other developers
   - Verify examples work as shown
   - Check that architecture diagrams are updated if needed

---

## Documentation Standards

### Quality Requirements

All documentation updates must:
- **Be accurate** - Reflect the actual implementation
- **Be complete** - Cover all aspects of the change
- **Be clear** - Written in simple, understandable language
- **Be current** - Include current version numbers and dates
- **Be consistent** - Use project terminology and style

### Format Requirements

- Use Markdown for all documentation
- Follow the project's existing structure and style
- Include code examples where appropriate
- Use proper headings and sections
- Maintain table of contents for long documents

### Content Requirements

- **What changed** - Clearly describe the changes
- **Why it changed** - Explain the rationale
- **How to use it** - Provide usage examples
- **Migration guide** - If breaking changes, explain how to migrate
- **Known issues** - Document limitations or known problems

---

## Enforcement

### Pull Request Requirements

PRs will not be approved unless:
- [ ] All required documentation is updated
- [ ] Documentation changes are included in the PR
- [ ] Documentation is reviewed alongside code
- [ ] PR description lists documentation updates

### Review Checklist

Reviewers must verify:
- [ ] README files are current
- [ ] API documentation is complete
- [ ] MASTER-TASKS.md reflects completion status
- [ ] Status files are accurate
- [ ] Code has proper XML comments
- [ ] Examples work as documented

### Automated Checks

Where possible, implement automated checks for:
- OpenAPI spec generation and validation
- Link validation in documentation
- Example code compilation
- Documentation coverage metrics

---

## Examples

### Example 1: Adding a New REST API Endpoint

**AI-Generated Code:**
```csharp
// New endpoint: POST /api/wallets/{id}/sign
[HttpPost("{id}/sign")]
public async Task<IActionResult> SignTransaction(string id, SignRequest request)
{
    // Implementation...
}
```

**Required Documentation Updates:**
1. ✅ **OpenAPI/XML Comments** - Add to the method
2. ✅ **README.md** - Add to API endpoints list (if exists)
3. ✅ **docs/api-reference.md** - Add endpoint documentation
4. ✅ **docs/wallet-service-status.md** - Update endpoints count
5. ✅ **.specify/MASTER-TASKS.md** - Mark task as complete
6. ✅ **.specify/specs/sorcha-wallet-service.md** - Update if spec needs revision

### Example 2: Implementing a New Service

**AI-Generated Code:**
- New service project: `Sorcha.NewService`
- Controllers, models, repositories

**Required Documentation Updates:**
1. ✅ **README.md** - Add service to architecture overview
2. ✅ **src/Services/Sorcha.NewService/README.md** - Create new README
3. ✅ **docs/reference/architecture.md** - Add service to architecture diagrams
4. ✅ **docs/reference/development-status.md** - Add service status section
5. ✅ **.specify/MASTER-PLAN.md** - Update phase completion
6. ✅ **.specify/MASTER-TASKS.md** - Mark all related tasks complete
7. ✅ **.specify/specs/sorcha-new-service.md** - Create or update spec
8. ✅ **CONTRIBUTING.md** - Update if build/test process changes

### Example 3: Bug Fix

**AI-Generated Code:**
```csharp
// Fixed: Memory leak in ExecutionEngine
protected virtual void Dispose(bool disposing)
{
    if (disposing)
    {
        _cache?.Dispose();
    }
}
```

**Required Documentation Updates:**
1. ✅ **CHANGELOG.md** - Add bug fix entry (if exists)
2. ✅ **GitHub Issue** - Add comment with fix details
3. ✅ **Code comments** - Add note about disposal pattern
4. ⚠️ **Minimal changes** - Bug fixes typically require less documentation

---

## Exceptions

Documentation updates may be deferred (but not skipped) for:

1. **Experimental/Proof-of-Concept Code**
   - Must be clearly marked as experimental
   - Must include TODO for documentation
   - Cannot be merged to main/production branches

2. **Internal Refactoring**
   - If public APIs don't change
   - If behavior is identical
   - Still update inline comments if complexity changes

3. **Emergency Hotfixes**
   - Documentation can be updated in follow-up PR
   - Must create tracking issue for documentation
   - Must be completed within 48 hours

**Note:** These exceptions require explicit approval from maintainers.

---

## Metrics and Monitoring

Track the following metrics:

- **Documentation Coverage**: % of public APIs with documentation
- **Documentation Staleness**: Time since last update vs. last code change
- **Documentation PRs**: Ratio of documentation-only PRs to code PRs
- **Review Feedback**: Number of PRs sent back for documentation updates

**Target Metrics:**
- 100% of public APIs documented
- <7 days staleness for actively developed components
- <10% PRs require documentation rework

---

## Review and Updates

This policy should be reviewed:
- Quarterly (every 3 months)
- After major project milestones
- When new AI tools are adopted
- When documentation gaps are identified

**Last Review:** 2025-11-17
**Next Review:** 2026-02-17
**Owner:** Sorcha Architecture Team

---

## Related Documents

- [Project Constitution](constitution.md) - Development standards and principles
- [CONTRIBUTING.md](../CONTRIBUTING.md) - General contribution guidelines
- [MASTER-TASKS.md](MASTER-TASKS.md) - Task tracking and status
- [.specify/README.md](README.md) - Spec-Kit overview and workflows

---

## Questions and Support

**Q: What if I'm not sure which documentation to update?**
A: When in doubt, ask in the PR or create a draft PR. Reviewers will help identify required updates.

**Q: Can AI tools automatically update all required documentation?**
A: AI tools can help, but human review is always required. Developers are responsible for accuracy.

**Q: What if the documentation contradicts the code?**
A: The code is the source of truth. Update documentation to match code, and investigate why they diverged.

**Q: How detailed should documentation updates be?**
A: Detailed enough for a new team member to understand the change without reading all the code.

---

**Document Control:**
- **Version:** 1.0
- **Created:** 2025-11-17
- **Status:** Active
- **Approval:** Required for all AI-generated code contributions
