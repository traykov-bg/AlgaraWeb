(function () {
    'use strict';
    var form = document.querySelector('[data-account-email-token]');
    if (!form) return;
    var input = form.querySelector('input[name="Token"]');
    var pattern = /^[0-9a-f]{32}\.[A-Za-z0-9_-]{42}[AEIMQUYcgkosw048]$/;
    // A URL fragment is never part of the HTTP request. Clear it before any form submission.
    var fragment = window.location.hash.slice(1);
    if (window.location.hash) {
        window.history.replaceState(null, '', window.location.pathname + window.location.search);
        input.value = pattern.test(fragment) ? fragment : '';
    }
    if (!pattern.test(input.value)) {
        input.value = '';
        var message = document.querySelector('[data-token-missing]');
        if (message) message.hidden = false;
        form.querySelectorAll('button[type="submit"]').forEach(function (button) { button.disabled = true; });
    }
})();
