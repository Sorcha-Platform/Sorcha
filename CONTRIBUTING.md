# Contributing to Sorcha

Thank you for your interest in contributing to Sorcha! This document provides guidelines and instructions for contributing.

## Code of Conduct

This project adheres to a code of conduct. By participating, you are expected to uphold this code. Please report unacceptable behavior to the project maintainers.

### Our Standards

- Be respectful and inclusive
- Welcome newcomers and help them learn
- Focus on what is best for the community
- Show empathy towards other community members

## Getting Started

1. Fork the repository
2. Clone your fork: `git clone https://github.com/yourusername/sorcha.git`
3. Create a feature branch: `git checkout -b feature/your-feature-name`
4. Make your changes
5. Commit with clear messages
6. Push to your fork
7. Create a Pull Request

## Development Setup

### Prerequisites

- .NET 10 SDK or later
- Git
- Your favorite IDE (Visual Studio 2025, VS Code, or Rider)

### Building the Project

```bash
dotnet restore
dotnet build
```

### Running Tests

```bash
dotnet test
```

### Running the Application

```bash
dotnet run --project src/Apps/Sorcha.AppHost
```

## Coding Guidelines

### C# Style

- Follow standard C# coding conventions
- Use nullable reference types (`<Nullable>enable</Nullable>`)
- Use implicit usings where appropriate
- Target .NET 10

### Code Organization

- Keep classes focused and single-purpose
- Use minimal APIs for endpoints
- Follow SOLID principles
- Write testable code

### Naming Conventions

- Use PascalCase for public members
- Use camelCase for private fields
- Use meaningful, descriptive names
- Avoid abbreviations unless widely known

### Comments and Documentation

- Write XML documentation for public APIs
- Use comments to explain "why," not "what"
- Keep comments up-to-date with code changes
- Document complex algorithms

## Commit Messages

Write clear, concise commit messages:

```
<type>: <subject>

<body>

<footer>
```

### Types

- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation changes
- `style`: Code style changes (formatting, etc.)
- `refactor`: Code refactoring
- `test`: Adding or updating tests
- `chore`: Maintenance tasks

### Examples

```
feat: add blueprint validation engine

Implements JSON schema validation for blueprint definitions
with comprehensive error reporting.

Closes #123
```

```
fix: resolve memory leak in execution engine

Fixed disposable resources not being properly released
in long-running blueprint executions.
```

## AI-Generated Code Requirements

### Overview

Sorcha welcomes the use of AI coding assistants (GitHub Copilot, Claude Code, ChatGPT, etc.) to enhance developer productivity. However, **all AI-generated code must meet the same quality standards as human-written code**, plus additional documentation requirements.

### Mandatory Documentation Updates

When you use AI tools to generate code, you **MUST** update the following documentation:

1. **README Files**
   - Project README if architecture/features change
   - Component README for new APIs or configuration

2. **Documentation Files** (`docs/`)
   - API reference for new endpoints
   - Architecture docs for new services
   - Development status for completion updates

3. **Spec-Kit Documentation** (`.specify/`)
   - **MASTER-TASKS.md** - Update task status (Required!)
   - Service specifications for API changes
   - Status files for progress tracking

4. **Code Documentation**
   - XML comments for all public APIs
   - OpenAPI documentation for REST endpoints
   - Inline comments for complex logic

### AI Code Checklist

Before submitting a PR with AI-generated code:

- [ ] All required documentation is updated (see policy below)
- [ ] MASTER-TASKS.md reflects task completion
- [ ] OpenAPI/XML documentation is complete
- [ ] README files are current
- [ ] Code passes all tests
- [ ] Manual review of AI-generated code completed

### Complete Policy

See [.specify/AI-CODE-DOCUMENTATION-POLICY.md](.specify/AI-CODE-DOCUMENTATION-POLICY.md) for complete requirements, examples, and workflow.

**Important:** PRs with AI-generated code will not be approved without proper documentation updates.

---

## Pull Request Process

1. **Update Documentation**: Ensure README.md and relevant docs are updated (REQUIRED for AI-generated code - see above)
2. **Add Tests**: Include unit tests for new functionality
3. **Update Changelog**: Add entry to CHANGELOG.md if it exists
4. **Follow Style**: Ensure code follows project style guidelines
5. **Pass CI**: All CI checks must pass
6. **Get Review**: At least one maintainer must approve

