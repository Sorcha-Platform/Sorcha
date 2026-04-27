<!--
  SPDX-License-Identifier: MIT
  Copyright (c) 2026 Sorcha Contributors

  Cookie consent banner for the docs site.

  Shows on first visit (no `sorcha-consent` localStorage entry). Two
  outcomes:
    - Accept  → gtag('consent','update',{ analytics_storage: 'granted' })
    - Decline → keeps the default-denied stance from config.mts head

  The user can change their mind at any time via the small
  "Cookie preferences" link rendered alongside the banner trigger
  (exposed by `openConsent()` on window for custom footer wiring).
-->
<script setup lang="ts">
import { onMounted, ref } from 'vue'

const STORAGE_KEY = 'sorcha-consent'
const visible = ref(false)

function applyConsent(state: 'granted' | 'denied') {
  // gtag is loaded by the script tag in config.mts head — guard for SSR.
  if (typeof window !== 'undefined' && (window as any).gtag) {
    ;(window as any).gtag('consent', 'update', {
      analytics_storage: state,
    })
  }
}

function accept() {
  localStorage.setItem(STORAGE_KEY, 'granted')
  applyConsent('granted')
  visible.value = false
}

function decline() {
  localStorage.setItem(STORAGE_KEY, 'denied')
  applyConsent('denied')
  visible.value = false
}

onMounted(() => {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'granted') {
    applyConsent('granted')
  } else if (!stored) {
    // First visit — show the banner. Default-denied is already applied
    // by config.mts head, so we don't need to re-apply on this branch.
    visible.value = true
  }

  // Expose a function so a footer link can re-open the banner later.
  ;(window as any).openConsent = () => {
    visible.value = true
  }
})
</script>

<template>
  <Transition name="consent-fade">
    <div v-if="visible" class="consent-banner" role="dialog" aria-live="polite" aria-label="Cookie consent">
      <div class="consent-text">
        <strong>We use cookies for analytics.</strong>
        We use Google Analytics to understand how this documentation is used so we can improve it.
        No advertising or cross-site tracking. See our
        <a href="/privacy" rel="noopener">privacy policy</a> for details.
      </div>
      <div class="consent-actions">
        <button class="consent-btn consent-btn--secondary" @click="decline">Decline</button>
        <button class="consent-btn consent-btn--primary" @click="accept">Accept</button>
      </div>
    </div>
  </Transition>
</template>

<style scoped>
.consent-banner {
  position: fixed;
  bottom: 1rem;
  left: 1rem;
  right: 1rem;
  max-width: 720px;
  margin: 0 auto;
  padding: 1rem 1.25rem;
  background: var(--vp-c-bg-elv);
  color: var(--vp-c-text-1);
  border: 1px solid var(--vp-c-divider);
  border-radius: 12px;
  box-shadow: 0 12px 32px rgba(0, 0, 0, 0.18);
  display: flex;
  flex-direction: column;
  gap: 0.875rem;
  z-index: 100;
  font-size: 0.9rem;
  line-height: 1.5;
}

@media (min-width: 720px) {
  .consent-banner {
    flex-direction: row;
    align-items: center;
    gap: 1.25rem;
  }
}

.consent-text {
  flex: 1;
}

.consent-text a {
  color: var(--vp-c-brand-1);
  text-decoration: underline;
  text-underline-offset: 2px;
}

.consent-actions {
  display: flex;
  gap: 0.5rem;
  flex-shrink: 0;
}

.consent-btn {
  appearance: none;
  border: 1px solid var(--vp-c-divider);
  background: transparent;
  color: var(--vp-c-text-1);
  font: inherit;
  font-weight: 500;
  padding: 0.5rem 1rem;
  border-radius: 8px;
  cursor: pointer;
  transition: background 0.15s, border-color 0.15s;
}

.consent-btn:hover {
  background: var(--vp-c-bg-soft);
}

.consent-btn--primary {
  background: var(--vp-c-brand-1);
  border-color: var(--vp-c-brand-1);
  color: var(--vp-c-white);
}

.consent-btn--primary:hover {
  background: var(--vp-c-brand-2);
  border-color: var(--vp-c-brand-2);
}

.consent-fade-enter-active,
.consent-fade-leave-active {
  transition: opacity 0.2s, transform 0.2s;
}
.consent-fade-enter-from,
.consent-fade-leave-to {
  opacity: 0;
  transform: translateY(8px);
}
</style>
