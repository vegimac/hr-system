// ══════════════════════════════════════════════════════════════════════
// sortable-table.js — kleine Helfer für klickbare Tabellen-Header.
// ──────────────────────────────────────────────────────────────────────
// Pattern: jede Stammdaten-Liste hält ihren Zustand in einem Objekt
//   { key: 'plz4', dir: 'asc' }
// — Spalten-Header rufen window.sortableHeaderClick(state, key, renderFn) auf.
// ══════════════════════════════════════════════════════════════════════

/**
 * Vergleichsfunktion für Sort. Strings case-insensitiv, Nullen letzt.
 */
window.sortableCompare = function(a, b, key, dir) {
    const av = a?.[key];
    const bv = b?.[key];
    const aEmpty = av === null || av === undefined || av === '';
    const bEmpty = bv === null || bv === undefined || bv === '';
    if (aEmpty && bEmpty) return 0;
    if (aEmpty) return 1;   // leere ans Ende
    if (bEmpty) return -1;
    let cmp;
    if (typeof av === 'number' && typeof bv === 'number') {
        cmp = av - bv;
    } else if (typeof av === 'boolean' && typeof bv === 'boolean') {
        cmp = (av === bv) ? 0 : (av ? -1 : 1);
    } else {
        cmp = String(av).localeCompare(String(bv), 'de-CH', { numeric: true, sensitivity: 'base' });
    }
    return dir === 'desc' ? -cmp : cmp;
};

/**
 * Sortiert ein Array IN PLACE nach dem State.
 */
window.sortableApply = function(arr, state) {
    if (!state || !state.key) return arr;
    arr.sort((a, b) => window.sortableCompare(a, b, state.key, state.dir));
    return arr;
};

/**
 * Wird vom Header-Click aufgerufen — toggelt Richtung wenn gleiche Spalte,
 * sonst neue Spalte aufsteigend. Ruft renderFn() auf.
 */
window.sortableHeaderClick = function(state, key, renderFn) {
    if (state.key === key) {
        state.dir = state.dir === 'asc' ? 'desc' : 'asc';
    } else {
        state.key = key;
        state.dir = 'asc';
    }
    if (typeof renderFn === 'function') renderFn();
};

/**
 * Liefert HTML für einen klickbaren Header. Pfeil zeigt aktuelle Sortierung.
 * stateVarName = der globale Variablenname des State-Objekts (als String),
 * damit der onclick-Handler ihn referenzieren kann.
 */
window.sortableHeader = function(label, key, state, stateVarName, renderFnName, extraStyle = '') {
    const isActive = state.key === key;
    const arrow = isActive ? (state.dir === 'asc' ? ' ▲' : ' ▼') : '';
    const color = isActive ? '#6b6152' : '#475569';
    return `<th onclick="sortableHeaderClick(${stateVarName}, '${key}', ${renderFnName})"
                style="padding:9px 12px;text-align:left;cursor:pointer;user-select:none;color:${color};${extraStyle}">
                ${label}<span style="font-size:10px">${arrow}</span>
            </th>`;
};
