// Algara — site.js

(function () {
    'use strict';

    // ════════════════════════════════════════════════════════
    //  Live-search dropdown
    // ════════════════════════════════════════════════════════
    var searchInput    = document.getElementById('algaraSearchInput');
    var searchDropdown = document.getElementById('algaraSearchDropdown');
    var searchForm     = document.getElementById('algaraSearchForm');

    if (!searchInput || !searchDropdown || !searchForm) return;

    var debounceTimer = null;
    var lastQuery     = '';

    // Слушаме въвеждане
    searchInput.addEventListener('input', function () {
        clearTimeout(debounceTimer);
        var q = this.value.trim();

        if (q.length < 2) {
            hideDropdown();
            lastQuery = '';
            return;
        }

        if (q === lastQuery) return; // същото — не повтаряме
        lastQuery = q;

        debounceTimer = setTimeout(function () { fetchResults(q); }, 280);
    });

    // Fetch резултати от /Product/Search
    function fetchResults(q) {
        fetch('/Product/Search?q=' + encodeURIComponent(q))
            .then(function (r) { return r.json(); })
            .then(function (items) { renderDropdown(items, q); })
            .catch(function () { hideDropdown(); });
    }

    // Рендерираме dropdown-а
    function renderDropdown(items, q) {
        if (!items || items.length === 0) {
            hideDropdown();
            return;
        }

        var html = '<ul class="algara-sd-list" role="listbox">';
        items.forEach(function (item) {
            var imgHtml = item.imageUrl
                ? '<img src="' + escHtml(item.imageUrl) + '" class="algara-sd-img" alt="" loading="lazy" />'
                : '<div class="algara-sd-img algara-sd-img--ph"></div>';

            var catHtml = item.category
                ? '<span class="algara-sd-cat">' + escHtml(item.category) + '</span> &middot; '
                : '';

            html += '<li class="algara-sd-item" role="option">'
                +   '<a href="/Product/Detail/' + item.n + '" class="algara-sd-link">'
                +     imgHtml
                +     '<div class="algara-sd-body">'
                +       '<div class="algara-sd-name">' + highlight(item.name, q) + '</div>'
                +       '<div class="algara-sd-meta">' + catHtml + formatPrice(item.price) + ' лв.</div>'
                +     '</div>'
                +   '</a>'
                + '</li>';
        });
        html += '</ul>';

        html += '<div class="algara-sd-footer">'
             +    '<a href="/Product?q=' + encodeURIComponent(q) + '" class="algara-sd-all">'
             +      '<i class="bi bi-search"></i> Виж всички резултати за \u201E' + escHtml(q) + '\u201C'
             +    '</a>'
             +  '</div>';

        searchDropdown.innerHTML = html;
        searchDropdown.classList.add('is-open');
    }

    function hideDropdown() {
        searchDropdown.classList.remove('is-open');
        searchDropdown.innerHTML = '';
        lastQuery = '';
    }

    // Скриване при клик навън
    document.addEventListener('click', function (e) {
        var wrapper = searchInput.closest('.algara-search-wrapper');
        if (wrapper && !wrapper.contains(e.target)) {
            hideDropdown();
        }
    });

    // ESC затваря dropdown
    searchInput.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') hideDropdown();
    });

    // При фокус — показваме пак ако има текст
    searchInput.addEventListener('focus', function () {
        var q = this.value.trim();
        if (q.length >= 2 && searchDropdown.innerHTML === '') {
            fetchResults(q);
        }
    });

    // ── Помощни функции ──────────────────────────────────────

    function escHtml(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Маркира търсения текст в bold
    function highlight(text, q) {
        var safe = escHtml(text);
        var safeQ = escHtml(q).replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
        return safe.replace(new RegExp('(' + safeQ + ')', 'gi'), '<strong>$1</strong>');
    }

    // Форматира цена с интервал за хилядите (напр. 1 250)
    function formatPrice(price) {
        return price.toString().replace(/\B(?=(\d{3})+(?!\d))/g, '\u00A0');
    }

})();
