const assert = require('node:assert/strict');
const test = require('node:test');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/delivery-quote.js'), 'utf8');

function fixture(initial = '0', withResult = true, directory = null, officeResponse = null) {
    let form;
    function node(value = '') {
        const handlers = {};
        return {
            value, hidden: false, handlers,
            addEventListener(event, handler) { (handlers[event] ??= []).push(handler); },
            attributes: {}, children: [],
            setAttribute(key, value) { this.attributes[key] = value; },
            removeAttribute(key) { delete this.attributes[key]; },
            replaceChildren(...items) { this.children = items; },
            scrollIntoView() {},
            focus() { this.fire('focus'); },
            contains(target) { return target === this || this.children.includes(target); },
            fire(event, details = {}) {
                const e = { target: this, preventDefault() { this.defaultPrevented = true; }, ...details };
                (handlers[event] ?? []).forEach(handler => handler(e));
                if (this !== form) (form.handlers[event] ?? []).forEach(handler => handler(e));
                return e;
            }
        };
    }
    form = node();
    const method = node(initial);
    const city = node('Plovdiv');
    const office = node('7');
    office.options = ['', '7', '8'];
    office.remove = index => office.options.splice(index, 1);
    office.appendChild = option => office.options.push(option);
    office.setCustomValidity = text => { office.error = text; };
    const result = withResult ? node() : null;
    const sections = Object.fromEntries(['destination', 'address', 'office', 'own', 'courier', 'pickup'].map(name => [name, node()]));
    form.querySelectorAll = selector => [sections[selector.slice('[data-delivery-'.length, -1)]];
    const nodes = { 'delivery-quote-form': form, 'delivery-method': method, 'delivery-city': city, 'delivery-office': office, 'delivery-quote-result': result };
    if (directory) {
        nodes['delivery-cities'] = Object.assign(node(), { id: 'delivery-cities' });
        for (const id of ['picker', 'toggle', 'popup', 'empty']) nodes['delivery-city-' + id] = node();
        nodes['delivery-city-status'] = { dataset: { select: 'Choose city', loading: 'Loading', error: 'Failed' } };
        city.setCustomValidity = text => { city.error = text; };

        nodes['delivery-office-status'] = { dataset: { loading: 'Loading offices', error: 'Lookup failed', select: 'Choose office', errors: JSON.stringify({ NoOffices: 'No offices', OfficeLookupUnavailable: 'Unavailable' }) } };
        form.dataset = { citiesUrl: '/Shipping/Cities', officesUrl: '/Shipping/Offices' };
        form.querySelector = () => ({ value: 'token' });
        form.requestSubmit = () => { throw new Error('City selection must not submit/navigate the form'); };
    }
    vm.runInNewContext(source, { URLSearchParams, fetch: async (url, options) => { if (url === '/Shipping/Offices') { form.lookup = options; return officeResponse ? officeResponse(options) : { ok: true, json: async () => ({ offices: [{ code: '8', name: 'Central', address: 'Street 1' }], errorKey: null }) }; } return typeof directory === 'function' ? directory(options) : ({ ok: true, json: async () => directory }); }, document: { getElementById: id => nodes[id], createElement: () => node(), addEventListener() {} } });
    return { form, method, city, office, result, sections, cities: nodes['delivery-cities'], popup: nodes['delivery-city-popup'], toggle: nodes['delivery-city-toggle'], picker: nodes['delivery-city-picker'], empty: nodes['delivery-city-empty'], officeStatus: nodes['delivery-office-status'] };
}

test('pickup shows the store location and hides address and courier fields', () => {
    const ui = fixture();
    assert.equal(ui.sections.pickup.hidden, false);
    for (const section of ['destination', 'address', 'office', 'own', 'courier']) assert.equal(ui.sections[section].hidden, true);
});

test('own transport shows the address and radius policy without courier office controls', () => {
    const ui = fixture('1');
    for (const section of ['destination', 'address', 'own']) assert.equal(ui.sections[section].hidden, false);
    for (const section of ['office', 'courier', 'pickup']) assert.equal(ui.sections[section].hidden, true);
});

