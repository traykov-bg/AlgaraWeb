const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/admin-product-variants.js'), 'utf8');

class Element {
    constructor(tag, attributes = {}) {
        this.tagName = tag;
        this.attributes = { ...attributes };
        this.children = [];
        this.handlers = {};
        this._value = '';
        this.hidden = false;
    }
    get parentNode() { return this.parent; }
    set value(value) { this._value = value == null ? '' : String(value); }
    get value() {
        if (this.tagName === 'select' && !this.children.some(option => option.value === this._value)) return '';
        return this._value;
    }
    matches(selector) {
        const match = /^([a-z]+)?(?:\[([^=\]]+)(?:="([^"]*)")?\])?$/.exec(selector);
        if (!match) throw new Error('Unsupported selector: ' + selector);
        return (!match[1] || this.tagName === match[1]) && (!match[2] ||
            (this.getAttribute(match[2]) !== undefined && (match[3] === undefined || this.getAttribute(match[2]) === match[3])));
    }
    getAttribute(name) { return this.attributes[name] ?? this[name]; }
    setAttribute(name, value) { this.attributes[name] = String(value); }
    querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
    querySelector(selector) { return this.querySelectorAll(selector)[0] || null; }
    closest(selector) { return this.matches(selector) ? this : this.parent?.closest(selector) || null; }
    appendChild(child) { child.remove(); child.parent = this; this.children.push(child); return child; }
    append(...children) { children.forEach(child => this.appendChild(child)); }
    replaceChildren(...children) { this.children.forEach(child => { child.parent = null; }); this.children = []; this.append(...children); }
    remove() { if (this.parent) this.parent.children.splice(this.parent.children.indexOf(this), 1); this.parent = null; }
    addEventListener(name, handler) { this.handlers[name] = handler; }
    trigger(name) { this.handlers[name]?.({ target: this, preventDefault() {} }); }
}

function color(n, fabricN = null, extra = {}) {
    return { n, name: 'Color ' + n, code: 'C-' + n, fabricN, fabricName: fabricN ? 'Fabric ' + fabricN : null, isActive: true, fabricActive: true, ...extra };
}
function variant(n, catalogColorN, extra = {}) {
    return { n, catalogColorN, sku: 'SKU-' + n, stockQuantity: null, surcharge: 0, isActive: true, isDefault: false, ...extra };
}
function initialize(state = {}, mode = '1') {
    const form = new Element('form');
    const editor = new Element('section', { 'data-product-variants-editor': '' });
    form.appendChild(editor);
    const nodes = {};
    const fields = {
        'variant-editor-data': 'script', 'option-kind': 'select', 'variant-rows': 'tbody', 'fabric-groups': 'div',
        'variant-choice': 'select', 'variant-search': 'input', 'variant-status': 'p', 'variant-search-label': 'label', 'variant-choice-label': 'label',
        'variants-empty': 'p', 'variant-table-wrap': 'div', 'simple-stock': 'div', 'option-controls': 'div', 'add-variant': 'button'
    };
    for (const [name, tag] of Object.entries(fields)) {
        nodes[name] = new Element(tag, { ['data-' + name]: '' }); editor.appendChild(nodes[name]);
    }
    for (const value of ['0', '1', '2']) { const option = new Element('option'); option.value = value; nodes['option-kind'].appendChild(option); }
    nodes['option-kind'].value = mode;
    nodes['variant-editor-data'].textContent = JSON.stringify({ colors: [], fabrics: [], variants: [], surcharges: [], ...state });
    const gallery = new Element('ol', { 'data-gallery-list': '' }); form.appendChild(gallery);
    function addImage(n, uploadId = '') {
        const card = new Element('li', { 'data-gallery-item': '' });
        const id = new Element('input', { 'data-image-field': 'N' }); id.value = n;
        const upload = new Element('input', { 'data-image-field': 'UploadId' }); upload.value = uploadId;
        card.append(id, upload); gallery.appendChild(card); return card;
    }
    (state.images || []).forEach(image => addImage(image.n, image.uploadId));
    let galleryChanged;
    vm.runInNewContext(source, {
        document: { querySelectorAll: () => [editor], createElement: tag => new Element(tag) },
        MutationObserver: class { constructor(callback) { galleryChanged = callback; } observe() {} }
    });
    return {
        nodes, gallery, addImage, galleryChanged: () => galleryChanged(),
        rows: () => editor.querySelectorAll('[data-variant-row]'),
        group(id) { return editor.querySelector('[data-fabric-group="' + id + '"]'); },
        removeFabric(id) { editor.querySelector('[data-remove-fabric="' + id + '"]').trigger('click'); },
        input(index, property) { return editor.querySelectorAll('[data-variant-row]')[index].querySelector('[name="Variants[' + index + '].' + property + '"]'); },
        fee(index = 0) { return editor.querySelector('[name="FabricSurcharges[' + index + '].Surcharge"]'); },
        add(id) { nodes['variant-choice'].value = id; nodes['add-variant'].trigger('click'); },
        mode(value) { nodes['option-kind'].value = value; nodes['option-kind'].trigger('change'); }
    };
}

