# Feature Specification: PWA Present — Camera-First Intake

**Feature Branch**: `159-pwa-present-camera-first`

**Created**: 2026-06-25

**Status**: Draft

**Input**: User description: "PWA Present camera-first: in Sorcha.Wallet.Pwa Present.razor default to camera scan on handheld with Paste-a-link-instead button; desktop shows paste plus Scan-with-camera button; no-camera shows paste only. Per docs/superpowers/specs/2026-06-25-pwa-nav-and-present-camera-design.md item 2"

## Overview

The citizen wallet "Present a credential" page is the entry point for responding to a verifier's request. A verifier presents a QR code (typically shown on a screen the holder is standing in front of) or sends an `openid4vp://` deep link. Today the page leads with a paste field and offers QR scanning as a secondary button — the same layout on every device.

In the most common real-world situation the holder is on a phone, standing in front of a verifier's QR code. Making them tap a "Scan QR code" button first adds friction to the dominant path. This feature makes the intake layout adapt to the holder's device so the fastest path is the default one: camera scanning leads on handheld devices, while paste leads on desktop, and devices without a camera only ever show paste.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Scan straight away on a phone (Priority: P1)

A citizen standing in front of a verifier's QR code opens the Present page on their phone. The camera viewfinder is already active and pointed at the code; they line it up and the request is captured without any extra taps. If they would rather paste a link they were sent, a clearly visible "Paste a link instead" control switches the page to the paste field.

**Why this priority**: This is the dominant in-person presentation flow and the entire reason for the feature. Removing the extra tap from the most common path is the core value.

**Independent Test**: On a handheld device with a working, permitted camera, open the Present page and confirm the camera viewfinder is live on arrival and a single QR code scan advances to credential matching — with no intermediate button press. Confirm the "Paste a link instead" control is present and switches to the paste field.

**Acceptance Scenarios**:

1. **Given** a handheld device whose camera is available and permitted, **When** the holder opens the Present page, **Then** the camera viewfinder is shown and actively scanning without any further interaction.
2. **Given** the camera viewfinder is active on a handheld device, **When** the holder points it at a valid verifier QR code, **Then** the request is captured and the page advances to credential matching (pick / consent / no-match) exactly as a pasted link would.
3. **Given** the camera viewfinder is active on a handheld device, **When** the holder taps "Paste a link instead", **Then** the camera stops and the page shows the paste field with a Continue action.

---

### User Story 2 - Paste-led intake on a desktop (Priority: P2)

A citizen using the wallet on a laptop or desktop opens the Present page. Because they are unlikely to point a webcam at a QR code, the page leads with the paste field so they can drop in a deep link they received. A "Scan with camera" control is still available for the case where a webcam is the easiest option.

**Why this priority**: Desktop is a real but secondary surface; leading with paste matches how those users actually receive requests, while still allowing camera use.

**Independent Test**: On a desktop-class device with a camera available, open the Present page and confirm the paste field is the default surface and a "Scan with camera" control is offered. Confirm activating it starts the viewfinder.

**Acceptance Scenarios**:

1. **Given** a desktop-class device with a camera available, **When** the holder opens the Present page, **Then** the paste field is shown by default alongside a "Scan with camera" control, and the camera is not active until requested.
2. **Given** the desktop paste view, **When** the holder activates "Scan with camera", **Then** the camera viewfinder starts and a successful scan advances to credential matching.
3. **Given** the desktop paste view, **When** the holder pastes a valid deep link and confirms, **Then** the page advances to credential matching exactly as today.

---

### User Story 3 - Paste-only on a device with no camera (Priority: P2)

A citizen on a device that exposes no usable camera opens the Present page. The page shows only the paste field — no scan affordance is offered, because it could not work — and pasting a deep link proceeds normally.

**Why this priority**: Showing a scan button that cannot work is a dead end and erodes trust. Suppressing it on camera-less devices keeps the surface honest and uncluttered.

**Independent Test**: On a device with no camera API (or no camera hardware), open the Present page and confirm only the paste field is shown, with no scan control anywhere on the intake surface. Confirm pasting a link still works.

**Acceptance Scenarios**:

1. **Given** a device with no usable camera, **When** the holder opens the Present page, **Then** only the paste field and its Continue action are shown, with no scan control offered.
2. **Given** the paste-only view, **When** the holder pastes a valid deep link and confirms, **Then** the page advances to credential matching exactly as today.

---

### Edge Cases

