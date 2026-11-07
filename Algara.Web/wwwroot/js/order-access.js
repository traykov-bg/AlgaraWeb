(function () {
    'use strict';
    const input = document.getElementById('order-access-token');
    if (!input) return;
    input.value = window.location.hash.slice(1);
    history.replaceState(null, '', window.location.pathname);
}());
