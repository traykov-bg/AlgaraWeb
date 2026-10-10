(function () {
    'use strict';

    function initializePicker(picker, detail, document, browser) {
        var choices = Array.from(picker.querySelectorAll('[data-variant-id]'));
        if (!choices.length) return;
        var fabric = picker.querySelector('[data-variant-fabric]');
        var main = detail.querySelector('[data-gallery-main]');
        var photoNote = detail.querySelector('[data-variant-photo-note]');
        var thumbnails = Array.from(detail.querySelectorAll('[data-gallery-src]'));
        var selected = null;

        function text(selector, value) {
            var element = detail.querySelector(selector);
            if (element) element.textContent = value || '';
        }

        function updatePhotoNote(source) {
            if (!photoNote || !selected) return;
            var exact = selected.getAttribute('data-exact-image') === 'true' && source === selected.getAttribute('data-image');
            photoNote.textContent = picker.getAttribute(!source ? 'data-no-photo-note' : exact ? 'data-exact-photo-note' : 'data-base-photo-note');
        }

        function select(choice, updateUrl) {
            selected = choice;
            choices.forEach(function (candidate) {
                var active = candidate === choice;
                candidate.classList.toggle('is-selected', active);
                candidate.setAttribute('aria-pressed', active ? 'true' : 'false');
            });
            text('[data-variant-price]', choice.getAttribute('data-price'));
            text('[data-variant-original]', choice.getAttribute('data-original'));
            text('[data-variant-name]', choice.getAttribute('data-name'));
            text('[data-variant-status]', choice.getAttribute('data-status'));

            var source = choice.getAttribute('data-image');
            if (main) {
                if (source) main.src = source;
                else main.removeAttribute('src');
                main.hidden = !source;
            }
            thumbnails.forEach(function (thumbnail) {
                var active = thumbnail.getAttribute('data-gallery-src') === source;
                thumbnail.classList.toggle('is-selected', active);
                thumbnail.setAttribute('aria-pressed', active ? 'true' : 'false');
            });
            updatePhotoNote(source);
            if (updateUrl && browser && browser.history && browser.location) {
                var url = new URL(browser.location.href);
                url.searchParams.set('variant', choice.getAttribute('data-variant-id'));
                browser.history.replaceState(null, '', url.pathname + url.search + url.hash);
                document.querySelectorAll('.algara-language-selector input[name="returnUrl"]').forEach(function (input) {
                    input.value = url.pathname + url.search;
                });
            }
        }

        function showFabricColors() {
            choices.forEach(function (choice) {
                var visible = !fabric || choice.getAttribute('data-fabric') === fabric.value;
                choice.closest('[data-variant-option]').hidden = !visible;
            });
        }

        function visibleChoices() {
            return choices.filter(function (choice) { return !choice.closest('[data-variant-option]').hidden; });
        }

        choices.forEach(function (choice) {
            choice.addEventListener('click', function (event) {
                if (event.ctrlKey || event.metaKey || event.shiftKey || event.altKey || event.button > 0) return;
                event.preventDefault();
                select(choice, true);
            });
            choice.addEventListener('keydown', function (event) {
                if (event.key === ' ') {
                    event.preventDefault();
                    select(choice, true);
                    return;
                }
                var forward = event.key === 'ArrowRight' || event.key === 'ArrowDown';
                var backward = event.key === 'ArrowLeft' || event.key === 'ArrowUp';
                if (!forward && !backward && event.key !== 'Home' && event.key !== 'End') return;
                event.preventDefault();
                var visible = visibleChoices();
                var index = visible.indexOf(choice);
                var target = event.key === 'Home' ? visible[0] : event.key === 'End' ? visible[visible.length - 1] :
                    visible[(index + (forward ? 1 : -1) + visible.length) % visible.length];
                if (target) { target.focus(); select(target, true); }
            });
        });

        if (fabric) fabric.addEventListener('change', function () {
            showFabricColors();
            var visible = visibleChoices();
            if (visible.length) select(visible[0], true);
        });

        thumbnails.forEach(function (thumbnail) {
            thumbnail.addEventListener('click', function () { updatePhotoNote(thumbnail.getAttribute('data-gallery-src')); });
        });

        var dialog = picker.querySelector('[data-swatch-dialog]');
        if (dialog && typeof dialog.showModal === 'function') {
            var dialogTitle = dialog.querySelector('[data-swatch-dialog-title]');
            var sample = dialog.querySelector('[data-swatch-dialog-sample]');
            var image = dialog.querySelector('[data-swatch-dialog-image]');
            picker.querySelectorAll('[data-swatch-zoom]').forEach(function (button) {
                button.hidden = false;
                button.addEventListener('click', function () {
                    var source = button.getAttribute('data-swatch-image');
                    dialogTitle.textContent = button.getAttribute('data-swatch-title');
                    sample.style.backgroundColor = button.getAttribute('data-swatch-hex');
                    image.hidden = !source;
                    if (source) image.src = source;
                    else image.removeAttribute('src');
                    dialog.showModal();
                });
            });
            dialog.querySelector('[data-swatch-close]').addEventListener('click', function () { dialog.close(); });
        }
        var filters = picker.querySelector('[data-variant-filters]');
        if (filters) filters.hidden = false;
        var initial = choices.find(function (choice) { return choice.getAttribute('data-variant-id') === picker.getAttribute('data-initial-variant'); }) || choices[0];
        if (fabric) fabric.value = initial.getAttribute('data-fabric');
        showFabricColors();
        select(initial, false);
    }

    if (typeof module !== 'undefined' && module.exports) module.exports = { initializePicker: initializePicker };
    if (typeof document !== 'undefined') {
        document.querySelectorAll('[data-variant-picker]').forEach(function (picker) {
            var detail = picker.closest('[data-product-detail]');
            if (detail) initializePicker(picker, detail, document, window);
        });
    }
})();
