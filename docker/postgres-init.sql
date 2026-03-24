-- SPDX-License-Identifier: MIT
-- Copyright (c) 2025 Sorcha Contributors
--
-- PostgreSQL initialization script
-- Creates databases for Sorcha services
-- This script runs automatically when the PostgreSQL container is first created

-- Create sorcha_wallet database
SELECT 'CREATE DATABASE sorcha_wallet'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_wallet')\gexec

-- Create sorcha_tenant database
SELECT 'CREATE DATABASE sorcha_tenant'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_tenant')\gexec

-- Create sorcha_peer database
SELECT 'CREATE DATABASE sorcha_peer'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'sorcha_peer')\gexec

-- Grant all privileges to the sorcha user
GRANT ALL PRIVILEGES ON DATABASE sorcha_wallet TO sorcha;
GRANT ALL PRIVILEGES ON DATABASE sorcha_tenant TO sorcha;
GRANT ALL PRIVILEGES ON DATABASE sorcha_peer TO sorcha;

-- Log completion
\echo 'Database initialization completed: sorcha_wallet, sorcha_tenant, and sorcha_peer created'