function enter(input, value) { input.value = value; input.trigger('input'); }
function fabric(n, extra = {}) { return { n, name: 'Fabric ' + n, code: 'F-' + n, isActive: true, ...extra }; }

test('adding a fabric adds the entire group with one shared surcharge and all active colors', () => {
    const ui = initialize({
        colors: [color(1, 10), color(2, 10), color(3), color(4, 10, { isActive: false }), color(5, 11)],
        fabrics: [fabric(10, { imageUrl: '/fabric.jpg' }), fabric(11, { isActive: false })]
    }, '2');
    assert.deepEqual(ui.nodes['variant-choice'].children.map(x => x.value), ['', '10']);
    ui.add('10');
    assert.equal(ui.rows().length, 2);
    assert.equal(ui.fee().value, '0');
    assert.equal(ui.fee(1), null);
    assert.equal(ui.group(10).querySelector('img').src, '/fabric.jpg');
    assert.deepEqual(ui.rows().map((_, i) => ui.input(i, 'IsActive').checked), [true, true]);
    assert.deepEqual(ui.rows().map((_, i) => ui.input(i, 'StockQuantity').value), ['', '']);
    assert.equal(ui.input(0, 'Surcharge').type, 'hidden');
    assert.equal(ui.rows()[0].children.length, 6);
    assert.equal(ui.input(0, 'IsDefault').value, 'true');
    ui.add('10');
    assert.equal(ui.rows().length, 2);
});

test('existing fabric retains edits, includes associated inactive colors, and shows new colors unchecked', () => {
    const ui = initialize({
        colors: [color(1, 10), color(2, 10, { isActive: false }), color(3, 10)], fabrics: [fabric(10)],
        variants: [variant(101, 1, { isDefault: true }), variant(102, 2, { isActive: false })],
        surcharges: [{ fabricN: 10, surcharge: 0 }]
    }, '2');
    assert.equal(ui.rows().length, 3);
    assert.equal(ui.input(1, 'N').value, '102');
    assert.equal(ui.input(1, 'IsActive').disabled, true);
    assert.equal(ui.input(2, 'IsActive').checked, false);
    assert.equal(ui.input(2, 'StockQuantity').value, '');
    enter(ui.fee(), '90.15');
    enter(ui.input(0, 'Sku'), 'EDITED');
    ui.galleryChanged();
    assert.equal(ui.fee().value, '90.15');
    assert.equal(ui.input(0, 'Sku').value, 'EDITED');
    assert.equal(ui.input(0, 'Surcharge').value, '0');
});

