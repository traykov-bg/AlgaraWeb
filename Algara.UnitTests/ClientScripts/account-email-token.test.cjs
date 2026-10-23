const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/account-email-token.js'), 'utf8');
const token = '0123456789abcdef0123456789abcdef.' + 'A'.repeat(43);

function open({ hash = '', retained = '', hasForm = true } = {}) {
    const input = { value: retained };
    const button = { disabled: false };
    const message = { hidden: true };
    const replacements = [];
    let submissions = 0;
    const form = {
        querySelector: (selector) => { assert.equal(selector, 'input[name="Token"]'); return input; },
        querySelectorAll: (selector) => { assert.equal(selector, 'button[type="submit"]'); return [button]; },
        submit: () => submissions++
    };
    vm.runInNewContext(source, {
        document: { querySelector: (selector) => selector === '[data-account-email-token]' ? (hasForm ? form : null) : message },
        window: {
            location: { hash, pathname: '/Account/ConfirmEmail', search: '' },
            history: { replaceState: (...args) => replacements.push(args) }
        }
    });
    return { input, button, message, replacements, submissions };
}

test('a canonical fragment is moved into the POST form and removed from browser history without submitting', () => {
    const page = open({ hash: '#' + token });
    assert.equal(page.input.value, token);
    assert.equal(page.replacements.length, 1);
    assert.equal(page.replacements[0][2], '/Account/ConfirmEmail');
    assert.equal(page.submissions, 0);
    assert.equal(page.button.disabled, false);
    assert.equal(page.message.hidden, true);
});

test('an error page retains the posted token but performs no automatic confirmation', () => {
    const page = open({ retained: token });
    assert.equal(page.input.value, token);
    assert.equal(page.replacements.length, 0);
    assert.equal(page.submissions, 0);
    assert.equal(page.button.disabled, false);
});

for (const [name, value] of [
    ['missing token', ''], ['encoded fragment', '%30' + token.slice(1)], ['additional parameter', token + '&next=other'],
    ['noncanonical padding bits', token.slice(0, -1) + 'B'], ['padded base64', token + '='],
    ['invalid id', 'z' + token.slice(1)], ['unexpected whitespace', token + ' ']
]) test(name + ' is rejected without submitting', () => {
    const page = open({ hash: value ? '#' + value : '' });
    assert.equal(page.input.value, '');
    assert.equal(page.button.disabled, true);
    assert.equal(page.message.hidden, false);
    assert.equal(page.submissions, 0);
});

test('an invalid new fragment cannot reuse a retained previous token', () => {
    const page = open({ hash: '#invalid', retained: token });
    assert.equal(page.input.value, '');
    assert.equal(page.button.disabled, true);
});

test('the script is inert on unrelated pages', () => {
    const page = open({ hash: '#unrelated', hasForm: false });
    assert.equal(page.replacements.length, 0);
    assert.equal(page.submissions, 0);
});
