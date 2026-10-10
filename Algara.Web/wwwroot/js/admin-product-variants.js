(function () {
    'use strict';
    document.querySelectorAll('[data-product-variants-editor]').forEach(function (editor) {
        var form = editor.closest('form');
        var state = JSON.parse(editor.querySelector('[data-variant-editor-data]').textContent);
        var kind = editor.querySelector('[data-option-kind]');
        var body = editor.querySelector('[data-variant-rows]');
        var groups = editor.querySelector('[data-fabric-groups]');
        var choice = editor.querySelector('[data-variant-choice]');
        var search = editor.querySelector('[data-variant-search]');
        var status = editor.querySelector('[data-variant-status]');
        var colors = new Map(state.colors.map(function (c) { return [c.n, c]; }));
        var fabrics = new Map(state.fabrics.map(function (f) { return [f.n, f]; }));
        var fees = new Map(state.surcharges.map(function (s) { return [s.fabricN, s.surcharge]; }));
        var selectedFabrics = new Set(fees.keys());
        var removedFabrics = new Set();
        var rows = state.variants;
        rows.forEach(function (row) {
            var color = colors.get(row.catalogColorN);
            if (row.isActive && color && color.fabricN) selectedFabrics.add(color.fabricN);
        });
        function element(tag, text, cls) {
            var node = document.createElement(tag);
            if (text !== undefined) node.textContent = text;
            if (cls) node.className = cls;
            return node;
        }
        function option(value, text) { var node = element('option', text); node.value = value; return node; }
        function labelFor(row) {
            var c = colors.get(row.catalogColorN);
            return c ? (c.fabricName ? c.fabricName + ' / ' : '') + c.name : 'Липсващ цвят';
        }
        function isAvailable(row) {
            var c = colors.get(row.catalogColorN);
            return c && c.isActive && c.fabricActive;
        }
        function newRow(id, active) {
            return { n: 0, catalogColorN: id, sku: '', stockQuantity: null, surcharge: 0, isActive: active, isDefault: false };
        }
        function ensureFabricRows(id, active) {
            state.colors.forEach(function (c) {
                if (c.fabricN === id && c.isActive && !rows.some(function (r) { return r.catalogColorN === c.n; })) rows.push(newRow(c.n, active));
            });
            if (!fees.has(id)) fees.set(id, 0);
        }
        selectedFabrics.forEach(function (id) { ensureFabricRows(id, false); });
        function visibleRows() {
            if (kind.value === '0') return [];
            return rows.filter(function (row) {
                var c = colors.get(row.catalogColorN);
                return c && (kind.value === '2' ? c.fabricN != null && selectedFabrics.has(c.fabricN) : c.fabricN == null);
            });
        }
        function amountFor(row) {
            var color = colors.get(row.catalogColorN);
            return color && color.fabricN ? fees.get(color.fabricN) : row.surcharge;
        }
        function eligibleDefault(row) { return row.isActive && isAvailable(row) && amountFor(row) === 0; }
        function repairDefault() {
            var visible = visibleRows();
            var keep = visible.find(function (r) { return r.isDefault && eligibleDefault(r); }) || visible.find(eligibleDefault);
            rows.forEach(function (r) { r.isDefault = r === keep; });
            return keep;
        }
        function syncDefaultFields() {
            editor.querySelectorAll('[data-variant-row]').forEach(function (tr) {
                var row = rows.find(function (r) { return r.catalogColorN === Number(tr.getAttribute('data-variant-row')); });
                tr.querySelector('[name="variantDefaultChoice"]').checked = row.isDefault;
                tr.querySelector('[data-variant-default-value]').value = row.isActive && row.isDefault ? 'true' : 'false';
            });
        }
        function describeDefault() {
            var message = 'Изберете поне един включен вариант с надценка 0 €, за да има основен вариант.';
            if (kind.value !== '0' && !visibleRows().some(function (r) { return r.isDefault && eligibleDefault(r); })) {
                status.textContent = message;
                return false;
            }
            if (status.textContent === message) status.textContent = '';
            return true;
        }
        function photoChoices() {
            return Array.from(form.querySelectorAll('[data-gallery-item]')).map(function (card, i) {
                var id = card.querySelector('[data-image-field="N"]').value;
                var upload = card.querySelector('[data-image-field="UploadId"]').value;
                return { value: Number(id) > 0 ? 'saved:' + id : 'draft:' + upload, text: 'Снимка ' + (i + 1) };
            });
        }
        function photoValue(row) { return row.productImageN ? 'saved:' + row.productImageN : row.productImageUploadId ? 'draft:' + row.productImageUploadId : ''; }
        function setPhoto(row, value) {
            row.productImageN = value.startsWith('saved:') ? Number(value.slice(6)) : null;
            row.productImageUploadId = value.startsWith('draft:') ? value.slice(6) : null;
        }
        function hidden(parent, name, value) {
            var input = element('input'); input.type = 'hidden'; input.name = name; input.value = value == null ? '' : value;
            parent.appendChild(input); return input;
        }
        function inputCell(tr, row, i, prop, type, title, options) {
            var td = element('td'); var input = element('input', undefined, 'form-control form-control-sm');
            input.type = type; input.name = 'Variants[' + i + '].' + prop;
            var key = prop.charAt(0).toLowerCase() + prop.slice(1);
            input.value = row[key] == null ? '' : row[key];
            input.setAttribute('aria-label', title + ': ' + labelFor(row));
            Object.keys(options || {}).forEach(function (key) { input.setAttribute(key, options[key]); });
            input.addEventListener('input', function () { row[key] = type === 'number' ? (input.value === '' ? null : Number(input.value)) : input.value; });
            td.appendChild(input); tr.appendChild(td); return input;
        }
        function refreshChoices() {
            var needle = search.value.trim().toLocaleLowerCase();
            var used = new Set(rows.map(function (r) { return r.catalogColorN; }));
            var isFabric = kind.value === '2';
            var candidates = isFabric ? state.fabrics.filter(function (f) {
                return f.isActive && !selectedFabrics.has(f.n) && [f.name, f.code].join(' ').toLocaleLowerCase().includes(needle) &&
                    state.colors.some(function (c) { return c.fabricN === f.n && c.isActive; });
            }) : state.colors.filter(function (c) {
                return c.isActive && c.fabricN == null && !used.has(c.n) && [c.name, c.code].join(' ').toLocaleLowerCase().includes(needle);
            });
            choice.replaceChildren(option('', candidates.length ? 'Изберете от картотеката' : (isFabric ? 'Няма други дамаски с активни цветове' : 'Няма съвпадащи нови цветове')));
            candidates.forEach(function (c) { choice.appendChild(option(c.n, c.name)); });
            editor.querySelector('[data-variant-search-label]').textContent = isFabric ? 'Търси дамаска' : 'Търси цвят';
            editor.querySelector('[data-variant-choice-label]').textContent = isFabric ? 'Добави дамаска с нейните цветове' : 'Добави цвят';
        }
        function drawRow(target, row, i, photos, fabricMode) {
            var tr = element('tr'); tr.setAttribute('data-variant-row', row.catalogColorN); if (!row.isActive) tr.className = 'text-muted';
            var activeCell = element('td');
            hidden(activeCell, 'Variants[' + i + '].N', row.n || 0); hidden(activeCell, 'Variants[' + i + '].CatalogColorN', row.catalogColorN);
            var active = element('input', undefined, 'form-check-input'); active.type = 'checkbox'; active.checked = row.isActive;
            active.name = 'Variants[' + i + '].IsActive'; active.value = 'true'; active.setAttribute('aria-label', 'Включен: ' + labelFor(row));
            active.disabled = !isAvailable(row) && !row.isActive;
            activeCell.appendChild(active); hidden(activeCell, active.name, 'false'); tr.appendChild(activeCell);
            if (!row.n && !fabricMode) {
                var remove = element('button', '×', 'btn btn-sm btn-outline-danger ms-2'); remove.type = 'button';
                remove.setAttribute('data-remove-new-variant', ''); remove.setAttribute('aria-label', 'Премахни новия вариант: ' + labelFor(row));
                remove.addEventListener('click', function () { rows = rows.filter(function (r) { return r !== row; }); repairDefault(); drawRows(); describeDefault(); });
                activeCell.appendChild(remove);
            }
            active.addEventListener('change', function () { row.isActive = active.checked; repairDefault(); drawRows(); describeDefault(); });
            var defaultCell = element('td'); var radio = element('input', undefined, 'form-check-input'); radio.type = 'radio'; radio.name = 'variantDefaultChoice';
            radio.value = row.catalogColorN; radio.checked = row.isDefault; radio.disabled = !row.isActive; radio.setAttribute('aria-label', 'Основен: ' + labelFor(row));
            radio.addEventListener('change', function () { rows.forEach(function (r) { r.isDefault = r === row; }); drawRows(); describeDefault(); });
            defaultCell.appendChild(radio); hidden(defaultCell, 'Variants[' + i + '].IsDefault', row.isActive && row.isDefault ? 'true' : 'false').setAttribute('data-variant-default-value', ''); tr.appendChild(defaultCell);
            var color = colors.get(row.catalogColorN);
            var nameCell = element('td', color ? color.name : 'Липсващ цвят');
            if (!isAvailable(row)) nameCell.appendChild(element('small', 'Неактивен в картотеката', 'd-block text-muted'));
            tr.appendChild(nameCell);
            inputCell(tr, row, i, 'Sku', 'text', 'Артикулен код', { maxlength: '64', placeholder: 'Автоматично' });
            inputCell(tr, row, i, 'StockQuantity', 'number', 'Налични бройки', { min: '0', max: '1000000', step: '1', placeholder: 'Невъведена' });
            if (fabricMode) {
                row.surcharge = 0; hidden(activeCell, 'Variants[' + i + '].Surcharge', 0);
            } else {
                var surcharge = inputCell(tr, row, i, 'Surcharge', 'number', 'Надценка', { min: '0', max: '9999999', step: '0.01', required: '' });
                hidden(surcharge.parentNode, '__Invariant', surcharge.name);
                surcharge.addEventListener('change', function () { repairDefault(); syncDefaultFields(); describeDefault(); });
            }
            var imageCell = element('td'); var select = element('select', undefined, 'form-select form-select-sm'); select.setAttribute('aria-label', 'Снимка: ' + labelFor(row));
            select.appendChild(option('', 'Основна снимка на продукта'));
            photos.forEach(function (p) { select.appendChild(option(p.value, p.text)); });
            select.value = photoValue(row); if (!select.value) setPhoto(row, '');
            var imageId = hidden(imageCell, 'Variants[' + i + '].ProductImageN', row.productImageN);
            var uploadId = hidden(imageCell, 'Variants[' + i + '].ProductImageUploadId', row.productImageUploadId);
            select.addEventListener('change', function () { setPhoto(row, select.value); imageId.value = row.productImageN || ''; uploadId.value = row.productImageUploadId || ''; });
            imageCell.appendChild(select); tr.appendChild(imageCell); target.appendChild(tr);
        }
        function drawFabric(id, index) {
            var fabric = fabrics.get(id);
            var group = element('section', undefined, 'algara-fabric-editor-group'); group.setAttribute('data-fabric-group', id);
            var summary = element('div', undefined, 'algara-fabric-editor-summary');
            var imageWrap = element('div', undefined, 'algara-fabric-editor-image');
            if (fabric && fabric.imageUrl) {
                var img = element('img'); img.src = fabric.imageUrl; img.alt = fabric.name; img.loading = 'lazy'; imageWrap.appendChild(img);
            } else imageWrap.appendChild(element('span', 'Няма снимка на дамаската', 'small text-muted text-center p-3'));
            var remove = element('button', '×', 'algara-fabric-editor-remove'); remove.type = 'button';
            remove.setAttribute('data-remove-fabric', id); remove.setAttribute('aria-label', 'Премахни дамаската: ' + (fabric ? fabric.name : id));
            remove.addEventListener('click', function () {
                selectedFabrics.delete(id); removedFabrics.add(id);
                if (selectedFabrics.size === 0) kind.value = '0';
                repairDefault(); updateMode();
                status.textContent = kind.value === '0' ? 'Всички дамаски са премахнати. Продуктът е без варианти.' : 'Дамаската е премахната от редакцията. Можете да я добавите отново преди запис.'; describeDefault();
            });
            imageWrap.appendChild(remove); summary.appendChild(imageWrap);
            summary.appendChild(element('h3', fabric ? fabric.name : 'Липсваща дамаска', 'h6 mt-3'));
            if (fabric && !fabric.isActive) summary.appendChild(element('p', 'Неактивна в картотеката', 'text-muted small'));
            var label = element('label', 'Надценка (€)', 'form-label small'); label.htmlFor = 'fabricSurcharge' + id;
            var input = element('input', undefined, 'form-control form-control-sm'); input.type = 'number'; input.step = '0.01'; input.min = '0'; input.max = '9999999';
            input.id = label.htmlFor; input.name = 'FabricSurcharges[' + index + '].Surcharge'; input.value = fees.get(id); input.required = true;
            hidden(summary, 'FabricSurcharges[' + index + '].FabricN', id); hidden(summary, '__Invariant', input.name);
            input.addEventListener('input', function () { fees.set(id, input.value === '' ? null : Number(input.value)); });
            input.addEventListener('change', function () { repairDefault(); syncDefaultFields(); describeDefault(); });
            summary.append(label, input); group.appendChild(summary);
            var wrap = element('div', undefined, 'table-responsive');
            var table = element('table', undefined, 'table table-sm align-middle algara-variant-editor-table');
            var head = element('thead'); var headings = element('tr');
            ['Включен', 'Основен', 'Цвят', 'Артикулен код', 'Бройки', 'Снимка от галерията'].forEach(function (text) { headings.appendChild(element('th', text)); });
            head.appendChild(headings); table.appendChild(head);
            var groupBody = element('tbody'); table.appendChild(groupBody); wrap.appendChild(table); group.appendChild(wrap); groups.appendChild(group);
            return groupBody;
        }
        function drawRows() {
            body.replaceChildren(); groups.replaceChildren();
            var photos = photoChoices(); var visible = visibleRows(); var index = 0;
            if (kind.value === '2') {
                var fabricIndex = 0;
                selectedFabrics.forEach(function (id) {
                    var target = drawFabric(id, fabricIndex++);
                    visible.filter(function (r) { return colors.get(r.catalogColorN).fabricN === id; }).forEach(function (row) { drawRow(target, row, index++, photos, true); });
                });
            } else visible.forEach(function (row) { drawRow(body, row, index++, photos, false); });
            editor.querySelector('[data-variants-empty]').hidden = kind.value === '0' || visible.length > 0;
            editor.querySelector('[data-variant-table-wrap]').hidden = kind.value !== '1' || visible.length === 0;
            groups.hidden = kind.value !== '2'; refreshChoices();
        }
        function updateMode() {
            editor.querySelector('[data-simple-stock]').hidden = kind.value !== '0';
            editor.querySelector('[data-option-controls]').hidden = kind.value === '0';
            drawRows();
        }
        kind.addEventListener('change', function () {
            rows.forEach(function (row) {
                var c = colors.get(row.catalogColorN);
                if (kind.value === '0' || !c || (kind.value === '2') !== (c.fabricN != null)) { row.isActive = false; row.isDefault = false; }
            });
            repairDefault(); updateMode(); status.textContent = 'Проверете включените варианти и основния вариант преди запис.'; describeDefault();
        });
        search.addEventListener('input', refreshChoices);
        editor.querySelector('[data-add-variant]').addEventListener('click', function () {
            var id = Number(choice.value); if (!id) return;
            if (kind.value === '2') {
                if (selectedFabrics.has(id)) return;
                var additional = state.colors.filter(function (c) { return c.fabricN === id && c.isActive && !rows.some(function (r) { return r.catalogColorN === c.n; }); }).length;
                var restoring = rows.filter(function (r) { return colors.get(r.catalogColorN)?.fabricN === id; }).length;
                if (visibleRows().length + restoring + additional > 500) { status.textContent = 'Достигнат е лимитът от 500 варианта.'; return; }
                if (!removedFabrics.has(id)) rows.forEach(function (r) { if (colors.get(r.catalogColorN)?.fabricN === id) r.isActive = Boolean(isAvailable(r)); });
                selectedFabrics.add(id); ensureFabricRows(id, true);
                status.textContent = 'Дамаската и цветовете ѝ са добавени. Попълнете надценката и наличностите.';
            } else {
                if (rows.some(function (r) { return r.catalogColorN === id; })) return;
                if (visibleRows().length >= 500) { status.textContent = 'Достигнат е лимитът от 500 варианта.'; return; }
                rows.push(newRow(id, true)); status.textContent = 'Цветът е добавен. Попълнете наличността и надценката.';
            }
            repairDefault(); drawRows(); describeDefault();
        });
        var gallery = form.querySelector('[data-gallery-list]');
        if (gallery) new MutationObserver(function () { drawRows(); }).observe(gallery, { childList: true, subtree: true });
        updateMode();
    });
})();
