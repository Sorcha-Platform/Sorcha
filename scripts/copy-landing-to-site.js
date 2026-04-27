// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Copies the Sorcha.UI landing page (index.html, landing.css, landing.js)
// into docs/site/ for the www.sorcha.dev GitHub Pages deploy, rewriting
// UI-relative links to their public absolute URLs.
//
// Single source of truth: src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/
// The outputs in docs/site/ are gitignored — this script is the only writer.

const fs = require('fs')
const path = require('path')

const root = path.resolve(__dirname, '..')
const srcDir = path.join(root, 'src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot')
const destDir = path.join(root, 'docs/site')

// Files copied verbatim (no rewrites).
const verbatim = ['landing.css', 'landing.js', 'consent-banner.js']

// Link rewrites applied to index.html only. The LHS is what appears in the UI
// source (served under the API gateway); the RHS is the public URL used on
// www.sorcha.dev. Order matters: more specific rules first.
//
// TODO: Add new entries here whenever the UI landing page introduces a new
//       UI-relative href. Missing entries will ship broken links to www.
const linkRewrites = [
  { from: 'href="/auth/login"', to: 'href="https://n1.sorcha.dev/auth/login"' },
  { from: 'href="/scalar/v1"', to: 'href="https://n1.sorcha.dev/scalar/v1"' },
  { from: 'href="/app/help"', to: 'href="https://docs.sorcha.dev"' },
]

// Injected into <head> so the public page advertises the canonical URL.
const canonicalTag = '    <link rel="canonical" href="https://www.sorcha.dev">\n'

function rewriteIndex(html) {
  let out = html
  for (const { from, to } of linkRewrites) {
    if (!out.includes(from)) {
      console.warn(`Warning: rewrite rule '${from}' did not match — UI source may have changed`)
      continue
    }
    out = out.split(from).join(to)
  }
  if (!out.includes('rel="canonical"')) {
    out = out.replace('</head>', canonicalTag + '</head>')
  }
  return out
}

function copy(file, transform) {
  const srcPath = path.join(srcDir, file)
  const destPath = path.join(destDir, file)
  if (!fs.existsSync(srcPath)) {
    console.error(`Error: ${srcPath} not found`)
    process.exit(1)
  }
  let content = fs.readFileSync(srcPath, 'utf-8')
  if (transform) content = transform(content)
  fs.mkdirSync(path.dirname(destPath), { recursive: true })
  fs.writeFileSync(destPath, content)
  console.log(`Copied ${file}${transform ? ' (rewritten)' : ''}`)
}

copy('index.html', rewriteIndex)
for (const file of verbatim) copy(file)
