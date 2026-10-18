const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');
const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/admin-partner-move.js'), 'utf8');

function element(selector, dataset = {}, parent = null) {
    const attributes = {};
    const classes = new Set();
    const item = {
        dataset, attributes, events: {}, children: {}, hidden: false, checked: false, disabled: false, textContent: '',
        classList: { toggle(name, value) { if (value) classes.add(name); else classes.delete(name); }, contains: name => classes.has(name) },
        closest: requested => selector === requested ? item : parent?.closest(requested) ?? null,
        setAttribute: (name, value) => { attributes[name] = value; },
        removeAttribute: name => { delete attributes[name]; },
        addEventListener: (name, handler) => { item.events[name] = handler; },
        querySelector: requested => item.children[requested] ?? null
    };
    return item;
}

function openPage({ stored = null, scope = '/admin/partners?groupN=0', blocked = false,
    pagePartners = [[101, 2], [102, 4]], fetchResponse = null, canMoveGroups = true, canMovePartners = true } = {}) {
    let storage = stored;
    let redirect = null;
    let reloaded = false;
    const requests = [];
    const page = element('[data-partner-move]', { moveKey: 'user-test', moveScope: scope, moveUrl: '/admin/partners/move',
        canMoveGroups: String(canMoveGroups), canMovePartners: String(canMovePartners) });
    const names = ['move-mode', 'move-submit', 'move-error', 'move-filters', 'move-summary', 'move-destination', 'move-storage-warning', 'move-filter-form', 'move-toggle-panel', 'move-reload'];
    for (const name of names) page.children[`[data-${name}]`] = element(`[data-${name}]`);
    page.children['[name="__RequestVerificationToken"]'] = { value: 'csrf-test' };
    const groups = [[0, null], [1, 0], [2, 1], [3, 2], [4, 0]].map(([n, parent]) => {
        const row = element('[data-group]', { group: String(n), parent: parent === null ? '' : String(parent), rowVersion: `version-${n}`, groupName: `Group ${n}`, groupPath: `Root / Group ${n}` });
        for (const name of ['tree-link', 'move-target-badge', 'move-group-included']) row.children[`[data-${name}]`] = element(`[data-${name}]`, {}, row);
        if (n && canMoveGroups) row.children['[data-move-group]'] = element('[data-move-group]', { moveGroup: String(n) }, row);
        return row;
    });
    const partners = pagePartners.map(([n, groupN]) => {
        const row = element('[data-move-partner-row]', { movePartnerRow: String(n), partnerGroup: String(groupN), rowVersion: `version-${n}`, partnerName: `Partner ${n}` });
        if (canMovePartners) row.children['[data-move-partner]'] = element('[data-move-partner]', { movePartner: String(n) }, row);
        row.children['[data-move-partner-included]'] = element('[data-move-partner-included]', {}, row);
        return row;
    });
    const collections = {
        '[data-group]': groups, '[data-move-partner-row]': partners,
        '[data-move-only]': [element('[data-move-only]')],
        '[data-move-source-actions]': [element('[data-move-source-actions]')],
        '[data-move-source-link]': [element('[data-move-source-link]')],
        '[data-move-page]': [element('[data-move-page]')]
    };
    page.querySelectorAll = selector => collections[selector] || [];
    vm.runInNewContext(source, {
        document: { querySelector: () => page },
        sessionStorage: {
            getItem: () => { if (blocked) throw Error('blocked'); return storage; },
            setItem: (_, value) => { if (blocked) throw Error('blocked'); storage = value; },
            removeItem: () => { if (blocked) throw Error('blocked'); storage = null; }
        },
        fetch: async (url, options) => {
            requests.push({ url, ...options, body: JSON.parse(options.body) });
            return fetchResponse ? await fetchResponse() : { ok: true, json: async () => ({ redirectUrl: '/admin/partners?groupN=4' }) };
        },
        window: { location: { assign: url => { redirect = url; }, reload: () => { reloaded = true; } } }
    });
    const control = name => page.children[`[data-move-${name}]`];
    const group = n => groups.find(row => Number(row.dataset.group) === n);
    const partner = n => partners.find(row => Number(row.dataset.movePartnerRow) === n);
    return {
        page, groups, partners, control, group, partner, collections, requests,
        stored: () => storage, redirect: () => redirect, reloaded: () => reloaded,
        enable(value = true) { control('mode').checked = value; control('mode').events.change(); },
        selectGroup(n, checked = true) { const input = group(n).children['[data-move-group]']; input.checked = checked; page.events.change({ target: input }); },
        selectPartner(n, checked = true) { const input = partner(n).children['[data-move-partner]']; input.checked = checked; page.events.change({ target: input }); },
        click(target) { let prevented = false; page.events.click({ target, preventDefault: () => { prevented = true; } }); return prevented; },
        target(n) { return this.click(group(n).children['[data-tree-link]']); },
        submit() { return control('submit').events.click(); }
    };
}

