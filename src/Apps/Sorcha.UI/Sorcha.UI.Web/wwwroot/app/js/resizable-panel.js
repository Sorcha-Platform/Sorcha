// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

// Resizable panel drag handler — runs entirely in JS for smooth 60fps resizing,
// calls back to .NET only on drag-end.
window.resizablePanel = {
    init: function (splitterElement, dotNetRef, defaultHeightPercent, minTopPx, minBottomPx, storageKey) {
        var stored = this.getStoredHeight(storageKey, defaultHeightPercent);
        var container = splitterElement ? splitterElement.parentElement : null;
        if (!container) return stored;

        var bottomPanel = container.querySelector('[data-role="bottom-panel"]');
        if (bottomPanel) {
            bottomPanel.style.height = stored + '%';
        }

        return stored;
    },

    startDrag: function (splitterElement, dotNetRef, startY, containerElement, minTopPx, minBottomPx, storageKey) {
        if (!containerElement) return;

        var bottomPanel = containerElement.querySelector('[data-role="bottom-panel"]');
        if (!bottomPanel) return;

        var containerRect = containerElement.getBoundingClientRect();

        function onMove(e) {
            e.preventDefault();
            var y = e.clientY - containerRect.top;
            var bottomHeight = containerRect.height - y;

            // Clamp: ensure top area has at least minTopPx and bottom has at least minBottomPx
            if (y < minTopPx) {
                bottomHeight = containerRect.height - minTopPx;
            }
            if (bottomHeight < minBottomPx) {
                bottomHeight = minBottomPx;
            }
            if (containerRect.height - bottomHeight < minTopPx) {
                bottomHeight = containerRect.height - minTopPx;
            }

            var percent = (bottomHeight / containerRect.height) * 100;
            var clamped = Math.min(85, Math.max(5, percent));
            bottomPanel.style.height = clamped.toFixed(1) + '%';
        }

        function onUp(e) {
            document.removeEventListener('pointermove', onMove);
            document.removeEventListener('pointerup', onUp);

            // Read the final height percentage
            var finalPercent = parseFloat(bottomPanel.style.height) || 40;

            // Save to localStorage
            try {
                localStorage.setItem(storageKey, finalPercent.toString());
            } catch (_) { }

            // Notify .NET
            dotNetRef.invokeMethodAsync('OnResizeComplete', finalPercent);
        }

        document.addEventListener('pointermove', onMove);
        document.addEventListener('pointerup', onUp);
    },

    getStoredHeight: function (storageKey, defaultValue) {
        try {
            var stored = localStorage.getItem(storageKey);
            if (stored !== null) {
                var parsed = parseFloat(stored);
                if (!isNaN(parsed) && parsed >= 5 && parsed <= 85) {
                    return parsed;
                }
            }
        } catch (_) { }
        return defaultValue;
    }
};