test('removing a group omits all its rows and fee, reindexes the rest and chooses a free default', () => {
    const ui = initialize({
        colors: [color(1, 10), color(2, 20)], fabrics: [fabric(10), fabric(20)],
        variants: [variant(101, 1, { isDefault: true }), variant(102, 2, { stockQuantity: 5 })],
        surcharges: [{ fabricN: 10, surcharge: 0 }, { fabricN: 20, surcharge: 0 }]
    }, '2');
    ui.removeFabric(10);
    assert.equal(ui.group(10), null);
    assert.equal(ui.rows().length, 1);
    assert.equal(ui.input(0, 'N').value, '102');
    assert.equal(ui.input(0, 'StockQuantity').value, '5');
    assert.equal(ui.input(0, 'IsDefault').value, 'true');
    assert.equal(ui.fee(1), null);
    assert.ok(ui.nodes['variant-choice'].children.some(x => x.value === '10'));
});

test('removing and readding within the edit restores IDs, disabled choices, fees and exact photos', () => {
    const ui = initialize({
        colors: [color(1, 10), color(2, 10), color(3, 20)], fabrics: [fabric(10), fabric(20)],
        variants: [variant(101, 1), variant(102, 2, { isActive: false }), variant(103, 3, { isDefault: true })],
        surcharges: [{ fabricN: 10, surcharge: 20 }, { fabricN: 20, surcharge: 0 }], images: [{ n: 9 }]
    }, '2');
    enter(ui.input(0, 'Sku'), 'KEEP-THIS'); enter(ui.input(0, 'StockQuantity'), '7'); enter(ui.fee(), '25.35');
    const photo = ui.rows()[0].querySelector('select'); photo.value = 'saved:9'; photo.trigger('change');
    ui.removeFabric(10); ui.add('10');
    assert.equal(ui.rows().length, 3);
    assert.equal(ui.input(1, 'N').value, '101');
    assert.equal(ui.input(1, 'Sku').value, 'KEEP-THIS');
    assert.equal(ui.input(1, 'StockQuantity').value, '7');
    assert.equal(ui.input(1, 'ProductImageN').value, '9');
    assert.equal(ui.input(2, 'IsActive').checked, false);
    assert.equal(ui.fee(1).value, '25.35');
});

test('a previously saved removed fabric restores stored identities and enables its available colors', () => {
    const ui = initialize({
        colors: [color(1, 10), color(2, 10, { isActive: false }), color(3, 10)], fabrics: [fabric(10)],
        variants: [variant(101, 1, { isActive: false, stockQuantity: 9 }), variant(102, 2, { isActive: false })]
    }, '2');
    assert.equal(ui.rows().length, 0);
    ui.add('10');
    assert.equal(ui.rows().length, 3);
    assert.equal(ui.input(0, 'N').value, '101');
    assert.equal(ui.input(0, 'StockQuantity').value, '9');
    assert.equal(ui.input(0, 'IsActive').checked, true);
    assert.equal(ui.input(1, 'IsActive').checked, false);
    assert.equal(ui.input(2, 'N').value, '0');
    assert.equal(ui.input(2, 'IsActive').checked, true);
});

test('removing the last fabric switches to no variants, and allows adding it back without losing values', () => {
    const ui = initialize({ colors: [color(1, 10)], fabrics: [fabric(10)], variants: [variant(101, 1, { isDefault: true, stockQuantity: 3 })], surcharges: [{ fabricN: 10, surcharge: 0 }] }, '2');
    ui.removeFabric(10);
    assert.equal(ui.nodes['option-kind'].value, '0');
    assert.equal(ui.rows().length, 0);
    assert.equal(ui.fee(), null);
    assert.equal(ui.nodes['simple-stock'].hidden, false);
    ui.mode('2'); ui.add('10');
    assert.equal(ui.input(0, 'N').value, '101');
    assert.equal(ui.input(0, 'StockQuantity').value, '3');
    assert.equal(ui.input(0, 'IsDefault').value, 'true');
});

test('removing the free fabric explains when remaining charged fabric cannot be the default', () => {
    const ui = initialize({ colors: [color(1, 10), color(2, 20)], fabrics: [fabric(10), fabric(20)], variants: [variant(101, 1, { isDefault: true }), variant(102, 2)], surcharges: [{ fabricN: 10, surcharge: 0 }, { fabricN: 20, surcharge: 40 }] }, '2');
    ui.removeFabric(10);
    assert.equal(ui.input(0, 'IsDefault').value, 'false');
    assert.match(ui.nodes['variant-status'].textContent, /0 €/);
    enter(ui.fee(), '0'); ui.fee().trigger('change');
    assert.equal(ui.input(0, 'IsDefault').value, 'true');
});

