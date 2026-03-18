// Auto-scroll module for Blueprint Chat Designer
// Uses IntersectionObserver to detect when user is at bottom
// and MutationObserver to auto-scroll on new content

export function initAutoScroll(container) {
    const sentinel = container.querySelector('.scroll-sentinel');
    if (!sentinel) return null;

    let autoScroll = true;

    const intersectionObserver = new IntersectionObserver(([entry]) => {
        autoScroll = entry.isIntersecting;
    }, { root: container, threshold: 0.1 });

    intersectionObserver.observe(sentinel);

    const mutationObserver = new MutationObserver(() => {
        if (autoScroll) {
            sentinel.scrollIntoView({ behavior: 'smooth', block: 'end' });
        }
    });

    mutationObserver.observe(container, { childList: true, subtree: true, characterData: true });

    // Initial scroll to bottom
    sentinel.scrollIntoView({ block: 'end' });

    return { intersectionObserver, mutationObserver, sentinel };
}

export function dispose(state) {
    if (state) {
        state.intersectionObserver?.disconnect();
        state.mutationObserver?.disconnect();
    }
}
