-- SPDX-License-Identifier: MIT
-- Copyright (c) 2026 Sorcha Contributors
--
-- PostgreSQL initialization for a Sorcha desktop node.
-- Runs automatically the first time the postgres container is created.
-- Creates the per-service databases the cascade resolver appends Database= onto.

SELECT 'CREATE DATABASE sorcha_wallet'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_wallet')\gexec

SELECT 'CREATE DATABASE sorcha_tenant'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_tenant')\gexec

SELECT 'CREATE DATABASE sorcha_peer'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_peer')\gexec

SELECT 'CREATE DATABASE sorcha_blueprint'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_blueprint')\gexec

GRANT ALL PRIVILEGES ON DATABASE sorcha_wallet TO sorcha;
GRANT ALL PRIVILEGES ON DATABASE sorcha_tenant TO sorcha;
GRANT ALL PRIVILEGES ON DATABASE sorcha_peer TO sorcha;
GRANT ALL PRIVILEGES ON DATABASE sorcha_blueprint TO sorcha;

\echo 'Database initialization completed: sorcha_wallet, sorcha_tenant, sorcha_peer, sorcha_blueprint'
