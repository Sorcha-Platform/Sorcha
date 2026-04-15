# API Resources

## Interactive API Explorer

The Sorcha API is documented with OpenAPI 3.0 and browsable via [Scalar UI](https://n1.sorcha.dev/openapi).

When running locally: [http://localhost/openapi](http://localhost/openapi)

## Per-Service Specs

OpenAPI specs are served live by each service at `/openapi/v1.json`. When running locally via Docker or Aspire:

| Service | Scalar UI | OpenAPI JSON |
|---------|-----------|-------------|
| Blueprint | `http://localhost/openapi` | `/api/blueprint/openapi/v1.json` |
| Tenant | `http://localhost/openapi` | `/api/tenant/openapi/v1.json` |
| Wallet | `http://localhost/openapi` | `/api/wallet/openapi/v1.json` |
| Register | `http://localhost/openapi` | `/api/register/openapi/v1.json` |
| Peer | `http://localhost/openapi` | `/api/peer/openapi/v1.json` |

## Importing into Postman

1. Copy the OpenAPI JSON URL for the service you want to explore
2. Open Postman → Import → Link → Paste the URL
3. Set the `baseUrl` variable to your gateway URL (default: `http://localhost`)
4. Use the login endpoint to get an `accessToken`, then set it in the collection variables

## SDK Generation

Generate typed API clients from the OpenAPI spec:

### C# (NSwag)
```bash
dotnet tool install --global NSwag.ConsoleCore
nswag openapi2csclient \
  /input:docs/api/openapi-aggregated.json \
  /output:SorchaClient.cs \
  /namespace:Sorcha.Client \
  /generateClientInterfaces:true
```

### TypeScript (openapi-typescript-codegen)
```bash
npx openapi-typescript-codegen \
  --input docs/api/openapi-aggregated.json \
  --output ./src/client \
  --client axios
```

### Python (openapi-generator)
```bash
npx @openapitools/openapi-generator-cli generate \
  -i docs/api/openapi-aggregated.json \
  -g python \
  -o ./python-client \
  --additional-properties=packageName=sorcha_client
```

> **Note:** SDK generation produces client code based on the current API surface. Generated clients are not officially supported — they are a convenience for rapid integration.
