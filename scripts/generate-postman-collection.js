// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Converts the aggregated OpenAPI spec to a Postman Collection v2.1.
// Usage: node scripts/generate-postman-collection.js [input] [output]

const fs = require('fs');
const path = require('path');

const inputPath = process.argv[2] || path.join(__dirname, '..', 'docs', 'api', 'openapi-aggregated.json');
const outputPath = process.argv[3] || path.join(__dirname, '..', 'docs', 'api', 'sorcha-postman-collection.json');

if (!fs.existsSync(inputPath)) {
  console.error(`Error: Input file not found: ${inputPath}`);
  console.error('Run scripts/export-openapi-spec.sh first to export the OpenAPI spec.');
  process.exit(1);
}

const spec = JSON.parse(fs.readFileSync(inputPath, 'utf-8'));

// Build Postman Collection v2.1 from OpenAPI spec
const collection = {
  info: {
    name: 'Sorcha Platform API',
    description: spec.info?.description || 'Sorcha distributed ledger platform API collection',
    schema: 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json',
    version: spec.info?.version || '1.0.0',
  },
  auth: {
    type: 'bearer',
    bearer: [{ key: 'token', value: '{{accessToken}}', type: 'string' }],
  },
  variable: [
    { key: 'baseUrl', value: 'http://localhost', description: 'API Gateway base URL' },
    { key: 'accessToken', value: '', description: 'JWT access token (set after login)' },
    { key: 'refreshToken', value: '', description: 'JWT refresh token' },
    { key: 'organizationId', value: '', description: 'Current organization ID' },
  ],
  item: [],
};

// Group endpoints by tag
const tagGroups = {};
const paths = spec.paths || {};

for (const [pathStr, methods] of Object.entries(paths)) {
  for (const [method, operation] of Object.entries(methods)) {
    if (['get', 'post', 'put', 'delete', 'patch'].indexOf(method) === -1) continue;

    const tag = (operation.tags && operation.tags[0]) || 'General';
    if (!tagGroups[tag]) tagGroups[tag] = [];

    const item = {
      name: operation.summary || operation.operationId || `${method.toUpperCase()} ${pathStr}`,
      request: {
        method: method.toUpperCase(),
        header: [{ key: 'Content-Type', value: 'application/json' }],
        url: {
          raw: `{{baseUrl}}${pathStr}`,
          host: ['{{baseUrl}}'],
          path: pathStr.split('/').filter(Boolean),
        },
      },
    };

    // Add request body if present
    if (operation.requestBody?.content?.['application/json']?.schema) {
      const example = operation.requestBody.content['application/json'].example;
      item.request.body = {
        mode: 'raw',
        raw: JSON.stringify(example || {}, null, 2),
        options: { raw: { language: 'json' } },
      };
    }

    // Add description
    if (operation.description) {
      item.request.description = operation.description;
    }

    tagGroups[tag].push(item);
  }
}

// Convert tag groups to Postman folders
for (const [tag, items] of Object.entries(tagGroups).sort((a, b) => a[0].localeCompare(b[0]))) {
  collection.item.push({
    name: tag,
    item: items,
  });
}

fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, JSON.stringify(collection, null, 2));
console.log(`Postman collection generated: ${outputPath}`);
console.log(`  ${Object.keys(tagGroups).length} folders, ${Object.values(tagGroups).flat().length} requests`);
