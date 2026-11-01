(function () {
    'use strict';

    function initialise(editor) {
        const list = editor.querySelector('[data-parcel-list]');
        const template = editor.querySelector('[data-parcel-template]');
        const useCatalog = editor.querySelector('[data-shipping-use-catalog]');
        const add = editor.querySelector('[data-parcel-add]');
        const maximum = Number(editor.dataset.maxParcels);
        const status = editor.querySelector('[data-parcel-status]');
        const rows = () => Array.from(list.querySelectorAll('[data-parcel-row]'));

        function refresh() {
            const current = rows();
            const reuse = useCatalog.checked && current.length === 1;
            current.forEach((row, index) => {
                row.querySelector('[data-parcel-title]').textContent = 'Колет ' + (index + 1);
                row.querySelector('[data-parcel-remove]').setAttribute('aria-label', 'Премахни колет ' + (index + 1));
                row.querySelectorAll('[data-parcel-field]').forEach(input => {
                    const field = input.dataset.parcelField;
                    input.name = 'ShippingParcels[' + index + '].' + field;
                    input.id = 'ShippingParcels_' + index + '__' + field;
                    const invariant = row.querySelector('[data-parcel-invariant="' + field + '"]');
                    invariant.value = input.name;
                    invariant.disabled = false;
                    row.querySelector('[data-parcel-label="' + field + '"]').htmlFor = input.id;
                    const error = row.querySelector('[data-parcel-error="' + field + '"]');
                    if (error) error.setAttribute('data-valmsg-for', input.name);
                });
                row.querySelectorAll('[data-parcel-dimension]').forEach(input => {
                    input.disabled = reuse;
                    input.required = !reuse;
                    row.querySelector('[data-parcel-invariant="' + input.dataset.parcelField + '"]').disabled = reuse;
                });
                row.querySelector('[data-parcel-catalog-note]').hidden = !reuse;
            });
            add.disabled = current.length >= maximum;
            editor.querySelector('[data-parcel-empty]').hidden = current.length > 0;
            status.textContent = useCatalog.checked && !reuse
                ? 'Потвърждението за каталогови размери изисква точно един колет.'
                : current.length + ' / ' + maximum + ' колета за една бройка.';
        }

        add.addEventListener('click', () => {
            if (rows().length >= maximum) return;
            const row = template.content.firstElementChild.cloneNode(true);
            list.appendChild(row);
            refresh();
            row.querySelector('[data-parcel-field]').focus();
        });
        list.addEventListener('click', event => {
            const button = event.target.closest('[data-parcel-remove]');
            if (!button || !list.contains(button)) return;
            button.closest('[data-parcel-row]').remove();
            refresh();
            add.focus();
        });
        useCatalog.addEventListener('change', refresh);
        refresh();
    }

    document.querySelectorAll('[data-product-shipping]').forEach(initialise);
})();
