# Wallet PWA — mobile login crash: investigation handoff

**Branch:** `fix/wallet-pwa-mobile-backend-url`
**Status:** substantive root cause identified; exact crash exception still to be captured on-device/in-repro. No source changes yet — this doc only.
**Started:** 2026-06-23/24. Delete this file before the PR is finalised.

---

## 1. Symptom

- The **installed** Sorcha Wallet mobile apps (iOS TestFlight + Android Play Internal), which wrap the Blazor **WASM** PWA `src/Apps/Sorcha.Wallet.Pwa` via Capacitor (`mobile/wallet/`), **crash when the user tries to log in**.
- On-screen: a full-page panel **"Something went wrong / This page hit an unexpected error. You can try again, or reload the app."** + **Try again** / **Reload** buttons.
- That exact wording = `AppErrorBoundary.razor:34-36` (`src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Shared/AppErrorBoundary.razor`). It is Blazor's `<ErrorBoundary>` catching an **UNHANDLED .NET exception** thrown by the routed page — **NOT** a graceful login failure (those render a small inline `MudAlert`, e.g. "Sign-in failed").
- **The web-served PWA at `https://n1.sorcha.dev/wallet/` works perfectly.** Only the installed/Capacitor build crashes ⇒ **the bug is specific to the Capacitor origin** (`http://localhost` on Android, `capacitor://localhost` on iOS) vs the real `https://n1.sorcha.dev` web origin.

---

## 2. Environment facts (verified)

- **n1 is UP and is NOT the problem.** Probed from the dev box:
  - `GET https://n1.sorcha.dev/health` → 200; `/api/auth/social/providers` → 200; `/.well-known/openapi.json` → 200; `/app` → 200; `/wallet/` → 200.
  - CORS preflight `OPTIONS /api/auth/login` with `Origin: https://localhost` → **204**, `Access-Control-Allow-Origin: *`, allows POST + content-type/authorization. So n1 does not reject the mobile origin at the CORS layer.
- Per the `sorcha-app` skill: all four mobile build lanes were proven end-to-end on 2026-06-23 (build/sign/upload). **This login crash is the first RUNTIME test of an installed build** — a functional bug here is fully consistent with that milestone.

---

## 3. Confirmed substantive root cause (HIGH confidence)

**The installed app sends every API/auth/SignalR call to its own local origin, not to n1.**

- `src/Apps/Sorcha.Wallet.Pwa/Program.cs:17` — generic `HttpClient.BaseAddress = builder.HostEnvironment.BaseAddress`.
- `Program.cs:22` — `var hostRoot = new Uri(builder.HostEnvironment.BaseAddress).GetLeftPart(UriPartial.Authority) + "/";` → passed to `AddCitizenWalletServices(hostRoot)`, which is the base address for **all** typed Sorcha clients (AuthService, SocialProvidersClient, CitizenWalletClient, …) and the SignalR hub.
- On a device, `builder.HostEnvironment.BaseAddress` = `http://localhost/` (Android) / `capacitor://localhost/` (iOS) — the local app bundle. So `hostRoot` becomes the local origin and **all backend calls go nowhere**.
- The load-bearing (now-false) assumption is documented in the code itself at `Program.cs:19-21`: *"the wallet PWA is mounted at /wallet/ behind the API Gateway"*. True for the web-served PWA; false once Capacitor bundles the assets into the app.
- **There is currently no mechanism to override the backend URL for the installed build:**
  - `mobile/wallet/capacitor.config.json` — sets only `appId`/`appName`/`webDir`; **no `server.url`**.
  - `mobile/scripts/build-web.sh` — `dotnet publish` + rewrites `<base href="/wallet/">`→`/` + strips precompressed dupes + `cap sync`. **Injects no backend URL.**
  - The PWA has **no `wwwroot/appsettings.json`** and no env/runtime hook for a backend URL.

---

## 4. Crash mechanism (PARTIALLY confirmed — exact throw still OPEN)

