// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

import DefaultTheme from 'vitepress/theme'
import type { EnhanceAppContext } from 'vitepress'
import { h } from 'vue'
import { MermaidPlugin } from 'vitepress-plugin-mermaid'
import ConsentBanner from './ConsentBanner.vue'

export default {
  extends: DefaultTheme,
  // Inject the cookie consent banner into the default layout's
  // `layout-bottom` slot so it sits above page content on every route.
  Layout: () => h(DefaultTheme.Layout, null, {
    'layout-bottom': () => h(ConsentBanner),
  }),
  enhanceApp({ app }: EnhanceAppContext) {
    app.use(MermaidPlugin)
  },
}
