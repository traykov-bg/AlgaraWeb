(function () {
    'use strict';
    var banner = document.getElementById('customer-assistance-banner');
    if (!banner) return;
    var remaining = Number(banner.dataset.remainingSeconds);
    if (!Number.isFinite(remaining)) remaining = 0;
    var deadline = Date.now() + Math.max(0, remaining) * 1000;
    var countdown = document.getElementById('assistance-countdown');
    var content = document.getElementById('assistance-content');
    var expired = document.getElementById('assistance-expired');

    function update() {
        var seconds = Math.max(0, Math.ceil((deadline - Date.now()) / 1000));
        if (countdown) countdown.textContent = String(Math.floor(seconds / 60)) + ':' + String(seconds % 60).padStart(2, '0');
        if (seconds > 0) return;
        if (content) {
            content.querySelectorAll('input, select, textarea, button').forEach(function (field) { field.disabled = true; });
            content.hidden = true;
        }
        if (expired) expired.hidden = false;
        if (window.bootstrap) {
            document.querySelectorAll('.modal.show').forEach(function (modal) {
                var instance = window.bootstrap.Modal.getInstance(modal);
                if (instance) instance.hide();
            });
        }
        window.clearInterval(timer);
    }

    var timer = window.setInterval(update, 1000);
    update();
    window.addEventListener('pageshow', function (event) {
        if (event.persisted) window.location.reload();
    });
})();
