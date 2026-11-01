(function () {
    'use strict';
    const form = document.getElementById('delivery-quote-form');
    if (!form) return;
    const method = document.getElementById('delivery-method');
    const city = document.getElementById('delivery-city');
    const office = document.getElementById('delivery-office');
    const result = document.getElementById('delivery-quote-result');
    const cities = document.getElementById('delivery-cities');
    const status = document.getElementById('delivery-city-status');
    const picker = document.getElementById('delivery-city-picker');
    const toggle = document.getElementById('delivery-city-toggle');
    const popup = document.getElementById('delivery-city-popup');
    const empty = document.getElementById('delivery-city-empty');
    const officeStatus = document.getElementById('delivery-office-status');
    let officeVersion = 0;
    let names = null;
    let filtered = [];
    let activeIndex = -1;
    let query = '';
    let requestVersion = 0;
    function isOffice() { return method.value === '2' || method.value === '4'; }
    function validateCity() {
        if (!cities) return;
        city.setCustomValidity(isOffice() && (!names || !names.includes(city.value)) ? status.dataset.select : '');
    }
    function closeCities() {
        popup.hidden = true;
        city.setAttribute('aria-expanded', 'false');
        toggle.setAttribute('aria-expanded', 'false');
        city.removeAttribute('aria-activedescendant');
        activeIndex = -1;
    }
    function selectCity(name) {
        city.value = name;
        clearOffice();
        clearQuote();
        validateCity();
        closeCities();
        loadOffices();
    }
    async function loadOffices() {
        const version = officeVersion;
        const selectedCity = city.value;
        const selectedMethod = method.value;
        officeStatus.textContent = officeStatus.dataset.loading;
        office.setAttribute('aria-busy', 'true');
        try {
            const token = form.querySelector('input[name="__RequestVerificationToken"]').value;
            const response = await fetch(form.dataset.officesUrl, {
                method: 'POST', credentials: 'same-origin',
                body: new URLSearchParams({ __RequestVerificationToken: token, method: selectedMethod, city: selectedCity, json: 'true' })
            });
            if (!response.ok) throw new Error('lookup');
            const data = await response.json();
            if (version !== officeVersion) return;
            if (data.errorKey) {
                const messages = JSON.parse(officeStatus.dataset.errors);
                officeStatus.textContent = Object.hasOwn(messages, data.errorKey) ? messages[data.errorKey] : officeStatus.dataset.error;
                return;
            }
            if (!Array.isArray(data.offices) || data.offices.some(row => !row ||
                typeof row.code !== 'string' || typeof row.name !== 'string' || typeof row.address !== 'string')) throw new Error('format');
            for (const row of data.offices) {
                const option = document.createElement('option');
                option.value = row.code;
                option.textContent = row.name + ' — ' + row.address;
                office.appendChild(option);
            }
            officeStatus.textContent = data.offices.length ? officeStatus.dataset.select : JSON.parse(officeStatus.dataset.errors).NoOffices;
        } catch {
            if (version === officeVersion) officeStatus.textContent = officeStatus.dataset.error;
        } finally {
            if (version === officeVersion) office.removeAttribute('aria-busy');
        }
    }
    function renderCities() {
        filtered = (names || []).filter(name => name.toLocaleLowerCase().includes(query.trim().toLocaleLowerCase()));
        activeIndex = -1;
        city.removeAttribute('aria-activedescendant');
        cities.replaceChildren(...filtered.map((name, index) => {
            const option = document.createElement('div');
            option.id = 'delivery-city-option-' + index;
            option.className = 'delivery-city-option';
            option.setAttribute('role', 'option');
            option.setAttribute('aria-selected', 'false');
            option.textContent = name;
            // Keep focus on the editable combobox while choosing with a mouse.
            option.addEventListener('mousedown', event => event.preventDefault());
            option.addEventListener('click', () => selectCity(name));
            return option;
        }));
        empty.hidden = names === null || filtered.length !== 0;
    }
    function openCities() {
        if (!isOffice()) return;
        query = city.value;
        renderCities();
        popup.hidden = false;
        city.setAttribute('aria-expanded', 'true');
        toggle.setAttribute('aria-expanded', 'true');
    }
    function moveActive(step) {
        if (!filtered.length) return;
        activeIndex = activeIndex < 0 ? (step > 0 ? 0 : filtered.length - 1)
            : (activeIndex + step + filtered.length) % filtered.length;
        Array.from(cities.children).forEach((option, index) => option.setAttribute('aria-selected', String(index === activeIndex)));
        const option = cities.children[activeIndex];
        city.setAttribute('aria-activedescendant', option.id);
        option.scrollIntoView({ block: 'nearest' });
    }
    async function loadCities() {
        if (!cities) return;
        const enabled = isOffice();
        const version = ++requestVersion;
        names = null;
        closeCities();
        cities.replaceChildren();
        empty.hidden = true;
        status.hidden = !enabled;
        toggle.hidden = !enabled;
        city.setAttribute('autocomplete', enabled ? 'off' : 'address-level2');
        if (enabled) {
            city.setAttribute('role', 'combobox');
            city.setAttribute('aria-autocomplete', 'list');
            city.setAttribute('aria-controls', cities.id);
        } else {
            ['role', 'aria-autocomplete', 'aria-controls', 'aria-expanded'].forEach(attribute => city.removeAttribute(attribute));
        }
        city.removeAttribute('aria-busy');
        validateCity();
        if (!enabled) return;
        status.textContent = status.dataset.loading;
        city.setAttribute('aria-busy', 'true');
        try {
            const token = form.querySelector('input[name="__RequestVerificationToken"]').value;
            const response = await fetch(form.dataset.citiesUrl, { method: 'POST', credentials: 'same-origin', body: new URLSearchParams({ __RequestVerificationToken: token, method: method.value }) });
            if (!response.ok) throw new Error('lookup');
            const values = await response.json();
            if (!Array.isArray(values) || values.some(x => typeof x !== 'string')) throw new Error('format');
            if (version !== requestVersion) return;
            names = values;
            renderCities();
            status.textContent = status.dataset.select;
        } catch { if (version === requestVersion) status.textContent = status.dataset.error; }
        finally {
            if (version === requestVersion) {
                city.removeAttribute('aria-busy');
                validateCity();
            }
        }
    }
    function clearQuote() { if (result) result.hidden = true; }
    function clearOffice() {
        ++officeVersion;
        if (officeStatus) {
            officeStatus.textContent = '';
            office.removeAttribute('aria-busy');
            office.setCustomValidity(isOffice() ? officeStatus.dataset.select : '');
        }
        if (office) { office.value = ''; while (office.options.length > 1) office.remove(1); }
    }
    function display() {
        const value = Number(method.value);
        const isOffice = value === 2 || value === 4;
        const visibility = {
            'destination': value !== 0, 'address': value !== 0 && !isOffice,
            'office': isOffice, 'own': value === 1, 'courier': value >= 2, 'pickup': value === 0
        };
        Object.keys(visibility).forEach(key => {
            form.querySelectorAll('[data-delivery-' + key + ']').forEach(element => { element.hidden = !visibility[key]; });
        });
    }
    form.addEventListener('input', clearQuote);
    form.addEventListener('change', clearQuote);
    method.addEventListener('change', function () { clearOffice(); display(); });
    city.addEventListener('input', clearOffice);
    if (officeStatus) office.addEventListener('change', () => office.setCustomValidity(isOffice() && !office.value ? officeStatus.dataset.select : ''));
    if (cities) {
        city.addEventListener('focus', () => openCities());
        city.addEventListener('click', () => { if (popup.hidden) openCities(); });
        city.addEventListener('input', function () { validateCity(); openCities(); });
        city.addEventListener('keydown', function (event) {
            if (!isOffice() || event.isComposing) return;
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
                event.preventDefault();
                if (popup.hidden) openCities();
                moveActive(event.key === 'ArrowDown' ? 1 : -1);
            } else if (event.key === 'Enter' && !popup.hidden) {
                event.preventDefault();
                if (activeIndex >= 0) selectCity(filtered[activeIndex]);
                else if (filtered.length === 1) selectCity(filtered[0]);
            } else if (event.key === 'Escape' && !popup.hidden) {
                event.preventDefault();
                closeCities();
            } else if (event.key === 'Tab') closeCities();
        });
        toggle.addEventListener('mousedown', event => event.preventDefault());
        toggle.addEventListener('click', function () {
            const wasOpen = !popup.hidden;
            city.focus();
            if (wasOpen) closeCities(); else openCities();
        });
        picker.addEventListener('focusout', event => {
            if (!picker.contains(event.relatedTarget)) closeCities();
        });
        document.addEventListener('pointerdown', event => {
            if (!picker.contains(event.target)) closeCities();
        });
        method.addEventListener('change', loadCities);
        loadCities();
    }
    display();
}());
