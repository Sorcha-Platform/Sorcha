// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Feature 107 — Assured Identity v1 — client-side portrait token resizer.
// Scales and centre-crops a source image (bytes) to the target dimensions,
// encodes as JPEG at the requested quality, and returns the raw byte array.
// The C# PhotoTokenResizer drives the quality-stepping loop.

export async function resizeAndEncodeJpeg(sourceBytes, width, height, quality) {
    const blob = new Blob([new Uint8Array(sourceBytes)]);
    const url = URL.createObjectURL(blob);
    try {
        const image = await loadImage(url);

        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        const ctx = canvas.getContext('2d');
        if (!ctx) {
            throw new Error('2D canvas context not available.');
        }

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
        img.onload = () => resolve(img);
        img.onerror = (e) => reject(e);
        img.src = src;
    });
}
