// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// SorchaPortrait — browser camera capture + JPEG resize for form portrait fields
// (Feature 174 follow-up; backs IWebCameraService + PortraitCaptureControl.resizeJpeg).
//
// window.SorchaPortrait.isCameraSupported() -> bool
// window.SorchaPortrait.capturePortrait(w, h, quality) -> base64 JPEG (no data: prefix) | null (cancel)
//     throws "camera-permission-denied" when the user blocks the camera.
// window.SorchaPortrait.resizeJpeg(base64, w, h, quality) -> base64 JPEG (no data: prefix)
(function () {
  "use strict";

  function isCameraSupported() {
    return !!(navigator.mediaDevices && typeof navigator.mediaDevices.getUserMedia === "function");
  }

  // Draw a source (video or image) center-cropped to the target aspect ratio, scaled to w×h.
  function drawCropped(source, sw, sh, w, h) {
    const canvas = document.createElement("canvas");
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext("2d");
    const targetAspect = w / h;
    const srcAspect = sw / sh;
    let cropW = sw, cropH = sh, cropX = 0, cropY = 0;
    if (srcAspect > targetAspect) {
      // source too wide — crop the sides
      cropW = Math.round(sh * targetAspect);
      cropX = Math.round((sw - cropW) / 2);
    } else {
      // source too tall — crop top/bottom
      cropH = Math.round(sw / targetAspect);
      cropY = Math.round((sh - cropH) / 2);
    }
    ctx.drawImage(source, cropX, cropY, cropW, cropH, 0, 0, w, h);
    return canvas;
  }

  function toBase64Jpeg(canvas, quality) {
    const url = canvas.toDataURL("image/jpeg", quality);
    const comma = url.indexOf(",");
    return comma >= 0 ? url.substring(comma + 1) : url;
  }

  async function capturePortrait(w, h, quality) {
    if (!isCameraSupported()) {
      throw new Error("camera-not-supported");
    }
    let stream;
    try {
      stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: "user", width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });
    } catch (err) {
      // NotAllowedError / SecurityError => the user (or policy) blocked the camera.
      throw new Error("camera-permission-denied");
    }

    return await new Promise((resolve) => {
      // Full-screen capture overlay: live preview + Capture / Cancel.
      const overlay = document.createElement("div");
      overlay.setAttribute("data-testid", "portrait-camera-overlay");
      overlay.style.cssText =
        "position:fixed;inset:0;z-index:20000;background:rgba(0,0,0,0.92);" +
        "display:flex;flex-direction:column;align-items:center;justify-content:center;gap:16px;padding:16px;";

      const video = document.createElement("video");
      video.autoplay = true;
      video.playsInline = true;
      video.muted = true;
      video.style.cssText = "max-width:min(92vw,480px);max-height:70vh;border-radius:8px;background:#000;transform:scaleX(-1);";
      video.srcObject = stream;

      const bar = document.createElement("div");
      bar.style.cssText = "display:flex;gap:12px;";

      function mkBtn(label, primary, testid) {
        const b = document.createElement("button");
        b.textContent = label;
        b.setAttribute("data-testid", testid);
        b.style.cssText =
          "padding:12px 24px;border:none;border-radius:24px;font-size:16px;cursor:pointer;" +
          (primary ? "background:#f5a623;color:#1a1a1a;font-weight:600;" : "background:#3a3a3a;color:#fff;");
        return b;
      }
      const capBtn = mkBtn("Capture", true, "portrait-camera-capture");
      const cancelBtn = mkBtn("Cancel", false, "portrait-camera-cancel");
      bar.appendChild(capBtn);
      bar.appendChild(cancelBtn);
      overlay.appendChild(video);
      overlay.appendChild(bar);
      document.body.appendChild(overlay);

      function cleanup() {
        try { stream.getTracks().forEach((t) => t.stop()); } catch (_) {}
        if (overlay.parentNode) overlay.parentNode.removeChild(overlay);
      }

      capBtn.addEventListener("click", () => {
        try {
          const sw = video.videoWidth || 1280;
          const sh = video.videoHeight || 720;
          const canvas = drawCropped(video, sw, sh, w, h);
          const b64 = toBase64Jpeg(canvas, quality);
          cleanup();
          resolve(b64);
        } catch (_) {
          cleanup();
          resolve(null);
        }
      });
      cancelBtn.addEventListener("click", () => { cleanup(); resolve(null); });
    });
  }

  function resizeJpeg(base64, w, h, quality) {
    return new Promise((resolve) => {
      const img = new Image();
      img.onload = () => {
        try {
          const canvas = drawCropped(img, img.naturalWidth, img.naturalHeight, w, h);
          resolve(toBase64Jpeg(canvas, quality));
        } catch (_) {
          resolve(base64); // fall back to the original on any canvas error
        }
      };
      img.onerror = () => resolve(base64);
      img.src = "data:image/jpeg;base64," + base64;
    });
  }

  window.SorchaPortrait = { isCameraSupported, capturePortrait, resizeJpeg };
})();
