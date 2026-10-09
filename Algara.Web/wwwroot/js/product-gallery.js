(function () {
    'use strict';

    document.querySelectorAll('[data-product-gallery]').forEach(function (gallery) {
        var main = gallery.querySelector('[data-gallery-main]');
        var buttons = gallery.querySelectorAll('[data-gallery-src]');
        if (!main) return;

        buttons.forEach(function (button) {
            button.addEventListener('click', function () {
                var source = button.getAttribute('data-gallery-src');
                if (!source) return;
                main.src = source;
                buttons.forEach(function (candidate) {
                    var selected = candidate === button;
                    candidate.classList.toggle('is-selected', selected);
                    candidate.setAttribute('aria-pressed', selected ? 'true' : 'false');
                });
            });
        });
    });
})();
