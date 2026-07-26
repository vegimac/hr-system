// ══════════════════════════════════════════════════════════════════════
// date-year-quickpick.js — Datumsfelder: heute vorschlagen, normaler Kalender
// ──────────────────────────────────────────────────────────────────────
// Walter 26.07.2026: Keine Jahr-Knöpfe mehr neben dem Feld (sah in engen
// Layouts schlecht aus). Der Browser-Kalender bleibt unverändert scrollbar
// (Vorjahr / Zukunft durch Scrollen in der Jahresliste).
//
// Einzige Hilfe: leeres Feld → beim Fokus heutiges Datum vorschlagen.
// Opt-out: data-yp="off". Mehrfach-Attach ist sicher.
// ══════════════════════════════════════════════════════════════════════
(function () {
    function pad2(n) { return String(n).padStart(2, '0'); }

    function todayIsoLocal() {
        const t = new Date();
        return `${t.getFullYear()}-${pad2(t.getMonth() + 1)}-${pad2(t.getDate())}`;
    }

    function attach(input) {
        if (!input || input._ypAttached) return;
        if (input.getAttribute('data-yp') === 'off') return;
        if ((input.type || '').toLowerCase() !== 'date') return;
        input._ypAttached = true;

        // Allfällige alte Jahr-Knopf-Reihe entfernen (Cache / Hot-Reload).
        const next = input.nextElementSibling;
        if (next && next.getAttribute && next.getAttribute('data-yp-row') === '1') {
            next.remove();
        }

        // Leeres Feld → heutiges Datum vorschlagen.
        input.addEventListener('focus', () => {
            if (input.value) return;
            if (input.readOnly || input.disabled) return;
            input.value = todayIsoLocal();
            input.dispatchEvent(new Event('input', { bubbles: true }));
        });
    }

    function attachById(id) {
        const el = document.getElementById(id);
        if (el) attach(el);
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
                    if (n.getAttribute && n.getAttribute('data-yp-row') === '1') {
                        n.remove();
                        continue;
                    }
                    if (n.matches && n.matches('input[type="date"]')) attach(n);
                    else if (n.querySelectorAll) scan(n);
                }
            }
        });
        mo.observe(document.documentElement, { childList: true, subtree: true });
        window._ypObserver = mo;
    }

    window.YearPick = { attach, attachById, scan, startAuto };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startAuto);
    } else {
        startAuto();
    }
})();
