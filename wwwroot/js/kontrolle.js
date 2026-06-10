// ══════════════════════════════════════════════════════════════════════
// kontrolle.js — Kontroll-Listen im HR-Bereich
// ──────────────────────────────────────────────────────────────────────
// Walter-Vorgabe 07.06.2026: proaktive Lücken-Erkennung. Erste Liste:
// MA mit QST-Bezug auf Ehegatten ohne hinterlegtes Spouse-Doku.
// Weitere Listen folgen.
// ══════════════════════════════════════════════════════════════════════

let _kontrolleSpouseCache = [];

function kontrolleInit() {
    kontrolleSpouseRefresh();
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
            </div>
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead>
                    <tr style="background:#fef2f2;border-top:1px solid #fee2e2;border-bottom:1px solid #fecaca">
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Mitarbeiter</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Nat.</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Bew.</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Ehepartner</th>
                        <th style="padding:9px 14px;text-align:left;color:#7f1d1d">Grund</th>
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

function kontrolleSpouseExportPdf() {
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
