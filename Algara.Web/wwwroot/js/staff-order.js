(() => {
    "use strict";
    const form = document.getElementById("staff-order-form");
    if (!form) return;
    const field = name => form.elements.namedItem(name);
    const scroll = field("ScrollY"), saved = Number(scroll.value);
    if (Number.isFinite(saved) && saved > 0 && saved <= 1000000)
        window.addEventListener("load", () => window.scrollTo(0, saved), { once: true });
    form.addEventListener("submit", () => { scroll.value = Math.round(window.scrollY); });
    const invalidate = () => {
        field("PreviewToken").value = "";
        const agreement = form.querySelector('input[type="checkbox"][name="CustomerAgreed"]');
        if (agreement) agreement.checked = false;
    };
    const updateDestination = () => {
        const isOffice = [2,4].includes(Number(field("Destination.Method").value));
        const pickup = Number(field("Destination.Method").value) === 0;
        for (const block of form.querySelectorAll("[data-staff-office]")) block.hidden = !isOffice;
        for (const block of form.querySelectorAll("[data-staff-address]")) block.hidden = isOffice || pickup;
        field("Destination.PostalCode").required = false;
    };
    updateDestination();
    field("Destination.Method").addEventListener("change", updateDestination);
    for (const control of [field("Destination.Method"), field("Destination.City")]) control.addEventListener("change", () => {
        field("Destination.OfficeCode").replaceChildren(new Option("Изберете офис", ""));
        invalidate();
    });
    field("CustomerUserN").addEventListener("change", () => {
        const selected = field("CustomerUserN").selectedOptions[0];
        if (!selected?.value) return;
        for (const [name, key] of [["ContactName", "contact"], ["Phone", "phone"], ["Email", "email"]])
            field(name).value = selected.dataset[key] || "";
        invalidate();
    });
    for (const input of form.querySelectorAll("[data-search]")) {
        const results = document.getElementById(input.getAttribute("aria-controls"));
        let timer, controller, sequence = 0;
        const hide = () => { results.hidden = true; input.setAttribute("aria-expanded", "false"); };
        const show = () => { results.hidden = false; input.setAttribute("aria-expanded", "true"); };
        input.addEventListener("input", () => {
            clearTimeout(timer); controller?.abort(); const current = ++sequence;
            results.replaceChildren(); hide();
            const query = input.value.trim();
            if (input.dataset.search === "partners" && query.length < 3) return;
            timer = setTimeout(async () => {
                controller = new AbortController();
                const url = new URL(form.dataset.searchUrl, window.location.origin);
                url.searchParams.set("q", query); url.searchParams.set("kind", input.dataset.search);
                try {
                    const response = await fetch(url, { signal: controller.signal, headers: { Accept: "application/json" } });
                    if (!response.ok) throw new Error("Search failed");
                    const items = await response.json();
                    if (current !== sequence) return;
                    results.replaceChildren();
                    for (const item of items) {
                        const button = document.createElement("button");
                        button.type = "submit"; button.name = "command"; button.value = item.command;
                        button.formNoValidate = true; button.className = "list-group-item list-group-item-action text-start";
                        button.textContent = (input.dataset.search === "partners" ? "Избери — " : "") + item.label;
                        if(input.dataset.search === "products") {
                            const row=document.createElement("div"), details=document.createElement("div"), label=document.createElement("div"), stock=document.createElement("div");
                            row.className="d-flex gap-2 align-items-center border-bottom py-2";
                            button.textContent="Добави";button.className="btn btn-sm btn-outline-primary";
                            label.textContent=item.label;stock.textContent="Наличност: "+item.stock;
                            stock.className="small text-muted";details.append(label,stock);row.append(button,details);results.append(row);
                        } else results.append(button);
                    }
                    if (!items.length) results.textContent = "Няма съвпадения.";
                    show();
                } catch (error) {
                    if (error.name === "AbortError" || current !== sequence) return;
                    results.textContent = "Търсенето не е достъпно. Опитайте отново."; show();
                }
            }, 250);
        });
        input.addEventListener("keydown", event => {
            if (event.key === "Escape") { ++sequence; controller?.abort(); hide(); }
            if (event.key === "ArrowDown" && !results.hidden) { event.preventDefault(); results.querySelector("button")?.focus(); }
        });
        results.addEventListener("keydown", event => {
            const buttons = [...results.querySelectorAll("button")], index = buttons.indexOf(document.activeElement);
            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                event.preventDefault(); buttons[(index + (event.key === "ArrowDown" ? 1 : -1) + buttons.length) % buttons.length]?.focus();
            }
            if (event.key === "Escape") { hide(); input.focus(); }
        });
        if(input.dataset.search === "products") input.dispatchEvent(new Event("input"));
        document.addEventListener("click", event => { if (input.dataset.search !== "products" && event.target !== input && !results.contains(event.target)) hide(); });
    }
})();
