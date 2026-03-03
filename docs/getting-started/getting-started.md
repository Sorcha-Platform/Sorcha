# Getting Started with Sorcha

This guide will help you get Sorcha up and running on your local development machine.

## Prerequisites

Before you begin, ensure you have the following installed:

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (version 10.0.100 or later)
- [Git](https://git-scm.com/)
- A code editor:
  - [Visual Studio 2025](https://visualstudio.microsoft.com/) (recommended for Windows)
  - [Visual Studio Code](https://code.visualstudio.com/) with C# extension
  - [JetBrains Rider](https://www.jetbrains.com/rider/)

Optional:
- [Docker Desktop](https://www.docker.com/products/docker-desktop) for containerization

## Installation

### 1. Clone the Repository

```bash
git clone https://github.com/yourusername/sorcha.git
cd sorcha
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Build the Solution

```bash
dotnet build
```

This will build all projects in the solution.

## Running Sorcha

### Using .NET Aspire (Recommended)

The easiest way to run Sorcha is using the .NET Aspire orchestration:

```bash
dotnet run --project src/Sorcha.AppHost
```

This will:
- Start the Blueprint Engine API
- Start the Blueprint Designer web UI
- Launch the Aspire dashboard
- Configure service discovery automatically

The Aspire dashboard will open in your browser at `http://localhost:15888` (or similar).

From the dashboard, you can:
- View all running services
- See logs and traces
- Monitor health status
- Access service endpoints

### Running Services Individually

If you prefer to run services separately:

**Blueprint Engine (API):**
```bash
cd src/Sorcha.Blueprint.Engine
dotnet run
```
The API will be available at `https://localhost:7001` and `http://localhost:5001`.

**Blueprint Designer (Web UI):**
```bash
cd src/Sorcha.Blueprint.Designer
dotnet run
```
The designer will be available at `https://localhost:7002` and `http://localhost:5002`.

## Accessing the Application

Once running, you can access:

- **Aspire Dashboard**: `http://localhost:15888`
- **Blueprint Designer**: `https://localhost:7002` (or the URL shown in console)
- **Blueprint Engine API**: `https://localhost:7001` (or the URL shown in console)
- **API Documentation**: `https://localhost:7001/scalar/v1` (interactive Scalar UI)
- **OpenAPI Spec**: `https://localhost:7001/openapi/v1.json`

## Your First Blueprint

### 1. Open the Designer

Navigate to the Blueprint Designer in your browser.

### 2. Create a Blueprint

(UI walkthrough will be added here)

### 3. Execute the Blueprint

Use the Designer UI or make a direct API call:

```bash
curl -X POST https://localhost:7001/blueprints/execute \
  -H "Content-Type: application/json" \
  -d '{
    "name": "My First Blueprint",
    "actions": [
      {
        "type": "log",
        "message": "Hello from Sorcha!"
      }
    ]
  }'
```

## Development Workflow

### 1. Make Changes

Edit the source code in your preferred editor.

### 2. Hot Reload

If running with `dotnet run`, many changes will be automatically reloaded without restarting.

### 3. Run Tests

```bash
dotnet test
```

### 4. Format Code

```bash
dotnet format
```

## Project Structure

```
Sorcha/
├── src/
│   ├── Sorcha.AppHost/              # Aspire orchestration host
│   ├── Sorcha.ServiceDefaults/      # Shared configurations
│   ├── Sorcha.Blueprint.Engine/     # Execution engine (API)
│   └── Sorcha.Blueprint.Designer/   # Visual designer (Web)
├── tests/                           # Test projects
├── docs/                            # Documentation
├── .github/                         # GitHub workflows
├── Sorcha.sln                       # Solution file
└── README.md                        # Main readme
```

## Configuration

Configuration files are located in each project:

- `appsettings.json` - Default settings
- `appsettings.Development.json` - Development overrides
- `appsettings.Production.json` - Production settings

### Common Settings

**Blueprint Engine (appsettings.json):**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "AllowedHosts": "*"
}
```

**Environment Variables:**
```bash
# Set log level
export ASPNETCORE_ENVIRONMENT=Development

# Set custom ports
export ASPNETCORE_URLS="https://localhost:8000;http://localhost:8001"
```

## Troubleshooting

### Port Already in Use

If you see port conflicts, you can change the ports in `Properties/launchSettings.json` for each project.

### SSL Certificate Issues

Trust the development certificate:
```bash
dotnet dev-certs https --trust
```

### Build Errors

Clean and rebuild:
```bash
dotnet clean
dotnet build
```

### .NET 10 Not Found

Ensure you have .NET 10 SDK installed:
```bash
dotnet --version
```

Should show `10.0.100` or later.

## Next Steps

Now that you have Sorcha running, explore:

- [Architecture Overview](../reference/architecture.md) - Understand the system design
- [Blueprint Schema](blueprint-schema.md) - Learn the blueprint format
- [API Reference](api-reference.md) - Explore the REST API
- [Contributing](../CONTRIBUTING.md) - Help improve Sorcha

## Getting Help

- Check the [Troubleshooting Guide](troubleshooting.md)
- Browse [Documentation](README.md)
- Search [GitHub Issues](https://github.com/yourusername/sorcha/issues)
- Ask in [GitHub Discussions](https://github.com/yourusername/sorcha/discussions)

## Common Commands

```bash
# Restore packages
dotnet restore

# Build solution
dotnet build

# Run tests
dotnet test

# Run with Aspire
dotnet run --project src/Sorcha.AppHost

# Clean build artifacts
dotnet clean

# Format code
dotnet format

# Check for security vulnerabilities
dotnet list package --vulnerable
```

Welcome to Sorcha! We're excited to have you here.