for (const method of ['2', '4']) {
    test(`office method ${method} shows office choices and preserves the selected result on initial render`, () => {
        const ui = fixture(method);
        assert.equal(ui.sections.destination.hidden, false);
        assert.equal(ui.sections.office.hidden, false);
        assert.equal(ui.sections.courier.hidden, false);
        assert.equal(ui.sections.address.hidden, true);
        assert.equal(ui.office.value, '7');
        assert.equal(ui.office.options.length, 3);
        assert.equal(ui.result.hidden, false);
    });
}

for (const method of ['3', '5']) {
    test(`address method ${method} shows address inputs and courier policy`, () => {
        const ui = fixture(method);
        assert.equal(ui.sections.destination.hidden, false);
        assert.equal(ui.sections.address.hidden, false);
        assert.equal(ui.sections.courier.hidden, false);
        assert.equal(ui.sections.office.hidden, true);
        assert.equal(ui.sections.own.hidden, true);
    });
}

test('changing the carrier clears office selection and hides the previous quote', () => {
    const ui = fixture('2');
    ui.method.value = '4';
    ui.method.fire('change');
    assert.equal(ui.office.value, '');
    assert.deepEqual(ui.office.options, ['']);
    assert.equal(ui.result.hidden, true);
    assert.equal(ui.sections.office.hidden, false);
});

test('typing a different city removes offices for the previous city', () => {
    const ui = fixture('4');
    ui.city.value = 'Sofia';
    ui.city.fire('input');
    assert.equal(ui.office.value, '');
    assert.deepEqual(ui.office.options, ['']);
    assert.equal(ui.result.hidden, true);
});

test('changing an address or office hides a stale quote', () => {
    const address = fixture('3');
    address.form.fire('input');
    assert.equal(address.result.hidden, true);
    const office = fixture('4');
    office.office.fire('change');
    assert.equal(office.result.hidden, true);
});

test('form interaction works before any quote has been calculated', () => {
    const ui = fixture('1', false);
    assert.doesNotThrow(() => { ui.city.fire('input'); ui.method.value = '0'; ui.method.fire('change'); });
    assert.equal(ui.sections.pickup.hidden, false);
});

test('empty or invalid carts without a form do not require script initialization', () => {
    assert.doesNotThrow(() => vm.runInNewContext(source, { document: { getElementById: () => null } }));
});

test('Econt directory requires an exact selection and automatically loads offices', async () => {
    const ui = fixture('2', true, ['Plovdiv', 'Sofia']);
    await new Promise(resolve => setImmediate(resolve));
    ui.city.value = 'Plo'; ui.city.fire('input');
    assert.equal(ui.city.error, 'Choose city');
    assert.deepEqual(ui.office.options, ['']);
    ui.city.value = 'Plovdiv'; ui.city.fire('input'); ui.city.fire('keydown', { key: 'Enter' });
    assert.equal(ui.city.error, ''); assert.ok(ui.form.lookup);
    ui.city.value = 'Other'; ui.method.value = '1'; ui.method.fire('change');
    assert.equal(ui.city.error, '');
});


test('switching carriers ignores a late directory from the previous carrier', async () => {
    let finishEcont;
    const ui = fixture('2', true, options => {
        if (options.body.get('method') === '2') return new Promise(resolve => { finishEcont = resolve; });
        return Promise.resolve({ ok: true, json: async () => ['Speedy city'] });
    });
    ui.method.value = '4'; ui.method.fire('change');
    await new Promise(resolve => setImmediate(resolve));
    finishEcont({ ok: true, json: async () => ['Econt city'] });
    await new Promise(resolve => setImmediate(resolve));
    ui.city.value = 'Econt city'; ui.city.fire('input');
    assert.equal(ui.city.error, 'Choose city');
    ui.city.value = 'Speedy city'; ui.city.fire('input'); ui.city.fire('keydown', { key: 'Enter' });
    assert.equal(ui.city.error, ''); assert.ok(ui.form.lookup);
});

