// ══════════════════════════════════════════════════════════════════════
// kontrolle.js — Kontroll-Listen im HR-Bereich
// ──────────────────────────────────────────────────────────────────────
// Walter-Vorgabe 07.06.2026: proaktive Lücken-Erkennung. Erste Liste:
// MA mit QST-Bezug auf Ehegatten ohne hinterlegtes Spouse-Doku.
// Weitere Listen folgen.
// ══════════════════════════════════════════════════════════════════════

let _kontrolleSpouseCache   = [];
let _kontrolleEmployeeCache = [];
let _kontrollePermitCache   = [];
let _kontrolleNachtCache    = [];

function kontrolleInit() {
    kontrolleEmployeeRefresh();
    kontrolleSpouseRefresh();
    kontrollePermitRefresh();
    kontrolleNachtRefresh();
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 22.06.2026 (ArGV1 Art. 30): Liste „Nachtarbeit-Nachweise fehlen"
// MA mit > 18 gearbeiteten Nächten in einem rollierenden 6-Wochen-Fenster ohne
// vollständige Nachweise (Arztzeugnis/Verzicht UND Ausnahmeregelung). Logik
// identisch zum Dashboard-Block.
// ══════════════════════════════════════════════════════════════════════
async function kontrolleNachtRefresh() {
    const el = document.getElementById('kontrolleNachtList');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder" style="height:120px"><span>Lade Liste…</span></div>';

    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    const url = cpId ? `/api/kontrolle/nacht-untersuchung-fehlt?companyProfileId=${cpId}` : '/api/kontrolle/nacht-untersuchung-fehlt';
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            el.innerHTML = '<div class="emp-placeholder" style="height:120px;color:#dc2626"><span>Fehler beim Laden (' + r.status + ')</span></div>';
            return;
        }
        const list = await r.json();
        _kontrolleNachtCache = Array.isArray(list) ? list : [];
        if (!Array.isArray(list) || list.length === 0) {
            el.innerHTML = `<div style="padding:24px;text-align:center;color:#16a34a;font-size:14px;font-weight:600">
                ✓ Keine offenen Lücken — alle MA mit >18 Nächten in 6 Wochen haben vollständige Nachtarbeit-Nachweise.
            </div>`;
            return;
        }
        const fmtDe = (iso) => {
            if (!iso) return '–';
            const s = String(iso).slice(0, 10);
            if (s.length !== 10) return '–';
            return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
        };
        el.innerHTML = `
            <div style="padding:12px 18px 14px;color:#7f1d1d;font-size:12.5px;font-weight:600">
                ${list.length} MA mit >18 Nächten in 6 Wochen ohne vollständige Nachtarbeit-Nachweise
                <span style="color:#94a3b8;font-weight:400;font-size:11.5px;margin-left:8px">· Bemerkungen optional — werden ins Excel/PDF übernommen, beim Aktualisieren zurückgesetzt</span>
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead>
                    <tr style="background:#fef2f2;border-top:1px solid #fee2e2;border-bottom:1px solid #fecaca">
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Mitarbeiter</th>
                        <th style="padding:9px 14px;text-align:center;color:#7f1d1d">Max 6W</th>
                        <th style="padding:9px 14px;text-align:center;color:#7f1d1d">Zeitraum (6 Wochen)</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Grund</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Bemerkung</th>
                        <th style="padding:9px 14px;text-align:right;color:#7f1d1d">Aktion</th>
                    </tr>
                </thead>
                <tbody>
                    ${list.map(r => `
                        <tr style="border-bottom:1px solid #f1f5f9">
                            <td style="padding:9px 14px">
                                <div style="font-weight:600;color:#0f172a">${_e(r.employeeName)}</div>
                                <div style="font-size:11.5px;color:#64748b">Nr. ${_e(r.employeeNumber)}</div>
                            </td>
                            <td style="padding:9px 14px;text-align:center;font-family:monospace;font-weight:700;color:#991b1b">${r.maxNaechte6Wochen}</td>
                            <td style="padding:9px 14px;text-align:center;font-family:monospace;font-size:12px">${fmtDe(r.windowFrom)}–${fmtDe(r.windowTo)}</td>
                            <td style="padding:9px 14px;color:#7f1d1d;font-size:12px">${_e(r.reason)}</td>
                            <td style="padding:6px 12px">
                                <input type="text" id="kontrolleNachtNote-${r.employeeId}" placeholder="Notiz…"
                                       style="width:100%;padding:5px 8px;border:1px solid #e2e8f0;border-radius:4px;font-size:12px;background:#fff;color:#0f172a">
                            </td>
                            <td style="padding:9px 14px;text-align:right;white-space:nowrap">
                                <button onclick="kontrolleOpenEmployeeNacht(${r.employeeId})"
                                        class="btn btn-primary" style="padding:5px 12px;font-size:12px">
                                    → Zum MA
                                </button>
                            </td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>
            <div style="padding:8px 18px 4px;color:#94a3b8;font-size:11px">Warnfall: >18 gearbeitete Nächte in einem 6-Wochen-Fenster (ArGV1 Art. 30) ohne vollständige Nachweise.</div>`;
    } catch (e) {
        el.innerHTML = '<div class="emp-placeholder" style="height:120px;color:#dc2626"><span>Verbindungsfehler: ' + _e(e.message) + '</span></div>';
    }
}

/** Sprung in die Persönlichen Angaben des MA — dort steht im ANSTELLUNG-Block
 *  das Feld „Nachtarbeit ausgestellt" + Dokument-Verknüpfung. */
function kontrolleOpenEmployeeNacht(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof showPage === 'function') showPage('mitarbeiter');
    setTimeout(() => {
        if (typeof switchEmpTab === 'function') switchEmpTab('personal');
    }, 300);
}

function _kontrolleNachtNote(empId) {
    const el = document.getElementById('kontrolleNachtNote-' + empId);
    return el ? (el.value || '').trim() : '';
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 13.06.2026: Liste „Bewilligungen laufen ab"
// (abgelaufen oder innerhalb 90 Tagen ablaufend). Logik analog zur
// Dashboard-Card permit_expiring.
// ══════════════════════════════════════════════════════════════════════
async function kontrollePermitRefresh() {
    const el = document.getElementById('kontrollePermitList');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder" style="height:120px"><span>Lade Liste…</span></div>';

    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    const url = cpId ? `/api/kontrolle/permit-expiring?companyProfileId=${cpId}` : '/api/kontrolle/permit-expiring';
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            el.innerHTML = '<div class="emp-placeholder" style="height:120px;color:#dc2626"><span>Fehler beim Laden (' + r.status + ')</span></div>';
            return;
        }
        const list = await r.json();
        _kontrollePermitCache = Array.isArray(list) ? list : [];
        if (!Array.isArray(list) || list.length === 0) {
            el.innerHTML = `<div style="padding:24px;text-align:center;color:#16a34a;font-size:14px;font-weight:600">
                ✓ Keine offenen Lücken — bei allen aktiven MA sind die Bewilligungen aktuell.
            </div>`;
            return;
        }
        const fmtDe = (iso) => {
            if (!iso) return '–';
            const s = String(iso).slice(0, 10);
            if (s.length !== 10) return '–';
            return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
        };
        el.innerHTML = `
            <div style="padding:12px 18px 14px;color:#7f1d1d;font-size:12.5px;font-weight:600">
                ${list.length} ${list.length === 1 ? 'Bewilligung' : 'Bewilligungen'} läuft/laufen ab
                <span style="color:#94a3b8;font-weight:400;font-size:11.5px;margin-left:8px">· Bemerkungen optional — werden ins Excel/PDF übernommen, beim Aktualisieren zurückgesetzt</span>
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead>
                    <tr style="background:#fef2f2;border-top:1px solid #fee2e2;border-bottom:1px solid #fecaca">
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Mitarbeiter</th>
                        <th style="padding:9px 14px;text-align:center;color:#7f1d1d">Bew.</th>
                        <th style="padding:9px 14px;text-align:center;color:#7f1d1d">Gültig bis</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Status</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Bemerkung</th>
                        <th style="padding:9px 14px;text-align:right;color:#7f1d1d">Aktion</th>
                    </tr>
                </thead>
                <tbody>
                    ${list.map(r => {
                        const severityColor = r.severity === 'expired' || r.severity === 'critical'
                            ? '#dc2626' : r.severity === 'warning' ? '#d97706' : '#6b6152';
                        const severityBg = r.severity === 'expired' || r.severity === 'critical'
                            ? '#fee2e2' : r.severity === 'warning' ? '#fef3c7' : '#ece9e2';
                        const daysText = !r.validTo
                            ? 'keine Bewilligung erfasst'   // permit_missing (Walter 12.07.2026)
                            : r.daysUntil < 0
                                ? `${-r.daysUntil} Tag(e) überfällig`
                                : r.daysUntil === 0 ? 'läuft heute ab' : `in ${r.daysUntil} Tagen`;
                        return `
                        <tr style="border-bottom:1px solid #f1f5f9">
                            <td style="padding:9px 14px">
                                <div style="font-weight:600;color:#0f172a">${_e(r.employeeName)}</div>
                                <div style="font-size:11.5px;color:#64748b">Nr. ${_e(r.employeeNumber)}</div>
                            </td>
                            <td style="padding:9px 14px;text-align:center;font-family:monospace;font-weight:700">${_e(r.permitCode || '–')}</td>
                            <td style="padding:9px 14px;text-align:center;font-family:monospace">${fmtDe(r.validTo)}</td>
                            <td style="padding:9px 14px">
                                <span style="background:${severityBg};color:${severityColor};padding:2px 9px;border-radius:5px;font-size:11.5px;font-weight:600;white-space:nowrap">
                                    ${_e(daysText)}
                                </span>
                            </td>
                            <td style="padding:6px 12px">
                                <input type="text" id="kontrollePermitNote-${r.employeeId}" placeholder="Notiz…"
                                       style="width:100%;padding:5px 8px;border:1px solid #e2e8f0;border-radius:4px;font-size:12px;background:#fff;color:#0f172a">
                            </td>
                            <td style="padding:9px 14px;text-align:right;white-space:nowrap">
                                <button onclick="kontrolleOpenEmployeePermit(${r.employeeId})"
                                        class="btn btn-primary" style="padding:5px 12px;font-size:12px">
                                    → Zum MA
                                </button>
                            </td>
                        </tr>`;
                    }).join('')}
                </tbody>
            </table>`;
    } catch (e) {
        el.innerHTML = '<div class="emp-placeholder" style="height:120px;color:#dc2626"><span>Verbindungsfehler: ' + _e(e.message) + '</span></div>';
    }
}

/**
 * Sprung in den Bewilligung/QST-Tab des MA — dort steht die Bewilligungs-
 * Historie mit „+ Neue Bewilligung"-Button für die Verlängerung.
 */
function kontrolleOpenEmployeePermit(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof showPage === 'function') showPage('mitarbeiter');
    setTimeout(() => {
        if (typeof switchEmpTab === 'function') switchEmpTab('quellensteuer');
    }, 300);
}

function _kontrollePermitNote(empId) {
    const el = document.getElementById('kontrollePermitNote-' + empId);
    return el ? (el.value || '').trim() : '';
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 13.06.2026: Liste „Ausweis Mitarbeiter fehlt"
// MA, deren QST-Befreiung am eigenen Ausweis hängt, aber das Beleg-Doku
// noch nicht verknüpft ist (id_pass_dokument_id / c_ausweis_dokument_id).
// ══════════════════════════════════════════════════════════════════════
async function kontrolleEmployeeRefresh() {
    const el = document.getElementById('kontrolleEmployeeList');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder" style="height:120px"><span>Lade Liste…</span></div>';

    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    const url = cpId ? `/api/kontrolle/employee-ausweis-fehlt?companyProfileId=${cpId}` : '/api/kontrolle/employee-ausweis-fehlt';
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            el.innerHTML = '<div class="emp-placeholder" style="height:120px;color:#dc2626"><span>Fehler beim Laden (' + r.status + ')</span></div>';
            return;
        }
        const list = await r.json();
        _kontrolleEmployeeCache = Array.isArray(list) ? list : [];
        if (!Array.isArray(list) || list.length === 0) {
            el.innerHTML = `<div style="padding:24px;text-align:center;color:#16a34a;font-size:14px;font-weight:600">
                ✓ Keine offenen Lücken — bei allen aktiven MA sind die Ausweise verknüpft.
            </div>`;
            return;
        }
        el.innerHTML = `
            <div style="padding:12px 18px 14px;color:#7f1d1d;font-size:12.5px;font-weight:600">
                ${list.length} MA mit fehlendem Ausweis
                <span style="color:#94a3b8;font-weight:400;font-size:11.5px;margin-left:8px">· Bemerkungen optional — werden ins Excel/PDF übernommen, beim Aktualisieren zurückgesetzt</span>
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead>
                    <tr style="background:#fef2f2;border-top:1px solid #fee2e2;border-bottom:1px solid #fecaca">
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Mitarbeiter</th>
                        <th style="padding:9px 14px;text-align:center;color:#7f1d1d">Grundlage</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Grund</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Bemerkung</th>
                        <th style="padding:9px 14px;text-align:right;color:#7f1d1d">Aktion</th>
                    </tr>
                </thead>
                <tbody>
                    ${list.map((r, i) => `
                        <tr style="border-bottom:1px solid #f1f5f9">
                            <td style="padding:9px 14px">
                                <div style="font-weight:600;color:#0f172a">${_e(r.employeeName)}</div>
                                <div style="font-size:11.5px;color:#64748b">Nr. ${_e(r.employeeNumber)}</div>
                            </td>
                            <td style="padding:9px 14px;text-align:center">
                                <span style="font-family:monospace;font-weight:600;font-size:12px;background:${r.kind === 'CH-Buerger' ? '#dcfce7' : '#ece9e2'};color:${r.kind === 'CH-Buerger' ? '#166534' : '#6b6152'};padding:2px 9px;border-radius:5px;white-space:nowrap">
                                    ${r.kind === 'CH-Buerger' ? '🇨🇭 CH-Bürger' : 'C-Ausweis'}
                                </span>
                            </td>
                            <td style="padding:9px 14px;color:#7f1d1d;font-size:12px">${_e(r.reason)}</td>
                            <td style="padding:6px 12px">
                                <input type="text" id="kontrolleEmpNote-${r.employeeId}" placeholder="Notiz…"
                                       style="width:100%;padding:5px 8px;border:1px solid #e2e8f0;border-radius:4px;font-size:12px;background:#fff;color:#0f172a">
                            </td>
                            <td style="padding:9px 14px;text-align:right;white-space:nowrap">
                                <button onclick="kontrolleOpenEmployeeQst(${r.employeeId})"
                                        class="btn btn-primary" style="padding:5px 12px;font-size:12px">
                                    → Zum MA
                                </button>
                            </td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>`;
    } catch (e) {
        el.innerHTML = '<div class="emp-placeholder" style="height:120px;color:#dc2626"><span>Verbindungsfehler: ' + _e(e.message) + '</span></div>';
    }
}

/**
 * Sprung in den Bewilligung/QST-Tab des MA — dort steht der rote Banner
 * mit „📎 Dokument verknüpfen", der dasselbe Modal öffnet.
 */
function kontrolleOpenEmployeeQst(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof showPage === 'function') showPage('mitarbeiter');
    setTimeout(() => {
        if (typeof switchEmpTab === 'function') switchEmpTab('quellensteuer');
    }, 300);
}

