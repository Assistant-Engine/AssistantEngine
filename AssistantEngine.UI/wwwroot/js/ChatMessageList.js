window.customElements.define('chat-messages', class ChatMessages extends HTMLElement {
    constructor() {
        super();
        this._first = true;
        this._next = 0;
    }

    connectedCallback() {
        // Observe child inserts and streaming text changes under this element
        this._observer = new MutationObserver(muts => this._schedule(muts));
        this._observer.observe(this, {
            childList: true,
            characterData: true,
            subtree: true
        });
    }

    disconnectedCallback() {
        this._observer?.disconnect();
        if (this._next) cancelAnimationFrame(this._next);
        this._next = 0;
    }

    _schedule(muts) {
        if (this._next) cancelAnimationFrame(this._next);
        // detect a newly added user message (works even with wrappers)
        this._addedUser = muts.some(m =>
            Array.from(m.addedNodes || []).some(n =>
                n.nodeType === 1 && n.closest?.('.user-message')
            )
        );
        this._next = requestAnimationFrame(() => this._autoScroll());
    }

    _autoScroll() {
        const nearBottom = this._isNearBottom(300);
        if (this._first || this._addedUser || nearBottom) {
            // Prefer container scroll for stability
            if (this.scrollHeight > this.clientHeight) {
                this.scrollTop = this.scrollHeight;
            } else {
                const anchor =
                    this.querySelector('.end-anchor')
                    ?? this.lastElementChild?.lastElementChild
                    ?? this.lastElementChild;

                anchor?.scrollIntoView({
                    behavior: this._first ? 'instant' : 'smooth',
                    block: 'end'
                });
            }
            this._first = false;
        }
        this._addedUser = false;
    }

    _isNearBottom(threshold) {
        // Use the element as the scroll container, not window
        const remaining = this.scrollHeight - this.clientHeight - this.scrollTop;
        return remaining < threshold;
    }
});