test('switching default preserves typed SKU, exact surcharge and stock without duplicate defaults', () => {
    const ui = initialize({ colors: [color(1), color(2)], variants: [variant(10, 1, { isDefault: true }), variant(11, 2)] });
    enter(ui.input(0, 'Sku'), 'MY-EDITED-CODE'); enter(ui.input(0, 'StockQuantity'), '7'); enter(ui.input(0, 'Surcharge'), '12.34');
    ui.rows()[1].querySelector('[name="variantDefaultChoice"]').trigger('change');
    assert.equal(ui.input(0, 'Sku').value, 'MY-EDITED-CODE'); assert.equal(ui.input(0, 'StockQuantity').value, '7'); assert.equal(ui.input(0, 'Surcharge').value, '12.34');
    assert.deepEqual([ui.input(0, 'IsDefault').value, ui.input(1, 'IsDefault').value], ['false', 'true']);
});

test('gallery removal clears only deleted photo and preserves input in both groups', () => {
    const ui = initialize({ colors: [color(1, 10), color(2, 20)], fabrics: [fabric(10), fabric(20)], variants: [variant(10, 1, { productImageN: 20 }), variant(11, 2, { productImageN: 21 })], surcharges: [{ fabricN: 10, surcharge: 0 }, { fabricN: 20, surcharge: 3 }], images: [{ n: 20 }, { n: 21 }] }, '2');
    enter(ui.input(0, 'Sku'), 'PRESERVED'); enter(ui.input(0, 'StockQuantity'), '4'); enter(ui.fee(1), '0.59');
    ui.gallery.children[0].remove(); ui.galleryChanged();
    assert.equal(ui.input(0, 'ProductImageN').value, ''); assert.equal(ui.input(1, 'ProductImageN').value, '21');
    assert.equal(ui.input(0, 'Sku').value, 'PRESERVED'); assert.equal(ui.input(0, 'StockQuantity').value, '4'); assert.equal(ui.fee(1).value, '0.59');
});

test('private draft photos bind upload identifiers and clear after deletion', () => {
    const ui = initialize({ colors: [color(1)], variants: [variant(0, 1)], images: [{ n: 0, uploadId: 'draft-abc' }] });
    const select = ui.rows()[0].querySelector('select'); select.value = 'draft:draft-abc'; select.trigger('change');
    assert.equal(ui.input(0, 'ProductImageN').value, ''); assert.equal(ui.input(0, 'ProductImageUploadId').value, 'draft-abc');
    ui.gallery.replaceChildren(); ui.galleryChanged(); assert.equal(ui.input(0, 'ProductImageUploadId').value, '');
});

test('incompatible modes omit old rows while retaining editable data in memory', () => {
    const ui = initialize({ colors: [color(1), color(2, 10)], fabrics: [fabric(10)], variants: [variant(7, 1, { isDefault: true, stockQuantity: 3 })] });
    enter(ui.input(0, 'Sku'), 'KEEP-CODE'); ui.mode('2');
    assert.equal(ui.rows().length, 0); ui.mode('1');
    assert.equal(ui.input(0, 'IsActive').checked, false); assert.equal(ui.input(0, 'IsDefault').value, 'false');
    assert.equal(ui.input(0, 'StockQuantity').value, '3'); assert.equal(ui.input(0, 'Sku').value, 'KEEP-CODE'); assert.equal(ui.input(0, 'N').value, '7');
    ui.mode('0'); assert.equal(ui.nodes['simple-stock'].hidden, false); assert.equal(ui.nodes['option-controls'].hidden, true); assert.equal(ui.rows().length, 0);
});