test('missing courier configuration leaves no selectable city and can be retried', async () => {
    const ui = fixture('4', true, async () => ({ ok: false }));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(ui.city.error, 'Choose city');
    assert.equal(ui.form.submitted, undefined);
    ui.method.value = '0'; ui.method.fire('change');
    assert.equal(ui.city.error, '');
});

for (const method of ['2', '4']) {
    test('office dropdown ' + method + ' opens all cities and filters Bulgarian case-insensitively', async () => {
        const ui = fixture(method, true, ['Пловдив', 'Плевен', 'София']);
        await new Promise(resolve => setImmediate(resolve));
        assert.equal(ui.toggle.hidden, false);
        assert.equal(ui.city.attributes.role, 'combobox');
        ui.city.value = '';
        ui.toggle.fire('click');
        assert.equal(ui.popup.hidden, false);
        assert.equal(ui.cities.children.length, 3);
        ui.city.value = 'пл'; ui.city.fire('input');
        assert.deepEqual(Array.from(ui.cities.children, x => x.textContent), ['Пловдив', 'Плевен']);
        ui.cities.children[1].fire('click');
        assert.equal(ui.city.value, 'Плевен');
        assert.equal(ui.popup.hidden, true);
        assert.ok(ui.form.lookup);
        assert.equal(ui.result.hidden, true);
    });
}

test('keyboard navigation selects the highlighted city and Escape closes without submission', async () => {
    const ui = fixture('2', true, ['Plovdiv', 'Sofia']);
    await new Promise(resolve => setImmediate(resolve));
    ui.city.value = '';
    ui.city.fire('keydown', { key: 'ArrowDown' });
    assert.equal(ui.city.attributes['aria-activedescendant'], ui.cities.children[0].id);
    ui.city.fire('keydown', { key: 'ArrowDown' });
    assert.equal(ui.cities.children[1].attributes['aria-selected'], 'true');
    ui.city.fire('keydown', { key: 'Escape' });
    assert.equal(ui.popup.hidden, true);
    assert.equal(ui.form.submitted, undefined);
    ui.city.fire('keydown', { key: 'ArrowUp' });
    ui.city.fire('keydown', { key: 'Enter' });
    assert.equal(ui.city.value, 'Sofia');
    assert.ok(ui.form.lookup);
});

test('no matches shows an empty state and Enter cannot submit an arbitrary city', async () => {
    const ui = fixture('4', true, ['Plovdiv']);
    await new Promise(resolve => setImmediate(resolve));
    ui.city.value = '<script>'; ui.city.fire('input');
    assert.equal(ui.cities.children.length, 0);
    assert.equal(ui.empty.hidden, false);
    assert.equal(ui.city.fire('keydown', { key: 'Enter' }).defaultPrevented, true);
    assert.equal(ui.form.submitted, undefined);
    ui.picker.fire('focusout', { relatedTarget: null });
    assert.equal(ui.popup.hidden, true);
});

test('switching to address removes dropdown semantics and permits ordinary city text', async () => {
    const ui = fixture('2', true, ['Plovdiv']);
    await new Promise(resolve => setImmediate(resolve));
    ui.city.fire('focus');
    ui.method.value = '3'; ui.method.fire('change');
    assert.equal(ui.popup.hidden, true);
    assert.equal(ui.toggle.hidden, true);
    assert.equal(ui.city.attributes.role, undefined);
    assert.equal(ui.city.attributes.autocomplete, 'address-level2');
    assert.equal(ui.city.error, '');
});

for (const method of ['2', '4']) {
    test('reopening a populated city keeps its filter for courier ' + method, async () => {
        const ui = fixture(method, true, ['Plovdiv', 'Pleven', 'Sofia']);
        await new Promise(resolve => setImmediate(resolve));
        ui.city.fire('focus');
        assert.deepEqual(Array.from(ui.cities.children, x => x.textContent), ['Plovdiv']);
        ui.city.fire('keydown', { key: 'Escape' });
        ui.city.fire('click');
        assert.deepEqual(Array.from(ui.cities.children, x => x.textContent), ['Plovdiv']);
        ui.city.value = 'pl'; ui.city.fire('input');
        ui.city.fire('keydown', { key: 'Escape' });
        ui.toggle.fire('click');
        assert.deepEqual(Array.from(ui.cities.children, x => x.textContent), ['Plovdiv', 'Pleven']);
        assert.equal(ui.form.submitted, undefined);
        ui.cities.children[1].fire('click');
        assert.ok(ui.form.lookup);
        assert.equal(ui.city.value, 'Pleven');
    });
}

