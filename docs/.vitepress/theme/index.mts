// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

import DefaultTheme from 'vitepress/theme'
import type { EnhanceAppContext } from 'vitepress'
import { MermaidPlugin } from 'vitepress-plugin-mermaid'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }: EnhanceAppContext) {
    app.use(MermaidPlugin)
  },
}