test('search reaches choices beyond twenty and does not fabricate stock or duplicate rows', () => {
    const ui = initialize({ colors: Array.from({ length: 35 }, (_, i) => color(i + 1)) });
    assert.equal(ui.nodes['variant-choice'].children.length, 36); enter(ui.nodes['variant-search'], 'C-35'); assert.equal(ui.nodes['variant-choice'].children.length, 2);
    ui.add('35'); assert.equal(ui.rows().length, 1); assert.equal(ui.input(0, 'CatalogColorN').value, '35'); assert.equal(ui.input(0, 'StockQuantity').value, ''); assert.equal(ui.input(0, 'Sku').value, '');
    ui.add('35'); assert.equal(ui.rows().length, 1);
});

test('deactivating the default selects another free active choice and preserves stock', () => {
    const ui = initialize({ colors: [color(1), color(2)], variants: [variant(7, 1, { isDefault: true, stockQuantity: 6 }), variant(8, 2)] });
    ui.input(0, 'IsActive').checked = false; ui.input(0, 'IsActive').trigger('change');
    assert.equal(ui.input(0, 'IsDefault').value, 'false'); assert.equal(ui.input(1, 'IsDefault').value, 'true'); assert.equal(ui.input(0, 'StockQuantity').value, '6');
});

test('removing an unsaved standalone color reindexes fields and preserves the remaining edits', () => {
    const ui = initialize({ colors: [color(1), color(2), color(3)], variants: [variant(90, 1, { isDefault: true })] });
    ui.add('2'); ui.add('3'); enter(ui.input(2, 'Sku'), 'KEEP-THIRD'); enter(ui.input(2, 'StockQuantity'), '12');
    assert.equal(ui.rows()[0].querySelector('[data-remove-new-variant]'), null); ui.rows()[1].querySelector('[data-remove-new-variant]').trigger('click');
    assert.equal(ui.rows().length, 2); assert.equal(ui.input(0, 'N').value, '90'); assert.equal(ui.input(1, 'CatalogColorN').value, '3'); assert.equal(ui.input(1, 'Sku').value, 'KEEP-THIRD'); assert.equal(ui.input(1, 'StockQuantity').value, '12');
    ui.add('2'); assert.equal(ui.rows().length, 3);
});

test('fabric selector search selects groups rather than their individual colors', () => {
    const ui = initialize({ fabrics: [fabric(10), fabric(20)], colors: [color(1, 10), color(2, 10), color(3, 20)] }, '2');
    enter(ui.nodes['variant-search'], 'Fabric 20');
    assert.deepEqual(ui.nodes['variant-choice'].children.map(x => x.value), ['', '20']);
    ui.add('20'); assert.equal(ui.rows().length, 1); assert.equal(ui.input(0, 'CatalogColorN').value, '3');
});

test('changing a fee repairs defaults without replacing the remove button being clicked', () => {
    const ui = initialize({ colors: [color(1, 10), color(2, 20)], fabrics: [fabric(10), fabric(20)], variants: [variant(101, 1, { isDefault: true }), variant(102, 2)], surcharges: [{ fabricN: 10, surcharge: 0 }, { fabricN: 20, surcharge: 0 }] }, '2');
    const group = ui.group(10);
    const remove = group.querySelector('[data-remove-fabric]');
    enter(ui.fee(), '5.20'); ui.fee().trigger('change');
    assert.equal(ui.group(10), group);
    assert.equal(ui.group(10).querySelector('[data-remove-fabric]'), remove);
    assert.equal(ui.input(0, 'IsDefault').value, 'false'); assert.equal(ui.input(1, 'IsDefault').value, 'true');
});

test('adding an oversized fabric is rejected atomically without partial variant rows', () => {
    const ui = initialize({ fabrics: [fabric(10)], colors: Array.from({ length: 501 }, (_, i) => color(i + 1, 10)) }, '2');
    ui.add('10');
    assert.equal(ui.rows().length, 0);
    assert.equal(ui.group(10), null);
    assert.match(ui.nodes['variant-status'].textContent, /500/);
});