test('office lookup has no form submitter and uses its own asynchronous endpoint', () => {
    const view = fs.readFileSync(path.join(__dirname, '../../Algara.Web/Views/Shipping/Index.cshtml'), 'utf8');
    assert.match(view, /data-offices-url=/);
    assert.doesNotMatch(view, /data-load-offices/);
    assert.doesNotMatch(source, /requestSubmit/);
});

const flush = () => new Promise(resolve => setImmediate(resolve));
for (const method of ['2', '4']) {
    test('city selection loads office options without navigation for method ' + method, async () => {
        const ui = fixture(method, true, ['Plovdiv']); await flush();
        ui.city.fire('focus'); ui.cities.children[0].fire('click');
        assert.equal(ui.officeStatus.textContent, 'Loading offices');
        assert.equal(ui.office.attributes['aria-busy'], 'true');
        assert.equal(ui.form.lookup.body.get('json'), 'true');
        assert.equal(ui.form.lookup.body.get('city'), 'Plovdiv');
        assert.equal(ui.form.lookup.body.get('method'), method);
        assert.equal(ui.form.lookup.body.get('__RequestVerificationToken'), 'token');
        await flush();
        assert.equal(ui.office.options.length, 2);
        assert.equal(ui.office.options[1].value, '8');
        assert.equal(ui.office.options[1].textContent, 'Central — Street 1');
        assert.equal(ui.officeStatus.textContent, 'Choose office');
        ui.office.value = '8'; ui.office.fire('change');
        assert.equal(ui.office.error, '');
    });
}
for (const change of ['typing', 'carrier', 'next-city']) {
    test('late office response is discarded after ' + change, async () => {
        let resolveFirst; let count = 0;
        const ui = fixture('2', true, ['Plovdiv', 'Sofia'], () => {
            if (++count === 1) return new Promise(resolve => { resolveFirst = resolve; });
            return { ok: true, json: async () => ({ offices: [{ code: '9', name: 'Sofia', address: '' }] }) };
        });
        await flush(); ui.city.fire('focus'); ui.cities.children[0].fire('click');
        if (change === 'carrier') { ui.method.value = '4'; ui.method.fire('change'); }
        else { ui.city.value = 'Sofia'; ui.city.fire('input'); if (change === 'next-city') ui.cities.children[0].fire('click'); }
        await flush();
        resolveFirst({ ok: true, json: async () => ({ offices: [{ code: '8', name: 'Old', address: '' }] }) });
        await flush();
        assert.equal(ui.office.options.length, change === 'next-city' ? 2 : 1);
        if (change === 'next-city') assert.equal(ui.office.options[1].value, '9');
        assert.equal(ui.office.attributes['aria-busy'], undefined);
    });
}
for (const mode of ['http-error', 'invalid-json', 'empty', 'carrier-error']) {
    test('office lookup displays safe inline feedback for ' + mode, async () => {
        const ui = fixture('2', true, ['Plovdiv'], async () => ({
            ok: mode !== 'http-error', json: async () => mode === 'invalid-json' ? null :
                { offices: [], errorKey: mode === 'carrier-error' ? 'OfficeLookupUnavailable' : null }
        }));
        await flush(); ui.city.fire('focus'); ui.cities.children[0].fire('click'); await flush();
        assert.equal(ui.office.options.length, 1);
        assert.equal(ui.officeStatus.textContent, mode === 'empty' ? 'No offices' : mode === 'carrier-error' ? 'Unavailable' : 'Lookup failed');
        assert.equal(ui.office.error, 'Choose office');
    });
}
