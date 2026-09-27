const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/admin-promotions.js'), 'utf8');

function element(value = '') {
    const listeners = {};
    const classes = new Set();
    return {
        value: String(value), checked: false, readOnly: false, textContent: '', valid: true,
        classList: {
            add: name => classes.add(name), remove: name => classes.delete(name),
            contains: name => classes.has(name),
            toggle: (name, enabled) => enabled ? classes.add(name) : classes.delete(name)
        },
        addEventListener(name, callback) { listeners[name] = callback; },
        trigger(name) { listeners[name]?.call(this); },
        removeAttribute(name) { delete this[name]; },
        reportValidity() { return this.valid; }
    };
}

function form(mode = 'percent', data = [{}]) {
    const ids = Object.fromEntries([
        'typeAmount', 'typePercent', 'bulkUnit', 'bulkValue', 'bulkApply', 'bulkReset',
        'bulkRefresh', 'selectedCount', 'productSearch', 'checkAll'
    ].map(id => [id, element()]));
    ids.typeAmount.checked = mode === 'amount';
    ids.typePercent.checked = mode === 'percent';
    const rows = data.map((item, index) => {
        const original = item.original ?? 99.49;
        const promo = item.promo ?? original;
        const fields = {
            original: element(original.toFixed(2)), promo: element(promo.toFixed(2)),
            percent: element(item.percent ?? 0), amount: element((original - promo).toFixed(2)),
            include: element(), 'refresh-original': element('false')
        };
        fields.include.checked = item.included ?? false;
        const row = element();
        row.fields = fields;
        row.querySelector = selector => fields[selector.replace('.row-', '')];
        row.getAttribute = name => name === 'data-current-price'
            ? String(item.current ?? original) : item.name ?? `product ${index}`;
        return row;
    });
    let ready;
    const window = {};
    vm.runInNewContext(source, {
        window,
        document: {
            getElementById: id => ids[id],
            addEventListener(name, callback) { if (name === 'DOMContentLoaded') ready = callback; },
            querySelectorAll(selector) {
                if (selector === '.promo-row' || selector === '#promoTable tbody tr') return rows;
                if (selector === '.row-include:checked') return rows.map(r => r.fields.include).filter(cb => cb.checked);
                if (selector === '.promo-row:not(.d-none) .row-include') {
                    return rows.filter(r => !r.classList.contains('d-none')).map(r => r.fields.include);
                }
                throw new Error(`Unexpected selector: ${selector}`);
            }
        }
    });
    ready();
    return {
        ids, rows,
        switchMode(newMode) {
            ids.typeAmount.checked = newMode === 'amount';
            ids.typePercent.checked = newMode === 'percent';
            ids[newMode === 'amount' ? 'typeAmount' : 'typePercent'].trigger('change');
        },
        input(rowIndex, field, value) {
            const input = rows[rowIndex].fields[field];
            input.value = String(value);
            input.trigger('input');
        }
    };
}

test('new rows keep cents and have no discount before selection in either mode', () => {
    for (const mode of ['percent', 'amount']) {
        const ui = form(mode);
        const fields = ui.rows[0].fields;
        assert.equal(fields.promo.value, '99.49');
        assert.equal(fields.amount.value, '0.00');
        fields.include.checked = true;
        fields.include.trigger('change');
        assert.equal(fields.promo.value, '99.49');
        assert.equal(ui.ids.selectedCount.textContent, '1 избрани');
    }
});

test('percent mode calculates fractional discounts and rounds half-cent prices away from zero', () => {
    const ui = form('percent', [{ original: 999.95 }]);
    ui.input(0, 'percent', '10');
    assert.equal(ui.rows[0].fields.promo.value, '899.96');
    assert.equal(ui.rows[0].fields.amount.value, '99.99');
    ui.input(0, 'percent', '90.055');
    assert.equal(ui.rows[0].fields.promo.value, '99.45');
    assert.equal(ui.rows[0].fields.amount.value, '900.50');
});

