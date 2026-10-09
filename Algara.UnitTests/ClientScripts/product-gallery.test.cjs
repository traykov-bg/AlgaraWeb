const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/product-gallery.js'), 'utf8');

function createGallery(urls, main = { src: urls[0] }) {
    const buttons = urls.map((url, index) => {
        const attrs = { 'data-gallery-src': url, 'aria-pressed': index === 0 ? 'true' : 'false' };
        const classes = new Set(index === 0 ? ['is-selected'] : []);
        return {
            attrs, classes,
            getAttribute: name => attrs[name],
            setAttribute: (name, value) => { attrs[name] = value; },
            classList: { toggle: (name, state) => state ? classes.add(name) : classes.delete(name) },
            addEventListener(name, handler) { this[name] = handler; }
        };
    });
    return { main, buttons, querySelector: () => main, querySelectorAll: () => buttons };
}

function initialize(galleries) {
    vm.runInNewContext(source, { document: { querySelectorAll: () => galleries } });
}

test('selecting a thumbnail switches the main image and the single accessible selection', () => {
    const gallery = createGallery(['/one.jpg', '/two.jpg', '/three.jpg']);
    initialize([gallery]);

    gallery.buttons[2].click();
    assert.equal(gallery.main.src, '/three.jpg');
    assert.deepEqual(gallery.buttons.map(button => button.attrs['aria-pressed']), ['false', 'false', 'true']);
    assert.deepEqual(gallery.buttons.map(button => button.classes.has('is-selected')), [false, false, true]);

    gallery.buttons[0].click();
    assert.equal(gallery.main.src, '/one.jpg');
    assert.deepEqual(gallery.buttons.map(button => button.attrs['aria-pressed']), ['true', 'false', 'false']);
});

test('independent galleries preserve each others image and missing-image products are safe', () => {
    const first = createGallery(['/first.jpg', '/another.jpg']);
    const second = createGallery(['/second.jpg', '/last.jpg']);
    const empty = createGallery([], null);
    initialize([first, second, empty]);

    second.buttons[1].click();
    assert.equal(second.main.src, '/last.jpg');
    assert.equal(first.main.src, '/first.jpg');
    assert.equal(first.buttons[0].attrs['aria-pressed'], 'true');
});
