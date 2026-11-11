const { test } = require('node:test');
const assert = require('node:assert/strict');
const { cents, totals, createSearch } = require('../../Algara.Web/wwwroot/js/inquiry-editor.js');

test('currency parsing accepts dot/comma without rounding invalid precision', () => {
    assert.equal(cents(' 125,50 '), 12550);
    assert.equal(cents('125.5'), 12550);
    for (const value of ['', '-1', '1,000.00', '1e3', '0.001', '999999999.01']) assert.equal(cents(value), null);
});
test('draft totals use integer cents and include services and delivery once', () => {
    assert.deepEqual(totals([{ quantity: '3', price: '0.10' }, { quantity: '2', price: '125,50' }], ['15.15', '0'], '4.55'),
        { goods: 25130, services: 1515, delivery: 455, total: 27100, pending: false });
});
test('blank delivery is visibly pending and different from agreed free delivery', () => {
    assert.equal(totals([{ quantity: 1, price: 10 }], [], '').pending, true);
    assert.equal(totals([{ quantity: 1, price: 10 }], [], '0').pending, false);
});
test('invalid draft input never produces a misleading numeric total', () => {
    for (const quantity of ['0', '-1', '100', '1.5']) assert.equal(totals([{ quantity, price: 10 }], [], '0').total, null);
    assert.equal(totals([{ quantity: 1, price: 0 }], [], '0').total, null);
    assert.equal(totals([{ quantity: 1, price: 10 }], ['invalid'], '0').total, null);
});
test('short and overlong product searches send no requests', async () => {
    let calls = 0;
    const states = [];
    const search = createSearch(async () => { calls++; return []; }, state => states.push(state));
    await search.run(' a '); await search.run('a'.repeat(101));
    assert.equal(calls, 0); assert.deepEqual(states, ['hint', 'hint']);
});
test('older search response cannot replace results of newer search', async () => {
    const pending = [], rendered = [];
    const search = createSearch((query, signal) => new Promise(resolve => pending.push({ query, signal, resolve })), (state, rows) => rendered.push({ state, rows }));
    const first = search.run('old'); const second = search.run('new');
    assert.equal(pending[0].signal.aborted, true);
    pending[1].resolve([{ name: 'new' }]); await second;
    pending[0].resolve([{ name: 'old' }]); await first;
    assert.deepEqual(rendered.at(-1), { state: 'results', rows: [{ name: 'new' }] });
    assert.equal(rendered.filter(r => r.state === 'results').length, 1);
});
test('clearing or cancelling a query prevents stale results even when abort is ignored', async () => {
    let resolve;
    const states = [];
    const search = createSearch(() => new Promise(r => { resolve = r; }), state => states.push(state));
    const running = search.run('table'); search.cancel(); resolve([{ name: 'table' }]); await running;
    assert.deepEqual(states, ['loading']);
});
test('network failure stays in the editor and a subsequent search can retry', async () => {
    let fail = true; const states = [];
    const search = createSearch(async () => { if (fail) throw new Error('offline'); return []; }, state => states.push(state));
    await search.run('table'); fail = false; await search.run('table');
    assert.deepEqual(states, ['loading', 'error', 'loading', 'empty']);
});
test('unexpected or oversized response produces an inline error', async () => {
    for (const response of [{ login: true }, new Array(31).fill({})]) {
        const states = [];
        await createSearch(async () => response, state => states.push(state)).run('table');
        assert.equal(states.at(-1), 'error');
    }
});
