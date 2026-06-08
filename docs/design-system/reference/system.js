/* ============================================================================
   SORCHA VISUAL SYSTEM — behaviour
   ============================================================================ */
(function () {
  "use strict";

  /* ---------- WCAG contrast ---------- */
  function lin(c) { c /= 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); }
  function lum(hex) {
    const h = hex.replace('#', '');
    const r = parseInt(h.substr(0, 2), 16), g = parseInt(h.substr(2, 2), 16), b = parseInt(h.substr(4, 2), 16);
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
  }
  function ratio(fg, bg) {
    const L1 = lum(fg), L2 = lum(bg);
    return (Math.max(L1, L2) + 0.05) / (Math.min(L1, L2) + 0.05);
  }
  function rate(r, large) {
    const v = r.toFixed(2);
    if (r >= 4.5) return { v, cls: 'pass', tag: 'AA' };
    if (r >= 3.0) return { v, cls: large ? 'pass' : 'lg-only', tag: 'AA·lg' };
    return { v, cls: 'fail', tag: 'FAIL' };
  }
  window.__sorchaRatio = ratio; // exposed for inline checks

  /* ---------- Palettes ---------- */
  const P = {
    dark: {
      bg: '#0A0B14', surface: '#14162B', alt: '#0E1020', sunk: '#070810',
      border: '#282C46', borderStrong: '#3A3F63',
      surfaces: [
        ['--bg', '#0A0B14', 'Page ground'],
        ['--surface', '#14162B', 'Raised card / panel'],
        ['--surface-alt', '#0E1020', 'Section banding'],
        ['--surface-sunk', '#070810', 'Code wells / inputs'],
        ['--border', '#282C46', 'Hairline'],
        ['--border-strong', '#3A3F63', 'Emphasised line / outline btn'],
      ],
      fg: [
        ['--text', '#EDEEF8', 'Body & headings — use anywhere'],
        ['--text-muted', '#9A9FBC', 'Secondary copy, captions'],
        ['--text-faint', '#6B7095', 'Decorative / large numerals only — not body'],
        ['--link / --verify', '#A5B4FC', 'Links, verified text, inline proof'],
        ['--primary', '#6366F1', 'Large display, icons, borders — NOT small body'],
        ['--primary-strong', '#818CF8', 'Large accents, hover glow, icons'],
        ['--success', '#34D399', 'Success text & icons'],
        ['--warning', '#FBBF24', 'Warning text & icons'],
        ['--error', '#F87171', 'Error text & icons'],
      ],
    },
    light: {
      bg: '#FCFCFE', surface: '#FFFFFF', alt: '#F3F4FB', sunk: '#F0F1F8',
      border: '#E4E5F1', borderStrong: '#CDD0E4',
      surfaces: [
        ['--bg', '#FCFCFE', 'Page ground'],
        ['--surface', '#FFFFFF', 'Raised card / panel'],
        ['--surface-alt', '#F3F4FB', 'Section banding (faint indigo)'],
        ['--surface-sunk', '#F0F1F8', 'Code wells / inputs'],
        ['--border', '#E4E5F1', 'Hairline'],
        ['--border-strong', '#CDD0E4', 'Emphasised line / outline btn'],
      ],
      fg: [
        ['--text', '#14162B', 'Body & headings — use anywhere'],
        ['--text-muted', '#585E7C', 'Secondary copy, captions'],
        ['--text-faint', '#8A8FA8', 'Decorative only — not body'],
        ['--link / --verify', '#4338CA', 'Links, verified text, inline proof'],
        ['--primary', '#6366F1', 'Large display, icons, borders — NOT small body'],
        ['--primary-strong', '#4F46E5', 'Links, large accents, icons'],
        ['--success', '#047857', 'Success text & icons'],
        ['--warning', '#B45309', 'Warning text & icons'],
        ['--error', '#C81E1E', 'Error text & icons'],
      ],
    },
  };

  const ACTIONS = {
    dark: [
      ['Primary button', '#FFFFFF', '#4F46E5', '--btn-primary-bg fill, white label'],
      ['Accent / CTA (violet)', '#0A0B14', '#A5B4FC', 'Lilac fill, near-black label'],
      ['Accent / CTA (gold alt)', '#1A1405', '#F4C04E', 'Hallmark fill, near-black label'],
      ['Outline button label', '#EDEEF8', '#0A0B14', 'Text on ground'],
      ['Chip “Implemented” tag', '#A5B4FC', '#14162B', 'Verify text on surface'],
    ],
    light: [
      ['Primary button', '#FFFFFF', '#4F46E5', '--btn-primary-bg fill, white label'],
      ['Accent / CTA (violet)', '#0A0B14', '#A5B4FC', 'Lilac fill, near-black label'],
      ['Accent / CTA (gold alt)', '#1A1405', '#F4C04E', 'Hallmark fill, near-black label'],
      ['Outline button label', '#14162B', '#FFFFFF', 'Text on surface'],
      ['Chip “Implemented” tag', '#4338CA', '#FFFFFF', 'Verify text on surface'],
    ],
  };

  function ratioCell(r, large) {
    const x = rate(r, large);
    return `<span class="ratio">${x.v}:1 <span class="ratio__pill ${x.cls}">${x.tag}</span></span>`;
  }

  function buildFg(theme) {
    const p = P[theme];
    let rows = `<tr><th>Token</th><th>on --bg</th><th>on --surface-alt</th><th>May / may-not use</th></tr>`;
    p.fg.forEach(([name, hex, use]) => {
      const large = /faint|primary\b|primary-strong/.test(name);
      rows += `<tr>
        <td><span class="sw"><span class="sw__chip" style="background:${hex}"></span>
          <span><span class="sw__name">${name}</span><br><span class="sw__hex">${hex}</span></span></span></td>
        <td>${ratioCell(ratio(hex, p.bg), large)}</td>
        <td>${ratioCell(ratio(hex, p.alt), large)}</td>
        <td class="usage">${use}</td></tr>`;
    });
    return rows;
  }
  function buildSurfaces(theme) {
    const p = P[theme];
    let rows = `<tr><th>Token</th><th>Hex</th><th>Role</th></tr>`;
    p.surfaces.forEach(([name, hex, role]) => {
      rows += `<tr>
        <td><span class="sw"><span class="sw__chip" style="background:${hex}"></span>
          <span class="sw__name">${name}</span></span></td>
        <td><span class="sw__hex">${hex}</span></td>
        <td class="usage">${role}</td></tr>`;
    });
    return rows;
  }
  function buildActions(theme) {
    let rows = `<tr><th>Pairing</th><th>Contrast</th><th>Detail</th></tr>`;
    ACTIONS[theme].forEach(([label, fg, bg, detail]) => {
      rows += `<tr>
        <td><span class="sw"><span class="sw__chip" style="background:${bg};color:${fg};display:grid;place-items:center;font-weight:800;font-size:11px;font-family:var(--font-mono)">A</span>
          <span class="sw__name">${label}</span></span></td>
        <td>${ratioCell(ratio(fg, bg), false)}</td>
        <td class="usage">${detail}</td></tr>`;
    });
    return rows;
  }

  function fill(id, html) { const el = document.getElementById(id); if (el) el.innerHTML = html; }
  document.addEventListener('DOMContentLoaded', function () {
    ['dark', 'light'].forEach(t => {
      fill('surf-' + t, buildSurfaces(t));
      fill('fg-' + t, buildFg(t));
      fill('act-' + t, buildActions(t));
    });

    /* ---------- Theme + accent toggles ---------- */
    const root = document.documentElement;
    function swap(fn) {
      root.classList.add('swapping');
      fn();
      requestAnimationFrame(() => requestAnimationFrame(() => root.classList.remove('swapping')));
    }
    function setTheme(t) {
      swap(() => root.setAttribute('data-theme', t));
      document.querySelectorAll('[data-theme-btn]').forEach(b =>
        b.setAttribute('aria-pressed', b.getAttribute('data-theme-btn') === t));
    }
    function setAccent(a) {
      swap(() => root.setAttribute('data-accent', a));
      document.querySelectorAll('[data-accent-btn]').forEach(b =>
        b.setAttribute('aria-pressed', b.getAttribute('data-accent-btn') === a));
    }
    document.querySelectorAll('[data-theme-btn]').forEach(b =>
      b.addEventListener('click', () => setTheme(b.getAttribute('data-theme-btn'))));
    document.querySelectorAll('[data-accent-btn]').forEach(b =>
      b.addEventListener('click', () => setAccent(b.getAttribute('data-accent-btn'))));
    setTheme(root.getAttribute('data-theme') || 'dark');
    setAccent(root.getAttribute('data-accent') || 'violet');

    /* ---------- Copy buttons ---------- */
    document.querySelectorAll('.code__copy').forEach(btn => {
      btn.addEventListener('click', () => {
        const pre = btn.closest('.code').querySelector('pre');
        navigator.clipboard && navigator.clipboard.writeText(pre.innerText);
        const o = btn.textContent; btn.textContent = 'Copied'; setTimeout(() => btn.textContent = o, 1400);
      });
    });

    /* ---------- Hero particle canvas ---------- */
    initParticles();
  });

  /* ---------- Calm drifting particles ---------- */
  function initParticles() {
    const canvas = document.getElementById('heroCanvas');
    if (!canvas) return;
    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const ctx = canvas.getContext('2d');
    let w, h, dpr, particles = [], raf = null, visible = true;
    const GRID = 56;

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
        const gx = Math.round((Math.random() * w) / GRID) * GRID;
        const gy = Math.round((Math.random() * h) / GRID) * GRID;
        particles.push({
          x: gx, y: gy, s: 1.5 + Math.random() * 2.5,
          vx: (Math.random() - 0.5) * 0.12, vy: (Math.random() - 0.5) * 0.12,
          a: 0.18 + Math.random() * 0.3, tw: Math.random() * Math.PI * 2,
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
        const s = p.s;
        ctx.fillRect(p.x - s / 2, p.y - s / 2, s, s);
      }
      ctx.shadowBlur = 0;
      if (visible) raf = requestAnimationFrame(draw);
    }
    function start() { if (!raf && !reduce) { visible = true; raf = requestAnimationFrame(draw); } }
    function stop() { visible = false; if (raf) { cancelAnimationFrame(raf); raf = null; } }

    size(); seed();
    if (reduce) { draw(); }      // single static frame
    else {
      const io = new IntersectionObserver((e) => { e[0].isIntersecting ? start() : stop(); }, { threshold: 0.01 });
      io.observe(canvas);
      start();
    }
    let to;
    window.addEventListener('resize', () => { clearTimeout(to); to = setTimeout(() => { size(); seed(); if (reduce) draw(); }, 180); });
  }
})();
