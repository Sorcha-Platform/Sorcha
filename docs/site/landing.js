// Sorcha Landing Page JavaScript

(function() {
    'use strict';

    // Smooth scroll for anchor links
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

    // Nav scroll effects
    function setupScrollEffects() {
        const nav = document.querySelector('.nav');
        window.addEventListener('scroll', () => {
            if (window.pageYOffset > 50) {
                nav.style.boxShadow = '0 2px 10px rgba(0, 0, 0, 0.1)';
            } else {
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

        // Close on link click
        links.querySelectorAll('a').forEach(link => {
            link.addEventListener('click', () => {
                if (window.innerWidth <= 768) {
                    links.style.display = 'none';
                }
            });
        });

        // Reset on resize
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

        // Animate cards and sections
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
        setupSmoothScroll();
        setupScrollEffects();
        setupMobileNav();
        setupScrollAnimations();
    });
})();
