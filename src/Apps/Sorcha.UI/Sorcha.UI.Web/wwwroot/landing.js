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
    // Hero particle canvas — calm drifting glow-squares snapped to the 56px grid.
    // ============================================================
    function setupParticles() {
        const canvas = document.getElementById('heroCanvas');
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        const GRID = 56;
        let w, h, dpr, particles = [], raf = null, running = false;

        function size() {
            dpr = Math.min(window.devicePixelRatio || 1, 2);
            const r = canvas.getBoundingClientRect();
            w = r.width; h = r.height;
            canvas.width = w * dpr; canvas.height = h * dpr;
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        }
        function seed() {
            const n = Math.max(10, Math.min(26, Math.round(w / 70)));
            particles = [];
            for (let i = 0; i < n; i++) {
                particles.push({
                    x: Math.round((Math.random() * w) / GRID) * GRID,
                    y: Math.round((Math.random() * h) / GRID) * GRID,
                    s: 1.5 + Math.random() * 2.5,
                    vx: (Math.random() - 0.5) * 0.12,
                    vy: (Math.random() - 0.5) * 0.12,
                    a: 0.18 + Math.random() * 0.3,
                    tw: Math.random() * Math.PI * 2,
                    violet: Math.random() > 0.5,
                });
            }
        }
        function draw() {
            ctx.clearRect(0, 0, w, h);
            for (const p of particles) {
                p.x += p.vx; p.y += p.vy; p.tw += 0.01;
                if (p.x < -10) p.x = w + 10; if (p.x > w + 10) p.x = -10;
                if (p.y < -10) p.y = h + 10; if (p.y > h + 10) p.y = -10;
                const a = p.a * (0.6 + 0.4 * Math.sin(p.tw));
                const col = p.violet ? '129,140,248' : '99,102,241';
                ctx.shadowColor = `rgba(${col},${a})`;
                ctx.shadowBlur = 8;
                ctx.fillStyle = `rgba(${col},${a})`;
                ctx.fillRect(p.x - p.s / 2, p.y - p.s / 2, p.s, p.s);
            }
            ctx.shadowBlur = 0;
            if (running) raf = requestAnimationFrame(draw);
        }
        function start() { if (!raf && !reduce) { running = true; raf = requestAnimationFrame(draw); } }
        function stop() { running = false; if (raf) { cancelAnimationFrame(raf); raf = null; } }

        size(); seed();
        if (reduce) { draw(); return; }                 // single static frame
        if ('IntersectionObserver' in window) {
            new IntersectionObserver((e) => { e[0].isIntersecting ? start() : stop(); }, { threshold: 0.01 }).observe(canvas);
        }
        start();
        let to;
        window.addEventListener('resize', () => { clearTimeout(to); to = setTimeout(() => { size(); seed(); }, 180); });
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
        setupParticles();
        setupReveal();
        setupMobileNav();
    });
})();
