// ══════════════════════════════════════════════════════════════════════
// date-year-quickpick.js — Jahr-Schnellauswahl für native Datumsfelder
// ──────────────────────────────────────────────────────────────────────
// Walter-Vorgabe 21.06.2026. Der native Browser-Kalender lässt sich nicht
// umgestalten — das Scrollen aufs Vorjahr ist mühsam. Dieser Helfer setzt
// kleine Jahr-Knöpfe DIREKT unter ein <input type="date">. Klick auf „2025"
// springt aufs Vorjahr (Monat/Tag bleiben), ohne im Kalender zu scrollen.
//
//   window.YearPick.attach(inputEl[, { years:[2025,2026] }])
//   window.YearPick.attachById('eawSyncFrom')
//
// Standard-Jahre: Vorjahr + aktuelles Jahr. Mehrfach-Aufruf ist sicher
// (Guard verhindert doppelte Knopfreihen).
// ══════════════════════════════════════════════════════════════════════
(function () {
    function setYear(input, year) {
        const cur = (input.value && /^\d{4}-\d{2}-\d{2}$/.test(input.value))
            ? input.value
            : new Date().toISOString().slice(0, 10);
        input.value = String(year) + cur.slice(4);   // Jahr ersetzen, -MM-DD behalten
        input.dispatchEvent(new Event('input',  { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
        if (typeof input._ypRender === 'function') input._ypRender();
    }

    function attach(input, opts) {
        if (!input || input._ypAttached) return;
        input._ypAttached = true;
        opts = opts || {};
        const curY  = new Date().getFullYear();
        const years = opts.years || [curY - 1, curY];

        const row = document.createElement('div');
        row.className = 'yp-row';
        row.style.cssText = 'display:flex;gap:4px;margin-top:4px;flex-wrap:wrap';

        function render() {
            const sel = (input.value || '').slice(0, 4);
            row.innerHTML = '';
            years.forEach(y => {
                const b = document.createElement('button');
                b.type = 'button';
                b.textContent = y;
                const active = String(y) === sel;
                b.style.cssText =
                    'font-size:11px;line-height:1;padding:3px 8px;border-radius:6px;cursor:pointer;font-weight:600;' +
                    'border:1px solid ' + (active ? '#2563eb' : '#cbd5e1') + ';' +
                    'background:' + (active ? '#2563eb' : '#fff') + ';' +
                    'color:' + (active ? '#fff' : '#475569') + ';';
                b.addEventListener('click', () => setYear(input, y));
                row.appendChild(b);
            });
        }

        input._ypRender = render;
        render();
        input.addEventListener('change', render);
        // Reihe direkt UNTER das Feld setzen.
        if (input.parentNode) input.parentNode.insertBefore(row, input.nextSibling);
    }

    function attachById(id, opts) {
        const el = document.getElementById(id);
        if (el) attach(el, opts);
    }

    window.YearPick = { attach, attachById, setYear };
})();
