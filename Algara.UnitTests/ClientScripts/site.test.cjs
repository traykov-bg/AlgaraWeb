const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const test = require('node:test');

const source = fs.readFileSync(path.join(__dirname, '../../Algara.Web/wwwroot/js/site.js'), 'utf8');

async function search(items, language = 'bg', query = 'диван', dataset = {}) {
    const listeners = {};
    const classes = new Set();
    const input = { value: query, addEventListener: (name, callback) => { listeners[name] = callback; } };
    const dropdown = {
        innerHTML: '',
        classList: { add: name => classes.add(name), remove: name => classes.delete(name) }
    };
    const elements = { algaraSearchInput: input, algaraSearchDropdown: dropdown, algaraSearchForm: { dataset } };
    let requestedUrl;
    vm.runInNewContext(source, {
        document: {
            documentElement: { lang: language },
            getElementById: id => elements[id],
            addEventListener() {}
        },
        Intl,
        clearTimeout() {},
        setTimeout: callback => callback(),
        fetch: async url => { requestedUrl = url; return { json: async () => items }; }
    });
    listeners.input.call(input);
    await new Promise(resolve => setImmediate(resolve));
    return { html: dropdown.innerHTML, open: classes.has('is-open'), requestedUrl };
}

test('live search follows the server-generated product URL and escapes its attributes', async () => {
    const result = await search([{
        n: 123, name: 'Диван', category: 'Мебели', price: 1234.56,
        url: '/shop/Product/Detail?n=123&source="search"'
    }]);
    assert.equal(result.open, true);
    assert.ok(result.html.includes('href="/shop/Product/Detail?n=123&amp;source=&quot;search&quot;"'));
    assert.equal(result.requestedUrl, '/Product/Search?q=' + encodeURIComponent('диван'));
});

for (const language of ['bg', 'en']) {
    test(`live search preserves promotional cents and whole-price decimals in ${language}`, async () => {
        const prices = [1234.56, 87.09, 50];
        const result = await search(prices.map((price, n) => ({
            n, name: 'Диван', price, url: '/Product/Detail?n=' + n
        })), language);
        for (const price of prices) {
            const formatted = new Intl.NumberFormat(language, {
                minimumFractionDigits: 2, maximumFractionDigits: 2
            }).format(price);
            assert.ok(result.html.includes(formatted + ' €'), `Missing exact displayed price: ${formatted}`);
        }
    });
}

test('empty live-search results hide the dropdown', async () => {
    const result = await search([]);
    assert.equal(result.open, false);
    assert.equal(result.html, '');
});

test('English UI uses supplied search labels and preserves the storefront price format and application paths', async () => {
    const result = await search([{ n: 1, name: 'Sofa', price: 1234.56, url: '/shop/Product/Detail?n=1' }],
        'en', 'sofa', {
            searchAllLabel: 'See all results for', priceLocale: 'bg-BG',
            searchUrl: '/shop/Product/Search', catalogUrl: '/shop/Product'
        });
    assert.equal(result.requestedUrl, '/shop/Product/Search?q=sofa');
    assert.ok(result.html.includes('See all results for'));
    assert.ok(result.html.includes('href="/shop/Product?q=sofa"'));
    assert.ok(result.html.includes(new Intl.NumberFormat('bg-BG', {
        minimumFractionDigits: 2, maximumFractionDigits: 2
    }).format(1234.56) + ' €'));
});
