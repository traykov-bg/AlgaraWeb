const assert = require('node:assert/strict');
const test = require('node:test');
const { initializePicker } = require('../../Algara.Web/wwwroot/js/product-variants.js');

function node(attrs = {}) {
    const classes = new Set();
    const handlers = {};
    return {
        attrs, classes, handlers, hidden: false, value: '', textContent: '',
        getAttribute: name => attrs[name] ?? '',
        setAttribute: (name, value) => { attrs[name] = value; },
        removeAttribute: name => { delete attrs[name]; },
        classList: { toggle: (name, active) => active ? classes.add(name) : classes.delete(name) },
        addEventListener: (event, handler) => { (handlers[event] ??= []).push(handler); },
        fire(event, args = {}) {
            let prevented = false;
            (handlers[event] ?? []).forEach(handler => handler({ preventDefault() { prevented = true; }, ...args }));
            return prevented;
        },
        focus() { this.focused = true; }
    };
}

function fixture(options = {}) {
    const cards = [node(), node(), node()];
    const choices = [
        node({ 'data-variant-id': '1', 'data-fabric': '10', 'data-name': 'Linen / Beige', 'data-price': '900.00 €', 'data-original': '1000.00 €', 'data-status': 'In stock', 'data-image': '/exact.jpg', 'data-exact-image': 'true' }),
        node({ 'data-variant-id': '2', 'data-fabric': '10', 'data-name': 'Linen / Blue', 'data-price': '900.00 €', 'data-original': '1000.00 €', 'data-status': 'Made to order', 'data-image': '/base.jpg', 'data-exact-image': 'false' }),
        node({ 'data-variant-id': '3', 'data-fabric': '11', 'data-name': 'Velvet / Rose', 'data-price': '980.13 €', 'data-original': '1080.13 €', 'data-status': 'Availability not specified', 'data-image': '/base.jpg', 'data-exact-image': 'false' })
    ];
    for (let index = 3; index < (options.count || 3); index++) {
        cards.push(node());
        choices.push(node({ ...choices[0].attrs, 'data-variant-id': String(index + 1), 'data-name': 'Linen / Color ' + (index + 1) }));
    }
    choices.forEach((choice, index) => { choice.closest = () => cards[index]; });
    const fabric = options.noFabric ? null : node();
    const filters = node(); filters.hidden = true;
    const output = Object.fromEntries(['price', 'original', 'name', 'status', 'photo-note'].map(name => ['[data-variant-' + name + ']', node()]));
    const main = node();
    const thumbnails = ['/base.jpg', '/exact.jpg', '/side.jpg'].map(src => node({ 'data-gallery-src': src }));
    const pickerNodes = {
        '[data-variant-fabric]': fabric, '[data-variant-filters]': options.noFabric ? null : filters
    };
    const picker = node({ 'data-initial-variant': options.initial || '1', 'data-base-photo-note': 'Base photo', 'data-exact-photo-note': 'Exact photo', 'data-no-photo-note': 'No photo' });
    picker.querySelector = selector => pickerNodes[selector] || null;
    picker.querySelectorAll = selector => selector === '[data-variant-id]' ? choices : [];
    const detail = {
        querySelector: selector => selector === '[data-gallery-main]' ? main : output[selector],
        querySelectorAll: () => thumbnails
    };
    const languageReturn = node();
    let latestUrl = null;
    const browser = { location: { href: 'http://localhost/Product/Detail?n=7&keep=yes' }, history: { replaceState(_state, _unused, value) { latestUrl = value; } } };
    initializePicker(picker, detail, { querySelectorAll: () => [languageReturn] }, browser);
    return { choices, cards, fabric, filters, output, main, thumbnails, languageReturn, latestUrl: () => latestUrl };
}