// Helfer: holt die ad-hoc-Bemerkungen aus dem DOM für eine MA-ID
function _kontrolleEmpNote(empId) {
    const el = document.getElementById('kontrolleEmpNote-' + empId);
    return el ? (el.value || '').trim() : '';
}
function _kontrolleSpouseNote(empId) {
    const el = document.getElementById('kontrolleSpouseNote-' + empId);
    return el ? (el.value || '').trim() : '';
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 13.06.2026: Excel + PDF enthalten BEIDE Listen kombiniert
// (Mitarbeiter zuerst, Ehepartner darunter) + die manuell eingegebenen
// Bemerkungen pro Zeile. Beide Buttons („Ausweis Mitarbeiter" + „Ausweis
// Ehegatte") rufen dieselben Combi-Exports auf — Walter bekommt immer ein
// Sammeldokument, egal wo er klickt.
// ══════════════════════════════════════════════════════════════════════
function kontrolleEmployeeExportExcel() { _kontrolleExportCombiExcel(); }
function kontrolleEmployeeExportPdf()   { _kontrolleExportCombiPdf(); }

function _kontrolleExportCombiExcel() {
    const hasEmp    = (_kontrolleEmployeeCache || []).length > 0;
    const hasSpouse = (_kontrolleSpouseCache   || []).length > 0;
    const hasPermit = (_kontrollePermitCache   || []).length > 0;
    const hasNacht  = (_kontrolleNachtCache    || []).length > 0;
    if (!hasEmp && !hasSpouse && !hasPermit && !hasNacht) {
        alert('Keine Daten zum Exportieren — alle Listen sind leer.');
        return;
    }
    const fmtDe = (iso) => {
        if (!iso) return '';
        const s = String(iso).slice(0, 10);
        if (s.length !== 10) return '';
        return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
    };
    const rows = [];

    // Sektion 1: MA-Ausweis
    if (hasEmp) {
        rows.push(['AUSWEIS MITARBEITER FEHLT']);
        rows.push(['Personal-Nr.', 'Mitarbeiter', 'Grundlage', 'Grund', 'Bemerkung']);
        for (const r of _kontrolleEmployeeCache) {
            rows.push([
                r.employeeNumber || '',
                r.employeeName || '',
                r.kind === 'CH-Buerger' ? 'CH-Bürger' : 'C-Ausweis',
                r.reason || '',
                _kontrolleEmpNote(r.employeeId)
            ]);
        }
        if (hasSpouse || hasPermit || hasNacht) rows.push([], []);
    }

    // Sektion 2: Ehegatten-Ausweis
    if (hasSpouse) {
        rows.push(['AUSWEIS EHEGATTE FEHLT']);
        rows.push(['Personal-Nr.', 'Mitarbeiter', 'Nat. MA', 'Bew. MA', 'Ehepartner', 'Nat. Ehepartner', 'Bew. Ehepartner', 'Grund', 'Bemerkung']);
        for (const r of _kontrolleSpouseCache) {
            rows.push([
                r.employeeNumber || '',
                r.employeeName || '',
                r.employeeNationality || '',
                r.employeePermitCode || '',
                r.spouseName || '',
                r.spouseNationality || '',
                r.spousePermitCode || '',
                r.reason || '',
                _kontrolleSpouseNote(r.employeeId)
            ]);
        }
        if (hasPermit || hasNacht) rows.push([], []);
    }

    // Sektion 3: Bewilligungen laufen ab
    if (hasPermit) {
        rows.push(['BEWILLIGUNGEN LAUFEN AB']);
        rows.push(['Personal-Nr.', 'Mitarbeiter', 'Bew.', 'Gültig bis', 'Status', 'Bemerkung']);
        for (const r of _kontrollePermitCache) {
            const statusText = !r.validTo
                ? 'keine Bewilligung erfasst'
                : r.daysUntil < 0
                    ? `${-r.daysUntil} Tag(e) überfällig`
                    : r.daysUntil === 0 ? 'läuft heute ab' : `in ${r.daysUntil} Tagen`;
            rows.push([
                r.employeeNumber || '',
                r.employeeName || '',
                r.permitCode || '',
                fmtDe(r.validTo),
                statusText,
                _kontrollePermitNote(r.employeeId)
            ]);
        }
        if (hasNacht) rows.push([], []);
    }

    // Sektion 4: Nachtarbeit-Nachweise fehlen
    if (hasNacht) {
        rows.push(['NACHTARBEIT-NACHWEISE FEHLEN']);
        rows.push(['Personal-Nr.', 'Mitarbeiter', 'Max 6 Wochen', 'Zeitraum', 'Grund', 'Bemerkung']);
        for (const r of _kontrolleNachtCache) {
            rows.push([
                r.employeeNumber || '',
                r.employeeName || '',
                (r.maxNaechte6Wochen != null ? r.maxNaechte6Wochen : ''),
                (r.windowFrom ? fmtDe(r.windowFrom) + '–' + fmtDe(r.windowTo) : ''),
                r.reason || '',
                _kontrolleNachtNote(r.employeeId)
            ]);
        }
    }

    const csv = rows.map(row =>
        (row || []).map(cell => {
            const s = String(cell ?? '');
            if (/[;"\n\r]/.test(s)) return '"' + s.replace(/"/g, '""') + '"';
            return s;
        }).join(';')
    ).join('\r\n');
    const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' });
    const filename = 'Kontrolle_Ausweis_' + new Date().toISOString().slice(0,10) + '.csv';
    if (typeof saveBlobAsk === 'function') {
        saveBlobAsk(blob, filename);
    } else {
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = filename;
        a.click();
        URL.revokeObjectURL(a.href);
    }
}

function _kontrolleExportCombiPdf() {
    const hasEmp    = (_kontrolleEmployeeCache || []).length > 0;
    const hasSpouse = (_kontrolleSpouseCache   || []).length > 0;
    const hasPermit = (_kontrollePermitCache   || []).length > 0;
    const hasNacht  = (_kontrolleNachtCache    || []).length > 0;
    if (!hasEmp && !hasSpouse && !hasPermit && !hasNacht) {
        alert('Keine Daten zum Exportieren — alle Listen sind leer.');
        return;
    }
    const today = new Date().toLocaleDateString('de-CH');
    const fmtDe = (iso) => {
        if (!iso) return '';
        const s = String(iso).slice(0, 10);
        if (s.length !== 10) return '';
        return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
    };

    const empRows = (_kontrolleEmployeeCache || []).map(r => `
        <tr>
            <td>${_kEsc(r.employeeNumber || '')}</td>
            <td>${_kEsc(r.employeeName || '')}</td>
            <td style="text-align:center">${_kEsc(r.kind === 'CH-Buerger' ? 'CH-Bürger' : 'C-Ausweis')}</td>
            <td>${_kEsc(r.reason || '')}</td>
            <td>${_kEsc(_kontrolleEmpNote(r.employeeId))}</td>
        </tr>`).join('');

    const spouseRows = (_kontrolleSpouseCache || []).map(r => `
        <tr>
            <td>${_kEsc(r.employeeNumber || '')}</td>
            <td>${_kEsc(r.employeeName || '')}</td>
            <td style="text-align:center">${_kEsc(r.employeeNationality || '')}</td>
            <td style="text-align:center">${_kEsc(r.employeePermitCode || '')}</td>
            <td>${_kEsc(r.spouseName || '')}</td>
            <td style="text-align:center">${_kEsc(r.spouseNationality || '')}</td>
            <td style="text-align:center">${_kEsc(r.spousePermitCode || '')}</td>
            <td>${_kEsc(r.reason || '')}</td>
            <td>${_kEsc(_kontrolleSpouseNote(r.employeeId))}</td>
        </tr>`).join('');

    const permitRows = (_kontrollePermitCache || []).map(r => {
        const statusText = !r.validTo
            ? 'keine Bewilligung erfasst'
            : r.daysUntil < 0
                ? `${-r.daysUntil} Tag(e) überfällig`
                : r.daysUntil === 0 ? 'läuft heute ab' : `in ${r.daysUntil} Tagen`;
        return `
        <tr>
            <td>${_kEsc(r.employeeNumber || '')}</td>
            <td>${_kEsc(r.employeeName || '')}</td>
            <td style="text-align:center">${_kEsc(r.permitCode || '')}</td>
            <td style="text-align:center">${_kEsc(fmtDe(r.validTo))}</td>
            <td>${_kEsc(statusText)}</td>
            <td>${_kEsc(_kontrollePermitNote(r.employeeId))}</td>
        </tr>`;
    }).join('');

    const empSection = hasEmp ? `
        <h2>Ausweis Mitarbeiter fehlt <span class="cnt">${_kontrolleEmployeeCache.length}</span></h2>
        <table>
            <thead>
                <tr><th>Pers.-Nr.</th><th>Mitarbeiter</th><th>Grundlage</th><th>Grund</th><th>Bemerkung</th></tr>
            </thead>
            <tbody>${empRows}</tbody>
        </table>` : '';

    const spouseSection = hasSpouse ? `
        <h2 style="margin-top:24px">Ausweis Ehegatte fehlt <span class="cnt">${_kontrolleSpouseCache.length}</span></h2>
        <table>
            <thead>
                <tr><th>Pers.-Nr.</th><th>Mitarbeiter</th><th>Nat.</th><th>Bew.</th><th>Ehepartner</th><th>Nat.</th><th>Bew.</th><th>Grund</th><th>Bemerkung</th></tr>
            </thead>
            <tbody>${spouseRows}</tbody>
        </table>` : '';

    const permitSection = hasPermit ? `
        <h2 style="margin-top:24px">Bewilligungen laufen ab <span class="cnt">${_kontrollePermitCache.length}</span></h2>
        <table>
            <thead>
                <tr><th>Pers.-Nr.</th><th>Mitarbeiter</th><th>Bew.</th><th>Gültig bis</th><th>Status</th><th>Bemerkung</th></tr>
            </thead>
            <tbody>${permitRows}</tbody>
        </table>` : '';

    const nachtRows = (_kontrolleNachtCache || []).map(r => `
        <tr>
            <td>${_kEsc(r.employeeNumber || '')}</td>
            <td>${_kEsc(r.employeeName || '')}</td>
            <td style="text-align:center">${r.maxNaechte6Wochen != null ? r.maxNaechte6Wochen : ''}</td>
            <td style="text-align:center">${r.windowFrom ? _kEsc(fmtDe(r.windowFrom) + '–' + fmtDe(r.windowTo)) : ''}</td>
            <td>${_kEsc(r.reason || '')}</td>
            <td>${_kEsc(_kontrolleNachtNote(r.employeeId))}</td>
        </tr>`).join('');

    const nachtSection = hasNacht ? `
        <h2 style="margin-top:24px">Nachtarbeit-Nachweise fehlen <span class="cnt">${_kontrolleNachtCache.length}</span></h2>
        <table>
            <thead>
                <tr><th>Pers.-Nr.</th><th>Mitarbeiter</th><th>Max 6W</th><th>Zeitraum</th><th>Grund</th><th>Bemerkung</th></tr>
            </thead>
            <tbody>${nachtRows}</tbody>
        </table>
        <div style="color:#94a3b8;font-size:10px;margin-top:4px">Warnfall: mehr als 18 gearbeitete Nächte in einem 6-Wochen-Fenster (ArGV1 Art. 30) ohne vollständige Nachweise (Arztzeugnis/Verzicht + Ausnahmeregelung).</div>` : '';

    const html = `<!doctype html><html><head><meta charset="utf-8"><title>Kontrolle — Lücken-Erkennung</title>
        <style>
            body { font-family: -apple-system, system-ui, sans-serif; margin: 24px; color: #0f172a; }
            h1 { color:#991b1b; font-size:18px; margin:0 0 4px; }
            h2 { color:#991b1b; font-size:14px; margin:0 0 8px; font-weight:700; }
            h2 .cnt { color:#94a3b8; font-weight:400; font-size:12px; margin-left:6px }
            .sub { color:#64748b; font-size:12px; margin-bottom:18px; }
            table { width:100%; border-collapse:collapse; font-size:11px; margin-bottom:8px }
            th, td { border:1px solid #e2e8f0; padding:5px 7px; text-align:left; vertical-align:top }
            th { background:#fef2f2; color:#7f1d1d; font-weight:700; }
            tr:nth-child(even) td { background:#fafafa }
            .toolbar { position:sticky; top:0; background:#fff; padding:8px 0 12px; margin-bottom:6px; border-bottom:1px solid #e2e8f0; display:flex; gap:8px; }
            .toolbar button { font-size:13px; padding:7px 14px; border-radius:7px; cursor:pointer; border:1px solid #cbd5e1; background:#fff; color:#0f172a; font-weight:600; }
            .toolbar button.primary { background:#1a1a1a; border-color:#1a1a1a; color:#fff; }
            @media print { body { margin: 10mm } h2 { page-break-after:avoid } .noprint { display:none !important } }
        </style></head><body>
        <div class="toolbar noprint">
            <button onclick="window.close()">← Schliessen</button>
            <button class="primary" onclick="window.print()">🖨 Drucken / PDF</button>
        </div>
        <h1>⚠ Kontrolle — Lücken-Erkennung</h1>
        <div class="sub">Stand ${today}</div>
        ${empSection}
        ${spouseSection}
        ${permitSection}
        ${nachtSection}
    </body></html>`;
    const w = window.open('', '_blank');
    if (!w) {
        alert('Pop-up-Blocker aktiv? Bitte für diese Seite Pop-ups erlauben.');
        return;
    }
    w.document.write(html);
    w.document.close();
}

async function kontrolleSpouseRefresh() {
    const el = document.getElementById('kontrolleSpouseList');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder" style="height:120px"><span>Lade Liste…</span></div>';

    const cpId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : '';
    const url = cpId ? `/api/kontrolle/spouse-doku-fehlt?companyProfileId=${cpId}` : '/api/kontrolle/spouse-doku-fehlt';
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            el.innerHTML = '<div class="emp-placeholder" style="height:120px;color:#dc2626"><span>Fehler beim Laden (' + r.status + ')</span></div>';
            return;
        }
        const list = await r.json();
        _kontrolleSpouseCache = Array.isArray(list) ? list : [];
        if (!Array.isArray(list) || list.length === 0) {
            el.innerHTML = `<div style="padding:24px;text-align:center;color:#16a34a;font-size:14px;font-weight:600">
                ✓ Keine offenen Lücken — bei allen MA ist der Ehegatten-Ausweis hinterlegt.
            </div>`;
            return;
        }
        el.innerHTML = `
            <div style="padding:12px 18px 14px;color:#7f1d1d;font-size:12.5px;font-weight:600">
                ${list.length} MA mit fehlendem Ehegatten-Ausweis
                <span style="color:#94a3b8;font-weight:400;font-size:11.5px;margin-left:8px">· Bemerkungen optional — werden ins Excel/PDF übernommen, beim Aktualisieren zurückgesetzt</span>
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead>
                    <tr style="background:#fef2f2;border-top:1px solid #fee2e2;border-bottom:1px solid #fecaca">
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Mitarbeiter</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Nat.</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Bew.</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Ehepartner</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Grund</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Bemerkung</th>
                        <th style="padding:9px 14px;text-align:right;color:#7f1d1d">Aktion</th>
                    </tr>
                </thead>
                <tbody>
                    ${list.map(r => `
                        <tr style="border-bottom:1px solid #f1f5f9">
                            <td style="padding:9px 14px">
                                <div style="font-weight:600;color:#0f172a">${_e(r.employeeName)}</div>
                                <div style="font-size:11.5px;color:#64748b">Nr. ${_e(r.employeeNumber)}</div>
                            </td>
                            <td style="padding:9px 14px;font-family:monospace;font-weight:600">${_e(r.employeeNationality || '–')}</td>
                            <td style="padding:9px 14px;font-family:monospace;font-weight:600">${_e(r.employeePermitCode || '–')}</td>
                            <td style="padding:9px 14px">
                                <div style="color:#475569">${_e(r.spouseName)}</div>
                                <div style="font-size:11.5px;color:#64748b">
                                    ${r.spouseNationality ? 'Nat. ' + _e(r.spouseNationality) : ''}
                                    ${r.spousePermitCode ? ' · ' + _e(r.spousePermitCode) : ''}
                                </div>
                            </td>
                            <td style="padding:9px 14px;color:#7f1d1d;font-size:12px">${_e(r.reason)}</td>
                            <td style="padding:6px 12px">
                                <input type="text" id="kontrolleSpouseNote-${r.employeeId}" placeholder="Notiz…"
                                       style="width:100%;padding:5px 8px;border:1px solid #e2e8f0;border-radius:4px;font-size:12px;background:#fff;color:#0f172a">
                            </td>
                            <td style="padding:9px 14px;text-align:right;white-space:nowrap">
                                <button onclick="kontrolleOpenEmployee(${r.employeeId})"
                                        class="btn btn-primary" style="padding:5px 12px;font-size:12px">
                                    → Zum MA
                                </button>
                            </td>
                        </tr>
                    `).join('')}
                </tbody>
            </table>`;
    } catch (e) {
        el.innerHTML = '<div class="emp-placeholder" style="height:120px;color:#dc2626"><span>Verbindungsfehler: ' + _e(e.message) + '</span></div>';
    }
}

/**
 * Springt in den Familie-Tab des MA, damit Walter sofort den Ehepartner
 * sehen + das fehlende Dokument hochladen kann.
 */
function kontrolleOpenEmployee(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof showPage === 'function') showPage('mitarbeiter');
    setTimeout(() => {
        if (typeof switchEmpTab === 'function') switchEmpTab('familie');
    }, 300);
}

