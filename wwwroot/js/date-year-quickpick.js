// ══════════════════════════════════════════════════════════════════════
// date-year-quickpick.js — Jahr-Schnellauswahl für native Datumsfelder
// ──────────────────────────────────────────────────────────────────────
// Walter-Vorgabe 21.06.2026 / verschärft 26.07.2026: Der native Browser-
// Kalender lässt sich nicht umgestalten — Scrollen aufs Vorjahr ist mühsam.
//
// 1) Kleine Jahr-Knöpfe DIREKT unter jedem <input type="date">
//    (Vorjahr zuerst, dann aktuelles Jahr). Klick setzt das Jahr, Monat/Tag
//    bleiben.
// 2) Leere Felder: beim Fokus vorläufig auf Vorjahr setzen, damit der
//    Kalender dort öffnet. Bei Abbruch (Blur ohne Auswahl) wieder leeren.
//
//   window.YearPick.attach(inputEl[, { years:[2025,2026] }])
//   window.YearPick.attachById('eawSyncFrom')
//   window.YearPick.scan()          — alle Felder im Dokument
//
// Opt-out: data-yp="off" am Input. Mehrfach-Attach ist sicher.
// ══════════════════════════════════════════════════════════════════════
(function () {
    function pad2(n) { return String(n).padStart(2, '0'); }

    function todayIsoLocal() {
        const t = new Date();
        return `${t.getFullYear()}-${pad2(t.getMonth() + 1)}-${pad2(t.getDate())}`;
    }

    function prevYearIsoLocal() {
        const t = new Date();
        return `${t.getFullYear() - 1}-${pad2(t.getMonth() + 1)}-${pad2(t.getDate())}`;
    }

    function setYear(input, year) {
        const cur = (input.value && /^\d{4}-\d{2}-\d{2}$/.test(input.value))
            ? input.value
            : todayIsoLocal();
        input.value = String(year) + cur.slice(4);   // Jahr ersetzen, -MM-DD behalten
        delete input.dataset.ypSeeded;
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
        // Vorjahr zuerst — das ist der häufige Fall (Walter 26.07.2026).
        const years = opts.years || [curY - 1, curY];

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
                b.title = idx === 0 ? 'Vorjahr' : 'Aktuelles Jahr';
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
        input.addEventListener('change', () => {
            delete input.dataset.ypSeeded;
            render();
        });
        input.addEventListener('input', render);

        // Leeres Feld → Kalender im Vorjahr öffnen (Wert nur vorläufig).
        input.addEventListener('focus', () => {
            if (input.value) return;
            if (input.readOnly || input.disabled) return;
            input.value = prevYearIsoLocal();
            input.dataset.ypSeeded = '1';
            render();
        });
        input.addEventListener('blur', () => {
            // Nach dem nativen Picker kommt oft erst change, dann blur —
            // microtask: wenn weiterhin nur Seed → zurücksetzen.
            setTimeout(() => {
                if (input.dataset.ypSeeded === '1') {
                    input.value = '';
                    delete input.dataset.ypSeeded;
                    render();
                }
            }, 0);
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
