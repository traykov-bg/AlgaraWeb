const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/admin-product-shipping.js'), 'utf8');

class Element {
    constructor(attributes = {}, children = []) {
        this.attributes = { ...attributes }; this.children = []; this.handlers = {};
        this.dataset = Object.fromEntries(Object.entries(attributes).filter(([key]) => key.startsWith('data-'))
            .map(([key, value]) => [key.slice(5).replace(/-([a-z])/g, (_, c) => c.toUpperCase()), value]));
        this.value = ''; this.hidden = false; this.checked = false;
        children.forEach(child => this.appendChild(child));
    }
    matches(selector) {
        const match = /^\[([^=\]]+)(?:="([^"]*)")?\]$/.exec(selector);
        assert.ok(match, selector);
        return Object.hasOwn(this.attributes, match[1]) && (match[2] === undefined || this.attributes[match[1]] === match[2]);
    }
    querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; }
    closest(selector) { return this.matches(selector) ? this : this.parent?.closest(selector) ?? null; }
    appendChild(child) { child.parent = this; this.children.push(child); }
    remove() { this.parent.children.splice(this.parent.children.indexOf(this), 1); this.parent = null; }
    contains(child) { return child === this || this.children.some(item => item.contains(child)); }
    setAttribute(name, value) { this.attributes[name] = value; }
    addEventListener(name, handler) { this.handlers[name] = handler; }
    focus() { this.focused = true; }
    cloneNode() { const clone = new Element(this.attributes, this.children.map(child => child.cloneNode())); clone.value = this.value; return clone; }
}
const element = (attribute, value = '', children = []) => new Element({ [attribute]: value }, children);
function parcel() {
    return element('data-parcel-row', '', [
        element('data-parcel-title'), element('data-parcel-remove'), element('data-parcel-catalog-note'),
        ...['WeightKg', 'WidthCm', 'HeightCm', 'DepthCm'].flatMap(field => [
            element('data-parcel-label', field), element('data-parcel-error', field), element('data-parcel-invariant', field),
            new Element({ 'data-parcel-field': field, ...(field !== 'WeightKg' ? { 'data-parcel-dimension': '' } : {}) })
        ])
    ]);
}
function setup(count = 0, useCatalog = false, maximum = 50) {
    const list = element('data-parcel-list', '', Array.from({ length: count }, parcel));
    const template = element('data-parcel-template');
    template.content = { firstElementChild: parcel() };
    const reuse = element('data-shipping-use-catalog'); reuse.checked = useCatalog;
    const add = element('data-parcel-add');
    const editor = new Element({ 'data-product-shipping': '', 'data-max-parcels': String(maximum) },
        [list, template, reuse, add, element('data-parcel-empty'), element('data-parcel-status')]);
    vm.runInNewContext(source, { document: { querySelectorAll: () => [editor] } });
    return {
        editor, list, reuse, add,
        rows: () => list.children,
        addRow: () => add.handlers.click(),
        toggle: checked => { reuse.checked = checked; reuse.handlers.change(); },
        remove: index => list.handlers.click({ target: list.children[index].querySelector('[data-parcel-remove]') })
    };
}

test('empty legacy editor invents no parcel and add creates fields without submitting', () => {
    const ui = setup();
    assert.equal(ui.rows().length, 0);
    ui.addRow();
    const weight = ui.rows()[0].querySelector('[data-parcel-field="WeightKg"]');
    assert.equal(weight.name, 'ShippingParcels[0].WeightKg');
    assert.equal(weight.value, '');
    assert.equal(weight.focused, true);
    assert.equal(ui.rows()[0].querySelector('[data-parcel-dimension]').required, true);
});

test('removing a middle parcel preserves values while reindexing names labels and errors', () => {
    const ui = setup(3);
    ui.rows()[2].querySelector('[data-parcel-field="WeightKg"]').value = '2.125';
    ui.remove(1);
    const last = ui.rows()[1];
    assert.equal(last.querySelector('[data-parcel-field="WeightKg"]').value, '2.125');
    assert.equal(last.querySelector('[data-parcel-field="WeightKg"]').name, 'ShippingParcels[1].WeightKg');
    assert.equal(last.querySelector('[data-parcel-label="WeightKg"]').htmlFor, 'ShippingParcels_1__WeightKg');
    assert.equal(last.querySelector('[data-parcel-error="WeightKg"]').attributes['data-valmsg-for'], 'ShippingParcels[1].WeightKg');
});

test('only explicit single-parcel reuse excludes separate dimensions from submission', () => {
    const ui = setup(1);
    const dimension = ui.rows()[0].querySelector('[data-parcel-dimension]');
    dimension.value = '15.5';
    assert.equal(dimension.disabled, false);
    ui.toggle(true);
    assert.equal(dimension.disabled, true);
    assert.equal(dimension.required, false);
    assert.equal(dimension.value, '15.5');
    ui.toggle(false);
    assert.equal(dimension.disabled, false);
    assert.equal(dimension.required, true);
    assert.equal(dimension.value, '15.5');
});

test('multiple parcels never silently reuse one set of catalog dimensions', () => {
    const ui = setup(1, true);
    ui.addRow();
    for (const row of ui.rows()) assert.equal(row.querySelector('[data-parcel-dimension]').disabled, false);
    assert.equal(ui.reuse.checked, true);
    assert.notEqual(ui.editor.querySelector('[data-parcel-status]').textContent, '');
});

test('parcel count bound applies even if add click is programmatically triggered', () => {
    const ui = setup(1, false, 2);
    ui.addRow(); ui.addRow();
    assert.equal(ui.rows().length, 2);
    assert.equal(ui.add.disabled, true);
    ui.remove(1);
    assert.equal(ui.add.disabled, false);
});

test('removing the last row restores empty state without creating a replacement', () => {
    const ui = setup(1, true);
    ui.remove(0);
    assert.equal(ui.rows().length, 0);
    assert.equal(ui.editor.querySelector('[data-parcel-empty]').hidden, false);
});

test('browser decimal invariant markers follow new row names and reindex after removal', () => {
    const ui = setup(1);
    ui.addRow();
    for (const field of ['WeightKg', 'WidthCm', 'HeightCm', 'DepthCm']) {
        const marker = ui.rows()[1].querySelector('[data-parcel-invariant="' + field + '"]');
        assert.equal(marker.value, 'ShippingParcels[1].' + field);
        assert.equal(marker.disabled, false);
    }
    ui.remove(0);
    for (const field of ['WeightKg', 'WidthCm', 'HeightCm', 'DepthCm'])
        assert.equal(ui.rows()[0].querySelector('[data-parcel-invariant="' + field + '"]').value, 'ShippingParcels[0].' + field);
});

test('catalog reuse excludes dimension invariant markers while keeping fractional weight binding', () => {
    const ui = setup(1, true);
    assert.equal(ui.rows()[0].querySelector('[data-parcel-invariant="WeightKg"]').disabled, false);
    for (const field of ['WidthCm', 'HeightCm', 'DepthCm'])
        assert.equal(ui.rows()[0].querySelector('[data-parcel-invariant="' + field + '"]').disabled, true);
    ui.toggle(false);
    for (const field of ['WidthCm', 'HeightCm', 'DepthCm'])
        assert.equal(ui.rows()[0].querySelector('[data-parcel-invariant="' + field + '"]').disabled, false);
});
