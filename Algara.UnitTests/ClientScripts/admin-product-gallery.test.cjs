const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/admin-product-gallery.js'), 'utf8');

class Element {
    constructor(tag, attributes = {}) {
        this.tagName = tag;
        this.attributes = { ...attributes };
        this.children = [];
        this.handlers = {};
        this.disabled = false;
        this.value = '';
        this.classes = new Set();
        this.classList = {
            add: (...names) => names.forEach(name => this.classes.add(name)),
            remove: (...names) => names.forEach(name => this.classes.delete(name)),
            toggle: (name, state) => state ? this.classes.add(name) : this.classes.delete(name),
            contains: name => this.classes.has(name)
        };
    }
    get className() { return [...this.classes].join(' '); }
    set className(value) { this.classes = new Set(value.split(' ')); }
    get dataset() {
        return Object.fromEntries(Object.entries(this.attributes).filter(([name]) => name.startsWith('data-'))
            .map(([name, value]) => [name.slice(5).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase()), value]));
    }
    matches(selector) {
        const match = /^([a-z]+)?(?:\[([^=\]]+)(?:="([^"]*)")?\])?$/.exec(selector);
        if (!match) throw new Error('Unsupported test selector: ' + selector);
        return (!match[1] || this.tagName === match[1]) && (!match[2] ||
            (this.hasAttribute(match[2]) && (match[3] === undefined || this.getAttribute(match[2]) === match[3])));
    }
    hasAttribute(name) { return Object.hasOwn(this.attributes, name) || ['name', 'type'].includes(name) && this[name] !== undefined; }
    getAttribute(name) { return this.attributes[name] ?? this[name]; }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    closest(selector) { return this.matches(selector) ? this : this.parent?.closest(selector) || null; }
    contains(element) { return element === this || this.children.some(child => child.contains(element)); }
    appendChild(child) { child.remove(); child.parent = this; this.children.push(child); return child; }
    append(...children) { children.forEach(child => this.appendChild(child)); }
    insertBefore(child, reference) {
        child.remove();
        child.parent = this;
        this.children.splice(reference ? this.children.indexOf(reference) : this.children.length, 0, child);
    }
    remove() {
        if (this.parent) this.parent.children.splice(this.parent.children.indexOf(this), 1);
        this.parent = null;
    }
    get nextSibling() { return this.parent?.children[this.parent.children.indexOf(this) + 1] || null; }
    addEventListener(name, handler) { this.handlers[name] = handler; }
    async trigger(name, event = {}) {
        const value = { target: this, preventDefault() { this.prevented = true; }, ...event };
        await this.handlers[name]?.(value);
        return value;
    }
    focus() { this.focused = true; }
}

function imageCard(n, uploadId = '') {
    const card = new Element('li', { 'data-gallery-item': '' });
    for (const [key, value] of Object.entries({ N: n, UploadId: uploadId, DisplayOrder: 0 })) {
        const input = new Element('input', { 'data-image-field': key });
        input.value = String(value);
        card.appendChild(input);
    }
    const previous = new Element('button', { 'data-image-move': '-1' });
    const next = new Element('button', { 'data-image-move': '1' });
    card.append(new Element('img'), new Element('span', { 'data-image-position': '' }),
        new Element('button', { 'data-image-delete': '' }), previous, next);
    return card;
}
function initialize(ids = [], options = {}) {
    const form = new Element('form');
    const token = new Element('input', { name: '__RequestVerificationToken' });
    token.value = 'csrf-token';
    const submit = new Element('button', { type: 'submit' });
    const gallery = new Element('section', {
        'data-product-gallery': '', 'data-product-n': options.productN ?? '12',
        'data-draft-id': 'owner-draft', 'data-upload-url': '/upload', 'data-delete-url': '/delete'
    });
    form.append(token, gallery, submit);
    const elements = {};
    for (const [name, tag] of Object.entries({ 'gallery-list': 'ol', 'product-image-input': 'input',
        'gallery-status': 'p', 'gallery-error': 'div', 'gallery-empty': 'p', 'gallery-count': 'span' })) {
        const node = new Element(tag, { ['data-' + name]: '' });
        elements[name] = node;
        gallery.appendChild(node);
    }
    const list = elements['gallery-list'];
    ids.forEach(id => list.appendChild(imageCard(id)));
    const requests = [];
    const pending = [];
    class FormData {
        constructor() { this.values = []; }
        append(key, value) { this.values.push([key, value]); }
        get(key) { return this.values.find(([name]) => name === key)?.[1]; }
    }
    vm.runInNewContext(source, {
        document: { querySelectorAll: () => [gallery], createElement: tag => new Element(tag) },
        window: { addEventListener() {} }, FormData, TypeError,
        fetch(url, settings) {
            requests.push({ url, ...settings });
            return new Promise((resolve, reject) => pending.push({ resolve, reject }));
        }
    });
    return {
        gallery, form, list, submit, requests, pending,
        files: elements['product-image-input'], error: elements['gallery-error'], status: elements['gallery-status'],
        cards: () => list.children,
        ids: () => list.children.map(card => card.querySelector('[data-image-field="N"]').value),
        async upload(files = [{ name: 'image.png', size: 123 }]) {
            elements['product-image-input'].files = files;
            return elements['product-image-input'].trigger('change');
        },
        resolve(body, status = 200, contentType = 'application/json', redirected = false) {
            pending.shift().resolve({ status, ok: status >= 200 && status < 300, redirected,
                headers: { get: () => contentType }, json: async () => body });
        }
    };
}

