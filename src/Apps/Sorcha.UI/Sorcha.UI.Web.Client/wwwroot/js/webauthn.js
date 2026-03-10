/**
 * WebAuthn/FIDO2 JS interop module for Blazor WASM.
 * Handles navigator.credentials.create() and navigator.credentials.get()
 * with base64url encoding/decoding for server round-trips.
 */

/**
 * Decodes a base64url-encoded string to a Uint8Array.
 * @param {string} base64url - The base64url-encoded string.
 * @returns {Uint8Array} The decoded bytes.
 */
function base64UrlToUint8Array(base64url) {
    // Replace base64url characters with standard base64
    let base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');

    // Pad to multiple of 4
    while (base64.length % 4 !== 0) {
        base64 += '=';
    }

    const binaryString = atob(base64);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
        bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes;
}

/**
 * Encodes an ArrayBuffer to a base64url string.
 * @param {ArrayBuffer} buffer - The buffer to encode.
 * @returns {string} The base64url-encoded string.
 */
function arrayBufferToBase64Url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binaryString = '';
    for (let i = 0; i < bytes.byteLength; i++) {
        binaryString += String.fromCharCode(bytes[i]);
    }
    const base64 = btoa(binaryString);
    // Convert standard base64 to base64url
    return base64.replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

/**
 * Checks if WebAuthn (PublicKeyCredential) is supported by the browser.
 * @returns {boolean} True if WebAuthn is available.
 */
export function isWebAuthnSupported() {
    return typeof window !== 'undefined' &&
           typeof window.PublicKeyCredential !== 'undefined';
}

/**
 * Creates a new WebAuthn credential (registration ceremony).
 * Converts base64url-encoded fields from the server options to ArrayBuffers,
 * calls navigator.credentials.create(), and converts the response back to
 * base64url for the server.
 *
 * @param {string} optionsJson - JSON string of PublicKeyCredentialCreationOptions from the server.
 * @returns {Promise<string>} JSON string of the credential response.
 */
export async function createCredential(optionsJson) {
    const options = JSON.parse(optionsJson);

    // Decode challenge from base64url to ArrayBuffer
    options.challenge = base64UrlToUint8Array(options.challenge);

    // Decode user.id from base64url to ArrayBuffer
    if (options.user && options.user.id) {
        options.user.id = base64UrlToUint8Array(options.user.id);
    }

    // Decode excludeCredentials[].id from base64url to ArrayBuffer
    if (options.excludeCredentials) {
        options.excludeCredentials = options.excludeCredentials.map(cred => ({
            ...cred,
            id: base64UrlToUint8Array(cred.id)
        }));
    }

    const credential = await navigator.credentials.create({
        publicKey: options
    });

    // Convert response ArrayBuffers back to base64url
    const response = {
        id: credential.id,
        rawId: arrayBufferToBase64Url(credential.rawId),
        type: credential.type,
        response: {
            attestationObject: arrayBufferToBase64Url(credential.response.attestationObject),
            clientDataJSON: arrayBufferToBase64Url(credential.response.clientDataJSON)
        }
    };

    return JSON.stringify(response);
}

/**
 * Gets a WebAuthn credential (authentication ceremony).
 * Converts base64url-encoded fields from the server options to ArrayBuffers,
 * calls navigator.credentials.get(), and converts the response back to
 * base64url for the server.
 *
 * @param {string} optionsJson - JSON string of PublicKeyCredentialRequestOptions from the server.
 * @returns {Promise<string>} JSON string of the assertion response.
 */
export async function getCredential(optionsJson) {
    const options = JSON.parse(optionsJson);

    // Decode challenge from base64url to ArrayBuffer
    options.challenge = base64UrlToUint8Array(options.challenge);

    // Decode allowCredentials[].id from base64url to ArrayBuffer
    if (options.allowCredentials) {
        options.allowCredentials = options.allowCredentials.map(cred => ({
            ...cred,
            id: base64UrlToUint8Array(cred.id)
        }));
    }

    const credential = await navigator.credentials.get({
        publicKey: options
    });

    // Convert response ArrayBuffers back to base64url
    const response = {
        id: credential.id,
        rawId: arrayBufferToBase64Url(credential.rawId),
        type: credential.type,
        response: {
            authenticatorData: arrayBufferToBase64Url(credential.response.authenticatorData),
            clientDataJSON: arrayBufferToBase64Url(credential.response.clientDataJSON),
            signature: arrayBufferToBase64Url(credential.response.signature),
            userHandle: credential.response.userHandle
                ? arrayBufferToBase64Url(credential.response.userHandle)
                : null
        }
    };

    return JSON.stringify(response);
}
