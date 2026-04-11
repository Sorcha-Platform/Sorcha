// Sorcha Landing Page JavaScript

(function() {
    'use strict';

    // ============================================================
    // Hero Grid Animation — peer nodes, transaction trains, parallax drift
    // ============================================================
    function setupHeroAnimation() {
        const canvas = document.getElementById('heroCanvas');
        if (!canvas) return;
        const ctx = canvas.getContext('2d');

        const CFG = {
            spacing: 35,
            gridOp: 0.08,
            color: { r: 74, g: 85, b: 104 },   // #4a5568 grey
            peerCount: 12,
            nodeSize: 8,
            glow: 15,
            speed: 2,
            txSz: 3,
            maxTrain: 9,
            rate: 59,
            drift: 0.4,
            bg: '#0d1117',
        };

        let peers = [], txTrains = [], frame = 0, scrollOffset = 0, animId;

        function resize() {
            canvas.width = window.innerWidth * devicePixelRatio;
            canvas.height = window.innerHeight * devicePixelRatio;
            canvas.style.width = window.innerWidth + 'px';
            canvas.style.height = window.innerHeight + 'px';
            ctx.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0);
        }

        function w() { return canvas.width / devicePixelRatio; }
        function h() { return canvas.height / devicePixelRatio; }

        function initPeers() {
            const sp = CFG.spacing;
            const cols = Math.floor(w() / sp);
            const rows = Math.floor(h() / sp);
            peers = [];
            for (let i = 0; i < CFG.peerCount; i++) {
                const col = Math.floor(Math.random() * (cols - 2)) + 1;
                const row = Math.floor(Math.random() * (rows - 2)) + 1;
                peers.push({ x: col * sp, y: row * sp, phase: Math.random() * Math.PI * 2 });
            }
            peers.sort((a, b) => a.x - b.x || a.y - b.y);
            txTrains = [];
        }

        function peerScreenX(p) { return p.x - scrollOffset; }

        function drawBg() {
            ctx.fillStyle = CFG.bg;
            ctx.fillRect(0, 0, w(), h());
        }

        function drawGrid() {
            const sp = CFG.spacing;
            const ox = -(scrollOffset % sp);
            const c = CFG.color;

            ctx.strokeStyle = `rgba(${c.r},${c.g},${c.b},${CFG.gridOp})`;
            ctx.lineWidth = 1;
            for (let x = ox; x < w() + sp; x += sp) {
                ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h()); ctx.stroke();
            }
            for (let y = sp; y < h(); y += sp) {
                ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w(), y); ctx.stroke();
            }

            ctx.fillStyle = `rgba(${c.r},${c.g},${c.b},${CFG.gridOp * 1.5})`;
            for (let x = ox; x < w() + sp; x += sp) {
                for (let y = sp; y < h(); y += sp) {
                    ctx.beginPath(); ctx.arc(x, y, 1.5, 0, Math.PI * 2); ctx.fill();
                }
            }
        }

        function drawPeers() {
            const c = CFG.color;
            peers.forEach(p => {
                const sx = peerScreenX(p);
                const scale = 1 + Math.sin(frame * 0.03 + p.phase) * 0.15;
                const sz = CFG.nodeSize * scale;

                if (CFG.glow > 0) {
                    const grad = ctx.createRadialGradient(sx, p.y, sz/2, sx, p.y, CFG.glow * scale);
                    grad.addColorStop(0, `rgba(${c.r},${c.g},${c.b},0.2)`);
                    grad.addColorStop(1, `rgba(${c.r},${c.g},${c.b},0)`);
                    ctx.fillStyle = grad;
                    ctx.fillRect(sx - CFG.glow, p.y - CFG.glow, CFG.glow*2, CFG.glow*2);
                }

                ctx.fillStyle = `rgba(${c.r},${c.g},${c.b},0.7)`;
                ctx.fillRect(sx - sz/2, p.y - sz/2, sz, sz);
                ctx.strokeStyle = `rgba(${c.r},${c.g},${c.b},0.9)`;
                ctx.lineWidth = 1.5;
                ctx.strokeRect(sx - sz/2, p.y - sz/2, sz, sz);
            });
        }

        function recyclePeers() {
            const sp = CFG.spacing;
            const rows = Math.floor(h() / sp);
            for (let i = peers.length - 1; i >= 0; i--) {
                if (peerScreenX(peers[i]) < -sp) {
                    const rightEdge = scrollOffset + w() + sp * 2;
                    const col = Math.ceil(rightEdge / sp) + Math.floor(Math.random() * 3);
                    const row = Math.floor(Math.random() * (rows - 2)) + 1;
                    peers[i] = { x: col * sp, y: row * sp, phase: Math.random() * Math.PI * 2 };
                }
            }
            peers.sort((a, b) => a.x - b.x || a.y - b.y);
        }

        function buildRoute(from, to) {
            const sp = CFG.spacing;
            const path = [{ x: from.x, y: from.y }];
            let cx = from.x, cy = from.y;
            while (cx < to.x) {
                if (cy !== to.y && Math.random() < 0.4) {
                    cy += cy < to.y ? sp : -sp;
                    path.push({ x: cx, y: cy });
                } else {
                    cx += sp;
                    path.push({ x: cx, y: cy });
                }
                if (path.length > 50) break;
            }
            while (cy !== to.y && path.length < 60) {
                cy += cy < to.y ? sp : -sp;
                path.push({ x: cx, y: cy });
            }
            return path;
        }

        function spawnTrain() {
            const visible = peers.filter(p => {
                const sx = peerScreenX(p);
                return sx > 0 && sx < w();
            });
            if (visible.length < 2) return;
            const si = Math.floor(Math.random() * (visible.length - 1));
            const ti = si + 1 + Math.floor(Math.random() * (visible.length - si - 1));
            txTrains.push({
                route: buildRoute(visible[si], visible[ti]),
                pos: 0,
                blocks: 1 + Math.floor(Math.random() * 2),
                speed: CFG.speed,
                age: 0,
            });
        }

        function drawTrains() {
            const c = CFG.color;
            const sp = CFG.spacing;
            const toRemove = [];

            txTrains.forEach((train, idx) => {
                train.age++;
                train.pos += train.speed;

                const totalSegs = train.route.length - 1;
                const totalLen = totalSegs * sp;
                const headDist = train.pos;

                if (headDist > totalLen + CFG.txSz * train.blocks * 2) {
                    toRemove.push(idx); return;
                }

                const growInterval = sp * 1.5;
                const expected = Math.min(CFG.maxTrain, 1 + Math.floor(headDist / growInterval));
                if (train.blocks < expected) train.blocks = expected;
                if (train.blocks >= CFG.maxTrain && headDist > totalLen * 0.6) {
                    train.blocks = 1 + Math.floor(Math.random() * 2);
                }

                for (let b = 0; b < train.blocks; b++) {
                    const bd = headDist - b * (CFG.txSz + 2);
                    if (bd < 0) continue;
                    const seg = Math.min(Math.floor(bd / sp), totalSegs - 1);
                    const prog = (bd - seg * sp) / sp;
                    if (seg < 0 || seg >= train.route.length - 1) continue;

                    const p1 = train.route[seg], p2 = train.route[seg + 1];
                    const bx = p1.x + (p2.x - p1.x) * Math.min(prog, 1) - scrollOffset;
                    const by = p1.y + (p2.y - p1.y) * Math.min(prog, 1);
                    const alpha = b === 0 ? 0.9 : Math.max(0.2, 0.8 - b * 0.12);

                    // Solid trail line to previous block
                    if (b > 0) {
                        const pd = headDist - (b-1) * (CFG.txSz + 2);
                        const ps = Math.min(Math.floor(pd / sp), totalSegs - 1);
                        const pp = (pd - ps * sp) / sp;
                        if (ps >= 0 && ps < train.route.length - 1) {
                            const q1 = train.route[ps], q2 = train.route[ps+1];
                            const px = q1.x + (q2.x - q1.x) * Math.min(pp,1) - scrollOffset;
                            const py = q1.y + (q2.y - q1.y) * Math.min(pp,1);
                            ctx.strokeStyle = `rgba(${c.r},${c.g},${c.b},${alpha * 0.4})`;
                            ctx.lineWidth = 1;
                            ctx.beginPath(); ctx.moveTo(px, py); ctx.lineTo(bx, by); ctx.stroke();
                        }
                    }

                    ctx.fillStyle = `rgba(${c.r},${c.g},${c.b},${alpha})`;
                    ctx.fillRect(bx - CFG.txSz/2, by - CFG.txSz/2, CFG.txSz, CFG.txSz);
                }
            });

            for (let i = toRemove.length - 1; i >= 0; i--) txTrains.splice(toRemove[i], 1);
        }

        function animate() {
            scrollOffset += CFG.drift;
            recyclePeers();
            ctx.setTransform(devicePixelRatio, 0, 0, devicePixelRatio, 0, 0);
            drawBg();
            drawGrid();
            drawPeers();
            if (frame % CFG.rate === 0) spawnTrain();
            drawTrains();
            frame++;
            animId = requestAnimationFrame(animate);
        }

        resize();
        initPeers();
        animate();
        window.addEventListener('resize', () => { resize(); initPeers(); });
    }

    // ============================================================
    // Smooth scroll for anchor links
    // ============================================================
    function setupSmoothScroll() {
        document.querySelectorAll('a[href^="#"]').forEach(anchor => {
            anchor.addEventListener('click', function(e) {
                e.preventDefault();
                const target = document.querySelector(this.getAttribute('href'));
                if (target) {
                    target.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
            });
        });
    }

    // Nav scroll effects — transparent on hero, solid on scroll
    function setupScrollEffects() {
        const nav = document.querySelector('.nav');
        window.addEventListener('scroll', () => {
            if (window.pageYOffset > 50) {
                nav.classList.add('scrolled');
                nav.style.boxShadow = '0 2px 10px rgba(0, 0, 0, 0.1)';
            } else {
                nav.classList.remove('scrolled');
                nav.style.boxShadow = 'none';
            }
        });
    }

    // Mobile nav toggle
    function setupMobileNav() {
        const toggle = document.querySelector('.nav-toggle');
        const links = document.querySelector('.nav-links');
        if (!toggle || !links) return;

        toggle.addEventListener('click', () => {
            const isOpen = links.style.display === 'flex';
            links.style.display = isOpen ? 'none' : 'flex';
            links.style.flexDirection = isOpen ? '' : 'column';
            links.style.position = isOpen ? '' : 'absolute';
            links.style.top = isOpen ? '' : '64px';
            links.style.left = isOpen ? '' : '0';
            links.style.right = isOpen ? '' : '0';
            links.style.background = isOpen ? '' : 'white';
            links.style.padding = isOpen ? '' : '16px 24px';
            links.style.boxShadow = isOpen ? '' : '0 4px 12px rgba(0,0,0,0.1)';
            links.style.gap = isOpen ? '' : '16px';
        });

        links.querySelectorAll('a').forEach(link => {
            link.addEventListener('click', () => {
                if (window.innerWidth <= 768) {
                    links.style.display = 'none';
                }
            });
        });

        window.addEventListener('resize', () => {
            if (window.innerWidth > 768) {
                links.removeAttribute('style');
            }
        });
    }

    // Intersection Observer for fade-in animations
    function setupScrollAnimations() {
        const observer = new IntersectionObserver((entries) => {
            entries.forEach(entry => {
                if (entry.isIntersecting) {
                    entry.target.style.opacity = '1';
                    entry.target.style.transform = 'translateY(0)';
                    observer.unobserve(entry.target);
                }
            });
        }, { threshold: 0.1, rootMargin: '0px 0px -40px 0px' });

        const selectors = [
            '.risk-card', '.benefit-card',
            '.dad-card', '.standard-card', '.quantum-card',
            '.sector-card', '.toolkit-card',
            '.step-card', '.tech-item',
            '.developer-card', '.security-layer'
        ];

        selectors.forEach(selector => {
            document.querySelectorAll(selector).forEach((el, i) => {
                el.style.opacity = '0';
                el.style.transform = 'translateY(20px)';
                el.style.transition = `opacity 0.5s ease ${i * 0.08}s, transform 0.5s ease ${i * 0.08}s`;
                observer.observe(el);
            });
        });
    }

    // Initialize
    document.addEventListener('DOMContentLoaded', function() {
        setupHeroAnimation();
        setupSmoothScroll();
        setupScrollEffects();
        setupMobileNav();
        setupScrollAnimations();
    });
})();
