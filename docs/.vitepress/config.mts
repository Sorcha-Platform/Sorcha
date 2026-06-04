// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

import { defineConfig } from 'vitepress'
import { withMermaid } from 'vitepress-plugin-mermaid'

export default withMermaid(
  defineConfig({
    title: 'Sorcha Docs',
    description: 'Documentation for the Sorcha decentralised register platform',
    base: '/',

    head: [
      ['link', { rel: 'icon', href: '/favicon.png' }],

      // Google Analytics gtag.js — measurement ID is public by design.
      // Configured in Consent Mode v2 default-denied; flip to 'granted'
      // when a future consent banner records explicit user opt-in.
      ['script', { async: '', src: 'https://www.googletagmanager.com/gtag/js?id=G-B2MKVEP45T' }],
      ['script', {}, `window.dataLayer = window.dataLayer || [];
function gtag(){dataLayer.push(arguments);}
gtag('consent', 'default', {
  ad_storage: 'denied',
  ad_user_data: 'denied',
  ad_personalization: 'denied',
  analytics_storage: 'denied',
  wait_for_update: 500
});
gtag('js', new Date());
gtag('config', 'G-B2MKVEP45T', { anonymize_ip: true });`],
    ],

    srcExclude: [
      'site/**',
      'archive/**',
      'plans/**',
      'superpowers/**',
      // Retired: per-service "% MVD" status snapshots are inherently
      // perpetually-stale. Files kept in-repo (so inbound links still resolve)
      // but not built/navigated. Live status lives in development-status.md and
      // the service READMEs.
      'reference/status/**',
    ],

    // TODO: Remove after PR 2 fixes broken links
    ignoreDeadLinks: true,

    themeConfig: {
      search: {
        provider: 'local',
      },

      nav: [
        { text: 'Getting Started', link: '/getting-started/DOCKER-QUICK-START' },
        { text: 'Guides', link: '/guides/AUTHENTICATION-SETUP' },
        { text: 'Reference', link: '/reference/API-DOCUMENTATION' },
        { text: 'Services', link: '/services/' },
        {
          text: 'Links',
          items: [
            { text: 'sorcha.dev', link: 'https://www.sorcha.dev' },
            { text: 'GitHub', link: 'https://github.com/Sorcha-Platform/Sorcha' },
          ],
        },
      ],

      sidebar: {
        '/': [
          {
            text: 'Getting Started',
            collapsed: false,
            items: [
              { text: 'Overview', link: '/getting-started/getting-started' },
              { text: 'Docker Quick Start', link: '/getting-started/DOCKER-QUICK-START' },
              { text: 'Docker Development', link: '/getting-started/DOCKER-DEVELOPMENT-WORKFLOW' },
              { text: 'First Run Setup', link: '/getting-started/FIRST-RUN-SETUP' },
              { text: 'Bootstrap Credentials', link: '/getting-started/BOOTSTRAP-CREDENTIALS' },
              { text: 'Port Configuration', link: '/getting-started/PORT-CONFIGURATION' },
              { text: 'Infrastructure Setup', link: '/getting-started/INFRASTRUCTURE-SETUP' },
              { text: 'Blueprint Quick Start', link: '/getting-started/blueprint-quick-start' },
              { text: 'NuGet Setup', link: '/getting-started/nuget-setup-guide' },
            ],
          },
          {
            text: 'Admin Guide',
            collapsed: true,
            items: [
              { text: 'Overview', link: '/admin/README' },
              { text: 'Prerequisites & Sizing', link: '/admin/prerequisites-sizing' },
              { text: 'Installation & First Run', link: '/admin/installation-first-run' },
              { text: 'Configuration Reference', link: '/admin/configuration-reference' },
              { text: 'Scaling & High Availability', link: '/admin/scaling-high-availability' },
              { text: 'Monitoring & Observability', link: '/admin/monitoring-observability' },
              { text: 'Administration', link: '/admin/administration' },
              { text: 'Troubleshooting', link: '/admin/troubleshooting' },
              { text: 'Upgrade & Migration', link: '/admin/upgrade-migration' },
            ],
          },
          {
            text: 'Guides',
            collapsed: true,
            items: [
              {
                text: 'Authentication & Security',
                collapsed: true,
                items: [
                  { text: 'Authentication Setup', link: '/guides/AUTHENTICATION-SETUP' },
                  { text: 'JWT Configuration', link: '/guides/JWT-CONFIGURATION' },
                ],
              },
              {
                text: 'Blueprints',
                collapsed: true,
                items: [
                  { text: 'Blueprint Format', link: '/guides/blueprints/blueprint-format' },
                  { text: 'Blueprint Architecture', link: '/guides/blueprints/blueprint-architecture' },
                  { text: 'JSON-LD Quick Reference', link: '/guides/blueprints/json-ld-quick-reference' },
                  { text: 'JSON-LD Implementation', link: '/guides/blueprints/json-ld-implementation-summary' },
                  { text: 'JSON-e Templates', link: '/guides/blueprints/json-e-templates' },
                  { text: 'JSON Logic Guide', link: '/guides/blueprints/json-logic-guide' },
                ],
              },
              { text: 'Blueprint Designer', link: '/guides/designer' },
              {
                text: 'Integration',
                collapsed: true,
                items: [
                  { text: 'Integration Guide', link: '/guides/INTEGRATION-GUIDE' },
                  { text: 'Payload Serialization', link: '/guides/PAYLOAD-SERIALIZATION-GUIDE' },
                ],
              },
              {
                text: 'Testing',
                collapsed: true,
                items: [
                  { text: 'Testing Guide', link: '/guides/testing/testing' },
                  { text: 'Peer Integration Tests', link: '/guides/testing/peer-service-integration-testing' },
                ],
              },
              {
                text: 'Deployment',
                collapsed: true,
                items: [
                  { text: 'Deployment Guide', link: '/guides/DEPLOYMENT' },
                  { text: 'Azure Quick Start', link: '/guides/azure/AZURE-DEPLOYMENT-QUICK-START' },
                  { text: 'Azure Database Setup', link: '/guides/azure/AZURE-DATABASE-INITIALIZATION' },
                  { text: 'Azure Custom Domain', link: '/guides/azure/AZURE-CUSTOM-DOMAIN-SETUP' },
                ],
              },
              { text: 'Troubleshooting', link: '/guides/TROUBLESHOOTING' },
              { text: 'Claude Code Guidelines', link: '/guides/claude-code-guidelines' },
            ],
          },
          {
            text: 'Reference',
            collapsed: true,
            items: [
              { text: 'API Documentation', link: '/reference/API-DOCUMENTATION' },
              { text: 'Architecture', link: '/reference/architecture' },
              { text: 'Project Structure', link: '/reference/project-structure' },
              { text: 'Development Status', link: '/reference/development-status' },
              { text: 'Security Requirements', link: '/reference/SECURITY-REQUIREMENTS' },
              { text: 'OpenAPI Decisions', link: '/reference/openapi-documentation-decisions' },
              {
                text: 'Data & Transactions',
                collapsed: true,
                items: [
                  { text: 'Blockchain Format', link: '/reference/blockchain-transaction-format' },
                  { text: 'Transaction Flow', link: '/reference/transaction-submission-flow' },
                  { text: 'Data Persistence', link: '/reference/data-persistence-architecture' },
                ],
              },
              {
                text: 'Cryptography & Wallets',
                collapsed: true,
                items: [
                  { text: 'Wallet Encryption', link: '/reference/wallet-encryption-architecture' },
                  { text: 'Crypto & Quantum Analysis', link: '/reference/cryptography-zkp-quantum-analysis' },
                ],
              },
              {
                text: 'Service Reference',
                collapsed: true,
                items: [
                  { text: 'Validator Design', link: '/reference/validator-service-design' },
                    { text: 'Architecture Decisions', link: '/reference/architecture/ADR-005-Validator-Service-Security-Boundary' },
                ],
              },
            ],
          },
          {
            text: 'Onboarding',
            collapsed: true,
            items: [
              { text: 'Organization Integration', link: '/onboarding/organization-integration' },
              { text: 'Public User Setup', link: '/onboarding/public-user-setup' },
            ],
          },
          {
            text: 'Security',
            collapsed: true,
            items: [
              { text: 'Security Audit 2026', link: '/security/SECURITY-AUDIT-2026-03-19' },
            ],
          },
          {
            text: 'Services',
            collapsed: true,
            items: [
              { text: 'Overview', link: '/services/' },
              { text: 'API Gateway', link: '/services/api-gateway' },
              { text: 'Blueprint Service', link: '/services/blueprint-service' },
              { text: 'Register Service', link: '/services/register-service' },
              { text: 'Tenant Service', link: '/services/tenant-service' },
              { text: 'Wallet Service', link: '/services/wallet-service' },
              { text: 'Validator Service', link: '/services/validator-service' },
              { text: 'Peer Service', link: '/services/peer-service' },
            ],
          },
        ],
      },

      socialLinks: [
        { icon: 'github', link: 'https://github.com/Sorcha-Platform/Sorcha' },
      ],

      editLink: {
        pattern: 'https://github.com/Sorcha-Platform/Sorcha/edit/master/docs/:path',
        text: 'Edit this page on GitHub',
      },

      footer: {
        message: 'Released under the MIT License.',
        copyright: 'Copyright 2026 Sorcha Contributors',
      },
    },

    mermaid: {},
  })
)
