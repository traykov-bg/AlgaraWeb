(() => {
    'use strict';
    const form = document.querySelector('[data-payment-form]');
    if (form) {
        const kind = form.querySelector('[name="Kind"]');
        const sync = () => {
            const reversal = kind.value !== '0';
            form.querySelector('[data-reversal]').hidden = !reversal;
            form.querySelector('[name="ReversesN"]').required = reversal;
            form.querySelector('[name="Note"]').required = reversal;
            form.querySelector('[name="MethodN"]').required = kind.value !== '1';
        };
        kind.addEventListener('change', sync);
        sync();
    }
    const statusForm = document.querySelector('[data-order-status-form]');
    if (statusForm) {
        const status = statusForm.querySelector('[name="status"]');
        const notices = Array.from(statusForm.querySelectorAll('[data-status-payment-notice]'));
        const exception = statusForm.querySelector('[data-payment-exception]');
        const reason = statusForm.querySelector('[name="paymentException"]');
        const label = statusForm.querySelector('[data-payment-exception-label]');
        const paymentError = statusForm.querySelector('[data-payment-error-status]');
        const delivery = statusForm.querySelector('[data-delivery-date]');
        const syncStatus = () => {
            const active = notices.find(notice => notice.dataset.statusPaymentNotice === status.value);
            const hasError = paymentError.dataset.paymentErrorStatus === status.value &&
                !!paymentError.querySelector('.field-validation-error');
            notices.forEach(notice => { notice.hidden = notice !== active || hasError; });
            exception.hidden = !active && !hasError;
            paymentError.hidden = !hasError;
            if (reason) {
                reason.disabled = !active;
                if (active) label.textContent = active.dataset.reasonLabel;
            }
            delivery.hidden = status.value !== '3';
            delivery.querySelector('input').disabled = delivery.hidden;
        };
        status.addEventListener('change', syncStatus);
        syncStatus();
    }
    const error = Array.from(document.querySelectorAll('.input-validation-error, .validation-summary-errors, .field-validation-error[tabindex]'))
        .find(element => !element.closest('[hidden]') && !element.disabled);
    if (error) error.focus();
})();
