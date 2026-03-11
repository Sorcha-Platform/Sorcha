// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

/**
 * WebAuthn/FIDO2 vanilla JS module for server-rendered Sorcha auth pages.
 * Provides passkey sign-in (assertion) and passkey registration flows.
 * No external dependencies — pure vanilla JS.
 *
 * Usage:
 *   sorcha.webauthn.signIn(optionsUrl, verifyUrl, returnUrl)
 *   sorcha.webauthn.register(optionsUrl, verifyUrl, displayName, email, returnUrl)
 */

var sorcha = sorcha || {};

sorcha.webauthn = (function () {

    // -------------------------------------------------------------------------
    // Utility: ArrayBuffer <-> base64url
    // -------------------------------------------------------------------------

    /**
     * Encodes an ArrayBuffer to a base64url string.
     * @param {ArrayBuffer} buffer
     * @returns {string}
     */
    function base64urlEncode(buffer) {
        var bytes = new Uint8Array(buffer);
        var binary = '';
        for (var i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary)
            .replace(/\+/g, '-')
            .replace(/\//g, '_')
            .replace(/=+$/, '');
    }

    /**
     * Decodes a base64url string to a Uint8Array.
     * @param {string} base64url
     * @returns {Uint8Array}
     */
    function base64urlDecode(base64url) {
        var base64 = base64url
            .replace(/-/g, '+')
            .replace(/_/g, '/');
        while (base64.length % 4 !== 0) {
            base64 += '=';
        }
        var binary = atob(base64);
        var bytes = new Uint8Array(binary.length);
        for (var i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes;
    }

    /**
     * Coerces a value that may be a base64url string or already an ArrayBuffer/Uint8Array
     * to a Uint8Array suitable for the WebAuthn API.
     * @param {string|ArrayBuffer|Uint8Array} thing
     * @returns {Uint8Array}
     */
    function coerceToArrayBuffer(thing) {
        if (typeof thing === 'string') {
            return base64urlDecode(thing);
        }
        if (thing instanceof Uint8Array) {
            return thing;
        }
        if (thing instanceof ArrayBuffer) {
            return new Uint8Array(thing);
        }
        // Handle array of numbers (some server serializers emit this)
        if (Array.isArray(thing)) {
            return new Uint8Array(thing);
        }
        throw new Error('Cannot coerce value to ArrayBuffer: ' + typeof thing);
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /**
     * Prepares PublicKeyCredentialRequestOptions from server JSON for navigator.credentials.get().
     * Decodes base64url-encoded binary fields to ArrayBuffers.
     * @param {object} options - Parsed JSON from the server assertion options endpoint.
     * @returns {object}
     */
    function prepareAssertionOptions(options) {
        var publicKey = options.publicKey || options;

        publicKey.challenge = coerceToArrayBuffer(publicKey.challenge);

        if (publicKey.allowCredentials) {
            publicKey.allowCredentials = publicKey.allowCredentials.map(function (cred) {
                return Object.assign({}, cred, { id: coerceToArrayBuffer(cred.id) });
            });
        }

        return publicKey;
    }

    /**
     * Prepares PublicKeyCredentialCreationOptions from server JSON for navigator.credentials.create().
     * Decodes base64url-encoded binary fields to ArrayBuffers.
     * @param {object} options - Parsed JSON from the server registration options endpoint.
     * @returns {object}
     */
    function prepareCreationOptions(options) {
        var publicKey = options.publicKey || options;

        publicKey.challenge = coerceToArrayBuffer(publicKey.challenge);

        if (publicKey.user && publicKey.user.id) {
            publicKey.user.id = coerceToArrayBuffer(publicKey.user.id);
        }

        if (publicKey.excludeCredentials) {
            publicKey.excludeCredentials = publicKey.excludeCredentials.map(function (cred) {
                return Object.assign({}, cred, { id: coerceToArrayBuffer(cred.id) });
            });
        }

        return publicKey;
    }

    /**
     * Serializes an AuthenticatorAssertionResponse for JSON transport.
     * Maps ArrayBuffer fields to base64url strings.
     * @param {PublicKeyCredential} credential
     * @returns {object} Serialized assertion response matching AuthenticatorAssertionRawResponse.
     */
    function serializeAssertionResponse(credential) {
        var resp = credential.response;
        return {
            id: credential.id,
            rawId: base64urlEncode(credential.rawId),
            type: credential.type,
            response: {
                authenticatorData: base64urlEncode(resp.authenticatorData),
                clientDataJSON: base64urlEncode(resp.clientDataJSON),
                signature: base64urlEncode(resp.signature),
                userHandle: resp.userHandle ? base64urlEncode(resp.userHandle) : null
            },
            extensions: credential.getClientExtensionResults
                ? credential.getClientExtensionResults()
                : null
        };
    }

    /**
     * Serializes an AuthenticatorAttestationResponse for JSON transport.
     * Maps ArrayBuffer fields to base64url strings.
     * @param {PublicKeyCredential} credential
     * @returns {object} Serialized attestation response matching AuthenticatorAttestationRawResponse.
     */
    function serializeAttestationResponse(credential) {
        var resp = credential.response;
        return {
            id: credential.id,
            rawId: base64urlEncode(credential.rawId),
            type: credential.type,
            response: {
                attestationObject: base64urlEncode(resp.attestationObject),
                clientDataJSON: base64urlEncode(resp.clientDataJSON)
            },
            extensions: credential.getClientExtensionResults
                ? credential.getClientExtensionResults()
                : null
        };
    }

    /**
     * Builds the redirect URL to the Sorcha app with token hash parameters.
     * @param {string} accessToken
     * @param {string} refreshToken
     * @param {string} [returnUrl]
     * @returns {string}
     */
    function buildAppRedirectUrl(accessToken, refreshToken, returnUrl) {
        var hash = 'token=' + encodeURIComponent(accessToken)
                 + '&refresh=' + encodeURIComponent(refreshToken);
        if (returnUrl) {
            hash += '&returnUrl=' + encodeURIComponent(returnUrl);
        }
        return '/app/#' + hash;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /**
     * Runs the passkey sign-in (assertion) flow.
     *
     * Steps:
     *  1. POST optionsUrl to get assertion challenge options and transaction ID.
     *  2. Call navigator.credentials.get() with the decoded options.
     *  3. Serialize the assertion response and POST to verifyUrl.
     *  4. On success: redirect to /app/ with token hash.
     *  5. On failure: show alert with error message.
     *
     * @param {string} optionsUrl  - URL for POST /api/auth/passkey/assertion/options
     * @param {string} verifyUrl   - URL for POST /api/auth/passkey/assertion/verify
     * @param {string} [returnUrl] - Optional return URL after sign-in
     * @returns {Promise<void>}
     */
    async function signIn(optionsUrl, verifyUrl, returnUrl) {
        try {
            // Step 1: Get assertion options from server
            var optionsResponse = await fetch(optionsUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({})
            });

            if (!optionsResponse.ok) {
                var errorData = await optionsResponse.json().catch(function () { return null; });
                var errorMsg = (errorData && (errorData.detail || errorData.title || errorData.error))
                    || 'Failed to get passkey options. Please try again.';
                alert(errorMsg);
                return;
            }

            var optionsData = await optionsResponse.json();
            var transactionId = optionsData.transaction_id;
            var publicKeyOptions = prepareAssertionOptions(optionsData.options);

            // Step 2: Invoke browser WebAuthn API
            var credential = await navigator.credentials.get({ publicKey: publicKeyOptions });

            if (!credential) {
                alert('Passkey sign-in was cancelled or no credential was selected.');
                return;
            }

            // Step 3: Serialize and POST assertion response to server
            var assertionPayload = {
                transaction_id: transactionId,
                assertion_response: serializeAssertionResponse(credential)
            };

            var verifyResponse = await fetch(verifyUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(assertionPayload)
            });

            if (!verifyResponse.ok) {
                var verifyError = await verifyResponse.json().catch(function () { return null; });
                var verifyMsg = (verifyError && (verifyError.detail || verifyError.title || verifyError.error))
                    || 'Passkey verification failed. Please try again.';
                alert(verifyMsg);
                return;
            }

            // Step 4: Redirect to app with tokens in hash
            var tokenData = await verifyResponse.json();
            window.location.href = buildAppRedirectUrl(
                tokenData.access_token,
                tokenData.refresh_token,
                returnUrl
            );

        } catch (err) {
            if (err && err.name === 'NotAllowedError') {
                alert('Passkey sign-in was cancelled or timed out. Please try again.');
            } else if (err && err.name === 'SecurityError') {
                alert('Passkey sign-in is not available on this page. Please use HTTPS.');
            } else {
                alert('Passkey sign-in failed: ' + (err && err.message ? err.message : String(err)));
            }
        }
    }

    /**
     * Runs the passkey registration (attestation) flow for new user signup.
     *
     * Steps:
     *  1. POST optionsUrl with { display_name, email } to get registration challenge and transaction ID.
     *  2. Call navigator.credentials.create() with the decoded options.
     *  3. Serialize the attestation response and POST to verifyUrl.
     *  4. On success: redirect to /app/ with token hash.
     *  5. On failure: show alert with error message.
     *
     * @param {string} optionsUrl   - URL for POST /api/auth/public/passkey/register/options
     * @param {string} verifyUrl    - URL for POST /api/auth/public/passkey/register/verify
     * @param {string} displayName  - Human-readable display name for the new user
     * @param {string} email        - Email address for the new user
     * @param {string} [returnUrl]  - Optional return URL after registration
     * @returns {Promise<void>}
     */
    async function register(optionsUrl, verifyUrl, displayName, email, returnUrl) {
        try {
            // Step 1: Get registration options from server
            var optionsResponse = await fetch(optionsUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    display_name: displayName,
                    email: email
                })
            });

            if (!optionsResponse.ok) {
                var statusCode = optionsResponse.status;
                var errorData = await optionsResponse.json().catch(function () { return null; });

                if (statusCode === 409) {
                    alert('An account with this email address already exists. Please sign in instead.');
                    return;
                }

                var errorMsg = (errorData && (errorData.detail || errorData.title || errorData.error))
                    || 'Failed to get passkey registration options. Please try again.';
                alert(errorMsg);
                return;
            }

            var optionsData = await optionsResponse.json();
            var transactionId = optionsData.transaction_id;
            var publicKeyOptions = prepareCreationOptions(optionsData.options);

            // Step 2: Invoke browser WebAuthn API
            var credential = await navigator.credentials.create({ publicKey: publicKeyOptions });

            if (!credential) {
                alert('Passkey registration was cancelled. Please try again.');
                return;
            }

            // Step 3: Serialize and POST attestation response to server
            var attestationPayload = {
                transaction_id: transactionId,
                attestation_response: serializeAttestationResponse(credential)
            };

            var verifyResponse = await fetch(verifyUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(attestationPayload)
            });

            if (!verifyResponse.ok) {
                var verifyError = await verifyResponse.json().catch(function () { return null; });
                var verifyMsg = (verifyError && (verifyError.detail || verifyError.title || verifyError.error))
                    || 'Passkey registration failed. Please try again.';
                alert(verifyMsg);
                return;
            }

            // Step 4: Redirect to app with tokens in hash
            var tokenData = await verifyResponse.json();
            window.location.href = buildAppRedirectUrl(
                tokenData.access_token,
                tokenData.refresh_token,
                returnUrl
            );

        } catch (err) {
            if (err && err.name === 'NotAllowedError') {
                alert('Passkey registration was cancelled or timed out. Please try again.');
            } else if (err && err.name === 'InvalidStateError') {
                alert('A passkey for this account is already registered on this device.');
            } else if (err && err.name === 'SecurityError') {
                alert('Passkey registration is not available on this page. Please use HTTPS.');
            } else {
                alert('Passkey registration failed: ' + (err && err.message ? err.message : String(err)));
            }
        }
    }

    // -------------------------------------------------------------------------
    // Expose public API
    // -------------------------------------------------------------------------

    return {
        signIn: signIn,
        register: register
    };

}());