test('amount mode accepts cents in both discount and final price', () => {
    const ui = form('amount');
    ui.input(0, 'amount', '12.34');
    assert.equal(ui.rows[0].fields.promo.value, '87.15');
    assert.equal(ui.rows[0].fields.percent.value, '12.403');
    ui.input(0, 'promo', '90.01');
    assert.equal(ui.rows[0].fields.amount.value, '9.48');
    assert.equal(ui.rows[0].fields.percent.value, '9.529');
});

test('switching modes preserves final prices and configures appropriate decimal precision', () => {
    const ui = form('percent', [{ original: 99.49, promo: 87.15, percent: 12.403 }]);
    const fields = ui.rows[0].fields;
    assert.equal(fields.percent.readOnly, false);
    assert.equal(fields.promo.readOnly, true);
    assert.equal(fields.amount.readOnly, true);
    assert.equal(ui.ids.bulkValue.step, '0.001');
    assert.equal(ui.ids.bulkValue.max, '99.999');
    ui.switchMode('amount');
    assert.equal(fields.percent.readOnly, true);
    assert.equal(fields.promo.readOnly, false);
    assert.equal(fields.amount.readOnly, false);
    assert.equal(ui.ids.bulkValue.step, '0.01');
    assert.equal(ui.ids.bulkValue.max, undefined);
    assert.equal(fields.promo.value, '87.15');
});

test('selecting and reselecting a row does not erase its entered amount discount', () => {
    const ui = form('amount');
    const fields = ui.rows[0].fields;
    ui.input(0, 'amount', '12.34');
    for (const checked of [true, false, true]) {
        fields.include.checked = checked;
        fields.include.trigger('change');
        assert.equal(fields.amount.value, '12.34');
        assert.equal(fields.promo.value, '87.15');
    }
});

test('refresh requests a trusted price-list snapshot and preserves the active discount mode', () => {
    for (const mode of ['percent', 'amount']) {
        const ui = form(mode, [{ original: 100, current: 110.99, promo: 85, percent: 15, included: true }]);
        const fields = ui.rows[0].fields;
        assert.equal(fields.original.value, '100.00');
        assert.equal(fields['refresh-original'].value, 'false');
        ui.ids.bulkRefresh.trigger('click');
        assert.equal(fields.original.value, '110.99');
        assert.equal(fields['refresh-original'].value, 'true');
        assert.equal(fields.promo.value, mode === 'percent' ? '94.34' : '95.99');
    }
});

test('bulk amount applies cents only to selected visible rows and refuses invalid input', () => {
    const ui = form('amount', [
        { name: 'sofa', included: true }, { name: 'chair', included: true }, { name: 'sofa other' }
    ]);
    ui.ids.productSearch.value = 'sofa';
    ui.ids.productSearch.trigger('input');
    ui.ids.bulkValue.value = '12.34';
    ui.ids.bulkValue.valid = false;
    ui.ids.bulkApply.trigger('click');
    assert.equal(ui.rows[0].fields.promo.value, '99.49');
    ui.ids.bulkValue.valid = true;
    ui.ids.bulkApply.trigger('click');
    assert.equal(ui.rows[0].fields.promo.value, '87.15');
    assert.equal(ui.rows[1].fields.promo.value, '99.49');
    assert.equal(ui.rows[2].fields.promo.value, '99.49');
    ui.ids.bulkReset.trigger('click');
    assert.equal(ui.rows[0].fields.promo.value, '99.49');
    assert.equal(ui.rows[0].fields.amount.value, '0.00');
});

test('select all retains edited prices and only selects visible products', () => {
    const ui = form('amount', [{ name: 'sofa' }, { name: 'chair' }]);
    ui.input(0, 'promo', '87.15');
    ui.ids.productSearch.value = 'sofa';
    ui.ids.productSearch.trigger('input');
    ui.ids.checkAll.checked = true;
    ui.ids.checkAll.trigger('change');
    assert.equal(ui.rows[0].fields.include.checked, true);
    assert.equal(ui.rows[1].fields.include.checked, false);
    assert.equal(ui.rows[0].fields.promo.value, '87.15');
});
