(function () {
    // ── Утилити ──────────────────────────────────────────────
    function parseNum(v) {
        if (v === null || v === undefined || v === '') return 0;
        var n = parseFloat(String(v).replace(',', '.'));
        return isNaN(n) ? 0 : n;
    }
    // Match decimal midpoint rounding on the server; compensate for binary
    // representation just below a half-cent (for example 999.95 less 10%).
    function round(n, digits) {
        var factor = Math.pow(10, digits);
        var scaled = Math.abs(n * factor);
        return Math.sign(n) * Math.round(scaled + Number.EPSILON * scaled) / factor;
    }
    function round2(n) { return round(n, 2); }
    function round3(n) { return round(n, 3); }

    function getMode() {
        return document.getElementById('typeAmount').checked ? 'amount' : 'percent';
    }

    // ── Двупосочно преизчисление при писане в един ред ───────
    function recalcFromPercent(tr) {
        var orig = parseNum(tr.querySelector('.row-original').value);
        var pct  = parseNum(tr.querySelector('.row-percent').value);
        var promo = round2(orig * (1 - pct / 100));
        if (promo < 0) promo = 0;
        tr.querySelector('.row-promo').value  = promo.toFixed(2);
        tr.querySelector('.row-amount').value = round2(orig - promo).toFixed(2);
    }
    function recalcFromPromo(tr) {
        var orig  = parseNum(tr.querySelector('.row-original').value);
        var promo = parseNum(tr.querySelector('.row-promo').value);
        var amt   = round2(orig - promo);
        var pct   = orig > 0 ? round3((amt / orig) * 100) : 0;
        tr.querySelector('.row-amount').value  = amt.toFixed(2);
        tr.querySelector('.row-percent').value = pct.toFixed(3).replace(/\.?0+$/, '') || '0';
    }
    function recalcFromAmount(tr) {
        var orig = parseNum(tr.querySelector('.row-original').value);
        var amt  = parseNum(tr.querySelector('.row-amount').value);
        var promo = round2(orig - amt);
        if (promo < 0) promo = 0;
        var pct = orig > 0 ? round3((amt / orig) * 100) : 0;
        tr.querySelector('.row-promo').value   = promo.toFixed(2);
        tr.querySelector('.row-percent').value = pct.toFixed(3).replace(/\.?0+$/, '') || '0';
    }
    function recalcFromOriginal(tr) {
        // Original се смени → преизчислява според активния режим
        if (getMode() === 'percent') recalcFromPercent(tr);
        else                          recalcFromAmount(tr);
    }

    // ── UI състояние според режима ───────────────────────────
    function applyModeToUi() {
        var mode = getMode();
        document.getElementById('bulkUnit').textContent = (mode === 'percent' ? '%' : '€');
        var bulkInput = document.getElementById('bulkValue');
        bulkInput.step = mode === 'percent' ? '0.001' : '0.01';
        if (mode === 'percent') bulkInput.max = '99.999';
        else bulkInput.removeAttribute('max');
        document.querySelectorAll('#promoTable tbody tr').forEach(function (tr) {
            var pctIn = tr.querySelector('.row-percent');
            var promoIn = tr.querySelector('.row-promo');
            var amtIn = tr.querySelector('.row-amount');

            if (mode === 'percent') {
                pctIn.readOnly   = false;
                promoIn.readOnly = true;
                amtIn.readOnly   = true;
                pctIn.classList.remove('promo-readonly');
                promoIn.classList.add('promo-readonly');
                amtIn.classList.add('promo-readonly');
            } else {
                pctIn.readOnly   = true;
                promoIn.readOnly = false;
                amtIn.readOnly   = false;
                pctIn.classList.add('promo-readonly');
                promoIn.classList.remove('promo-readonly');
                amtIn.classList.remove('promo-readonly');
            }
        });
    }

    // ── Брояч избрани ────────────────────────────────────────
    function updateCount() {
        var n = document.querySelectorAll('.row-include:checked').length;
        document.getElementById('selectedCount').textContent = n + ' избрани';
    }

    // ── Филтър по име ────────────────────────────────────────
    window.algaraPromoFilter = function () {
        var q = document.getElementById('productSearch').value.toLowerCase().trim();
        document.querySelectorAll('.promo-row').forEach(function (tr) {
            tr.classList.toggle('d-none', !(tr.getAttribute('data-product-name') || '').includes(q));
        });
    };

    // ── Bulk действия ────────────────────────────────────────
    function selectedRows() {
        return Array.from(document.querySelectorAll('.promo-row'))
            .filter(function (tr) {
                return !tr.classList.contains('d-none')
                    && tr.querySelector('.row-include').checked;
            });
    }
    function applyBulk() {
        var input = document.getElementById('bulkValue');
        if (!input.reportValidity()) return;
        var v = parseNum(input.value);
        if (v <= 0) return;
        var mode = getMode();
        selectedRows().forEach(function (tr) {
            if (mode === 'percent') {
                tr.querySelector('.row-percent').value = v;
                recalcFromPercent(tr);
            } else {
                tr.querySelector('.row-amount').value = v;
                recalcFromAmount(tr);
            }
        });
    }
    function resetSelectedToOriginal() {
        selectedRows().forEach(function (tr) {
            var orig = parseNum(tr.querySelector('.row-original').value);
            tr.querySelector('.row-percent').value = '0';
            tr.querySelector('.row-amount').value  = '0.00';
            tr.querySelector('.row-promo').value   = orig.toFixed(2);
        });
    }
    function refreshOriginalToCurrent() {
        selectedRows().forEach(function (tr) {
            var cur = parseNum(tr.getAttribute('data-current-price'));
            tr.querySelector('.row-original').value = cur.toFixed(2);
            tr.querySelector('.row-refresh-original').value = 'true';
            recalcFromOriginal(tr);
        });
    }

    // ── Event wiring ─────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', function () {
        document.getElementById('productSearch').addEventListener('input', window.algaraPromoFilter);

        // Ред: промени на отделните полета
        document.querySelectorAll('#promoTable tbody tr').forEach(function (tr) {
            tr.querySelector('.row-percent').addEventListener('input',  function () { recalcFromPercent(tr); });
            tr.querySelector('.row-promo').addEventListener('input',    function () { recalcFromPromo(tr); });
            tr.querySelector('.row-amount').addEventListener('input',   function () { recalcFromAmount(tr); });
            tr.querySelector('.row-include').addEventListener('change', updateCount);
        });

        // Режим (radio)
        document.getElementById('typePercent').addEventListener('change', applyModeToUi);
        document.getElementById('typeAmount').addEventListener('change', applyModeToUi);

        // Bulk бутони
        document.getElementById('bulkApply').addEventListener('click', applyBulk);
        document.getElementById('bulkReset').addEventListener('click', resetSelectedToOriginal);
        document.getElementById('bulkRefresh').addEventListener('click', refreshOriginalToCurrent);

        // Check all видими
        document.getElementById('checkAll').addEventListener('change', function () {
            var checked = this.checked;
            document.querySelectorAll('.promo-row:not(.d-none) .row-include').forEach(function (cb) {
                cb.checked = checked;
            });
            updateCount();
        });

        applyModeToUi();
        updateCount();
    });
}());
