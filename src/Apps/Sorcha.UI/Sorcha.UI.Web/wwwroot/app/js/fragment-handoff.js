// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Fragment token handoff for server-rendered auth pages.
// After login, the server redirects to /app/#token=...&refresh=...&returnUrl=...
// This script eagerly extracts the tokens from the fragment on load (before Blazor boots),
// clears the URL to prevent token leakage, and stages them for the auth provider.
//
// The token is staged in both localStorage and window.__sorcha_fragment_token (a temporary
// window global) because Blazor WASM's JS interop may not be able to read localStorage
// set by inline scripts in all contexts. The window global is cleared as soon as the
// auth provider consumes it. The exposure window is sub-second during WASM boot only.
window.sorcha = window.sorcha || {};
window.sorcha.fragmentHandoff = (function () {
    var _pending = null;
    var _returnUrl = null;

    // Eagerly extract token from URL fragment on script load (before Blazor boots).
    // This prevents the race condition where AuthorizeRouteView evaluates auth state
    // before FragmentTokenHandler.OnAfterRenderAsync can extract the token.
    var hash = window.location.hash;
    if (hash && hash.length > 1) {
        var params = new URLSearchParams(hash.substring(1));
        var token = params.get('token');
        if (token) {
            var payload = JSON.stringify({ token: token, refresh: params.get('refresh') });
            _pending = {
                token: token,
                refresh: params.get('refresh'),
                returnUrl: params.get('returnUrl')
            };
            _returnUrl = params.get('returnUrl');

            // Stage the token in localStorage and as a window global.
            // The auth provider reads these via JS interop during GetAuthenticationStateAsync.
            try { localStorage.setItem('sorcha:fragment-pending', payload); } catch (e) {}
            window.__sorcha_fragment_token = payload;

            // Clear the fragment from the URL immediately to prevent token leakage
            history.replaceState(null, '', window.location.pathname + window.location.search);
        }
    }

    return {
        // Returns the pending token and clears all staging locations
        getAndClear: function () {
            var result = _pending;
            _pending = null;
            window.__sorcha_fragment_token = null;
            try { localStorage.removeItem('sorcha:fragment-pending'); } catch (e) {}
            return result;
        },
        // Returns and clears the return URL (consumed by FragmentTokenHandler after auth completes)
        getReturnUrl: function () {
            var url = _returnUrl;
            _returnUrl = null;
            return url;
        },
        // Returns the window-global staged token (CSP-friendly alternative to eval)
        getWindowToken: function () {
            return window.__sorcha_fragment_token || null;
        },
        // Clears token staging locations (preserves returnUrl for FragmentTokenHandler)
        clearTokenStaging: function () {
            _pending = null;
            window.__sorcha_fragment_token = null;
            try { localStorage.removeItem('sorcha:fragment-pending'); } catch (e) {}
        }
    };
})();
