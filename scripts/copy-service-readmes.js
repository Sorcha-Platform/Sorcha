// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Copies service README.md files into docs/services/ for VitePress to serve.
// These copies are gitignored — run this as a prebuild step.

const fs = require('fs')
const path = require('path')

const services = [
  { src: 'src/Services/Sorcha.ApiGateway/README.md', dest: 'docs/services/api-gateway.md' },
  { src: 'src/Services/Sorcha.Blueprint.Service/README.md', dest: 'docs/services/blueprint-service.md' },
  { src: 'src/Services/Sorcha.Register.Service/README.md', dest: 'docs/services/register-service.md' },
  { src: 'src/Services/Sorcha.Tenant.Service/README.md', dest: 'docs/services/tenant-service.md' },
  { src: 'src/Services/Sorcha.Wallet.Service/README.md', dest: 'docs/services/wallet-service.md' },
  { src: 'src/Services/Sorcha.Validator.Service/README.md', dest: 'docs/services/validator-service.md' },
  { src: 'src/Services/Sorcha.Peer.Service/README.md', dest: 'docs/services/peer-service.md' },
]

const root = path.resolve(__dirname, '..')

for (const { src, dest } of services) {
  const srcPath = path.join(root, src)
  const destPath = path.join(root, dest)

  if (!fs.existsSync(srcPath)) {
    console.warn(`Warning: ${src} not found, skipping`)
    continue
  }

  let content = fs.readFileSync(srcPath, 'utf-8')

  // Add frontmatter noting this is a generated copy
  const header = `---\neditLink: false\n---\n\n<!-- This file is auto-generated from ${src}. Do not edit directly. -->\n\n`
  content = header + content

  fs.mkdirSync(path.dirname(destPath), { recursive: true })
  fs.writeFileSync(destPath, content)
  console.log(`Copied ${src} -> ${dest}`)
}
