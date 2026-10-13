const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/admin-partner-tree.js'), 'utf8');

function openTree({ selected = '0', stored = null, unavailableStorage = false } = {}) {
    let focused = null;
    const events = {};
    const definition = [['0', null], ['1', '0'], ['2', '1'], ['3', '2'], ['4', '0']];
    const nodes = definition.map(([id, parent]) => {
        const row = {
            dataset: { group: id, parent: parent ?? '' },
            hidden: false,
            closest: selector => selector === '[data-group]' ? row : null
        };
        const link = {
            getAttribute: name => name === 'aria-current' && selected === id ? 'page' : null,
            closest: selector => selector === '[data-tree-link]' ? link : row.closest(selector),
            focus: () => { focused = id; }
        };
        const toggle = definition.some(([, p]) => p === id) ? {
            dataset: { expandLabel: `Expand ${id}`, collapseLabel: `Collapse ${id}` },
            attributes: {},
            setAttribute: (name, value) => { toggle.attributes[name] = value; },
            closest: selector => selector === '[data-tree-toggle]' ? toggle : row.closest(selector)
        } : null;
        const action = { closest: () => null };
        row.querySelector = selector => selector === '[data-tree-link]' ? link : toggle;
        return { id, row, link, toggle, action };
    });
    const tree = {
        dataset: {},
        querySelectorAll: () => nodes.map(node => node.row),
        addEventListener: (name, handler) => { events[name] = handler; }
    };
    let storage = stored;
    vm.runInNewContext(source, {
        document: { querySelector: () => tree },
        localStorage: {
            getItem: () => { if (unavailableStorage) throw Error('blocked'); return storage; },
            setItem: (_, value) => { if (unavailableStorage) throw Error('blocked'); storage = value; }
        }
    });
    return {
        tree,
        node: id => nodes.find(node => node.id === id),
        visible: () => nodes.filter(node => !node.row.hidden).map(node => node.id),
        focused: () => focused,
        stored: () => storage,
        click(id) { events.click({ target: this.node(id).toggle }); },
        key(id, key, extra = {}, target = 'link') {
            let prevented = false;
            events.keydown({ target: this.node(id)[target], key, preventDefault: () => { prevented = true; }, ...extra });
            return prevented;
        }
    };
}

test('the root starts open and nested branches can expand without navigating', () => {
    const page = openTree();
    assert.deepEqual(page.visible(), ['0', '1', '4']);
    assert.equal(page.tree.dataset.enhanced, 'true');
    page.click('1');
    assert.deepEqual(page.visible(), ['0', '1', '2', '4']);
    assert.equal(page.node('1').toggle.attributes['aria-expanded'], 'true');
    assert.equal(page.node('1').toggle.attributes['aria-label'], 'Collapse 1');
    page.click('1');
    assert.equal(page.node('1').toggle.attributes['aria-label'], 'Expand 1');
});

test('selecting a deep group reveals the complete ancestor path', () => {
    const page = openTree({ selected: '3', stored: '[]' });
    assert.deepEqual(page.visible(), ['0', '1', '2', '3', '4']);
});

test('collapsing a branch hides all descendants but preserves nested expansion on reopen', () => {
    const page = openTree({ stored: '["0","1","2"]' });
    page.click('1');
    assert.deepEqual(page.visible(), ['0', '1', '4']);
    page.click('1');
    assert.deepEqual(page.visible(), ['0', '1', '2', '3', '4']);
    const restored = openTree({ stored: page.stored() });
    assert.deepEqual(restored.visible(), page.visible());
});

test('saved collapsed root and unknown or removed group IDs are handled', () => {
    const page = openTree({ stored: '["obsolete", 1]' });
    assert.deepEqual(page.visible(), ['0']);
    page.click('0');
    assert.deepEqual(page.visible(), ['0', '1', '4']);
    assert.deepEqual(JSON.parse(page.stored()), ['0']);
});

test('broken or blocked storage never prevents expanding the tree', () => {
    for (const options of [{ stored: '{broken' }, { unavailableStorage: true }, { stored: '{}' }]) {
        const page = openTree(options);
        assert.deepEqual(page.visible(), ['0', '1', '4']);
        page.click('1');
        assert.deepEqual(page.visible(), ['0', '1', '2', '4']);
    }
});

test('up/down and home/end navigate visible group links, skipping collapsed descendants', () => {
    const page = openTree();
    assert.equal(page.key('1', 'ArrowDown'), true);
    assert.equal(page.focused(), '4');
    page.key('4', 'ArrowUp');
    assert.equal(page.focused(), '1');
    page.key('1', 'Home');
    assert.equal(page.focused(), '0');
    page.key('0', 'End');
    assert.equal(page.focused(), '4');
});

test('right opens a branch then enters it; left collapses or returns to the parent', () => {
    const page = openTree();
    page.key('1', 'ArrowRight');
    assert.deepEqual(page.visible(), ['0', '1', '2', '4']);
    page.key('1', 'ArrowRight');
    assert.equal(page.focused(), '2');
    page.key('2', 'ArrowLeft');
    assert.equal(page.focused(), '1');
    page.key('1', 'ArrowLeft');
    assert.deepEqual(page.visible(), ['0', '1', '4']);
});

test('normal navigation, modified keys and inline actions retain their default behavior', () => {
    const page = openTree();
    for (const key of ['Enter', 'Tab', ' ']) assert.equal(page.key('1', key), false);
    for (const modifier of ['ctrlKey', 'metaKey', 'altKey', 'shiftKey', 'isComposing']) {
        assert.equal(page.key('1', 'ArrowDown', { [modifier]: true }), false);
    }
    assert.equal(page.key('1', 'ArrowDown', {}, 'action'), false);
    assert.equal(page.key('1', 'ArrowDown', {}, 'toggle'), false);
});

test('pages without a partner tree are unaffected', () => {
    assert.doesNotThrow(() => vm.runInNewContext(source, { document: { querySelector: () => null } }));
});
