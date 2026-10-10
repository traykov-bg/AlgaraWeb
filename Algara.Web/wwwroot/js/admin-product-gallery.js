(function () {
    'use strict';

    document.querySelectorAll('[data-product-gallery]').forEach(function (gallery) {
        var form = gallery.closest('form');
        var list = gallery.querySelector('[data-gallery-list]');
        var files = gallery.querySelector('[data-product-image-input]');
        var status = gallery.querySelector('[data-gallery-status]');
        var error = gallery.querySelector('[data-gallery-error]');
        var empty = gallery.querySelector('[data-gallery-empty]');
        var count = gallery.querySelector('[data-gallery-count]');
        if (!form || !list || !files) return;

        var busy = false;
        var dragged = null;
        var submitStates = [];
        function items() { return Array.from(list.querySelectorAll('[data-gallery-item]')); }
        function field(item, name) { return item.querySelector('[data-image-field="' + name + '"]'); }
        function showError(message) {
            error.textContent = message || '';
            error.hidden = !message;
        }
        function refresh() {
            var cards = items();
            cards.forEach(function (card, index) {
                ['N', 'UploadId', 'DisplayOrder'].forEach(function (name) {
                    field(card, name).name = 'Images[' + index + '].' + name;
                });
                field(card, 'DisplayOrder').value = index;
                card.classList.toggle('is-main', index === 0);
                card.draggable = !busy;
                card.setAttribute('aria-label', 'Снимка ' + (index + 1) + (index === 0 ? ', основна' : ''));
                card.querySelector('img').alt = 'Снимка ' + (index + 1);
                card.querySelector('[data-image-position]').textContent = index + 1;
                var remove = card.querySelector('[data-image-delete]');
                remove.disabled = busy;
                remove.setAttribute('aria-label', 'Изтрий снимка ' + (index + 1));
                remove.title = 'Изтрий снимка ' + (index + 1);
                card.querySelectorAll('[data-image-move]').forEach(function (button) {
                    var direction = Number(button.dataset.imageMove);
                    button.disabled = busy || index + direction < 0 || index + direction >= cards.length;
                    button.setAttribute('aria-label', 'Премести снимка ' + (index + 1) + (direction < 0 ? ' наляво' : ' надясно'));
                });
            });
            count.textContent = cards.length + ' / 20';
            empty.hidden = cards.length !== 0;
            files.disabled = busy || cards.length >= 20;
        }
        function setBusy(value, message) {
            busy = value;
            gallery.setAttribute('aria-busy', String(value));
            if (value) {
                submitStates = Array.from(form.querySelectorAll('[type="submit"]')).map(function (button) {
                    var state = { button: button, disabled: button.disabled };
                    button.disabled = true;
                    return state;
                });
            } else {
                submitStates.forEach(function (state) { state.button.disabled = state.disabled; });
                submitStates = [];
            }
            status.textContent = message || '';
            refresh();
        }
        function requestData() {
            var data = new FormData();
            var token = form.querySelector('input[name="__RequestVerificationToken"]');
            data.append('__RequestVerificationToken', token ? token.value : '');
            data.append('productN', gallery.dataset.productN || '0');
            data.append('draftId', gallery.dataset.draftId || '');
            var version = form.querySelector('input[name="RowVersion"]');
            data.append('rowVersion', version ? version.value : '');
            return data;
        }
        async function request(url, data) {
            var response = await fetch(url, {
                method: 'POST', body: data, credentials: 'same-origin',
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'application/json' }
            });
            if (response.redirected || response.status === 401 || response.status === 403) {
                throw new Error('Сесията е изтекла или нямате достъп. Влезте отново, преди да променяте снимките.');
            }
            var type = response.headers.get('content-type') || '';
            if (!type.includes('application/json')) {
                throw new Error('Промяната не бе потвърдена. Проверете връзката и опитайте отново.');
            }
            var result = await response.json();
            if (!response.ok) {
                throw new Error(result && typeof result.error === 'string' ? result.error : 'Снимките не бяха променени. Опитайте отново.');
            }
            var version = form.querySelector('input[name="RowVersion"]');
            if (version && result && typeof result.rowVersion === 'string') version.value = result.rowVersion;
            return result;
        }
        function button(className, text, attribute, value) {
            var node = document.createElement('button');
            node.type = 'button';
            node.className = className;
            node.textContent = text;
            node.setAttribute(attribute, value || '');
            return node;
        }
        function createCard(row) {
            var card = document.createElement('li');
            card.className = 'algara-image-tile';
            card.setAttribute('data-gallery-item', '');
            card.tabIndex = 0;
            ['N', 'UploadId', 'DisplayOrder'].forEach(function (name) {
                var input = document.createElement('input');
                input.type = 'hidden';
                input.setAttribute('data-image-field', name);
                input.value = name === 'N' ? row.n || 0 : name === 'UploadId' ? row.uploadId || '' : 0;
                card.appendChild(input);
            });
            var image = document.createElement('img');
            image.src = row.imageUrl;
            image.draggable = false;
            var main = document.createElement('span');
            main.className = 'algara-image-main';
            main.setAttribute('data-image-main', '');
            main.textContent = 'Основна';
            var remove = button('algara-image-delete', '×', 'data-image-delete');
            var controls = document.createElement('div');
            controls.className = 'algara-image-moves';
            var previous = button('', '←', 'data-image-move', '-1');
            previous.title = 'Премести наляво';
            var position = document.createElement('span');
            position.className = 'algara-image-position';
            position.setAttribute('data-image-position', '');
            var next = button('', '→', 'data-image-move', '1');
            next.title = 'Премести надясно';
            controls.append(previous, position, next);
            card.append(image, main, remove, controls);
            return card;
        }
        function move(card, targetIndex) {
            var cards = items();
            var oldIndex = cards.indexOf(card);
            if (busy || oldIndex < 0 || targetIndex < 0 || targetIndex >= cards.length || oldIndex === targetIndex) return;
            if (targetIndex < oldIndex) list.insertBefore(card, cards[targetIndex]);
            else list.insertBefore(card, cards[targetIndex].nextSibling);
            refresh();
            card.focus({ preventScroll: true });
            status.textContent = 'Снимката е преместена на позиция ' + (targetIndex + 1) + '. Запазете подредбата с „Запиши“.';
        }
        function clearDrag() {
            items().forEach(function (card) { card.classList.remove('is-dragging', 'is-drop-target'); });
            dragged = null;
        }

        files.addEventListener('change', async function () {
            if (busy) return;
            var selected = Array.from(files.files || []);
            if (!selected.length) return;
            showError('');
            if (items().length + selected.length > 20) {
                showError('Галерията може да съдържа до 20 снимки. Изберете по-малко файлове.');
                files.value = '';
                return;
            }
            if (selected.some(function (file) { return file.size > 5 * 1024 * 1024; })) {
                showError('Всяка снимка трябва да е до 5 MB. Изберете по-малки файлове.');
                files.value = '';
                return;
            }
            var data = requestData();
            selected.forEach(function (file) { data.append('files', file); });
            setBusy(true, 'Качване на ' + selected.length + (selected.length === 1 ? ' снимка…' : ' снимки…'));
            var completed = false;
            try {
                var result = await request(gallery.dataset.uploadUrl, data);
                if (!result || !Array.isArray(result.images) || result.images.some(function (row) {
                    return !row || typeof row.imageUrl !== 'string' || (!row.n && !row.uploadId);
                })) throw new Error('Качването не бе потвърдено. Презаредете страницата, за да проверите галерията.');
                result.images.forEach(function (row) { list.appendChild(createCard(row)); });
                completed = true;
                showError('');
            } catch (failure) {
                showError(failure instanceof TypeError ? 'Няма връзка със сървъра. Проверете връзката и опитайте отново.' : failure.message);
            } finally {
                files.value = '';
                setBusy(false, completed ? 'Снимките са качени. Първата вляво е основна; запазете подредбата с „Запиши“.' : '');
            }
        });

        list.addEventListener('click', async function (event) {
            var target = event.target.closest('button');
            if (!target || !list.contains(target) || busy) return;
            var card = target.closest('[data-gallery-item]');
            if (!card) return;
            if (target.hasAttribute('data-image-move')) {
                move(card, items().indexOf(card) + Number(target.dataset.imageMove));
                return;
            }
            if (!target.hasAttribute('data-image-delete')) return;
            showError('');
            var data = requestData();
            data.append('imageN', field(card, 'N').value);
            data.append('uploadId', field(card, 'UploadId').value);
            var oldIndex = items().indexOf(card);
            setBusy(true, 'Изтриване на снимката…');
            var completed = false;
            try {
                await request(gallery.dataset.deleteUrl, data);
                card.remove();
                completed = true;
                showError('');
            } catch (failure) {
                showError(failure instanceof TypeError ? 'Няма връзка със сървъра. Снимката остава в галерията; опитайте отново.' : failure.message);
            } finally {
                setBusy(false, completed ? 'Снимката е изтрита.' : '');
                if (completed) {
                    var nextCard = items()[Math.min(oldIndex, items().length - 1)];
                    if (nextCard) nextCard.focus({ preventScroll: true });
                    else files.focus({ preventScroll: true });
                }
            }
        });
        list.addEventListener('keydown', function (event) {
            var card = event.target.closest('[data-gallery-item]');
            if (busy || event.target !== card || !['ArrowLeft', 'ArrowRight'].includes(event.key)) return;
            event.preventDefault();
            move(card, items().indexOf(card) + (event.key === 'ArrowLeft' ? -1 : 1));
        });
        list.addEventListener('dragstart', function (event) {
            var card = event.target.closest('[data-gallery-item]');
            if (busy || !card || event.target.closest('button')) { event.preventDefault(); return; }
            dragged = card;
            card.classList.add('is-dragging');
            event.dataTransfer.effectAllowed = 'move';
            event.dataTransfer.setData('text/plain', 'product-gallery-image');
        });
        list.addEventListener('dragover', function (event) {
            var card = event.target.closest('[data-gallery-item]');
            if (!dragged || busy || !card) return;
            event.preventDefault();
            event.dataTransfer.dropEffect = 'move';
            items().forEach(function (item) { item.classList.toggle('is-drop-target', item === card && item !== dragged); });
        });
        list.addEventListener('drop', function (event) {
            var card = event.target.closest('[data-gallery-item]');
            if (!dragged || busy || !card) return;
            event.preventDefault();
            move(dragged, items().indexOf(card));
            clearDrag();
        });
        list.addEventListener('dragend', clearDrag);
        form.addEventListener('submit', function (event) {
            if (busy) {
                event.preventDefault();
                showError('Изчакайте качването или изтриването на снимките да завърши, след това натиснете „Запиши“.');
                return;
            }
            refresh();
        });
        window.addEventListener('beforeunload', function (event) {
            if (!busy) return;
            event.preventDefault();
            event.returnValue = '';
        });
        refresh();
    });
})();
