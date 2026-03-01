# Sorcha Schema Library Architecture

## Overview

The Sorcha Schema Library is a comprehensive system for managing, searching, and using JSON Schema documents from multiple sources. It provides a unified interface for blueprint designers to discover and incorporate data schemas into their workflow actions.

## Architecture

### Core Components

```
Sorcha.Blueprint.Schemas/
├── SchemaMetadata.cs          # Metadata model for schemas
├── SchemaDocument.cs           # Complete schema with metadata + JSON
├── ISchemaRepository.cs        # Repository interface
├── BuiltInSchemaRepository.cs  # Built-in schemas shipped with Sorcha
├── SchemaStoreRepository.cs    # Integration with SchemaStore.org
└── SchemaLibraryService.cs     # Unified aggregation service
```

### Schema Sources

1. **Built-In Schemas** (`SchemaSource.BuiltIn`)
   - Shipped with Sorcha
   - Common schemas: Person, Address, Payment, Document
   - Always available offline
   - Fast access, no network required

2. **Local Schemas** (`SchemaSource.Local`)
   - User-defined schemas
   - Stored locally in the application
   - Custom business-specific data models

3. **SchemaStore.org** (`SchemaSource.SchemaStore`)
   - Public schema repository
   - 600+ community-maintained schemas
   - Configuration files, package manifests, API schemas
   - Lazy-loaded on demand

4. **Blueprint Service** (`SchemaSource.BlueprintService`)
   - Future: Central schema repository service
   - Organization-wide shared schemas
   - Version control and governance

5. **External** (`SchemaSource.External`)
   - Custom URLs
   - Third-party schema providers
   - Industry-specific registries

## Data Model

### SchemaMetadata
```csharp
public class SchemaMetadata
{
    string Id              // Unique URI identifier
    string Title           // Human-readable name
    string Description     // What this schema defines
    string Version         // Semantic version
    string Category        // Domain/category
    List<string> Tags      // Searchable keywords
    SchemaSource Source    // Where it came from
    string? SchemaUrl      // Remote URL (if applicable)
    string? Author         // Creator/maintainer
    string? License        // Licensing information
    DateTimeOffset AddedAt // When added to library
    bool IsFavorite        // User favorite flag
    int UsageCount         // Popularity metric
}
```

### SchemaDocument
```csharp
public class SchemaDocument
{
    SchemaMetadata Metadata         // Schema metadata
    JsonDocument Schema             // Actual JSON Schema
    List<string> PropertyNames      // Indexed property names
    bool IsValid                    // Validation status
    List<string>? ValidationErrors  // Errors if invalid
}
```

## Repository Pattern

### ISchemaRepository Interface
All repositories implement a common interface:

```csharp
public interface ISchemaRepository
{
    SchemaSource SourceType { get; }

    Task<IEnumerable<SchemaDocument>> GetAllSchemasAsync();
    Task<SchemaDocument?> GetSchemaByIdAsync(string id);
    Task<IEnumerable<SchemaDocument>> SearchSchemasAsync(string query);
    Task<IEnumerable<SchemaDocument>> GetSchemasByCategoryAsync(string category);
    Task RefreshAsync();
}
```

### Built-In Repository
- Initializes schemas in memory on startup
- Includes 4 common schemas:
  - **Person**: firstName, lastName, email, phone, dateOfBirth
  - **Address**: street, city, state, postalCode, country
  - **Payment**: amount, currency, paymentMethod, reference, date
  - **Document**: fileName, fileType, fileSize, fileUrl, uploadedAt

### SchemaStore Repository
- Fetches catalog from https://www.schemastore.org/api/json/catalog.json
- Caches schema metadata for 24 hours
- Lazy-loads full schemas only when needed
- Categorizes schemas automatically
- Extracts tags from schema names and descriptions

## SchemaLibraryService

The unified service aggregates all repositories and provides:

### Search & Discovery
- `SearchAsync(query)` - Full-text search across all sources
- `GetByCategoryAsync(category)` - Filter by category
- `GetBySourceAsync(source)` - Filter by source type
- `GetCategoriesAsync()` - List all categories

### Favorites & Usage
- `GetFavorites()` - User-starred schemas
- `AddToFavorites(schema)` - Mark as favorite
- `IncrementUsageAsync(schemaId)` - Track usage
- `GetMostUsedAsync(count)` - Popular schemas
- `GetRecentlyAddedAsync(count)` - Newest schemas

### Statistics
- `GetStatisticsAsync()` - Library metrics
  - Total schemas by source
  - Favorite count
  - Category breakdown

## UI Components

### Schema Library Page (`/schemas`)

Features:
- **Search Bar**: Full-text search with Enter key support
- **Source Filter**: Dropdown to filter by schema source
- **Category Filter**: Dropdown to filter by category
- **Statistics Chips**: Total count, favorites count
- **Refresh Button**: Reload all repositories
- **Data Grid**: Sortable table with:
  - Favorite toggle (star icon)
  - Schema name
  - Description
  - Category
  - Source
  - Usage count
  - View action (magnifying glass)
  - Use action (plus icon)

### Schema Details Dialog

Displays:
- Full metadata (ID, description, version, category, source, author)
- Tags as chips
- Property names as chips
- Full JSON Schema (formatted, scrollable)
- "Use This Schema" button

## Integration with Blueprint Designer

### Service Registration

In `Program.cs`:
```csharp
builder.Services.AddSingleton<SchemaLibraryService>(sp =>
{
    var schemaLibrary = new SchemaLibraryService();

    // Built-in repository added by default

    // Add SchemaStore
    var httpClient = new HttpClient();
    schemaLibrary.AddRepository(new SchemaStoreRepository(httpClient));

    return schemaLibrary;
});
```