- **Camera permission denied while auto-scanning**: When the camera fails to start on a handheld because permission was refused, the page must fall back gracefully to the paste field and explain in plain language that the link can be pasted instead — never a blank or stuck viewfinder.
- **No camera hardware on an otherwise handheld device**: A handheld with the camera API present but no actual camera is treated as paste-only for that session (graceful fallback), with a plain-language note.
- **Returning after a denied permission**: If the holder has already refused the camera in this session, re-opening the intake should not trap them in a failing viewfinder; the paste field must remain reachable at all times.
- **Mid-scan navigation away**: Leaving the page (or switching to paste) while the camera is live must stop the camera and release the device so it is not left running.
- **Unreadable or non-verifier QR code**: Scanning a code that is not a valid verifier request must show a recoverable "couldn't read that / not a valid request" message and keep the intake usable (retry scan or switch to paste).
- **Ambiguous form factor**: A device that is hard to classify (e.g. a large tablet, or a touchscreen laptop) must still land in exactly one of the three layouts and never present a broken or empty intake surface.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Present page MUST select one of three intake layouts on load based on the holder's device: camera-first (handheld with usable camera), paste-with-scan (desktop with usable camera), or paste-only (no usable camera).
- **FR-002**: On a handheld device with a usable, permitted camera, the page MUST start the camera viewfinder automatically on load, with no intermediate tap required to begin scanning.
- **FR-003**: The camera-first layout MUST offer a clearly visible "Paste a link instead" control that stops the camera and switches to the paste field.
- **FR-004**: On a desktop-class device with a usable camera, the page MUST default to the paste field and offer a "Scan with camera" control that the holder activates to start the viewfinder; the camera MUST NOT start until requested.
- **FR-005**: On a device with no usable camera, the page MUST show only the paste field and MUST NOT display any scan control.
- **FR-006**: All three layouts MUST feed the same downstream flow — a captured or pasted request proceeds identically through parse, credential match, pick, consent, send, and done.
- **FR-007**: When camera start fails (permission denied, no camera found, or any other error), the page MUST fall back to a usable paste field and present a plain-language explanation; it MUST NOT leave the holder on a blank or stuck viewfinder.
- **FR-008**: Switching away from the camera (choosing paste, leaving the page, cancelling, or completing a scan) MUST stop the camera and release the device.
- **FR-009**: A scan of a code that cannot be read or is not a valid verifier request MUST produce a recoverable message and keep the intake usable (retry or switch to paste).
- **FR-010**: Every device classification MUST resolve to exactly one of the three layouts; the page MUST never render an intake surface with no usable way to provide a request.
- **FR-011**: The paste path MUST remain functionally unchanged from current behaviour (paste an `openid4vp://`-style link, Continue, proceed to matching).

### Key Entities

- **Intake layout**: The chosen presentation of the intake surface — one of camera-first, paste-with-scan, or paste-only — derived from device form factor and camera availability.
- **Device profile**: The holder's device as classified for this decision, characterised by form factor (handheld vs desktop) and camera availability (usable camera present vs not).
- **Verifier request**: The captured `openid4vp://`-style presentation request (whether scanned or pasted) that the downstream matching/consent/send flow operates on.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a handheld device with a permitted camera, the holder can begin scanning a verifier QR code with zero taps after the page loads (down from at least one tap today).
- **SC-002**: 100% of device classifications resolve to exactly one of the three intake layouts, with a usable way to provide a request in every case.
- **SC-003**: Whenever the camera cannot start or is unavailable, the holder still reaches a working paste field with no dead-end screens — verified across denied-permission, no-camera-hardware, and no-camera-API cases.
- **SC-004**: Desktop holders see paste as the default surface while retaining a one-action route to camera scanning when a camera is available.
- **SC-005**: All existing presentation outcomes (single match, multiple matches, no match, consent, send success/failure, activity logging) are reached identically regardless of which intake layout captured the request.

## Assumptions

- "Handheld" means a device a holder typically holds and points at a QR code (phone or small tablet); "desktop" means a laptop/desktop-class device. The exact classification heuristic (touch/pointer capability, viewport, platform hints) is an implementation choice; the requirement is only that each device lands in exactly one of the three layouts.
- "Usable camera" means the device exposes a working camera capture capability the wallet can drive; a device whose camera is blocked, absent, or unavailable is treated as having no usable camera for layout purposes.
- Camera permission prompting follows the platform's standard behaviour; auto-starting on handheld may surface the OS/browser permission prompt, and a refusal falls back to paste per FR-007.
- The existing QR scanning, request parsing, credential matching, consent, send, and activity-logging behaviour on the Present page is reused unchanged; this feature only changes which intake layout is shown first and whether scanning auto-starts.
- This feature is scoped to the citizen wallet PWA's Present page intake surface only; the verifier-side experience and the rest of the presentation flow are out of scope.
- The referenced design note (`docs/superpowers/specs/2026-06-25-pwa-nav-and-present-camera-design.md`, item 2) is the design source of truth; this spec captures item 2's intended behaviour even where that note is not yet committed.
