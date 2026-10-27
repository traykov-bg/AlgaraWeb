const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/admin-form-keyboard.js'), 'utf8');

function openAdmin(surface = '.admin-main') {
    let keydown;
    vm.runInNewContext(source, {
        document: {
            querySelector: selector => selector === surface ? ({
                addEventListener: (name, handler) => {
                    assert.equal(name, 'keydown');
                    keydown = handler;
                }
            }) : null
        }
    });
    return function press(target, extra = {}) {
        let prevented = false;
        if (keydown) keydown({ key: 'Enter', target, preventDefault: () => { prevented = true; }, ...extra });
        return prevented;
    };
}

const input = (type = 'text', method = 'post') => ({ tagName: 'INPUT', type, form: { method } });

test('Enter in the live variant search does not implicitly save the product', () => {
    const press = openAdmin();
    assert.equal(press({ ...input('search'), id: 'variantSearch' }), true);
});

test('Enter cannot implicitly save other editor values, including newly added variant rows', () => {
    const press = openAdmin();
    for (const type of ['text', 'email', 'url', 'tel', 'password', 'number', 'date', 'datetime-local', 'month', 'week', 'time']) {
        assert.equal(press(input(type)), true, type);
    }
});

test('catalog GET searches keep their normal Enter action', () => {
    const press = openAdmin();
    assert.equal(press(input('search', 'get')), false);
    assert.equal(press(input('text', 'get')), false);
});

test('multiline descriptions keep Enter for a new line', () => {
    assert.equal(openAdmin()({ tagName: 'TEXTAREA', form: { method: 'post' } }), false);
});

test('focused Save and Add buttons and native controls retain keyboard activation', () => {
    const press = openAdmin();
    assert.equal(press({ tagName: 'BUTTON', type: 'submit', form: { method: 'post' } }), false);
    assert.equal(press({ tagName: 'BUTTON', type: 'button', form: { method: 'post' } }), false);
    assert.equal(press({ tagName: 'SELECT', form: { method: 'post' } }), false);
    for (const type of ['submit', 'button', 'reset', 'file', 'color', 'checkbox', 'radio']) {
        assert.equal(press(input(type)), false, type);
    }
});

test('typing, navigation and composition are not consumed', () => {
    const press = openAdmin();
    for (const key of ['a', 'Tab', 'Escape', 'ArrowDown', ' ']) {
        assert.equal(press(input('search'), { key }), false, key);
    }
    assert.equal(press(input(), { isComposing: true }), false);
    assert.equal(press(input(), { keyCode: 229 }), false);
});

test('inputs outside a form and pages without admin content remain unaffected', () => {
    assert.equal(openAdmin()({ tagName: 'INPUT', type: 'text', form: null }), false);
    assert.doesNotThrow(() => vm.runInNewContext(source, { document: { querySelector: () => null } }));
});

test('customer assistance profile and address inputs require an explicit save', () => {
    const press = openAdmin('#assistance-content');
    for (const type of ['text', 'email', 'tel']) {
        assert.equal(press(input(type)), true, type);
    }
});

test('customer assistance preserves textarea newlines and focused save activation', () => {
    const press = openAdmin('#assistance-content');
    assert.equal(press({ tagName: 'TEXTAREA', form: { method: 'post' } }), false);
    assert.equal(press({ tagName: 'BUTTON', type: 'submit', form: { method: 'post' } }), false);
    assert.equal(press(input('text'), { isComposing: true }), false);
});

test('ordinary customer profile pages do not receive the staff keyboard guard', () => {
    assert.equal(openAdmin('.algara-profile')(input('text')), false);
});
