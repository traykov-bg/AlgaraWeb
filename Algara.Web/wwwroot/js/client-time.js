(function () {
    'use strict';
    function format(value, zone) {
        var date = new Date(value);
        if (!Number.isFinite(date.getTime())) return null;
        var options = { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit', hourCycle: 'h23' };
        if (zone) options.timeZone = zone;
        try {
            var parts = new Intl.DateTimeFormat('en-GB', options).formatToParts(date);
            var get = function (type) { return parts.find(function (p) { return p.type === type; }).value; };
            return get('day') + '.' + get('month') + '.' + get('year') + ' ' + get('hour') + ':' + get('minute');
        } catch (_) { return null; }
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { format: format };
    if (typeof document !== 'undefined') document.querySelectorAll('[data-client-time]').forEach(function (element) {
        var text = format(element.getAttribute('datetime'), element.getAttribute('data-time-zone'));
        if (text) element.textContent = text;
    });
}());
