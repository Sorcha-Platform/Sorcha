# API Resources

## Interactive API Explorer

The Sorcha API is documented with OpenAPI 3.0 and browsable via Scalar UI.

When running locally: [http://localhost/openapi](http://localhost/openapi)

On the public demo node (n1): [https://n1.sorcha.dev/openapi](https://n1.sorcha.dev/openapi)

## Per-Service Specs

OpenAPI specs are served live by each service at `/openapi/v1.json`. When running locally via Docker or Aspire:

| Service | Scalar UI | OpenAPI JSON |
|---------|-----------|-------------|
| Blueprint | `http://localhost/api/blueprint/scalar/v1` | `/api/blueprint/openapi/v1.json` |
| Tenant | `http://localhost/api/tenant/scalar/v1` | `/api/tenant/openapi/v1.json` |
| Wallet | `http://localhost/api/wallet/scalar/v1` | `/api/wallet/openapi/v1.json` |
| Register | `http://localhost/api/register/scalar/v1` | `/api/register/openapi/v1.json` |
| Peer | `http://localhost/api/peer/scalar/v1` | `/api/peer/openapi/v1.json` |

## Importing into Postman

1. Copy the OpenAPI JSON URL for the service you want to explore
2. Open Postman → Import → Link → Paste the URL
3. Set the `baseUrl` variable to your gateway URL (default: `http://localhost`)
4. Use the login endpoint to get an `accessToken`, then set it in the collection variables

## SDK Generation

Generate typed API clients from the OpenAPI spec:

### C# (NSwag)

Generate a client per service by pointing at the live per-service OpenAPI endpoint:

```bash
dotnet tool install --global NSwag.ConsoleCore

# Example: Blueprint service
nswag openapi2csclient \
  /input:http://localhost/api/blueprint/openapi/v1.json \
  /output:SorchaBluprintClient.cs \
  /namespace:Sorcha.Client \
  /generateClientInterfaces:true

# Repeat for other services: /api/tenant/, /api/wallet/, /api/register/, /api/peer/
```

### TypeScript (openapi-typescript-codegen)

```bash
# Example: Tenant service
npx openapi-typescript-codegen \
  --input http://localhost/api/tenant/openapi/v1.json \
  --output ./src/client/tenant \
  --client axios

# Repeat for other services: /api/blueprint/, /api/wallet/, /api/register/, /api/peer/
```

### Python (openapi-generator)

```bash
# Example: Register service
npx @openapitools/openapi-generator-cli generate \
  -i http://localhost/api/register/openapi/v1.json \
  -g python \
  -o ./python-client \
  --additional-properties=packageName=sorcha_client

# Repeat for other services: /api/blueprint/, /api/tenant/, /api/wallet/, /api/peer/
```

> **Note:** SDK generation produces client code based on the current API surface. Generated clients are not officially supported — they are a convenience for rapid integration.