### PR Checklist

- [ ] Code builds without errors or warnings
- [ ] All tests pass
- [ ] New tests added for new functionality
- [ ] Documentation updated
- [ ] Commit messages are clear and follow guidelines
- [ ] No merge conflicts
- [ ] Follows coding guidelines

## Testing

### Unit Tests

- Write unit tests for all new functionality
- Use xUnit as the testing framework
- Follow AAA pattern (Arrange, Act, Assert)
- Mock external dependencies

### Integration Tests

- Test service interactions
- Use test containers where appropriate
- Clean up resources after tests

### Test Organization

```
tests/
├── Sorcha.Blueprint.Engine.Tests/
├── Sorcha.Blueprint.Designer.Tests/
└── Sorcha.Integration.Tests/
```

## Documentation

### Code Documentation

- Use XML documentation comments for public APIs
- Document parameters, return values, and exceptions
- Include usage examples for complex APIs

### User Documentation

Documentation lives in the `docs/` directory:

- `docs/architecture.md` - System architecture
- `docs/getting-started/getting-started.md` - Getting started guide
- `docs/guides/blueprints/blueprint-format.md` - Blueprint definition schema
- `docs/reference/API-DOCUMENTATION.md` - API documentation

### Documentation Style

- Use clear, simple language
- Include code examples
- Add diagrams where helpful
- Keep docs up-to-date with code

## Issue Reporting

### Bug Reports

Include:
- Clear description of the issue
- Steps to reproduce
- Expected vs actual behavior
- Environment details (.NET version, OS, etc.)
- Stack traces or error messages

### Feature Requests

Include:
- Clear description of the feature
- Use cases and benefits
- Proposed implementation (if any)
- Alternatives considered

### Issue Labels

- `bug` - Something isn't working
- `enhancement` - New feature or request
- `documentation` - Documentation improvements
- `good first issue` - Small, self-contained, and a reasonable way to learn the codebase. Doesn't require deep familiarity with Sorcha's architecture to complete safely.
- `help wanted` - The maintainers want outside help on this; scope may be larger or less well-defined than `good first issue`.
- `agent-friendly` - Meets the bar below: a fresh autonomous agent (or a fresh Claude session with no prior context) can pick it up cold and complete it without needing to ask a human a clarifying question first. See "The agent-friendly bar" below before applying or requesting this label.

### The agent-friendly bar

An issue labelled `agent-friendly` is a promise that the issue text itself is a sufficient brief — an agent shouldn't need to guess intent, hunt for the relevant code, or invent its own success criteria. Before applying the label (maintainers) or asking for it (contributors filing an issue meant for agent pickup), the issue must contain:

1. **Affected files/area** — the specific file(s), directory, or service the change lives in (e.g. `src/Services/Sorcha.Tenant.Service/Services/LoginService.cs`, not "the auth system"). If the exact file is unknown, name the narrowest area you can (service + rough responsibility) so the agent's first move is confirmation, not a codebase-wide search.
2. **Inputs** — what the agent needs to know to start: relevant request/response shapes, an example payload, a reproducing command, a linked PR/commit that shows the pattern to follow, or a pointer to a similar already-solved case elsewhere in the repo.
3. **Explicit done-criteria** — a concrete, checkable statement of what "finished" looks like. Not "fix the bug" but "calling `POST /api/x` with `{...}` returns `200` instead of `500`" or "`FooTests.Bar_Baz_Should()` passes." If the fix is structural (e.g. a naming/pattern cleanup), name the specific instances or the exact rule to apply everywhere.
4. **How to verify** — the command(s) to run to confirm the done-criteria are met (a `dotnet test --filter ...` invocation, a specific `curl`/CLI call, a script under `walkthroughs/`). An agent should be able to close the loop itself, not rely on a maintainer to eyeball the result.

An issue missing any of these isn't wrong to file — it's just not `agent-friendly` yet. Add the missing piece (or ask the reporter to) before applying the label.

## Release Process

1. Update version numbers
2. Update CHANGELOG.md
3. Create release branch
4. Run all tests
5. Create GitHub release
6. Publish NuGet packages (if applicable)

## Getting Help

- Check existing documentation
- Search closed issues
- Ask in GitHub Discussions
- Join community chat (if available)

## Recognition

Contributors will be recognized in:
- README.md contributors section
- Release notes
- Project documentation

Thank you for contributing to Sorcha!