- `AppErrorBoundary` wraps `@Body` (`Sorcha.Wallet.Pwa/MainLayout.razor:123`). An unauthenticated user is routed to `/signin` (`App.razor`: `AuthorizeRouteView` → `NotAuthorized` → `RedirectToSignIn`). So the uncaught throw is in the **SignIn page** render/lifecycle (if it were in MainLayout itself you'd get the default `#blazor-error-ui`, not this panel).
- `Pages/SignIn.razor` load-path calls, audited for defensiveness:

  | Call | Line | Defensive? |
  |---|---|---|
  | `Auth.TryConsumeSocialReturnAsync(Js)` | 119 | YES — `IAuthService.cs:282-283` `try…catch { return false; }` |
  | `Auth.IsSignedInAsync()` | 126 | **NO** — `IAuthService.cs:120-121` → `IndexedDbAccessTokenStore.GetAsync` (`IAccessTokenStore.cs:83-94`) calls JS `SorchaIndexedDb.get`, also not wrapped |
  | `Providers.GetConfiguredAsync()` | 133 | YES — `ISocialProvidersClient.cs:32` `catch { return []; }` |
  | `Passkey.IsSupportedAsync()` (OnAfterRender) | 140 | YES — `IPasskeyInterop.cs:38-39` `catch { return false; }` |

- **Lone non-defensive call on the signin load path: `IsSignedInAsync` → `GetAsync` → JS `SorchaIndexedDb.get`.** BUT this is not an obvious throw: `index.html:49` loads `js/indexeddb-bridge.js` via a **relative** `src` (resolves under base `/` on device), and IndexedDB is available on both `http://localhost` and `capacitor://localhost` (secure contexts). **So the exact uncaught exception is UNCONFIRMED — it needs the device console or a faithful repro.**

### Eliminated leads (with evidence — do NOT re-investigate)
- **`new Uri("capacitor://localhost/").GetLeftPart(UriPartial.Authority)` throwing** — **DISPROVEN** empirically on .NET 10: returns `"capacitor://localhost"`, no throw (also fine for `http://localhost/`). So `SignIn.razor:164 GoToWebSignup` and `Program.cs:22` do not throw on the scheme.
- **SignalR hub crash** — `Index.razor:292` is `_ = Hub.StartAsync()` (fire-and-forget → unobserved, can't trip ErrorBoundary); `CitizenWalletHubConnection.StartAsync` wraps the connect in try/catch and swallows (`:132-141`); and `Index` only renders **after** auth, not during login.
- **social-providers fetch** — swallowed (`catch { return []; }`).

---

## 5. The fix (DESIGNED — not yet implemented)

Two parts:

### (A) Make the installed app talk to n1 — pick one:
- **Option A1 (RECOMMENDED for a first working build — simplest + all login methods work):** set `mobile/wallet/capacitor.config.json` `server.url = "https://n1.sorcha.dev/wallet"`. The app then runs **on the n1 origin**, so password, social OAuth redirect, AND passkey (WebAuthn rpId match) all work. Downside: it's effectively a web wrapper — needs connectivity to boot, no offline. May need `allowNavigation` for OAuth provider hosts.
- **Option A2 (offline-capable but partial auth):** keep assets bundled; add `wwwroot/appsettings.json` `{"ApiBaseUrl":""}` (empty ⇒ web behaviour unchanged) and read it in `Program.cs` to override only `hostRoot` (leave line 17's app-relative HttpClient alone); `build-web.sh` stamps `{"ApiBaseUrl":"https://n1.sorcha.dev"}` into the bundle. Downside on device: **social login (OAuth redirect lands on n1, not the app) and passkey (WebAuthn rpId/origin mismatch) DO NOT work** — only password/2FA. Blazor WASM auto-loads `wwwroot/appsettings.json` relative to base href.

> **Origin caveat (verify regardless of option):** on a wrapped PWA that is NOT actually served from n1, OAuth-redirect social login and WebAuthn passkeys both depend on the app's origin matching the server's redirect/rpId origin. **Only A1 satisfies that.** Document clearly in the PR which login methods work on device.

### (B) Defensive guard (robustness — do regardless of A):
Wrap `SignIn.OnInitializedAsync` init calls (esp. `IsSignedInAsync`) so a failed init degrades to a usable sign-in screen instead of a white-screen `ErrorBoundary` crash. Consider also making `IndexedDbAccessTokenStore.GetAsync` swallow read errors → `null` (signed-out), matching the other stores' resilience pattern. This prevents the crash *class* regardless of the exact throw.

---

## 6. Repro harness (recreate on the faster machine)

> The current box failed `npm install puppeteer-core` with `UNABLE_TO_VERIFY_LEAF_SIGNATURE` (a CA-intercepting proxy/firewall). On a clean machine it should just work; else `npm install --strict-ssl=false` or `NODE_OPTIONS=--use-system-ca`.
> Also: the local Playwright **browser cache** already has chromium + **webkit** (`~/AppData/Local/ms-playwright`). **webkit ≈ iOS WKWebView** — prefer it for an iOS-faithful repro if you wire up Playwright.

Steps:
1. `dotnet publish src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj -c Release -o <pub>`
2. In `<pub>/wwwroot/index.html`, rewrite `<base href="/wallet/" />` → `<base href="/" />` (mimics `build-web.sh`).
3. `node server.js <pub>/wwwroot 5099` (static server below; SPA-fallback, 404s /api so it mimics "no backend at this origin").
4. `node repro.js http://localhost:5099/ "C:\Program Files\Google\Chrome\Application\chrome.exe"` (captures console + pageerror + which URLs requests target).
5. **Expectation:** confirms requests target the local origin (not n1). The crash itself may NOT reproduce in plain Chrome (IndexedDB works there, no Capacitor runtime) — if not, capture it from the device console instead (Safari Web Inspector for iOS / `chrome://inspect` for Android). To **verify the fix**, rebuild with the chosen option pointed at n1 and confirm no crash + the social-providers list loads.

### `server.js`
```js
const http = require('http'), fs = require('fs'), path = require('path');
const root = process.argv[2], port = parseInt(process.argv[3] || '5099', 10);
const mime = { '.html':'text/html','.js':'text/javascript','.mjs':'text/javascript','.css':'text/css','.json':'application/json','.wasm':'application/wasm','.dll':'application/octet-stream','.dat':'application/octet-stream','.blat':'application/octet-stream','.pdb':'application/octet-stream','.woff':'font/woff','.woff2':'font/woff2','.png':'image/png','.svg':'image/svg+xml','.ico':'image/x-icon','.webmanifest':'application/manifest+json','.map':'application/json' };
http.createServer((req, res) => {
  let urlPath = decodeURIComponent(req.url.split('?')[0]);
  if (urlPath === '/') urlPath = '/index.html';
  const filePath = path.join(root, urlPath);
  fs.stat(filePath, (err, st) => {
    if (!err && st.isFile()) { res.setHeader('Content-Type', mime[path.extname(filePath).toLowerCase()] || 'application/octet-stream'); fs.createReadStream(filePath).pipe(res); return; }
    if (/^\/(api|hubs|_framework)\b/.test(urlPath)) { res.statusCode = 404; res.end('not found'); return; }
    res.setHeader('Content-Type', 'text/html'); fs.createReadStream(path.join(root, 'index.html')).pipe(res);
  });
}).listen(port, () => console.log('SERVING ' + root + ' on http://localhost:' + port));
```

### `repro.js` (needs `npm install puppeteer-core`)
```js
const puppeteer = require('puppeteer-core');
const url = process.argv[2], chrome = process.argv[3];
(async () => {
  const logs=[],errors=[],failed=[],apis=[];
  const browser = await puppeteer.launch({ executablePath: chrome, headless: 'new', args: ['--no-sandbox','--disable-gpu'] });
  const page = await browser.newPage();
  page.on('console', m => logs.push('['+m.type()+'] '+m.text()));
  page.on('pageerror', e => errors.push('PAGEERROR: '+(e.stack||e.message||String(e))));
  page.on('requestfailed', r => failed.push(r.url()+' :: '+(r.failure()&&r.failure().errorText)));
  page.on('response', r => { const u=r.url(); if (u.includes('/api/')||u.includes('/hubs/')) apis.push(r.status()+' '+u); });
  try { await page.goto(url, { waitUntil:'networkidle2', timeout:45000 }); } catch(e){ errors.push('GOTO: '+e.message); }
  await new Promise(r=>setTimeout(r,7000));
  let body=''; try { body = await page.evaluate(()=>document.body.innerText); } catch {}
  console.log('=== CURRENT URL: '+page.url());
  console.log('=== CRASHED: '+/Something went wrong|unexpected error/i.test(body));
  console.log('=== VISIBLE TEXT (first 500):\n'+body.slice(0,500));
  console.log('=== API/HUB RESPONSES:\n'+apis.join('\n'));
  console.log('=== REQUEST FAILURES:\n'+failed.join('\n'));
  console.log('=== PAGE ERRORS:\n'+errors.join('\n'));
  console.log('=== CONSOLE (last 50):\n'+logs.slice(-50).join('\n'));
  await browser.close();
})().catch(e => { console.error('HARNESS_ERROR: '+(e.stack||e.message)); process.exit(2); });
```

---

## 7. Diagnostics inventory (reference)

- **Device console (gold standard for the exact exception):** iOS → Safari ▸ Develop ▸ *[iPhone]* ▸ `Sorcha Wallet` ▸ Web Inspector ▸ Console. Android → `chrome://inspect` (USB debugging) ▸ inspect the webview ▸ Console. The framework logs the ErrorBoundary exception there (`AppErrorBoundary.razor:19-20` notes it does not re-log because the built-in boundary already logs via the framework logger).
- **Client error states:** `SignIn.razor` `MapAuthError` (`no_account`/`refused`/generic); `AuthService` messages; `BearerTokenHandler` silent refresh on 401.
- **Server-side (n1):** Aspire dashboard / container logs; OTel meter `Sorcha.Tenant.Auth`; ILogger lines in `Sorcha.Tenant.Service/Endpoints/AuthEndpoints.cs` + `SocialLoginEndpoints.cs`. n1 log access is via `az vm run-command` (the dev sandbox cannot reach n1's control plane; public HTTPS to n1 works). **If the base-URL root cause holds, n1 will show NOTHING for these login attempts — the requests never leave the device.**

---

## 8. Next steps (todo)

1. **Repro + capture the exact exception** (faster machine; device console if the plain-browser repro doesn't crash).
2. **Confirm** whether the throw is network-rooted (base-URL fix resolves it) or device-JS-rooted (needs the §5B guard).
3. **Implement** §5A (A1 recommended) + §5B.
4. `dotnet build` the PWA; verify via the repro (no crash, social-providers list loads from n1).
5. **Trigger a mobile build** (`sorcha-app` skill) and **verify login on a real device**.
6. Finalise the PR; **delete this debug doc** before merge.

---

## 9. Source map (key files referenced above)

- `src/Apps/Sorcha.Wallet.Pwa/Program.cs` (`:17`, `:22` — base-URL derivation)
- `src/Apps/Sorcha.Wallet.Pwa/Pages/SignIn.razor` (`OnInitializedAsync` `:116-134`, `OnAfterRenderAsync` `:136-142`)
- `src/Apps/Sorcha.Wallet.Pwa/Services/IAuthService.cs` (`IsSignedInAsync` `:120`, `TryConsumeSocialReturnAsync` `:279`)
- `src/Apps/Sorcha.Wallet.Pwa/Services/IAccessTokenStore.cs` (`IndexedDbAccessTokenStore.GetAsync` `:83`)
- `src/Apps/Sorcha.Wallet.Pwa/Services/ISocialProvidersClient.cs` (`:25-33`)
- `src/Apps/Sorcha.Wallet.Pwa/Services/IPasskeyInterop.cs` (`:32-40`)
- `src/Apps/Sorcha.Wallet.Pwa/Services/CitizenWalletHubConnection.cs` (`:73-142`)
- `src/Apps/Sorcha.Wallet.Pwa/App.razor`, `MainLayout.razor:123`
- `src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Components/Shared/AppErrorBoundary.razor`
- `src/Apps/Sorcha.Wallet.Pwa/wwwroot/index.html` (script tags `:47-56`)
- `mobile/wallet/capacitor.config.json`, `mobile/scripts/build-web.sh`
