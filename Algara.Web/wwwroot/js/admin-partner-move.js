(function () {
    'use strict';

    const page = document.querySelector('[data-partner-move]');
    if (!page) return;
    const canMoveGroups = page.dataset.canMoveGroups !== 'false';
    const canMovePartners = page.dataset.canMovePartners !== 'false';
    const query = selector => page.querySelector(selector);
    const queryAll = selector => Array.from(page.querySelectorAll(selector));
    const mode = query('[data-move-mode]');
    const submit = query('[data-move-submit]');
    const error = query('[data-move-error]');
    const groups = queryAll('[data-group]').map(row => ({
        n: Number(row.dataset.group), parentN: row.dataset.parent === '' ? null : Number(row.dataset.parent),
        rowVersion: row.dataset.rowVersion, label: row.dataset.groupName, path: row.dataset.groupPath,
        row, link: row.querySelector('[data-tree-link]'), checkbox: row.querySelector('[data-move-group]'),
        badge: row.querySelector('[data-move-target-badge]'), included: row.querySelector('[data-move-group-included]')
    }));
    const groupById = new Map(groups.map(group => [group.n, group]));
    const partners = queryAll('[data-move-partner-row]').map(row => ({
        n: Number(row.dataset.movePartnerRow), groupN: Number(row.dataset.partnerGroup),
        rowVersion: row.dataset.rowVersion, label: row.dataset.partnerName,
        row, checkbox: row.querySelector('[data-move-partner]'), included: row.querySelector('[data-move-partner-included]')
    }));
    const partnerById = new Map(partners.map(partner => [partner.n, partner]));
    const selectedGroups = new Map();
    const selectedPartners = new Map();
    const storageKey = 'algara.partner-move.v1.' + page.dataset.moveKey;
    const scope = page.dataset.moveScope;
    const limit = 1000;
    let destination = null;
    let storageAvailable = true;
    let busy = false;

    function snapshot(item) {
        return { n: item.n, rowVersion: item.rowVersion, label: item.label,
            ...(item.parentN !== undefined ? { parentN: item.parentN } : {}),
            ...(item.groupN !== undefined ? { groupN: item.groupN } : {}),
            ...(item.path !== undefined ? { path: item.path } : {}) };
    }

    function validItem(item, rootAllowed = false) {
        return item && Number.isInteger(item.n) && item.n >= (rootAllowed ? 0 : 1)
            && typeof item.rowVersion === 'string' && item.rowVersion.length <= 32
            && typeof item.label === 'string' && item.label.length <= 400;
    }

    function saveSelection() {
        if (!storageAvailable) return;
        try {
            if (!mode.checked) sessionStorage.removeItem(storageKey);
            else sessionStorage.setItem(storageKey, JSON.stringify({ scope, mode: true,
                groups: Array.from(selectedGroups.values()), partners: Array.from(selectedPartners.values()), destination }));
        } catch (_) { storageAvailable = false; }
    }

    try {
        const saved = JSON.parse(sessionStorage.getItem(storageKey));
        if (saved?.mode === true && saved.scope === scope && Array.isArray(saved.groups) && Array.isArray(saved.partners)
            && saved.groups.length + saved.partners.length <= limit
            && saved.groups.every(item => validItem(item) && groupById.has(item.n))
            && saved.partners.every(item => validItem(item) && Number.isInteger(item.groupN))) {
            if (canMoveGroups) for (const item of saved.groups) selectedGroups.set(item.n, item);
            if (canMovePartners) for (const item of saved.partners) selectedPartners.set(item.n, item);
            if (validItem(saved.destination, true) && groupById.has(saved.destination.n)) destination = saved.destination;
            mode.checked = true;
        } else sessionStorage.removeItem(storageKey);
        // Some browsers allow reads but deny writes (for example a full quota).
        saveSelection();
    } catch (_) { storageAvailable = false; }

    function selectedAncestor(n, includeSelf) {
        const visited = new Set();
        let current = includeSelf ? groupById.get(n) : groupById.get(groupById.get(n)?.parentN);
        while (current && !visited.has(current.n)) {
            if (selectedGroups.has(current.n)) return current.n;
            visited.add(current.n);
            current = groupById.get(current.parentN);
        }
        return null;
    }

    function normalizeSelection() {
        for (const n of selectedGroups.keys()) if (selectedAncestor(n, false) !== null) selectedGroups.delete(n);
        for (const [n, partner] of selectedPartners) if (selectedAncestor(partner.groupN, true) !== null) selectedPartners.delete(n);
        if (destination && selectedAncestor(destination.n, true) !== null) destination = null;
    }

    function setError(message, allowReload = false) {
        error.textContent = message;
        error.hidden = !message;
        query('[data-move-reload]').hidden = !message || !allowReload;
    }

    function render() {
        const enabled = mode.checked;
        page.classList.toggle('partner-move-active', enabled);
        for (const item of queryAll('[data-move-only]')) item.hidden = !enabled;
        for (const item of queryAll('[data-move-source-actions]')) item.hidden = enabled;
        query('[data-move-filters]').disabled = enabled;
        mode.disabled = busy;
        for (const item of queryAll('[data-move-source-link]')) {
            if (enabled) item.setAttribute('aria-disabled', 'true');
            else item.removeAttribute('aria-disabled');
        }
        for (const item of queryAll('[data-move-page]')) {
            if (enabled && (!storageAvailable || busy)) item.setAttribute('aria-disabled', 'true');
            else item.removeAttribute('aria-disabled');
        }
        for (const group of groups) {
            const inherited = selectedAncestor(group.n, false) !== null;
            const forbidden = enabled && selectedAncestor(group.n, true) !== null;
            const target = enabled && destination?.n === group.n;
            group.row.classList.toggle('is-move-target', target);
            group.row.classList.toggle('is-move-forbidden', forbidden);
            group.badge.hidden = !target;
            group.included.hidden = !enabled || !inherited;
            if (forbidden || (enabled && busy)) group.link.setAttribute('aria-disabled', 'true');
            else group.link.removeAttribute('aria-disabled');
            if (group.checkbox) {
                group.checkbox.checked = selectedGroups.has(group.n) || inherited;
                group.checkbox.disabled = busy || inherited;
            }
        }
        for (const partner of partners) {
            const inherited = selectedAncestor(partner.groupN, true) !== null;
            if (partner.checkbox) {
                partner.checkbox.checked = selectedPartners.has(partner.n) || inherited;
                partner.checkbox.disabled = busy || inherited || !canMovePartners;
            }
            partner.included.hidden = !enabled || !inherited;
        }
        let summary = `Маркирани групи: ${selectedGroups.size}; контрагенти: ${selectedPartners.size}`;
        if (Array.from(selectedPartners.keys()).some(n => !partnerById.has(n))) summary += ' (включително от други страници)';
        summary += '.';
        const changes = destination && (Array.from(selectedGroups.values()).some(group => group.parentN !== destination.n)
            || Array.from(selectedPartners.values()).some(partner => partner.groupN !== destination.n));
        if (destination && selectedGroups.size + selectedPartners.size > 0 && !changes) summary += ' Избраните записи вече са в тази група.';
        query('[data-move-summary]').textContent = summary;
        query('[data-move-destination]').textContent = destination?.path || destination?.label || 'Не е избрана';
        query('[data-move-storage-warning]').hidden = !enabled || storageAvailable;
        submit.disabled = !enabled || busy || !changes;
        submit.setAttribute('aria-busy', String(busy));
    }

    function changed() {
        normalizeSelection();
        saveSelection();
        render();
    }

    mode.addEventListener('change', function () {
        if (!mode.checked) {
            selectedGroups.clear();
            selectedPartners.clear();
            destination = null;
        }
        setError('');
        changed();
    });

    page.addEventListener('change', function (event) {
        if (!mode.checked || busy) return;
        const groupInput = event.target.closest('[data-move-group]');
        const partnerInput = event.target.closest('[data-move-partner]');
        const item = groupInput ? groupById.get(Number(groupInput.dataset.moveGroup))
            : partnerInput ? partnerById.get(Number(partnerInput.dataset.movePartner)) : null;
        if (!item) return;
        const input = groupInput || partnerInput;
        const collection = groupInput ? selectedGroups : selectedPartners;
        if (input.checked) {
            if (selectedGroups.size + selectedPartners.size >= limit) {
                input.checked = false;
                setError(`Можете да маркирате най-много ${limit} записа наведнъж.`);
                return;
            }
            collection.set(item.n, snapshot(item));
        } else collection.delete(item.n);
        setError('');
        changed();
    });

    page.addEventListener('click', function (event) {
        if (!mode.checked) return;
        const link = event.target.closest('[data-tree-link]');
        if (link) {
            event.preventDefault();
            if (busy) return;
            const group = groupById.get(Number(link.closest('[data-group]').dataset.group));
            if (!group || selectedAncestor(group.n, true) !== null) return;
            destination = snapshot(group);
            setError('');
            changed();
            return;
        }
        if (event.target.closest('[data-move-source-link]') || event.target.closest('[data-move-source-actions]')
            || ((!storageAvailable || busy) && event.target.closest('[data-move-page]'))) event.preventDefault();
    });
    query('[data-move-filter-form]').addEventListener('submit', function (event) {
        if (mode.checked) event.preventDefault();
    });

    submit.addEventListener('click', async function () {
        if (submit.disabled || busy || !destination) return;
        busy = true;
        setError('');
        render();
        const identity = item => ({ n: item.n, rowVersion: item.rowVersion });
        try {
            const response = await fetch(page.dataset.moveUrl, {
                method: 'POST', credentials: 'same-origin',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': query('[name="__RequestVerificationToken"]').value },
                body: JSON.stringify({ destinationGroupN: destination.n, destinationRowVersion: destination.rowVersion,
                    groups: Array.from(selectedGroups.values(), identity), partners: Array.from(selectedPartners.values(), identity) })
            });
            if (response.redirected) throw new Error('Сесията е изтекла. Презаредете страницата и влезте отново.');
            const result = await response.json();
            if (!response.ok) throw new Error(result.message || 'Преместването не беше извършено. Презаредете страницата и опитайте отново.');
            if (!result.redirectUrl || typeof result.redirectUrl !== 'string' || !result.redirectUrl.startsWith('/') || result.redirectUrl.startsWith('//'))
                throw new Error('Презаредете страницата, за да проверите резултата от преместването.');
            mode.checked = false;
            selectedGroups.clear();
            selectedPartners.clear();
            destination = null;
            saveSelection();
            window.location.assign(result.redirectUrl);
        } catch (failure) {
            const message = failure instanceof SyntaxError || failure instanceof TypeError
                ? 'Не успяхме да потвърдим преместването. Презаредете страницата, за да проверите записите, преди да опитате отново.'
                : failure.message;
            setError(message || 'Преместването не беше извършено. Презаредете страницата и опитайте отново.', true);
        } finally {
            busy = false;
            render();
        }
    });

    query('[data-move-reload]').addEventListener('click', function () {
        if (busy) return;
        mode.checked = false;
        selectedGroups.clear();
        selectedPartners.clear();
        destination = null;
        saveSelection();
        window.location.reload();
    });

    normalizeSelection();
    render();
    query('[data-move-toggle-panel]').hidden = false;
})();