test('move mode is opt-in and freezes source navigation while keeping pagination available', () => {
    const ui = openPage();
    assert.equal(ui.control('mode').checked, false);
    assert.equal(ui.control('toggle-panel').hidden, false);
    assert.equal(ui.control('submit').disabled, true);
    assert.equal(ui.target(4), false);
    ui.enable();
    assert.equal(ui.control('filters').disabled, true);
    assert.equal(ui.collections['[data-move-source-actions]'][0].hidden, true);
    assert.equal(ui.click(ui.collections['[data-move-source-link]'][0]), true);
    assert.equal(ui.click(ui.collections['[data-move-page]'][0]), false);
    const toggle = element('[data-tree-toggle]');
    assert.equal(ui.click(toggle), false, 'the existing tree can still expand and collapse');
    let prevented = false;
    ui.control('filter-form').events.submit({ preventDefault: () => { prevented = true; } });
    assert.equal(prevented, true);
});

test('target selection is distinct from source checkboxes and root is a valid target', () => {
    const ui = openPage();
    ui.enable();
    ui.selectPartner(101);
    assert.equal(ui.control('submit').disabled, true);
    assert.equal(ui.target(0), true);
    assert.equal(ui.group(0).children['[data-move-group]'], undefined);
    assert.equal(ui.group(0).classList.contains('is-move-target'), true);
    assert.equal(ui.group(0).children['[data-move-target-badge]'].hidden, false);
    assert.equal(ui.control('destination').textContent, 'Root / Group 0');
    assert.equal(ui.control('submit').disabled, false);
    assert.equal(ui.control('summary').textContent, 'Маркирани групи: 0; контрагенти: 1.');
});

test('selecting an ancestor includes descendants and their partners without double selection', async () => {
    const ui = openPage();
    ui.enable();
    ui.selectGroup(2);
    ui.selectPartner(101);
    ui.selectGroup(1);
    ui.target(4);
    assert.equal(ui.group(2).children['[data-move-group]'].checked, true);
    assert.equal(ui.group(2).children['[data-move-group]'].disabled, true);
    assert.equal(ui.group(3).children['[data-move-group-included]'].hidden, false);
    assert.equal(ui.partner(101).children['[data-move-partner]'].disabled, true);
    const saved = JSON.parse(ui.stored());
    assert.deepEqual(saved.groups.map(group => group.n), [1]);
    assert.deepEqual(saved.partners, []);
    await ui.submit();
    assert.deepEqual(ui.requests[0].body.groups, [{ n: 1, rowVersion: 'version-1' }]);
    assert.deepEqual(ui.requests[0].body.partners, []);
});

test('self and descendant targets are prevented and invalidated when selection changes', () => {
    const ui = openPage();
    ui.enable();
    ui.target(3);
    ui.selectGroup(1);
    assert.equal(ui.control('destination').textContent, 'Не е избрана');
    for (const n of [1, 2, 3]) {
        assert.equal(ui.group(n).children['[data-tree-link]'].attributes['aria-disabled'], 'true');
        ui.target(n);
        assert.equal(ui.control('submit').disabled, true);
    }
    ui.target(4);
    assert.equal(ui.control('submit').disabled, false);
});

test('already-present selections disable the move until an actual change is selected', () => {
    const ui = openPage();
    ui.enable();
    ui.selectPartner(102);
    ui.target(4);
    assert.equal(ui.control('submit').disabled, true);
    assert.match(ui.control('summary').textContent, /вече са в тази група/);
    ui.selectPartner(101);
    assert.equal(ui.control('submit').disabled, false);
});

test('turning the mode off clears selection, destination and storage', () => {
    const ui = openPage();
    ui.enable(); ui.selectGroup(1); ui.selectPartner(102); ui.target(4);
    ui.enable(false);
    assert.equal(ui.stored(), null);
    assert.equal(ui.control('filters').disabled, false);
    assert.equal(ui.collections['[data-move-source-actions]'][0].hidden, false);
    ui.enable();
    assert.equal(ui.group(1).children['[data-move-group]'].checked, false);
    assert.equal(ui.control('destination').textContent, 'Не е избрана');
});

