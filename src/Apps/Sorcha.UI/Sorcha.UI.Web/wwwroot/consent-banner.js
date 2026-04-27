// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Cookie consent banner for the www.sorcha.dev landing page.
//
// Shows on first visit (no `sorcha-consent` localStorage entry). Two outcomes:
//   - Accept  → gtag('consent','update',{ analytics_storage: 'granted' })
//   - Decline → keeps the default-denied stance from index.html head
//
// window.openConsent() is exposed so a footer link can re-open the banner later.

(function () {
    const STORAGE_KEY = 'sorcha-consent';

    function applyConsent(state) {
        if (typeof window.gtag === 'function') {
            window.gtag('consent', 'update', { analytics_storage: state });
        }
    }

    function injectStyles() {
        if (document.getElementById('sorcha-consent-style')) return;
        const style = document.createElement('style');
        style.id = 'sorcha-consent-style';
        style.textContent = `
            .sorcha-consent {
                position: fixed;
                bottom: 1rem;
                left: 1rem;
                right: 1rem;
                max-width: 720px;
                margin: 0 auto;
                padding: 1rem 1.25rem;
                background: #ffffff;
                color: #111827;
                border: 1px solid #e5e7eb;
                border-radius: 12px;
                box-shadow: 0 12px 32px rgba(0, 0, 0, 0.18);
                display: flex;
                flex-direction: column;
                gap: 0.875rem;
                z-index: 9999;
                font-family: 'Inter', system-ui, -apple-system, sans-serif;
                font-size: 0.9rem;
                line-height: 1.5;
            }
            @media (min-width: 720px) {
                .sorcha-consent {
                    flex-direction: row;
                    align-items: center;
                    gap: 1.25rem;
                }
            }
            .sorcha-consent__text { flex: 1; }
            .sorcha-consent__text strong { color: #111827; }
            .sorcha-consent__text a {
                color: #4f46e5;
                text-decoration: underline;
                text-underline-offset: 2px;
            }
            .sorcha-consent__actions {
                display: flex;
                gap: 0.5rem;
                flex-shrink: 0;
            }
            .sorcha-consent__btn {
                appearance: none;
                border: 1px solid #d1d5db;
                background: transparent;
                color: #111827;
                font: inherit;
                font-weight: 500;
                padding: 0.5rem 1rem;
                border-radius: 8px;
                cursor: pointer;
                transition: background 0.15s, border-color 0.15s;
            }
            .sorcha-consent__btn:hover { background: #f3f4f6; }
            .sorcha-consent__btn--primary {
                background: #4f46e5;
                border-color: #4f46e5;
                color: #ffffff;
            }
            .sorcha-consent__btn--primary:hover {
                background: #4338ca;
                border-color: #4338ca;
            }
            @media (prefers-color-scheme: dark) {
                .sorcha-consent {
                    background: #1f2937;
                    color: #f3f4f6;
                    border-color: #374151;
                }
                .sorcha-consent__text strong { color: #f3f4f6; }
                .sorcha-consent__btn {
                    border-color: #4b5563;
                    color: #f3f4f6;
                }
                .sorcha-consent__btn:hover { background: #374151; }
            }
        `;
        document.head.appendChild(style);
    }

    function showBanner() {
        injectStyles();
        const banner = document.createElement('div');
        banner.className = 'sorcha-consent';
        banner.setAttribute('role', 'dialog');
        banner.setAttribute('aria-live', 'polite');
        banner.setAttribute('aria-label', 'Cookie consent');
        banner.innerHTML = `
            <div class="sorcha-consent__text">
                <strong>We use cookies for analytics.</strong>
                We use Google Analytics to understand how this site is used so we can improve it.
                No advertising or cross-site tracking. See our
                <a href="https://docs.sorcha.dev/privacy" rel="noopener">privacy policy</a> for details.
            </div>
            <div class="sorcha-consent__actions">
                <button class="sorcha-consent__btn" data-action="decline">Decline</button>
                <button class="sorcha-consent__btn sorcha-consent__btn--primary" data-action="accept">Accept</button>
            </div>
        `;
        banner.querySelector('[data-action="accept"]').addEventListener('click', function () {
            localStorage.setItem(STORAGE_KEY, 'granted');
            applyConsent('granted');
            banner.remove();
        });
        banner.querySelector('[data-action="decline"]').addEventListener('click', function () {
            localStorage.setItem(STORAGE_KEY, 'denied');
            applyConsent('denied');
            banner.remove();
        });
        document.body.appendChild(banner);
    }

    function init() {
        const stored = localStorage.getItem(STORAGE_KEY);
        if (stored === 'granted') {
            applyConsent('granted');
        } else if (!stored) {
            showBanner();
        }
        window.openConsent = showBanner;
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
