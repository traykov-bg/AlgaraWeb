(() => {
    'use strict';
    document.querySelectorAll('[data-admin-save-notice]').forEach(notice => {
        const close = () => notice.remove();
        let timer;
        const pause = () => window.clearTimeout(timer);
        const resume = () => { pause(); timer = window.setTimeout(close, 7000); };
        notice.querySelector('[data-save-notice-close]').addEventListener('click', close);
        notice.addEventListener('mouseenter', pause);
        notice.addEventListener('mouseleave', resume);
        notice.addEventListener('focusin', pause);
        notice.addEventListener('focusout', resume);
        resume();
    });
})();
