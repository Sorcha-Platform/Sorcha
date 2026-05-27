// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Feature 107 — Assured Identity v1 — client-side portrait token resizer.
// Scales and centre-crops a source image (bytes) to the target dimensions,
// encodes as JPEG at the requested quality, and returns the raw byte array.
// The C# PhotoTokenResizer drives the quality-stepping loop.

// Reused across quality steps (up to 5 per upload) — saves GC churn on
// low-end mobile during camera capture.
let sharedCanvas = null;

function getCanvas(width, height) {
    if (!sharedCanvas) {
        sharedCanvas = document.createElement('canvas');
    }
    sharedCanvas.width = width;
    sharedCanvas.height = height;
    return sharedCanvas;
}

export async function resizeAndEncodeJpeg(sourceBytes, width, height, quality) {
    const blob = new Blob([new Uint8Array(sourceBytes)]);
    const url = URL.createObjectURL(blob);
    try {
        const image = await loadImage(url);

        const canvas = getCanvas(width, height);
        const ctx = canvas.getContext('2d');
        if (!ctx) {
            throw new Error('2D canvas context not available.');
        }
        // Setting width/height implicitly clears the canvas, but be explicit
        // for the case where width and height match the previous step.
        ctx.clearRect(0, 0, width, height);

        // Cover-style scaling — compute the scale so the image fills the
        // target exactly, then centre-crop the overflow.
        const scale = Math.max(width / image.width, height / image.height);
        const drawWidth = image.width * scale;
        const drawHeight = image.height * scale;
        const dx = (width - drawWidth) / 2;
        const dy = (height - drawHeight) / 2;

        ctx.drawImage(image, dx, dy, drawWidth, drawHeight);

        const encodedBlob = await new Promise((resolve, reject) => {
            canvas.toBlob(
                (b) => b ? resolve(b) : reject(new Error('JPEG encode failed.')),
                'image/jpeg',
                quality);
        });
        const buffer = await encodedBlob.arrayBuffer();
        return new Uint8Array(buffer);
    } finally {
        URL.revokeObjectURL(url);
    }
}

function loadImage(src) {
    return new Promise((resolve, reject) => {
        const img = new Image();
        // Sources are blob: URLs (same-origin, see resizeAndEncodeJpeg) so the
        // canvas is not tainted and toBlob() succeeds. If a future change
        // routes pre-signed cross-origin URLs through here, set
        // img.crossOrigin = 'anonymous' before assigning src and ensure the
        // remote responds with Access-Control-Allow-Origin, otherwise
        // canvas.toBlob will throw a SecurityError.
        img.onload = () => resolve(img);
        img.onerror = (e) => reject(new Error(`Image load failed: ${e?.type ?? 'unknown error'}`));
        img.src = src;
    });
}
