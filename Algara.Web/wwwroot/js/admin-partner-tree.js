(function () {
    'use strict';

    const tree = document.querySelector('[data-partner-tree]');
    if (!tree) return;

    const nodes = Array.from(tree.querySelectorAll('[data-group]')).map(row => ({
        id: row.dataset.group,
        parent: row.dataset.parent || null,
        row,
        link: row.querySelector('[data-tree-link]'),
        toggle: row.querySelector('[data-tree-toggle]')
    }));
    const byId = new Map(nodes.map(node => [node.id, node]));
    const storageKey = 'algara.partner-groups.expanded.v1';
    let expanded = new Set(nodes.filter(node => node.parent === null).map(node => node.id));
    try {
        const saved = JSON.parse(localStorage.getItem(storageKey));
        if (Array.isArray(saved)) expanded = new Set(saved.filter(id => typeof id === 'string' && byId.has(id)));
    } catch (_) { /* The tree also works when storage is disabled or unavailable. */ }

    // Opening a deep group from a link must reveal its path even when ancestors
    // were collapsed during an earlier visit. Keep other branches as they were.
    let ancestor = nodes.find(node => node.link.getAttribute('aria-current') === 'page');
    const visited = new Set();
    while (ancestor?.parent && !visited.has(ancestor.parent)) {
        visited.add(ancestor.parent);
        expanded.add(ancestor.parent);
        ancestor = byId.get(ancestor.parent);
    }

    function render() {
        // Rows arrive in preorder, so each parent's visibility is already known.
        for (const node of nodes) {
            const parent = byId.get(node.parent);
            node.row.hidden = Boolean(parent && (parent.row.hidden || !expanded.has(parent.id)));
            if (node.toggle) {
                const open = expanded.has(node.id);
                node.toggle.setAttribute('aria-expanded', String(open));
                const label = open ? node.toggle.dataset.collapseLabel : node.toggle.dataset.expandLabel;
                node.toggle.setAttribute('aria-label', label);
                node.toggle.title = label;
            }
        }
    }

    function setExpanded(node, open) {
        if (open) expanded.add(node.id);
        else expanded.delete(node.id);
        render();
        try { localStorage.setItem(storageKey, JSON.stringify(Array.from(expanded))); }
        catch (_) { /* Expansion stays available for the current page. */ }
    }

    tree.addEventListener('click', function (event) {
        const toggle = event.target.closest('[data-tree-toggle]');
        if (!toggle) return;
        const node = byId.get(toggle.closest('[data-group]').dataset.group);
        if (node) setExpanded(node, !expanded.has(node.id));
    });

    tree.addEventListener('keydown', function (event) {
        if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.isComposing) return;
        // Action links and toggle buttons retain their usual keyboard behavior.
        const link = event.target.closest('[data-tree-link]');
        if (!link) return;
        const node = byId.get(link.closest('[data-group]').dataset.group);
        const visible = nodes.filter(item => !item.row.hidden);
        const index = visible.indexOf(node);
        let next;
        switch (event.key) {
            case 'ArrowDown': next = visible[Math.min(index + 1, visible.length - 1)]; break;
            case 'ArrowUp': next = visible[Math.max(index - 1, 0)]; break;
            case 'Home': next = visible[0]; break;
            case 'End': next = visible[visible.length - 1]; break;
            case 'ArrowRight':
                if (node.toggle && !expanded.has(node.id)) setExpanded(node, true);
                else next = visible.find(item => item.parent === node.id);
                break;
            case 'ArrowLeft':
                if (node.toggle && expanded.has(node.id)) setExpanded(node, false);
                else next = byId.get(node.parent);
                break;
            default: return;
        }
        event.preventDefault();
        next?.link.focus();
    });

    render();
    tree.dataset.enhanced = 'true';
})();
