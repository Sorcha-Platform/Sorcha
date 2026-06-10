// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Copies service README.md files into docs/services/ for VitePress to serve.
// These copies are gitignored -- run this as a prebuild step.
//
// Link rewriting applied during copy:
//
//   1. Relative links that climb out of the service README to reach docs/:
//        ../../../docs/<path>   (and the ./../../../docs/ variant)
//      are rewritten to VitePress-relative paths from docs/services/:
//        ../<path>
//
//   2. Links into VitePress-excluded directories (superpowers/, archive/,
//      plans/, reference/status/) are rewritten to absolute GitHub URLs so
//      they remain useful on the published docs site.
//
//   3. Links that escape the service dir into other src/ subtrees (e.g.
//      ../../Core/Sorcha.Blueprint.Engine/) are converted to absolute GitHub
//      URLs because after copying to docs/services/ the relative path breaks.
//
//   4. All other links (external URLs, GitHub URLs already in the source,
//      links to specs/ or .specify/ already fixed in the source READMEs)
//      are passed through unchanged.

const fs = require('fs')
const path = require('path')

const GITHUB_BASE = 'https://github.com/Sorcha-Platform/Sorcha/blob/master/docs'
const GITHUB_REPO = 'https://github.com/Sorcha-Platform/Sorcha'

// VitePress srcExclude dirs (relative to docs/).
// Links whose docs-relative path starts with one of these prefixes are
// converted to absolute GitHub URLs instead of VitePress-relative ones.
const EXCLUDED_DIRS = [
  'superpowers/',
  'archive/',
  'plans/',
  'reference/status/',
]

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

/**
 * Rewrites a single Markdown link target found inside a [...](target) construct.
 *
 * Source READMEs live at src/Services/<ServiceName>/README.md (3 levels deep).
 *
 * Pattern 1: ../../../docs/<path>  -- link into docs/ -- rewrite to VitePress-relative
 *            (or GitHub URL if path is inside an excluded docs/ directory).
 * Pattern 2: ../../<path>          -- link that escapes into src/ but NOT into docs/
 *            (e.g. ../../Core/Sorcha.Blueprint.Engine/) -- convert to GitHub tree/blob URL
 *            so the generated docs/services/ copy has a working absolute link.
 */
function rewriteTarget(target) {
  // Pattern 1: three-level climb into docs/
  // Normalise ./../../../docs/  and  ../../../docs/  to a single pattern.
  const docsRelativeMatch = target.match(/^(?:\.\/)?\.\.\/\.\.\/\.\.\/docs\/(.*)$/)
  if (docsRelativeMatch) {
    const docsPath = docsRelativeMatch[1] // e.g. "getting-started/DOCKER-QUICK-START.md"

    // If the path falls inside an excluded directory, use a GitHub absolute URL.
    for (const excluded of EXCLUDED_DIRS) {
      if (docsPath.startsWith(excluded)) {
        return `${GITHUB_BASE}/${docsPath}`
      }
    }

    // Otherwise rewrite to a VitePress-relative path from docs/services/.
    // docs/services/ -> docs/<rest>  requires going up one level: ../<rest>
    return `../${docsPath}`
  }

  // Pattern 2: two-level climb that does NOT land in docs/ -- these are links
  // to source files/dirs elsewhere in the repo (e.g. ../../Core/...).
  // After copying to docs/services/ the relative path would be broken, so
  // convert to an absolute GitHub URL instead.
  const srcRelativeMatch = target.match(/^(?:\.\/)?\.\.\/\.\.\/(?!\.\.\/docs\/)(.*)$/)
  if (srcRelativeMatch) {
    const repoPath = `src/${srcRelativeMatch[1]}`
    // Use /tree/ for directory links (trailing slash), /blob/ for files.
    const isDir = target.endsWith('/')
    const ghMode = isDir ? 'tree' : 'blob'
    const cleanPath = repoPath.replace(/\/$/, '')
    return `${GITHUB_REPO}/${ghMode}/master/${cleanPath}`
  }

  return target
}

/**
 * Rewrite all Markdown link targets in content.
 * Handles both inline links  [text](target)  and  [text](target "title").
 */
function rewriteLinks(content) {
  // Match Markdown links: [anything](target) or [anything](target "title")
  // Use a regex that captures the URL (stopping at whitespace or closing paren).
  return content.replace(/(\[(?:[^\]]*)\]\()([^)"'\s]+)((?:\s+"[^"]*"|\s+'[^']*')?\))/g,
    (match, open, target, close) => {
      const rewritten = rewriteTarget(target)
      return `${open}${rewritten}${close}`
    }
  )
}

for (const { src, dest } of services) {
  const srcPath = path.join(root, src)
  const destPath = path.join(root, dest)

  if (!fs.existsSync(srcPath)) {
    console.warn(`Warning: ${src} not found, skipping`)
    continue
  }

  let content = fs.readFileSync(srcPath, 'utf-8')

  // Rewrite relative links that point into docs/ so they resolve correctly
  // from docs/services/ rather than from src/Services/X/.
  content = rewriteLinks(content)

  // Add frontmatter noting this is a generated copy
  const header = `---\neditLink: false\n---\n\n<!-- This file is auto-generated from ${src}. Do not edit directly. -->\n\n`
  content = header + content

  fs.mkdirSync(path.dirname(destPath), { recursive: true })
  fs.writeFileSync(destPath, content)
  console.log(`Copied ${src} -> ${dest}`)
}
