// ══════════════════════════════════════════════════════════════════════
// date-year-quickpick.js — Jahr-Schnellauswahl für native Datumsfelder
// ──────────────────────────────────────────────────────────────────────
// Walter-Vorgabe 21.06.2026 / präzisiert 26.07.2026:
// Der native Browser-Kalender lässt sich nicht umgestalten (Jahre-Liste
// 2027/2028… steuert der Browser). Deshalb:
//
// 1) Leeres Feld → beim Fokus HEUTE vorschlagen (aktuelles Datum).
// 2) Jahr-Knöpfe UNTER dem Feld: Vorjahr · aktuelles Jahr · nächstes Jahr
//    (z.B. 2025 · 2026 · 2027) — Vorjahr oben/links zum schnellen Wechsel.
//
//   window.YearPick.attach(inputEl[, { years:[2025,2026,2027] }])
//   window.YearPick.attachById('eawSyncFrom')
//   window.YearPick.scan()
//
// Opt-out: data-yp="off" am Input. Mehrfach-Attach ist sicher.
// ══════════════════════════════════════════════════════════════════════
(function () {
    function pad2(n) { return String(n).padStart(2, '0'); }

    function todayIsoLocal() {
        const t = new Date();
        return `${t.getFullYear()}-${pad2(t.getMonth() + 1)}-${pad2(t.getDate())}`;
    }

    function setYear(input, year) {
        const cur = (input.value && /^\d{4}-\d{2}-\d{2}$/.test(input.value))
            ? input.value
            : todayIsoLocal();
        input.value = String(year) + cur.slice(4);   // Jahr ersetzen, -MM-DD behalten
        input.dispatchEvent(new Event('input',  { bubbles: true }));
        input.dispatchEvent(new Event('change', { bubbles: true }));
        if (typeof input._ypRender === 'function') input._ypRender();
    }

    function attach(input, opts) {
        if (!input || input._ypAttached) return;
        if (input.getAttribute('data-yp') === 'off') return;
        if ((input.type || '').toLowerCase() !== 'date') return;
        input._ypAttached = true;
        opts = opts || {};
        const curY  = new Date().getFullYear();
        // Vorjahr · aktuell · nächstes — Vorjahr zuerst zum Anklicken (Walter 26.07.2026).
        const years = opts.years || [curY - 1, curY, curY + 1];
        const titles = {
            [curY - 1]: 'Vorjahr',
            [curY]: 'Aktuelles Jahr',
            [curY + 1]: 'Nächstes Jahr',
        };

        const row = document.createElement('div');
        row.className = 'yp-row';
        row.setAttribute('data-yp-row', '1');

        function render() {
            const sel = (input.value || '').slice(0, 4);
            row.innerHTML = '';
            years.forEach((y, idx) => {
                const b = document.createElement('button');
                b.type = 'button';
                b.textContent = y;
                b.title = titles[y] || String(y);
                const active = String(y) === sel;
                b.className = 'yp-btn' + (active ? ' yp-btn-active' : '') + (idx === 0 ? ' yp-btn-prev' : '');
                b.addEventListener('click', (ev) => {
                    ev.preventDefault();
                    ev.stopPropagation();
                    setYear(input, y);
                    try { input.focus(); } catch { /* ignore */ }
                });
                row.appendChild(b);
            });
        }

        input._ypRender = render;
        render();
        input.addEventListener('change', render);
        input.addEventListener('input', render);

        // Leeres Feld → heutiges Datum vorschlagen (Walter 26.07.2026).
        // Felder mit bewusstem Default haben beim Fokus bereits einen Wert.
        input.addEventListener('focus', () => {
            if (input.value) return;
            if (input.readOnly || input.disabled) return;
            input.value = todayIsoLocal();
            input.dispatchEvent(new Event('input', { bubbles: true }));
            render();
        });

        if (input.parentNode) input.parentNode.insertBefore(row, input.nextSibling);
    }

    function attachById(id, opts) {
        const el = document.getElementById(id);
        if (el) attach(el, opts);
    }

    function scan(root) {
        const scope = root && root.querySelectorAll ? root : document;
        if (scope.matches && scope.matches('input[type="date"]')) attach(scope);
        scope.querySelectorAll('input[type="date"]').forEach(el => attach(el));
    }

    function startAuto() {
        scan(document);
        if (window._ypObserver) return;
        const mo = new MutationObserver(muts => {
            for (const m of muts) {
                for (const n of m.addedNodes) {
                    if (n.nodeType !== 1) continue;
                    if (n.getAttribute && n.getAttribute('data-yp-row') === '1') continue;
                    if (n.matches && n.matches('input[type="date"]')) attach(n);
                    else if (n.querySelectorAll) scan(n);
                }
            }
        });
        mo.observe(document.documentElement, { childList: true, subtree: true });
        window._ypObserver = mo;
    }

    window.YearPick = { attach, attachById, setYear, scan, startAuto };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startAuto);
    } else {
        startAuto();
    }
})();