test('a variant without its own image resets a previous exact photo to the base photo', () => {
    const ui = fixture();
    assert.equal(ui.main.src, '/exact.jpg');
    assert.equal(ui.output['[data-variant-photo-note]'].textContent, 'Exact photo');
    ui.choices[1].fire('click');
    assert.equal(ui.main.src, '/base.jpg');
    assert.equal(ui.output['[data-variant-photo-note]'].textContent, 'Base photo');
    assert.deepEqual(ui.choices.map(choice => choice.attrs['aria-pressed']), ['false', 'true', 'false']);
    assert.equal(ui.output['[data-variant-status]'].textContent, 'Made to order');
});

test('selection preserves the full server price and language URL without separate surcharge or code fields', () => {
    const ui = fixture();
    ui.fabric.value = '11';
    ui.fabric.fire('change');
    assert.equal(ui.output['[data-variant-price]'].textContent, '980.13 €');
    assert.equal(ui.output['[data-variant-original]'].textContent, '1080.13 €');
    assert.equal(ui.output['[data-variant-name]'].textContent, 'Velvet / Rose');
    assert.equal(ui.latestUrl(), '/Product/Detail?n=7&keep=yes&variant=3');
    assert.equal(ui.languageReturn.value, '/Product/Detail?n=7&keep=yes&variant=3');
});

test('changing fabric shows its colors and selects an actual color in that fabric', () => {
    const ui = fixture();
    ui.fabric.value = '11';
    ui.fabric.fire('change');
    assert.deepEqual(ui.cards.map(card => card.hidden), [true, true, false]);
    assert.equal(ui.choices[2].attrs['aria-pressed'], 'true');
});

test('more than twenty colors stay selectable without search controls', () => {
    const ui = fixture({ count: 50 });
    assert.equal(ui.cards.filter(card => !card.hidden).length, 49);
    ui.choices[0].fire('keydown', { key: 'End' });
    assert.equal(ui.choices[49].focused, true);
    assert.equal(ui.output['[data-variant-name]'].textContent, 'Linen / Color 50');
    assert.equal(ui.latestUrl(), '/Product/Detail?n=7&keep=yes&variant=50');
});

test('color-only products show every choice and update the price without filter controls', () => {
    const ui = fixture({ noFabric: true });
    assert.deepEqual(ui.cards.map(card => card.hidden), [false, false, false]);
    ui.choices[2].fire('click');
    assert.equal(ui.output['[data-variant-price]'].textContent, '980.13 €');
    assert.equal(ui.choices[2].attrs['aria-pressed'], 'true');
});

test('keyboard navigation stays within the selected fabric and supports Home, End and Space', () => {
    const ui = fixture();
    ui.choices[0].fire('keydown', { key: 'ArrowRight' });
    assert.equal(ui.choices[1].focused, true);
    assert.equal(ui.choices[1].attrs['aria-pressed'], 'true');
    ui.choices[1].fire('keydown', { key: 'Home' });
    assert.equal(ui.choices[0].attrs['aria-pressed'], 'true');
    ui.choices[0].fire('keydown', { key: 'End' });
    assert.equal(ui.choices[1].attrs['aria-pressed'], 'true');
    assert.equal(ui.choices[0].fire('keydown', { key: ' ' }), true);
    assert.equal(ui.choices[0].attrs['aria-pressed'], 'true');
});

test('viewing a general gallery image does not imply that it shows the selected variant', () => {
    const ui = fixture();
    ui.thumbnails[2].fire('click');
    assert.equal(ui.output['[data-variant-photo-note]'].textContent, 'Base photo');
    ui.thumbnails[1].fire('click');
    assert.equal(ui.output['[data-variant-photo-note]'].textContent, 'Exact photo');
});

test('deep-linked choice selects its fabric and modified clicks keep normal link behavior', () => {
    const ui = fixture({ initial: '3' });
    assert.equal(ui.fabric.value, '11');
    assert.equal(ui.choices[2].attrs['aria-pressed'], 'true');
    assert.equal(ui.choices[0].fire('click', { ctrlKey: true }), false);
    assert.equal(ui.choices[2].attrs['aria-pressed'], 'true');
});