test('pagination preserves selection and original row versions; changing the source scope clears it', async () => {
    const first = openPage();
    first.enable(); first.selectPartner(101); first.target(4);
    const second = openPage({ stored: first.stored(), pagePartners: [[103, 0]] });
    assert.equal(second.control('mode').checked, true);
    second.selectPartner(103);
    await second.submit();
    assert.deepEqual(second.requests[0].body.partners, [{ n: 101, rowVersion: 'version-101' }, { n: 103, rowVersion: 'version-103' }]);
    const changed = openPage({ stored: first.stored(), scope: '/admin/partners?groupN=4' });
    assert.equal(changed.control('mode').checked, false);
    assert.equal(changed.stored(), null);
});

test('blocked storage keeps same-page moves usable and prevents silently losing selection through pagination', async () => {
    const ui = openPage({ blocked: true });
    ui.enable(); ui.selectPartner(101); ui.target(4);
    assert.equal(ui.control('storage-warning').hidden, false);
    assert.equal(ui.click(ui.collections['[data-move-page]'][0]), true);
    await ui.submit();
    assert.equal(ui.requests.length, 1);
});

test('move submission sends antiforgery and only selected identities then clears state and redirects', async () => {
    const ui = openPage();
    ui.enable(); ui.selectPartner(101); ui.target(4);
    await ui.submit();
    assert.equal(ui.requests.length, 1);
    assert.equal(ui.requests[0].url, '/admin/partners/move');
    assert.equal(ui.requests[0].method, 'POST');
    assert.equal(ui.requests[0].credentials, 'same-origin');
    assert.equal(ui.requests[0].headers.RequestVerificationToken, 'csrf-test');
    assert.deepEqual(ui.requests[0].body, { destinationGroupN: 4, destinationRowVersion: 'version-4', groups: [], partners: [{ n: 101, rowVersion: 'version-101' }] });
    assert.equal(ui.stored(), null);
    assert.equal(ui.redirect(), '/admin/partners?groupN=4');
});

test('stale errors preserve selection and concurrent clicks issue only one request', async () => {
    let finish;
    const pending = new Promise(resolve => { finish = resolve; });
    const ui = openPage({ fetchResponse: () => pending });
    ui.enable(); ui.selectPartner(101); ui.target(4);
    const move = ui.submit();
    assert.equal(ui.control('mode').disabled, true);
    await ui.submit();
    assert.equal(ui.requests.length, 1);
    finish({ ok: false, status: 409, json: async () => ({ message: 'Данните са променени. Презаредете страницата.' }) });
    await move;
    assert.equal(ui.control('error').hidden, false);
    assert.match(ui.control('error').textContent, /Презаредете/);
    assert.equal(ui.partner(101).children['[data-move-partner]'].checked, true);
    assert.equal(ui.control('mode').disabled, false);
    assert.equal(ui.redirect(), null);
    assert.equal(JSON.parse(ui.stored()).partners[0].n, 101);
    assert.equal(ui.control('reload').hidden, false);
    ui.control('reload').events.click();
    assert.equal(ui.stored(), null);
    assert.equal(ui.reloaded(), true);
});

test('authentication redirects never report a successful move or clear selection', async () => {
    const ui = openPage({ fetchResponse: async () => ({ redirected: true }) });
    ui.enable(); ui.selectPartner(101); ui.target(4);
    await ui.submit();
    assert.match(ui.control('error').textContent, /Сесията е изтекла/);
    assert.equal(ui.redirect(), null);
    assert.equal(JSON.parse(ui.stored()).partners.length, 1);
});

test('pages without bulk move controls are unaffected', () => {
    assert.doesNotThrow(() => vm.runInNewContext(source, { document: { querySelector: () => null } }));
});

test('a group mover can move groups when partner selection controls are absent', async () => {
    const ui = openPage({ canMovePartners: false });
    ui.enable();
    ui.selectGroup(1);
    ui.target(4);
    await ui.submit();
    assert.equal(ui.requests.length, 1);
    assert.equal(ui.requests[0].body.groups.length, 1);
    assert.equal(ui.requests[0].body.partners.length, 0);
});

test('revoked group moving clears saved groups but keeps allowed partner selections', async () => {
    const previous = openPage();
    previous.enable();
    previous.selectGroup(1);
    previous.selectPartner(102);
    const ui = openPage({ stored: previous.stored(), canMoveGroups: false });
    ui.target(0);
    await ui.submit();
    assert.equal(ui.requests.length, 1);
    assert.equal(ui.requests[0].body.groups.length, 0);
    assert.equal(ui.requests[0].body.partners.length, 1);
    assert.equal(ui.requests[0].body.partners[0].n, 102);
});