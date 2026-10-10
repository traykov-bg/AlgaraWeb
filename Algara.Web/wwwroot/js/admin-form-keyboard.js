(function () {
    'use strict';

    const adminContent = document.querySelector('.admin-main');
    if (!adminContent) return;

    const entryTypes = new Set([
        'text', 'search', 'email', 'url', 'tel', 'password', 'number',
        'date', 'datetime-local', 'month', 'week', 'time'
    ]);

    // Edit forms save explicitly. Keep Enter available for multiline text,
    // native controls, focused buttons and the catalog's GET search forms.
    adminContent.addEventListener('keydown', function (event) {
        if (event.key !== 'Enter' || event.isComposing || event.keyCode === 229) return;

        const input = event.target;
        if (input?.tagName !== 'INPUT' || !entryTypes.has(input.type)) return;
        if (input.form?.method.toLowerCase() !== 'post') return;

        event.preventDefault();
    });
})();
