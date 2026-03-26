#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Exports OpenAPI specifications from running Sorcha services.
# Prerequisites: Services must be running (docker-compose up -d)

set -euo pipefail

GATEWAY_URL="${SORCHA_GATEWAY_URL:-http://localhost}"
OUTPUT_DIR="${1:-docs/api}"

mkdir -p "$OUTPUT_DIR"

echo "Exporting OpenAPI specs from $GATEWAY_URL..."

# Aggregated spec (all services combined)
echo "  -> Aggregated spec..."
curl -sf "$GATEWAY_URL/openapi/aggregated.json" -o "$OUTPUT_DIR/openapi-aggregated.json" \
  || echo "  !! Failed to fetch aggregated spec (is the API Gateway running?)"

# Individual service specs
for service in blueprint tenant wallet register peer; do
  echo "  -> $service service..."
  curl -sf "$GATEWAY_URL/api/$service/openapi/v1.json" -o "$OUTPUT_DIR/openapi-$service.json" \
    || echo "  !! Failed to fetch $service spec"
done

echo ""
echo "Specs exported to $OUTPUT_DIR/"
ls -la "$OUTPUT_DIR"/*.json 2>/dev/null || echo "No specs exported. Are the services running?"
