const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/customer-assistance.js'), 'utf8');

function open({ remaining = '1800', bannerPresent = true, modalOpen = false } = {}) {
    let clock = 100000;
    const fields = [{ disabled: false }, { disabled: false }];
    const content = { hidden: false, querySelectorAll: () => fields };
    const countdown = { textContent: '' };
    const expired = { hidden: true };
    const elements = {
        'customer-assistance-banner': bannerPresent ? { dataset: { remainingSeconds: remaining } } : null,
        'assistance-countdown': countdown, 'assistance-content': content, 'assistance-expired': expired
    };
    const listeners = new Map();
    let interval = null;
    let cleared = 0;
    let reloads = 0;
    let modalHides = 0;
    vm.runInNewContext(source, {
        Date: { now: () => clock },
        document: { getElementById: id => elements[id], querySelectorAll: () => modalOpen ? [{}] : [] },
        window: {
            setInterval: callback => { interval = callback; return 77; },
            clearInterval: id => { assert.equal(id, 77); cleared++; },
            addEventListener: (name, callback) => listeners.set(name, callback),
            location: { reload: () => reloads++ },
            bootstrap: { Modal: { getInstance: () => ({ hide: () => modalHides++ }) } }
        }
    });
    return {
        fields, content, countdown, expired, listeners,
        advance: milliseconds => { clock += milliseconds; if (interval) interval(); },
        get cleared() { return cleared; }, get reloads() { return reloads; }, get modalHides() { return modalHides; }
    };
}

test('countdown begins at the server supplied remaining lifetime and does not slide', () => {
    const page = open();
    assert.equal(page.countdown.textContent, '30:00');
    page.advance(1000);
    assert.equal(page.countdown.textContent, '29:59');
    page.advance(59000);
    assert.equal(page.countdown.textContent, '29:00');
    assert.equal(page.content.hidden, false);
    assert.equal(page.expired.hidden, true);
});

test('at expiry all visible editor controls are disabled and customer data is concealed', () => {
    const page = open({ remaining: '1' });
    page.advance(1000);
    assert.equal(page.countdown.textContent, '0:00');
    assert.equal(page.content.hidden, true);
    assert.equal(page.expired.hidden, false);
    assert.ok(page.fields.every(field => field.disabled));
    assert.equal(page.cleared, 1);
});

test('a delayed background timer expires against elapsed time instead of its tick count', () => {
    const page = open({ remaining: '60' });
    page.advance(120000);
    assert.equal(page.countdown.textContent, '0:00');
    assert.equal(page.content.hidden, true);
});

for (const remaining of ['0', '-10', 'NaN', 'Infinity', 'not-a-number']) {
    test(`invalid or exhausted remaining lifetime ${remaining} fails closed`, () => {
        const page = open({ remaining });
        assert.equal(page.content.hidden, true);
        assert.equal(page.expired.hidden, false);
        assert.equal(page.countdown.textContent, '0:00');
    });
}

test('an open address modal is closed when the context expires', () => {
    const page = open({ remaining: '1', modalOpen: true });
    page.advance(1000);
    assert.equal(page.modalHides, 1);
});

test('returning from browser back-forward cache forces fresh server authorization', () => {
    const page = open();
    page.listeners.get('pageshow')({ persisted: false });
    assert.equal(page.reloads, 0);
    page.listeners.get('pageshow')({ persisted: true });
    assert.equal(page.reloads, 1);
});

test('unrelated normal customer pages receive no timer or lifecycle handler', () => {
    const page = open({ bannerPresent: false });
    assert.equal(page.listeners.size, 0);
    page.advance(1900000);
    assert.equal(page.content.hidden, false);
    assert.equal(page.expired.hidden, true);
});