function assertOrder(state, expected) {
    assert.deepEqual(state.ids().map(String), expected.map(String));
    state.cards().forEach((card, index) => {
        assert.equal(card.classList.contains('is-main'), index === 0);
        assert.equal(card.querySelector('[data-image-field="DisplayOrder"]').value, index);
        assert.equal(card.querySelector('[data-image-field="N"]').name, `Images[${index}].N`);
    });
}

test('selection uploads immediately with CSRF and draft ownership, locks submit and makes the first image main', async () => {
    const state = initialize([], { productN: '0' });
    const upload = state.upload();
    assert.equal(state.requests.length, 1);
    assert.equal(state.requests[0].method, 'POST');
    assert.equal(state.requests[0].body.get('__RequestVerificationToken'), 'csrf-token');
    assert.equal(state.requests[0].body.get('draftId'), 'owner-draft');
    assert.equal(state.requests[0].body.get('productN'), '0');
    assert.equal(state.submit.disabled, true);
    assert.equal((await state.form.trigger('submit')).prevented, true);
    state.resolve({ images: [{ n: 0, uploadId: 'upload-1', imageUrl: '/first.png' }] });
    await upload;
    assertOrder(state, [0]);
    assert.equal(state.cards()[0].querySelector('[data-image-field="UploadId"]').value, 'upload-1');
    assert.equal(state.cards()[0].querySelector('img').src, '/first.png');
    assert.equal(state.submit.disabled, false);
    assert.equal(state.files.disabled, false);
});

test('unsaved reordering stays local and remains intact when another upload succeeds', async () => {
    const state = initialize([1, 2, 3]);
    await state.list.trigger('click', { target: state.cards()[1].querySelector('[data-image-move="-1"]') });
    assertOrder(state, [2, 1, 3]);
    assert.equal(state.requests.length, 0);
    const upload = state.upload();
    state.resolve({ images: [{ n: 4, imageUrl: '/four.png' }, { n: 5, imageUrl: '/five.png' }] });
    await upload;
    assertOrder(state, [2, 1, 3, 4, 5]);
    assert.equal(state.cards()[0].querySelector('[data-image-move="-1"]').disabled, true);
    assert.equal(state.cards()[4].querySelector('[data-image-move="1"]').disabled, true);
});

test('delete removes only after server success and promotes the remaining first image', async () => {
    const state = initialize([1, 2, 3]);
    const deletion = state.list.trigger('click', { target: state.cards()[0].querySelector('[data-image-delete]') });
    assertOrder(state, [1, 2, 3]);
    assert.equal(state.requests[0].url, '/delete');
    assert.equal(state.requests[0].body.get('imageN'), '1');
    assert.equal(state.submit.disabled, true);
    state.resolve({});
    await deletion;
    assertOrder(state, [2, 3]);
    assert.equal(state.submit.disabled, false);
    assert.equal(state.cards()[0].focused, true);
});

test('failed deletion preserves images and local order, reports the server message and unlocks the form', async () => {
    const state = initialize([1, 2]);
    await state.list.trigger('keydown', { target: state.cards()[1], key: 'ArrowLeft' });
    const deletion = state.list.trigger('click', { target: state.cards()[0].querySelector('[data-image-delete]') });
    state.resolve({ error: 'Deletion failed safely.' }, 409);
    await deletion;
    assertOrder(state, [2, 1]);
    assert.equal(state.error.textContent, 'Deletion failed safely.');
    assert.equal(state.error.hidden, false);
    assert.equal(state.submit.disabled, false);
});

test('dragging a later image to the first position changes the main image and hidden order without a request', async () => {
    const state = initialize([1, 2, 3]);
    const transfer = { setData() {} };
    await state.list.trigger('dragstart', { target: state.cards()[2], dataTransfer: transfer });
    await state.list.trigger('dragover', { target: state.cards()[0], dataTransfer: transfer });
    await state.list.trigger('drop', { target: state.cards()[0], dataTransfer: transfer });
    assertOrder(state, [3, 1, 2]);
    assert.equal(state.requests.length, 0);
    assert.equal(state.cards()[0].classList.contains('is-dragging'), false);
    await state.list.trigger('keydown', { target: state.cards()[0], key: 'ArrowRight' });
    assertOrder(state, [1, 3, 2]);
});

test('HTML/login redirects and network failures never remove existing images or leave the form locked', async () => {
    for (const failure of ['redirect', 'html', 'network']) {
        const state = initialize([7]);
        const upload = state.upload();
        if (failure === 'network') state.pending.shift().reject(new TypeError('Failed to fetch'));
        else state.resolve(null, 200, 'text/html', failure === 'redirect');
        await upload;
        assertOrder(state, [7]);
        assert.equal(state.error.hidden, false);
        assert.ok(state.error.textContent.length > 10);
        assert.equal(state.submit.disabled, false);
        assert.equal(state.files.disabled, false);
    }
});

test('over-limit selections are rejected before upload and the 20-image gallery disables further selection', async () => {
    const state = initialize(Array.from({ length: 19 }, (_, index) => index + 1));
    await state.upload([{ size: 1 }, { size: 1 }]);
    assert.equal(state.requests.length, 0);
    assert.equal(state.error.hidden, false);
    await state.upload([{ size: 5 * 1024 * 1024 + 1 }]);
    assert.equal(state.requests.length, 0);
    const upload = state.upload();
    state.resolve({ images: [{ n: 20, imageUrl: '/last.png' }] });
    await upload;
    assert.equal(state.cards().length, 20);
    assert.equal(state.files.disabled, true);
    const deletion = state.list.trigger('click', { target: state.cards()[19].querySelector('[data-image-delete]') });
    state.resolve({});
    await deletion;
    assert.equal(state.files.disabled, false);
});
