// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

/**
 * Triggers a file download in the browser.
 * @param {string} fileName - The file name for the download
 * @param {string} mimeType - MIME type (e.g., 'application/json')
 * @param {string} content - The file content as a string
 */
window.downloadFile = function (fileName, mimeType, content) {
    const blob = new Blob([content], { type: mimeType });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};
