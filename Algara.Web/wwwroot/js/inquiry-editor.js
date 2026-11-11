(function (root) {
    'use strict';

    function cents(value) {
        const text = String(value ?? '').trim();
        if (!/^\d{1,9}(?:[.,]\d{1,2})?$/.test(text)) return null;
        const parts = text.replace(',', '.').split('.');
        const amount = Number(parts[0]) * 100 + Number((parts[1] || '').padEnd(2, '0'));
        return amount <= 99999999900 ? amount : null;
    }
    function totals(lines, services, delivery) {
        let goods = 0, extras = 0;
        for (const line of lines) {
            const price = cents(line.price), quantity = String(line.quantity).trim();
            if (price === null || price <= 0 || !/^\d{1,2}$/.test(quantity) || Number(quantity) < 1) { goods = null; break; }
            goods += price * Number(quantity);
        }
        for (const service of services) {
            const amount = cents(service);
            if (amount === null) { extras = null; break; }
            extras += amount;
        }
        const pending = String(delivery ?? '').trim() === '';
        const transport = pending ? 0 : cents(delivery);
        return { goods, services: extras, delivery: transport, pending,
            total: goods === null || extras === null || transport === null ? null : goods + extras + transport };
    }

    // A generation guard also protects against transports that do not honor AbortSignal.
    function createSearch(fetchProducts, render) {
        let generation = 0, controller;
        return {
            cancel() { generation++; controller?.abort(); },
            async run(value) {
                const current = ++generation;
                controller?.abort();
                const query = String(value || '').trim();
                if (query.length < 2 || query.length > 100) { render('hint', []); return; }
                controller = new AbortController();
                render('loading', []);
                try {
                    const items = await fetchProducts(query, controller.signal);
                    if (current !== generation) return;
                    if (!Array.isArray(items) || items.length > 30) throw new Error('Invalid search result');
                    render(items.length ? 'results' : 'empty', items);
                } catch (error) {
                    if (current === generation && error.name !== 'AbortError') render('error', []);
                }
            }
        };
    }

    function init(editor) {
        const form = editor.closest('form');
        const lineList = editor.querySelector('[data-offer-lines]');
        const serviceList = editor.querySelector('[data-offer-services]');
        const lineTemplate = editor.querySelector('[data-line-template]');
        const serviceTemplate = editor.querySelector('[data-service-template]');
        if (!form || !lineTemplate?.content || !serviceTemplate?.content || !root.fetch || !root.AbortController) return;
        const searchInput = editor.querySelector('#product-search');
        const results = editor.querySelector('[data-search-results]');
        const searchStatus = editor.querySelector('[data-search-status]');
        const status = editor.querySelector('[data-editor-status]');
        const delivery = editor.querySelector('#offer-delivery');
        const money = new Intl.NumberFormat(document.documentElement.lang || 'bg', { style: 'currency', currency: 'EUR' });
        const lines = () => Array.from(lineList.querySelectorAll('[data-offer-line]'));
        const services = () => Array.from(serviceList.querySelectorAll('[data-offer-service]'));
        const field = (row, key) => row.querySelector('[data-field="' + key + '"]');
        const display = amount => amount === null ? '—' : money.format(amount / 100);
        let timer;
        const orderLink = form.querySelector('[data-create-order]');
        const warning = form.querySelector('[data-accepted-warning]');
        function lockOrder() {
            if (!orderLink) return;
            orderLink.removeAttribute('href'); orderLink.setAttribute('aria-disabled', 'true');
            orderLink.classList.add('disabled'); warning.hidden = false;
        }
        if (form.dataset.draftError?.toLowerCase() === 'true') lockOrder();
        function showResults(show) { results.hidden = !show; searchInput.setAttribute('aria-expanded', String(show)); }
        searchInput.addEventListener('focus', () => showResults(true));
        document.addEventListener('click', event => {
            showResults(event.target === searchInput || results.contains(event.target) || !!event.target.closest('[data-offer-action="search"]'));
        });
        const agreementError = form.querySelector('[data-agreement-error]');
        const agreement = form.querySelector('#staff-agreement');
        function focusError(element) { element.focus(); element.scrollIntoView({ block: 'center' }); }
        agreement?.addEventListener('change', () => { if (agreement.checked) { agreementError.hidden = true; agreement.removeAttribute('aria-invalid'); } });
        const messages = JSON.parse(form.dataset.fieldMessages || '{}');
        function clearFieldErrors() {
            form.querySelectorAll('[data-field-error]').forEach(e => e.remove());
            form.querySelectorAll('[aria-invalid="true"]').forEach(e => { e.removeAttribute('aria-invalid'); e.removeAttribute('aria-describedby'); });
            agreementError.hidden = true;
        }
        function fieldError(name, message) {
            const input = Array.from(form.elements).find(e => e.name === name && e.type !== 'hidden');
            if (!input) return null;
            input.setAttribute('aria-invalid', 'true');
            const label = document.createElement('span'); label.className = 'text-danger d-block'; label.dataset.fieldError = name;
            label.id = (input.id || name.replace(/[^a-z0-9]/gi, '-')) + '-validation'; label.textContent = message;
            input.setAttribute('aria-describedby', label.id);
            (input.type === 'checkbox' ? input.closest('.form-check') || input.parentElement : input).after(label);
            return input;
        }
        function validateAction(command) {
            clearFieldErrors(); let first;
            const error = (name, message) => { const input = fieldError(name, message); first ||= input; };
            const value = name => Array.from(form.elements).find(e => e.name === name && e.type !== 'hidden')?.value.trim() || '';
            if (!value('Note')) error('Note', messages.required);
            const offer = command === 'Offer' || (command === 'Complete' && form.dataset.existingOffer?.toLowerCase() !== 'true');
            if (offer) {
                if (!lines().length) error('ProductSearch', messages.items);
                lines().forEach((row, i) => {
                    const quantity = field(row, 'Quantity').value, price = cents(field(row, 'UnitPrice').value);
                    if (!/^[0-9]{1,2}$/.test(quantity) || Number(quantity) < 1) error('Lines['+i+'].Quantity', messages.quantity);
                    if (price === null || price <= 0) error('Lines['+i+'].UnitPrice', messages.price);
                    if ((field(row, 'IndividualManufacture').checked || price !== cents(field(row, 'CatalogUnitPrice').value)) && !field(row, 'Comment').value.trim()) error('Lines['+i+'].Comment', messages.comment);
                });
                services().forEach((row,i) => {
                    if (!field(row,'Name').value.trim()) error('Services['+i+'].Name',messages.required);
                    if (cents(field(row,'Amount').value) === null) error('Services['+i+'].Amount',messages.invalid);
                });
                if (delivery.value.trim() && cents(delivery.value) === null) error('DeliveryAmount',messages.invalid);
                if (!value('Terms')) error('Terms',messages.required);
                if (!value('Description')) error('Description',messages.required);
            }
            if (command === 'Complete' && agreement && !agreement.checked) error('Agreement',messages.agreement);
            if (first) focusError(first);
            return !first;
        }
        form.addEventListener('submit', event => {
            if (!validateAction(event.submitter?.value)) event.preventDefault();
        });


        function reindex() {
            for (const [rows, prefix, idPrefix, command] of [[lines(), 'Lines', 'line', 'Remove'], [services(), 'Services', 'service', 'RemoveService']]) {
                rows.forEach((row, index) => {
                    for (const input of row.querySelectorAll('[data-field]')) {
                        input.name = prefix + '[' + index + '].' + input.dataset.field;
                        if (input.type !== 'hidden') input.id = idPrefix + '-' + index + '-' + input.dataset.field;
                    }
                    for (const label of row.querySelectorAll('[data-label-field]')) label.htmlFor = idPrefix + '-' + index + '-' + label.dataset.labelField;
                    row.querySelector('[data-offer-action]').value = command + ':' + index;
                });
            }
            // Keep all local operations out of submit/HTML validation, including freshly cloned rows.
            for (const button of editor.querySelectorAll('[data-offer-action]')) button.type = 'button';
        }
        function updateTotals() {
            const sum = totals(lines().map(row => ({ quantity: field(row, 'Quantity').value, price: field(row, 'UnitPrice').value })),
                services().map(row => field(row, 'Amount').value), delivery.value);
            for (const [selector, amount] of [['goods', sum.goods], ['services', sum.services], ['delivery', sum.delivery], ['grand', sum.total]])
                editor.querySelector('[data-' + selector + '-total]').textContent = selector === 'delivery' && sum.pending ? '—' : display(amount);
            editor.querySelector('[data-total-note]').textContent = sum.total === null ? editor.dataset.invalid : sum.pending ? editor.dataset.pending : '';
            editor.querySelector('[data-empty-lines]').hidden = lines().length > 0;
            for (const button of results.querySelectorAll('button')) button.disabled = lines().length >= 30;
            editor.querySelector('[data-offer-action="add-service"]').disabled = services().length >= 20;
        }
        function changed() {
            lockOrder();
            const agreement = form.querySelector('#staff-agreement');
            if (agreement) agreement.checked = false;
            reindex(); updateTotals();
        }
        function appendLine(choice) {
            if (lines().length >= 30) { status.textContent = editor.dataset.limit; return; }
            if (!choice || !Number.isInteger(choice.productN) || choice.productN < 1 ||
                (choice.variantN !== null && choice.variantN !== undefined && (!Number.isInteger(choice.variantN) || choice.variantN < 1)) ||
                !Number.isFinite(choice.catalogUnitPrice)) { searchStatus.textContent = editor.dataset.searchError; return; }
            const row = lineTemplate.content.firstElementChild.cloneNode(true);
            const values = { ProductN: choice.productN, VariantN: choice.variantN ?? '', Name: choice.name ?? '', VariantName: choice.variantName ?? '',
                Sku: choice.sku ?? '', CatalogUnitPrice: choice.catalogUnitPrice, Quantity: 1, UnitPrice: choice.catalogUnitPrice, Comment: '' };
            for (const [key, value] of Object.entries(values)) field(row, key).value = String(value);
            field(row, 'IndividualManufacture').checked = !!choice.individualManufacture;
            row.querySelector('[data-line-title]').textContent = choice.name || '';
            row.querySelector('[data-line-variant]').textContent = [choice.variantName, choice.sku].filter(Boolean).join(' · ');
            row.querySelector('[data-catalog-price]').textContent = money.format(choice.catalogUnitPrice);
            lineList.append(row); changed();
            searchStatus.textContent = editor.dataset.added + ' ' + (choice.name || '');
            field(row, 'Quantity').focus({ preventScroll: true });
        }
        const search = createSearch(async (query, signal) => {
            const url = new URL(editor.dataset.searchUrl, root.location.href);
            url.searchParams.set('q', query);
            const response = await root.fetch(url, { signal, credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            if (!response.ok || response.redirected || !response.headers.get('content-type')?.includes('application/json')) throw new Error('Search unavailable');
            return await response.json();
        }, (state, items) => {
            results.replaceChildren();
            results.setAttribute('aria-busy', String(state === 'loading'));
            searchStatus.textContent = ({ hint: editor.dataset.searchHint, loading: editor.dataset.searching,
                empty: editor.dataset.empty, error: editor.dataset.searchError })[state] || '';
            for (const choice of items) {
                const row = document.createElement('div'); row.className = 'offer-search-result';
                const button = document.createElement('button'); button.type = 'button'; button.className = 'btn btn-outline-primary btn-sm';
                button.textContent = editor.dataset.addLabel; button.disabled = lines().length >= 30;
                button.addEventListener('click', () => appendLine(choice));
                const label = document.createElement('span');
                label.textContent = [choice.name, choice.variantName, choice.sku].filter(Boolean).join(' · ') + ' · ' + money.format(choice.catalogUnitPrice);
                row.append(button, label); results.append(row);
            }
        });
        function searchNow() { showResults(true); clearTimeout(timer); return search.run(searchInput.value); }
        searchInput.addEventListener('input', () => {
            clearTimeout(timer); search.cancel(); results.replaceChildren();
            results.setAttribute('aria-busy', 'false'); searchStatus.textContent = editor.dataset.searchHint;
            timer = setTimeout(searchNow, 300);
        });
        searchInput.addEventListener('keydown', event => {
            if (event.key === 'Enter' && !event.isComposing) { event.preventDefault(); searchNow(); }
            if (event.key === 'Escape') { clearTimeout(timer); search.cancel(); showResults(false); searchStatus.textContent = ''; }
        });
        editor.addEventListener('click', event => {
            const button = event.target.closest('[data-offer-action]');
            if (!button || !editor.contains(button)) return;
            event.preventDefault();
            switch (button.dataset.offerAction) {
                case 'search': searchNow(); break;
                case 'add-choice':
                    try { appendLine(JSON.parse(button.dataset.choice)); } catch { searchStatus.textContent = editor.dataset.searchError; }
                    break;
                case 'remove-line':
                    button.closest('[data-offer-line]').remove(); changed(); status.textContent = editor.dataset.removed;
                    searchInput.focus({ preventScroll: true }); break;
                case 'add-service': {
                    if (services().length >= 20) { status.textContent = editor.dataset.limit; break; }
                    const row = serviceTemplate.content.firstElementChild.cloneNode(true);
                    serviceList.append(row); changed(); field(row, 'Name').focus({ preventScroll: true }); break;
                }
                case 'remove-service':
                    button.closest('[data-offer-service]').remove(); changed(); status.textContent = editor.dataset.removed;
                    editor.querySelector('[data-offer-action="add-service"]').focus({ preventScroll: true }); break;
            }
        });
        form.addEventListener('input', event => {
            if (event.target === searchInput) return;
            if (editor.contains(event.target) || ['Terms', 'Description'].includes(event.target.name)) changed();
        });
        form.addEventListener('change', event => { if (editor.contains(event.target) && event.target.type === 'checkbox') changed(); });
        form.addEventListener('submit', () => {
            reindex();
            const scroll = form.querySelector('[name="ScrollY"]');
            if (scroll) scroll.value = String(Math.min(1000000, Math.max(0, Math.round(root.scrollY))));
        });
        reindex(); updateTotals(); editor.querySelector('[data-offer-summary]').hidden = false;
        const scroll = Number(form.dataset.restoreScroll);
        let firstFieldError;
        for (const [name, message] of Object.entries(JSON.parse(form.dataset.fieldErrors || '{}'))) {
            const input = fieldError(name, message); firstFieldError ||= input;
        }
        const error = firstFieldError || form.querySelector('[data-form-error]');
        if (error) root.requestAnimationFrame(() => focusError(!agreementError.hidden && agreement ? agreement : error));
        else if (Number.isFinite(scroll) && scroll > 0 && scroll <= 1000000) root.requestAnimationFrame(() => root.scrollTo(0, scroll));
        root.addEventListener('pagehide', () => { clearTimeout(timer); search.cancel(); });
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { cents, totals, createSearch, init };
    if (typeof document !== 'undefined') document.querySelectorAll('[data-offer-editor]').forEach(init);
})(typeof window === 'undefined' ? globalThis : window);