### Usage in Actions

When designing a Blueprint action:
1. Navigate to Schema Library (`/schemas`)
2. Search/browse for relevant schemas
3. Click "Use" or view details and click "Use This Schema"
4. Schema is added to the action's `DataSchemas` collection
5. Usage count is incremented for popularity tracking

## Future Enhancements

### Planned Features
1. **Local Schema Management**
   - Create custom schemas in the UI
   - Edit existing schemas
   - Import/export schemas
   - Local storage persistence

2. **Blueprint Service Integration**
   - OAuth authentication
   - Organization schema repositories
   - Version control and branching
   - Schema approval workflows

3. **Advanced Search**
   - Property-level search ("find schemas with 'email' field")
   - JSON Schema validation level
   - Required vs optional properties
   - Data type filtering

4. **Schema Visualization**
   - Interactive schema tree view
   - Property dependencies
   - Example data generation
   - JSON-LD support

5. **Schema Composition**
   - Combine multiple schemas
   - Schema inheritance
   - `$ref` resolution
   - Composition validation

6. **Export & Sharing**
   - Export selected schemas as bundle
   - Share schema collections
   - Generate TypeScript interfaces
   - Generate C# classes

## Design Decisions

### Why Multiple Sources?
- **Flexibility**: Different teams/use cases have different schema needs
- **Reusability**: Leverage existing public schemas (SchemaStore)
- **Governance**: Central service for org-wide standards
- **Offline**: Built-in schemas work without network

### Why Lazy Loading (SchemaStore)?
- **Performance**: Don't fetch 600+ full schemas on startup
- **Bandwidth**: Only download what's actually used
- **Cache**: 24-hour cache reduces API calls

### Why Repository Pattern?
- **Extensibility**: Easy to add new sources
- **Testability**: Mock repositories in unit tests
- **Separation**: Each source has unique implementation
- **Aggregation**: Service combines all seamlessly

### Why Usage Tracking?
- **Discovery**: Popular schemas surface in recommendations
- **Metrics**: Understand which data models are common
- **Onboarding**: New users see what others use

## Examples

### Searching for Email-Related Schemas
```csharp
var results = await schemaLibrary.SearchAsync("email");
// Returns: Person schema (has email property),
//          plus any SchemaStore schemas with "email" in name/description
```

### Getting Finance Schemas
```csharp
var financeSchemas = await schemaLibrary.GetByCategoryAsync("Finance");
// Returns: Payment schema, plus any categorized as Finance
```

### Adding to Favorites
```csharp
var personSchema = await schemaLibrary.GetSchemaByIdAsync("https://sorcha.dev/schemas/person/v1");
schemaLibrary.AddToFavorites(personSchema);
```

### Getting Statistics
```csharp
var stats = await schemaLibrary.GetStatisticsAsync();
Console.WriteLine($"Total: {stats.TotalSchemas}");
Console.WriteLine($"Built-in: {stats.BuiltInSchemas}");
Console.WriteLine($"SchemaStore: {stats.SchemaStoreSchemas}");
```

## File Locations

| Component | Path |
|-----------|------|
| Schema Models | `src/Core/Sorcha.Blueprint.Schemas/` |
| UI Component | `src/Apps/UI/Sorcha.Blueprint.Designer.Client/Pages/SchemaLibrary.razor` |
| Service Registration | `src/Apps/UI/Sorcha.Blueprint.Designer.Client/Program.cs` |
| Navigation | `src/Apps/UI/Sorcha.Blueprint.Designer.Client/Layout/NavMenu.razor` |
| Documentation | `docs/schema-library-architecture.md` |

## Testing

### Unit Tests (Recommended)
```csharp
// Test built-in repository
[Fact]
public async Task BuiltInRepository_Returns_ExpectedSchemas()
{
    var repo = new BuiltInSchemaRepository();
    var schemas = await repo.GetAllSchemasAsync();

    Assert.Contains(schemas, s => s.Metadata.Title == "Person");
    Assert.Contains(schemas, s => s.Metadata.Title == "Address");
    Assert.Contains(schemas, s => s.Metadata.Title == "Payment");
    Assert.Contains(schemas, s => s.Metadata.Title == "Document");
}

// Test search functionality
[Fact]
public async Task SchemaLibrary_Search_FindsRelevantSchemas()
{
    var library = new SchemaLibraryService();
    var results = await library.SearchAsync("payment");

    Assert.NotEmpty(results);
    Assert.All(results, s =>
        Assert.Contains("payment", s.Metadata.Title.ToLower()) ||
        Assert.Contains("payment", s.Metadata.Description.ToLower())
    );
}
```

## Performance Considerations

- **Built-in schemas**: In-memory, instant access
- **SchemaStore**: First load fetches catalog (~2MB), subsequent loads use cache
- **Full schemas**: Lazy-loaded only when viewed or used
- **Search**: In-memory LINQ queries, sub-millisecond for typical library size
- **Caching**: 24-hour expiration for remote catalogs

## Security

- **HTTPS only**: All remote schema fetches use HTTPS
- **No storage of credentials**: SchemaStore is public, no auth needed
- **Future Blueprint Service**: Will use OAuth 2.0 / OpenID Connect
- **Schema validation**: All schemas validated before use in actions
- **XSS prevention**: JSON displayed in `<pre>` tags, not evaluated

## Conclusion

The Sorcha Schema Library provides a robust, extensible system for managing JSON Schemas across multiple sources. It enables blueprint designers to easily discover, search, and incorporate well-defined data structures into their workflows, promoting reusability and standardization.
