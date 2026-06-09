// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Copies the Sorcha.UI marketing site (the landing page, its sub-pages, and the
// shared assets) into docs/site/ for the www.sorcha.dev GitHub Pages deploy.
//
// Single source of truth: src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot/
// The outputs in docs/site/ are gitignored — this script is the only writer.
//
// GitHub Pages serves `foo.html` at the clean URL `/foo`, so the sub-pages keep
// their extensionless internal links (e.g. /developers) and their root-relative
// assets (landing.css, sorcha-icon.svg) resolve from `/`. App-only routes that
// don't exist on the static host (/wallet/, /get) are emitted as redirect stubs
// that bounce to the deployed app on n1.

const fs = require('fs')
const path = require('path')

const root = path.resolve(__dirname, '..')
const srcDir = path.join(root, 'src/Apps/Sorcha.UI/Sorcha.UI.Web/wwwroot')
const destDir = path.join(root, 'docs/site')

// HTML pages. index.html gets link rewrites + a canonical tag; the sub-pages
// already carry their own canonical/OG tags and only use links that resolve on
// the static host (other sub-pages, #anchors, redirect stubs, external URLs),
// so they are copied verbatim.
const pages = [
  { file: 'index.html', rewrite: true },
  { file: 'wallet-info.html' },
  { file: 'designer-overview.html' },
  { file: 'solutions.html' },
  { file: 'compare.html' },
  { file: 'developers.html' },
  { file: 'contact.html' },
]

// Text assets copied verbatim.
const textAssets = ['landing.css', 'landing.js', 'consent-banner.js']

// Binary assets (OG preview images + favicons/app icons referenced by the pages).
const binAssets = [
  'og-default.png', 'og-wallet.png', 'og-designer.png',
  'sorcha-icon.svg', 'sorcha.ico', 'favicon.png',
  'sorcha-icon-128.png', 'sorcha-icon-256.png', 'sorcha-icon-512.png',
]

// Link rewrites applied to index.html only. The LHS is what appears in the UI
// source (served under the API gateway); the RHS is the public URL on
// www.sorcha.dev. Order matters: more specific rules first.
//
// TODO: Add an entry here whenever index.html introduces a UI-relative href that
//       is NOT a marketing sub-page and NOT one of the redirect stubs below.
const linkRewrites = []

// Injected into <head> so the public page advertises the canonical URL.
const canonicalTag = '    <link rel="canonical" href="https://www.sorcha.dev">\n'

// Standalone redirect stubs for app-only routes the static host doesn't have.
const redirects = [
  { path: 'wallet', to: 'https://n1.sorcha.dev/wallet' },
  { path: 'get', to: 'https://n1.sorcha.dev/get' },
]

function redirectHtml(to) {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Redirecting…</title>
  <meta name="robots" content="noindex">
  <link rel="canonical" href="${to}">
  <meta http-equiv="refresh" content="0; url=${to}">
  <script>location.replace(${JSON.stringify(to)} + location.search + location.hash)</script>
</head>
<body>
  <p>Redirecting to <a href="${to}">${to}</a>…</p>
</body>
</html>
`
}

function writeRedirect({ path: routePath, to }) {
  const destPath = path.join(destDir, routePath, 'index.html')
  fs.mkdirSync(path.dirname(destPath), { recursive: true })
  fs.writeFileSync(destPath, redirectHtml(to))
  console.log(`Wrote redirect /${routePath}/ -> ${to}`)
}

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

function copyText(file, transform) {
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

function copyBinary(file) {
  const srcPath = path.join(srcDir, file)
  const destPath = path.join(destDir, file)
  if (!fs.existsSync(srcPath)) {
    console.error(`Error: ${srcPath} not found`)
    process.exit(1)
  }
  fs.mkdirSync(path.dirname(destPath), { recursive: true })
  fs.copyFileSync(srcPath, destPath)
  console.log(`Copied ${file} (binary)`)
}

for (const { file, rewrite } of pages) copyText(file, rewrite ? rewriteIndex : undefined)
for (const file of textAssets) copyText(file)
for (const file of binAssets) copyBinary(file)
for (const redirect of redirects) writeRedirect(redirect)
