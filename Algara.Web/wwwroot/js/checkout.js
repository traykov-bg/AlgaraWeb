(function () {
    'use strict';
    const form = document.getElementById('delivery-quote-form');
    if (!form || !form.dataset.quoteUrl) return;
    // MVC renders a second hidden input for checkboxes. Select the visible control,
    // rather than the RadioNodeList returned by form.elements.namedItem.
    const field = name => form.querySelector('[name="' + name + '"]');
    const submit = document.getElementById('checkout-submit');
    const status = document.getElementById('quote-status');
    const deliveryFields = ['Method', 'City', 'PostalCode', 'Address', 'OfficeCode'];
    let version = 0, timer, request, submitting = false;
    function schedule() {
        field('Address').required = Number(field('Method').value) === 1;
        ++version;
        clearTimeout(timer);
        if (request) request.abort();
        field('QuoteToken').value = '';
        submit.disabled = true;
        ['delivery', 'total'].forEach(x => document.getElementById('quote-' + x).textContent = '—');
        document.getElementById('quote-address').textContent = '';
        document.getElementById('quote-google').hidden = true;
        status.textContent = '';
        timer = setTimeout(calculate, 700);
    }
    async function calculate() {
        const current = version;
        const method = Number(field('Method').value);
        if (method !== 0 && (!field('City').value.trim() ||
            ((method === 2 || method === 4) ? !field('OfficeCode').value : method !== 1 && !field('Address').value.trim()))) return;
        const body = new URLSearchParams();
        [...deliveryFields, 'SubmissionToken', '__RequestVerificationToken'].forEach(name => body.set(name, field(name).value));
        status.textContent = form.dataset.loading;
        request = new AbortController();
        try {
            const response = await fetch(form.dataset.quoteUrl, { method: 'POST', credentials: 'same-origin', body, signal: request.signal });
            if (!response.ok) throw new Error('quote');
            const data = await response.json();
            if (current !== version) return;
            if (data.error) { status.textContent = data.error; return; }
            if (typeof data.token !== 'string') throw new Error('quote');
            if (!Array.isArray(data.lines)) throw new Error('lines');
            document.getElementById('checkout-lines').replaceChildren(...data.lines.map(line => {
                const item = document.createElement('p');
                item.textContent = line.name + ' ' + line.variant + ' · ' + line.quantity + ' × ' + line.price + ' € · ' + line.availability;
                return item;
            }));
            field('QuoteToken').value = data.token;
            ['goods', 'delivery', 'total'].forEach(x => document.getElementById('quote-' + x).textContent = data[x]);
            document.getElementById('quote-address').textContent = data.address;
            document.getElementById('quote-google').hidden = !data.google;
            status.textContent = '';
            submit.disabled = submitting;
        } catch (error) {
            if (current === version && error.name !== 'AbortError') status.textContent = form.dataset.failed;
        }
    }
    function optional(checkbox, panel, names) {
        const check = field(checkbox);
        if (!check) return;
        const update = () => {
            document.getElementById(panel).hidden = !check.checked;
            names.forEach(name => { field(name).disabled = !check.checked; field(name).required = check.checked; if (!check.checked) field(name).value = ''; });
        };
        check.addEventListener('change', update); update();
    }
    ['input', 'change'].forEach(event => form.addEventListener(event, e => { if (deliveryFields.includes(e.target.name)) schedule(); }));
    form.addEventListener('delivery-invalidated', schedule);
    form.addEventListener('submit', e => {
        if (submitting || !field('QuoteToken').value) { e.preventDefault(); return; }
        submitting = true; submit.disabled = true;
    });
    window.addEventListener('pageshow', () => { submitting = false; schedule(); });
    optional('CreateAccount', 'account-fields', ['Password', 'ConfirmPassword']);
    optional('InvoiceRequested', 'invoice-fields', ['InvoiceDetails']);
    schedule();
}());
