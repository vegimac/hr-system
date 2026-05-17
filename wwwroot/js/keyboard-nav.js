// ══════════════════════════════════════════════════════════════════════
// keyboard-nav.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

document.addEventListener('keydown', e => {
    // Guards: in Inputs, mit Modifier oder bei offenem Modal nicht reagieren
    const t = e.target;
    const tag = (t?.tagName || '').toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select' || t?.isContentEditable) return;
    if (e.metaKey || e.ctrlKey || e.altKey) return;
    const drawerOpen = document.querySelector('.drawer-open, [id$="Drawer"][style*="display:block"], [id$="Modal"][style*="display:flex"]');
    if (drawerOpen) return;

    if (e.key !== 'ArrowDown' && e.key !== 'ArrowUp') return;

    // Verträge-Seite: navigiert allVtEmployees via selectedVtEmployee
    const onVertraegeePage = document.getElementById('page-vertraege')?.classList.contains('active');
    if (onVertraegeePage && typeof allVtEmployees !== 'undefined' && allVtEmployees.length) {
        const idx = allVtEmployees.findIndex(x => x.id === selectedVtEmployee?.id);
        let next = idx;
        if (e.key === 'ArrowDown') next = idx < 0 ? 0 : Math.min(idx + 1, allVtEmployees.length - 1);
        if (e.key === 'ArrowUp')   next = idx < 0 ? 0 : Math.max(idx - 1, 0);
        if (next !== idx && allVtEmployees[next]) {
            e.preventDefault();
            selectVtEmployee(allVtEmployees[next].id);
            setTimeout(() => {
                document.querySelector('#vtList .emp-list-item.active')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
            }, 50);
        }
        return;
    }

    // Lohn-Seite: navigiert .lohn-emp-row Liste via _lohnSelectedEmpId.
    //
    // Walter-Bug-Fix 16.05.2026: Im Akonto-Modus übernimmt der akonto-workflow.js-
    // Handler die Tastatur-Navigation (eigene MA-Liste mit eigener Status-State).
    // Dieser hier muss dann stillhalten, sonst feuern beide Handler parallel,
    // der eine überschreibt den anderen, und die Selektion "springt zurück" auf
    // den falschen MA — in dunklem Theme war der Effekt am sichtbarsten weil
    // beide Listen die gleichen .lohn-emp-row-Klassen tragen.
    const onLohnPage = document.getElementById('page-lohn')?.classList.contains('active');
    if (!onLohnPage) return;
    if (typeof _akWfMode !== 'undefined' && _akWfMode === 'akonto') return;

    // Nur im Definitivlauf — Liste hat .lohn-emp-row mit data-emp-id
    // (gerendert von loadLohnList in payroll.js).
    const lohnContainer = document.getElementById('lohnEmpList');
    const rows = lohnContainer
        ? Array.from(lohnContainer.querySelectorAll('.lohn-emp-row'))
        : Array.from(document.querySelectorAll('.lohn-emp-row'));
    if (!rows.length) return;
    const idx = rows.findIndex(r => Number(r.dataset.empId) === Number(_lohnSelectedEmpId));
    let next = idx;
    if (e.key === 'ArrowDown') next = idx < 0 ? 0 : Math.min(idx + 1, rows.length - 1);
    if (e.key === 'ArrowUp')   next = idx < 0 ? 0 : Math.max(idx - 1, 0);
    if (next !== idx && rows[next]) {
        e.preventDefault();
        rows[next].click();
        setTimeout(() => {
            rows[next].scrollIntoView({ block: 'nearest', behavior: 'smooth' });
        }, 50);
    }
});
