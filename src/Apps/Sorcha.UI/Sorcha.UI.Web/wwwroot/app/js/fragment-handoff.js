// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Fragment token handoff for server-rendered auth pages.
// After login, the server redirects to /app/#token=...&refresh=...&returnUrl=...
// This script extracts the tokens from the fragment, clears the URL, and returns them.
window.sorcha = window.sorcha || {};
window.sorcha.fragmentHandoff = {
    getAndClear: function () {
        var hash = window.location.hash;
        if (!hash || hash.length < 2) return null;

        // Clear the fragment from the URL immediately to prevent token leakage
        history.replaceState(null, '', window.location.pathname + window.location.search);

        var params = new URLSearchParams(hash.substring(1));
        var token = params.get('token');
        var refresh = params.get('refresh');
        var returnUrl = params.get('returnUrl');

        if (!token) return null;

        return { token: token, refresh: refresh, returnUrl: returnUrl };
    }
};