// ══════════════════════════════════════════════════════════════════════
// Walter-Vorgabe 07.06.2026: Export der Kontroll-Liste als Excel/PDF.
// ══════════════════════════════════════════════════════════════════════
function kontrolleSpouseExportExcel() {
    // Walter-Vorgabe 13.06.2026: alle Excel-Buttons liefern jetzt das gleiche
    // Sammeldokument (MA + Ehepartner). Egal welcher Button geklickt wird.
    return _kontrolleExportCombiExcel();
}
function kontrolleSpouseExportPdf() {
    return _kontrolleExportCombiPdf();
}

// Die alten Single-Section-Funktionen bleiben unten als toter Code stehen,
// falls Walter irgendwann doch Einzel-Exports will. Aktuell unerreichbar.
function _kontrolleSpouseExportExcel_LEGACY() {
    if (!_kontrolleSpouseCache || _kontrolleSpouseCache.length === 0) {
        alert('Keine Daten zum Exportieren — Liste ist leer oder noch nicht geladen.');
        return;
    }
    // CSV mit Excel-kompatiblen Einstellungen:
    //   • Semicolon-Separator (CH-Excel-Standard)
    //   • UTF-8 BOM für Umlaute
    //   • Felder in Anführungszeichen wenn nötig
    const rows = [
        ['Personal-Nr.', 'Mitarbeiter', 'Nat. MA', 'Bewilligung MA', 'Ehepartner', 'Nat. Ehepartner', 'Bewilligung Ehepartner', 'Grund']
    ];
    for (const r of _kontrolleSpouseCache) {
        rows.push([
            r.employeeNumber || '',
            r.employeeName || '',
            r.employeeNationality || '',
            r.employeePermitCode || '',
            r.spouseName || '',
            r.spouseNationality || '',
            r.spousePermitCode || '',
            r.reason || ''
        ]);
    }
    const csv = rows.map(row =>
        row.map(cell => {
            const s = String(cell ?? '');
            // Wenn Semicolon, Anführungszeichen oder Newline → quoten
            if (/[;"\n\r]/.test(s)) return '"' + s.replace(/"/g, '""') + '"';
            return s;
        }).join(';')
    ).join('\r\n');
    const blob = new Blob(['﻿' + csv], { type: 'text/csv;charset=utf-8' });
    const filename = 'Kontrolle_Ausweis_Ehegatte_fehlt_' + new Date().toISOString().slice(0,10) + '.csv';
    if (typeof saveBlobAsk === 'function') {
        saveBlobAsk(blob, filename);
    } else {
        // Fallback (sollte nicht passieren — save-blob.js wird in index.html geladen)
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = filename;
        a.click();
        URL.revokeObjectURL(a.href);
    }
}

function _kontrolleSpouseExportPdf_LEGACY() {
    if (!_kontrolleSpouseCache || _kontrolleSpouseCache.length === 0) {
        alert('Keine Daten zum Exportieren — Liste ist leer oder noch nicht geladen.');
        return;
    }
    // Druckbare HTML-Seite in neuem Fenster — der Browser-eigene Druckdialog
    // erzeugt das PDF („Speichern als PDF"). Spart Backend-PDF-Generator.
    const rows = _kontrolleSpouseCache.map(r => `
        <tr>
            <td>${_kEsc(r.employeeNumber || '')}</td>
            <td>${_kEsc(r.employeeName || '')}</td>
            <td style="text-align:center">${_kEsc(r.employeeNationality || '')}</td>
            <td style="text-align:center">${_kEsc(r.employeePermitCode || '')}</td>
            <td>${_kEsc(r.spouseName || '')}</td>
            <td style="text-align:center">${_kEsc(r.spouseNationality || '')}</td>
            <td style="text-align:center">${_kEsc(r.spousePermitCode || '')}</td>
            <td>${_kEsc(r.reason || '')}</td>
        </tr>`).join('');
    const today = new Date().toLocaleDateString('de-CH');
    const html = `<!doctype html><html><head><meta charset="utf-8"><title>Kontrolle — Ausweis Ehegatte fehlt</title>
        <style>
            body { font-family: -apple-system, system-ui, sans-serif; margin: 24px; color: #0f172a; }
            h1 { color:#991b1b; font-size:18px; margin:0 0 4px; }
            .sub { color:#64748b; font-size:12px; margin-bottom:14px; }
            table { width:100%; border-collapse:collapse; font-size:11.5px }
            th, td { border:1px solid #e2e8f0; padding:6px 8px; text-align:left; vertical-align:top }
            th { background:#fef2f2; color:#7f1d1d; font-weight:700; }
            tr:nth-child(even) td { background:#fafafa }
            @media print { body { margin: 10mm } }
        </style></head><body>
        <h1>⚠ Kontrolle — Ausweis Ehegatte fehlt</h1>
        <div class="sub">${_kontrolleSpouseCache.length} MA · Stand ${today}</div>
        <table>
            <thead>
                <tr>
                    <th>Pers.-Nr.</th><th>Mitarbeiter</th><th>Nat.</th><th>Bew.</th>
                    <th>Ehepartner</th><th>Nat.</th><th>Bew.</th><th>Grund</th>
                </tr>
            </thead>
            <tbody>${rows}</tbody>
        </table>
        <script>window.onload=()=>window.print();</script>
    </body></html>`;
    const w = window.open('', '_blank');
    if (!w) {
        alert('Pop-up-Blocker aktiv? Bitte für diese Seite Pop-ups erlauben.');
        return;
    }
    w.document.write(html);
    w.document.close();
}

// HTML-Escape (lokal, damit unabhängig vom Page-Kontext)
function _kEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({
        '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'
    }[c]));
}
