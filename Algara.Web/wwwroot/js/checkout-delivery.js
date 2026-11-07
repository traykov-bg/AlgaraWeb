(function () {
    'use strict';
    const provider = document.getElementById('checkout-provider');
    if (!provider) return;
    const method = document.getElementById('delivery-method');
    const group = document.getElementById('checkout-destination-kind');
    const radios = Array.from(group.querySelectorAll('input[type="radio"]'));
    const initial = Number(method.value);
    const base = initial >= 4 ? 4 : initial >= 2 ? 2 : initial;
    provider.value = String(base);
    if (!provider.value) provider.value = '0';
    radios.forEach(r => r.checked = r.value === (initial === 3 || initial === 5 ? 'address' : 'office'));
    function update(notify) {
        const selected = Number(provider.value);
        group.hidden = selected < 2;
        radios.forEach(r => r.disabled = selected < 2);
        const address = radios.some(r => r.checked && r.value === 'address');
        method.value = String(selected < 2 ? selected : selected + (address ? 1 : 0));
        if (notify) method.dispatchEvent(new Event('change', { bubbles: true }));
    }
    provider.addEventListener('change', () => update(true));
    radios.forEach(r => r.addEventListener('change', () => update(true)));
    update(false);
}());
