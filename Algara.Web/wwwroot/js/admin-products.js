(function () {
    'use strict';
    var category = document.getElementById('productCategorySelect');
    var subcategories = document.getElementById('subcategoryCheckboxes');
    var requestNumber = 0;
    function message(text, error) {
        subcategories.replaceChildren();
        var paragraph = document.createElement('p');
        paragraph.className = (error ? 'text-danger' : 'text-muted') + ' small mb-0';
        paragraph.textContent = text;
        subcategories.appendChild(paragraph);
    }
    if (category && subcategories) {
        category.addEventListener('change', async function () {
            var currentRequest = ++requestNumber;
            if (!category.value) {
                message('Изберете категория, за да видите подкатегориите.');
                return;
            }
            message('Зареждане...');
            try {
                var url = category.dataset.subcategoriesUrl.replace('/0/', '/' + encodeURIComponent(category.value) + '/');
                var response = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (!response.ok) throw new Error('Category request failed');
                var rows = await response.json();
                if (currentRequest !== requestNumber) return;
                if (!rows.length) { message('Няма подкатегории за тази категория.'); return; }
                subcategories.replaceChildren();
                rows.forEach(function (row) {
                    var wrapper = document.createElement('div');
                    wrapper.className = 'form-check';
                    var input = document.createElement('input');
                    input.className = 'form-check-input';
                    input.type = 'checkbox';
                    input.name = 'SelectedSubCategoryNs';
                    input.value = row.n;
                    input.id = 'sub_' + row.n;
                    var label = document.createElement('label');
                    label.className = 'form-check-label';
                    label.htmlFor = input.id;
                    label.textContent = row.name;
                    wrapper.append(input, label);
                    subcategories.appendChild(wrapper);
                });
            } catch (_) {
                if (currentRequest === requestNumber) message('Грешка при зареждане на подкатегориите.', true);
            }
        });
    }
})();
