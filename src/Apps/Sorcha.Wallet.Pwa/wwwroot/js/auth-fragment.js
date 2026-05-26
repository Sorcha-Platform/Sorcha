// SPDX-License-Identifier: MIT
// Captures an OAuth fragment-return token from the URL hash BEFORE Blazor boots,
// so the router can't redirect a signed-out user to /signin and lose the token.
// The social callback redirects to /wallet/#token=…&refresh=…&expires_in=…[&returnUrl=…].
(function () {
    window.sorchaAuthFragment = window.sorchaAuthFragment || {};
    var pending = null;
    try {
        var hash = window.location.hash || '';
        if (hash.indexOf('token=') !== -1) {
            var params = new URLSearchParams(hash.replace(/^#/, ''));
            var token = params.get('token');
            if (token) {
                pending = {
                    token: token,
                    refresh: params.get('refresh'),
                    expiresIn: parseInt(params.get('expires_in') || '0', 10),
                    returnUrl: params.get('returnUrl')
                };
                history.replaceState(null, '', window.location.pathname + window.location.search);
            }
        }
    } catch (e) { pending = null; }

    window.sorchaAuthFragment.consume = function () {
        var p = pending;
        pending = null;
        return p; // null when nothing was staged
    };
})();
