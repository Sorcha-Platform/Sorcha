# API Resources

## Interactive API Explorer

The Sorcha API is documented with OpenAPI 3.0 and browsable via [Scalar UI](https://n1.sorcha.dev/openapi).

When running locally: [http://localhost/openapi](http://localhost/openapi)

## Downloads

| Resource | Description |
|----------|-------------|
| [Aggregated OpenAPI Spec](./openapi-aggregated.json) | Combined spec for all services |
| [Postman Collection](./sorcha-postman-collection.json) | Import into Postman for quick testing |

### Per-Service Specs

| Service | Spec |
|---------|------|
| Blueprint | [openapi-blueprint.json](./openapi-blueprint.json) |
| Tenant | [openapi-tenant.json](./openapi-tenant.json) |
| Wallet | [openapi-wallet.json](./openapi-wallet.json) |
| Register | [openapi-register.json](./openapi-register.json) |
| Peer | [openapi-peer.json](./openapi-peer.json) |

## Importing into Postman

1. Download the [Postman Collection](./sorcha-postman-collection.json)
2. Open Postman → Import → Upload File
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
