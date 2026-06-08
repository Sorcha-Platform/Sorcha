// Sorcha marketing site — behaviour.
// Implements the docs/design-system motion + theme brief: a calm, grid-snapped
// particle canvas confined to the hero (paused off-screen, single static frame
// under prefers-reduced-motion), a light/dark theme toggle with the one-frame
// transition guard, the mobile nav, and scroll-reveal.

(function () {
    'use strict';

    const root = document.documentElement;
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    // ============================================================
    // Theme toggle — persists to localStorage; suppresses transitions for one
    // frame on swap so var()-driven colours re-resolve instantly (Chromium).
    // ============================================================
    function setupThemeToggle() {
        function swap(fn) {
            root.classList.add('swapping');
            fn();
            requestAnimationFrame(() => requestAnimationFrame(() => root.classList.remove('swapping')));
        }
        function apply(theme) {
            swap(() => root.setAttribute('data-theme', theme));
            try { localStorage.setItem('sorcha-theme', theme); } catch (e) { /* ignore */ }
            document.querySelectorAll('[data-theme-toggle]').forEach(b =>
                b.setAttribute('aria-pressed', theme === 'dark' ? 'true' : 'false'));
        }
        document.querySelectorAll('[data-theme-toggle]').forEach(btn => {
            btn.addEventListener('click', () => {
                const next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
                apply(next);
            });
        });
    }

    // ============================================================
    // Hero animation — the drifting peer network with transaction "trains"
    // travelling routes between nodes, plus glow-square data particles, over a
    // slowly drifting 56px grid. Recoloured to the brand indigo/violet and
    // drawn transparently so the hero ground gradient shows through. Confined
    // to the hero, paused off-screen, single static frame under reduced-motion.
    // ============================================================
    function setupHeroAnimation() {
        const canvas = document.getElementById('heroCanvas');
        if (!canvas) return;
        const ctx = canvas.getContext('2d');

        const CFG = {
            spacing: 56,                 // matches the design-system grid motif
            gridOp: 0.09,
            indigo: { r: 99, g: 102, b: 241 },   // #6366F1
            violet: { r: 129, g: 140, b: 248 },  // #818CF8
            peerCount: 12,
            nodeSize: 7,
            glow: 16,
            speed: 1.4,
            txSz: 3,
            maxTrain: 8,
            rate: 70,
            drift: 0.22,
        };

        let w, h, dpr, peers = [], trains = [], frame = 0, scrollOffset = 0, raf = null, running = false;

        function size() {
            dpr = Math.min(window.devicePixelRatio || 1, 2);
            const r = canvas.getBoundingClientRect();
            w = r.width; h = r.height;
            canvas.width = w * dpr; canvas.height = h * dpr;
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        }
        function initPeers() {
            const sp = CFG.spacing;
            const cols = Math.max(3, Math.floor(w / sp));
            const rows = Math.max(3, Math.floor(h / sp));
            peers = [];
            for (let i = 0; i < CFG.peerCount; i++) {
                peers.push({
                    x: (Math.floor(Math.random() * (cols - 2)) + 1) * sp,
                    y: (Math.floor(Math.random() * (rows - 2)) + 1) * sp,
                    phase: Math.random() * Math.PI * 2,
                });
            }
            peers.sort((a, b) => a.x - b.x || a.y - b.y);
            trains = [];
        }
        const screenX = p => p.x - scrollOffset;

        function drawGrid() {
            const sp = CFG.spacing, ox = -(scrollOffset % sp), c = CFG.indigo;
            ctx.strokeStyle = `rgba(${c.r},${c.g},${c.b},${CFG.gridOp})`;
            ctx.lineWidth = 1;
            for (let x = ox; x < w + sp; x += sp) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h); ctx.stroke(); }
            for (let y = sp; y < h; y += sp) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke(); }
        }
        function drawPeers() {
            const c = CFG.violet;
            peers.forEach(p => {
                const sx = screenX(p);
                const scale = 1 + Math.sin(frame * 0.02 + p.phase) * 0.15;
                const sz = CFG.nodeSize * scale;
                const grad = ctx.createRadialGradient(sx, p.y, sz / 2, sx, p.y, CFG.glow * scale);
                grad.addColorStop(0, `rgba(${c.r},${c.g},${c.b},0.22)`);
                grad.addColorStop(1, `rgba(${c.r},${c.g},${c.b},0)`);
                ctx.fillStyle = grad;
                ctx.fillRect(sx - CFG.glow, p.y - CFG.glow, CFG.glow * 2, CFG.glow * 2);
                ctx.fillStyle = `rgba(${c.r},${c.g},${c.b},0.7)`;
                ctx.fillRect(sx - sz / 2, p.y - sz / 2, sz, sz);
            });
        }
        function recyclePeers() {
            const sp = CFG.spacing, rows = Math.max(3, Math.floor(h / sp));
            for (let i = peers.length - 1; i >= 0; i--) {
                if (screenX(peers[i]) < -sp) {
                    const col = Math.ceil((scrollOffset + w + sp * 2) / sp) + Math.floor(Math.random() * 3);
                    peers[i] = { x: col * sp, y: (Math.floor(Math.random() * (rows - 2)) + 1) * sp, phase: Math.random() * Math.PI * 2 };
                }
            }
            peers.sort((a, b) => a.x - b.x || a.y - b.y);
        }
        function buildRoute(from, to) {
            const sp = CFG.spacing, path = [{ x: from.x, y: from.y }];
            let cx = from.x, cy = from.y;
            while (cx < to.x) {
                if (cy !== to.y && Math.random() < 0.4) cy += cy < to.y ? sp : -sp; else cx += sp;
                path.push({ x: cx, y: cy });
                if (path.length > 50) break;
            }
            while (cy !== to.y && path.length < 60) { cy += cy < to.y ? sp : -sp; path.push({ x: cx, y: cy }); }
            return path;
        }
        function spawnTrain() {
            const vis = peers.filter(p => { const sx = screenX(p); return sx > 0 && sx < w; });
            if (vis.length < 2) return;
            const si = Math.floor(Math.random() * (vis.length - 1));
            const ti = si + 1 + Math.floor(Math.random() * (vis.length - si - 1));
            trains.push({ route: buildRoute(vis[si], vis[ti]), pos: 0, blocks: 1, speed: CFG.speed });
        }
        function drawTrains() {
            const c = CFG.indigo, sp = CFG.spacing, gone = [];
            trains.forEach((t, idx) => {
                t.pos += t.speed;
                const segs = t.route.length - 1, total = segs * sp;
                if (t.pos > total + CFG.txSz * t.blocks * 2) { gone.push(idx); return; }
                const expected = Math.min(CFG.maxTrain, 1 + Math.floor(t.pos / (sp * 1.5)));
                if (t.blocks < expected) t.blocks = expected;
                for (let b = 0; b < t.blocks; b++) {
                    const bd = t.pos - b * (CFG.txSz + 2);
                    if (bd < 0) continue;
                    const seg = Math.min(Math.floor(bd / sp), segs - 1);
                    if (seg < 0 || seg >= t.route.length - 1) continue;
                    const prog = (bd - seg * sp) / sp;
                    const p1 = t.route[seg], p2 = t.route[seg + 1];
                    const bx = p1.x + (p2.x - p1.x) * Math.min(prog, 1) - scrollOffset;
                    const by = p1.y + (p2.y - p1.y) * Math.min(prog, 1);
                    const alpha = b === 0 ? 0.9 : Math.max(0.2, 0.8 - b * 0.12);
                    if (b > 0) {
                        const pd = t.pos - (b - 1) * (CFG.txSz + 2);
                        const ps = Math.min(Math.floor(pd / sp), segs - 1);
                        if (ps >= 0 && ps < t.route.length - 1) {
                            const pp = (pd - ps * sp) / sp, q1 = t.route[ps], q2 = t.route[ps + 1];
                            const px = q1.x + (q2.x - q1.x) * Math.min(pp, 1) - scrollOffset;
                            const py = q1.y + (q2.y - q1.y) * Math.min(pp, 1);
                            ctx.strokeStyle = `rgba(${c.r},${c.g},${c.b},${alpha * 0.4})`;
                            ctx.lineWidth = 1;
                            ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(bx, by); ctx.stroke();
                        }
                    }
                    ctx.fillStyle = `rgba(${c.r},${c.g},${c.b},${alpha})`;
                    ctx.fillRect(bx - CFG.txSz / 2, by - CFG.txSz / 2, CFG.txSz, CFG.txSz);
                }
            });
            for (let i = gone.length - 1; i >= 0; i--) trains.splice(gone[i], 1);
        }
        function frameDraw(animate) {
            ctx.clearRect(0, 0, w, h);
            drawGrid();
            drawPeers();
            if (animate) {
                if (frame % CFG.rate === 0) spawnTrain();
                drawTrains();
            }
        }
        function loop() {
            if (!running) return;
            scrollOffset += CFG.drift;
            recyclePeers();
            frameDraw(true);
            frame++;
            raf = requestAnimationFrame(loop);
        }
        function start() { if (!raf && !reduce) { running = true; raf = requestAnimationFrame(loop); } }
        function stop() { running = false; if (raf) { cancelAnimationFrame(raf); raf = null; } }

        size(); initPeers();
        if (reduce) { frameDraw(false); return; }        // single static frame, no trains
        if ('IntersectionObserver' in window) {
            new IntersectionObserver((e) => { e[0].isIntersecting ? start() : stop(); }, { threshold: 0.01 }).observe(canvas);
        }
        start();
        let to;
        window.addEventListener('resize', () => { clearTimeout(to); to = setTimeout(() => { size(); initPeers(); }, 180); });
        document.addEventListener('visibilitychange', () => { document.hidden ? stop() : start(); });
    }

    // ============================================================
    // Scroll-reveal.
    // ============================================================
    function setupReveal() {
        const items = document.querySelectorAll('.reveal');
        if (reduce || !('IntersectionObserver' in window)) {
            items.forEach(el => el.classList.add('is-visible'));
            return;
        }
        const obs = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) { entry.target.classList.add('is-visible'); obs.unobserve(entry.target); }
            });
        }, { threshold: 0.12, rootMargin: '0px 0px -40px 0px' });
        items.forEach(el => obs.observe(el));
    }

    // ============================================================
    // Mobile nav.
    // ============================================================
    function setupMobileNav() {
        const toggle = document.querySelector('.nav-toggle');
        const links = document.getElementById('navLinks');
        if (!toggle || !links) return;
        toggle.addEventListener('click', () => {
            const open = links.classList.toggle('open');
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
        links.querySelectorAll('a').forEach(a => a.addEventListener('click', () => {
            if (window.innerWidth <= 820) { links.classList.remove('open'); toggle.setAttribute('aria-expanded', 'false'); }
        }));
        window.addEventListener('resize', () => {
            if (window.innerWidth > 820) { links.classList.remove('open'); toggle.setAttribute('aria-expanded', 'false'); }
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        setupThemeToggle();
        setupHeroAnimation();
        setupReveal();
        setupMobileNav();
    });
})();
