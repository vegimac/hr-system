// ══════════════════════════════════════════════════════════════════════
// absence-report.js — Auswertung Krankheit / Unfall / Mutter-Vater
// ══════════════════════════════════════════════════════════════════════
// Zwei Modi:
//   • branch (Default): pro Filiale aus der Sidebar
//                       → /api/reports/absences/branch/{cpid}?year=YYYY
//   • cross:            filialübergreifend (Top-Liste über alle Filialen)
//                       → /api/reports/absences/cross-branch?year=YYYY
//                       → Tabelle bekommt eine Filiale-Spalte.
//
// Tabelle nach Total-Tage absteigend; Klick auf Zeile = Drilldown ein/aus.
// CSV-Export der Übersicht (Excel-kompatibel, UTF-8 BOM).

let _arMode = 'branch';                 // 'branch' | 'cross'
let _arRows = [];
let _arYear = (new Date()).getFullYear();
let _arExpanded = new Set();
const _AR_LABEL = {
    KRANK: 'Krankheit', UNFALL: 'Unfall', MUTT_VATER: 'Mutter-/Vaterschaft'
};

function arInit() {
    // Modus bleibt erhalten (Walter kann zwischen den Pages wechseln, ohne
    // immer wieder umzuschalten). Nur die Ergebnisse werden zurückgesetzt.
    arRefreshBanner();
    arUpdateModeButtons();
    document.getElementById('arSummary').innerHTML = '';
    document.getElementById('arTable').innerHTML = '';
    document.getElementById('arAlert').innerHTML = '';
    arSetExportButtonsEnabled(false);
    _arRows = [];
    _arExpanded = new Set();
    const yearInp = document.getElementById('arYearInput');
    if (yearInp && !yearInp.value) yearInp.value = _arYear;
}

function arSetExportButtonsEnabled(enabled) {
    const xlsx = document.getElementById('arExportXlsxBtn');
    const pdf  = document.getElementById('arExportPdfBtn');
    if (xlsx) xlsx.disabled = !enabled;
    if (pdf)  pdf.disabled  = !enabled;
}

function arRefreshBanner() {
    const banner = document.getElementById('arBranchBanner');
    if (!banner) return;
    if (_arMode === 'cross') {
        banner.innerHTML = `<b>Modus:</b> Alle Filialen <span style="color:#94a3b8">— filialübergreifende Top-Liste der schlimmsten Krank-/Unfall-/Mutter-Vater-Absenzen</span>`;
        return;
    }
    const cid = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                  ? fixedCompanyProfileId : null;
    if (cid && typeof allBranches !== 'undefined' && Array.isArray(allBranches)) {
        const b = allBranches.find(x => x.id === cid);
        if (b) {
            const code = b.restaurantCode ? '#' + b.restaurantCode + ' · ' : '';
            const bn   = b.branchName || b.companyName || '–';
            banner.innerHTML = `<b>Filiale:</b> ${code}${bn} <span style="color:#94a3b8">— wird aus dem Hauptmenü übernommen</span>`;
            return;
        }
    }
    banner.innerHTML = `<span style="color:#92400e">⚠️ Keine Filiale gewählt — bitte oben links in der Sidebar eine Filiale wählen oder oben „Alle Filialen" wählen.</span>`;
}

// Modus-Umschalter — wirkt SOFORT auf die UI (Banner, Spalten-Vorbereitung),
// startet aber keine Auswertung neu. Walter klickt anschließend „Auswerten".
function arSetMode(mode) {
    if (mode !== 'branch' && mode !== 'cross') return;
    if (_arMode === mode) return;
    _arMode = mode;
    _arRows = [];
    _arExpanded = new Set();
    arRefreshBanner();
    arUpdateModeButtons();
    document.getElementById('arSummary').innerHTML = '';
    document.getElementById('arTable').innerHTML = '';
    arSetExportButtonsEnabled(false);
}

function arUpdateModeButtons() {
    const bBranch = document.getElementById('arModeBranchBtn');
    const bCross  = document.getElementById('arModeCrossBtn');
    if (!bBranch || !bCross) return;
    const active   = 'background:#6b7280;color:white;border-color:#6b7280';
    const inactive = 'background:white;color:#475569;border-color:#cbd5e1';
    bBranch.setAttribute('style',
        `padding:7px 14px;border:1px solid;border-radius:7px 0 0 7px;font-size:13px;font-weight:600;cursor:pointer;margin-right:-1px;${_arMode === 'branch' ? active : inactive}`);
    bCross.setAttribute('style',
        `padding:7px 14px;border:1px solid;border-radius:0 7px 7px 0;font-size:13px;font-weight:600;cursor:pointer;${_arMode === 'cross' ? active : inactive}`);
}

// onBranchChange-Handler aus index.html — in Cross-Mode bleibt alles bestehen.
function arOnBranchChange() {
    if (_arMode === 'cross') {
        arRefreshBanner();   // Banner-Text bleibt eh „Alle Filialen", aber sicher ist sicher
        return;
    }
    arInit();
}

function _arFmtDate(iso) {
    if (!iso) return '';
    const p = String(iso).slice(0, 10).split('-');
    return p.length === 3 ? `${p[2]}.${p[1]}.${p[0]}` : iso;
}

function _arTile(label, value, color) {
    return `<div style="background:white;border:1px solid #e2e8f0;border-radius:9px;padding:10px 14px">
        <div style="font-size:11px;color:#64748b;text-transform:uppercase;letter-spacing:.05em">${label}</div>
        <div style="font-size:22px;font-weight:700;color:${color};margin-top:2px">${value}</div>
    </div>`;
}

async function arRun() {
    const alertBox = document.getElementById('arAlert');
    const year     = parseInt(document.getElementById('arYearInput').value, 10) || _arYear;
    _arYear = year;
    _arExpanded = new Set();
    arSetExportButtonsEnabled(false);

    let url;
    if (_arMode === 'branch') {
        const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                           ? String(fixedCompanyProfileId) : '';
        if (!branchId) {
            alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Bitte zuerst oben links in der Sidebar eine Filiale wählen — oder oben den Modus auf „Alle Filialen" stellen.</div>`;
            return;
        }
        url = `/api/reports/absences/branch/${branchId}?year=${year}`;
    } else {
        url = `/api/reports/absences/cross-branch?year=${year}`;
    }

    alertBox.innerHTML = `
        <div style="padding:14px 18px;background:#fef3c7;border:1px solid #fde68a;border-radius:9px;display:flex;gap:14px;align-items:center">
            <div class="import-spinner" style="border-color:#fde68a;border-top-color:#a16207;width:24px;height:24px"></div>
            <div style="font-weight:600;color:#78350f;font-size:14px">Auswertung läuft…</div>
        </div>`;

    try {
        const r = await fetch(url, {
            headers: { 'Authorization': `Bearer ${authToken}` }
        });
        if (!r.ok) {
            let errMsg = 'HTTP ' + r.status;
            try { const j = await r.json(); errMsg = j.error || errMsg; } catch {}
            alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Fehler: ${errMsg}</div>`;
            return;
        }
        const data = await r.json();
        _arRows = data.rows || [];
        renderAbsenceReport(data);
        alertBox.innerHTML = '';
        arSetExportButtonsEnabled(_arRows.length > 0);
    } catch (e) {
        alertBox.innerHTML = `<div style="padding:10px 14px;background:#fef2f2;color:#b91c1c;border-radius:7px;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    }
}

function renderAbsenceReport(data) {
    const summary = document.getElementById('arSummary');
    const tableEl = document.getElementById('arTable');
    const isCross = _arMode === 'cross';

    // Summary: in Cross-Mode zusätzlich „Anzahl Filialen"
    const tiles = [
        _arTile('MA mit Absenzen', data.totalEmployees, '#475569'),
        _arTile('Total Fälle',     data.totalCases,     '#6b6152'),
        _arTile('Total Tage',      data.totalDays,      '#b91c1c'),
    ];
    if (isCross && data.totalBranches != null) {
        tiles.push(_arTile('Filialen betroffen', data.totalBranches, '#5b21b6'));
    }
    summary.innerHTML = `<div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px">${tiles.join('')}</div>`;

    if (!_arRows.length) {
        const where = isCross ? 'in irgendeiner Filiale' : 'für diese Filiale';
        tableEl.innerHTML = `<div style="padding:24px;text-align:center;color:#64748b;background:white;border:1px solid #e2e8f0;border-radius:9px">Keine Krank-/Unfall-/Mutter-Absenzen im Jahr ${data.year} ${where}.</div>`;
        return;
    }

    // Anzahl Header-Zellen — wird auch im Detail-Row colspan gebraucht.
    const headerCols = isCross ? 10 : 8;

    const rowsHtml = _arRows.map((r, idx) => {
        const expanded = _arExpanded.has(r.employeeId);
        const expandIcon = expanded ? '▾' : '▸';
        const inact = r.isActive === false
            ? ' <span style="font-size:10px;background:#fee2e2;color:#b91c1c;padding:1px 6px;border-radius:6px;font-weight:600">inaktiv</span>'
            : '';
        const cell = (faelle, tage, color) => {
            if (faelle === 0) return '<span style="color:#cbd5e1">–</span>';
            return `<span style="color:${color};font-weight:600">${tage}</span> <span style="color:#94a3b8;font-size:11px">(${faelle})</span>`;
        };
        // Rang-Zelle nur in Cross-Mode — die Top-Liste lebt vom Ranking.
        const rankCell = isCross
            ? `<td style="padding:10px 12px;text-align:center;color:#475569;font-weight:700">${idx + 1}</td>`
            : '';
        // Filiale-Zelle nur in Cross-Mode.
        const branchLabel = r.restaurantCode
            ? `<span style="font-size:11px;color:#94a3b8">#${r.restaurantCode}</span> ${r.branchName || ''}`
            : (r.branchName || '<span style="color:#cbd5e1">–</span>');
        const branchCell = isCross
            ? `<td style="padding:10px 12px;color:#475569">${branchLabel}</td>`
            : '';

        const mainRow = `
            <tr style="background:white;border-bottom:1px solid #f1f5f9;font-size:13px;cursor:pointer"
                onclick="arToggleDetail(${r.employeeId})">
                ${rankCell}
                <td style="padding:10px 12px;color:#94a3b8;width:24px">${expandIcon}</td>
                <td style="padding:10px 12px"><b>${r.firstName} ${r.lastName}</b>${inact}</td>
                <td style="padding:10px 12px;color:#64748b">${r.employeeNumber || '–'}</td>
                ${branchCell}
                <td style="padding:10px 12px">
                    ${r.employmentModel ? `<span style="font-size:10px;background:#f1f5f9;color:#475569;padding:1px 6px;border-radius:8px">${r.employmentModel}</span>` : '<span style="color:#cbd5e1">–</span>'}
                </td>
                <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums">${cell(r.krankFaelle,     r.krankTage,     '#b91c1c')}</td>
                <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums">${cell(r.unfallFaelle,    r.unfallTage,    '#9a3412')}</td>
                <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums">${cell(r.muttVaterFaelle, r.muttVaterTage, '#5b21b6')}</td>
                <td style="padding:10px 12px;text-align:right;font-variant-numeric:tabular-nums;font-weight:700;color:#0f172a">${r.totalTage}</td>
            </tr>`;

        let detailRow = '';
        if (expanded) {
            const details = (r.details || []).map(d => `
                <tr style="font-size:12px;background:#fafafa;border-bottom:1px solid #f1f5f9">
                    <td style="padding:6px 12px" colspan="${isCross ? 3 : 2}"></td>
                    <td style="padding:6px 12px;color:#475569">${_AR_LABEL[d.absenceType] || d.absenceType}</td>
                    <td style="padding:6px 12px;color:#475569;white-space:nowrap" colspan="${isCross ? 2 : 1}">${_arFmtDate(d.dateFrom)} – ${_arFmtDate(d.dateTo)}</td>
                    <td style="padding:6px 12px;text-align:right;color:#475569" colspan="3">${d.daysInYear} ${d.daysInYear === 1 ? 'Tag' : 'Tage'}${d.prozent && d.prozent < 100 ? ` <span style="color:#94a3b8">(${d.prozent}%)</span>` : ''}</td>
                    <td style="padding:6px 12px;color:#94a3b8;font-size:11px">${d.notes || ''}</td>
                </tr>`).join('');
            detailRow = `<tr><td colspan="${headerCols}" style="padding:0;background:#fafafa">
                <table style="width:100%;border-collapse:collapse">${details}</table>
            </td></tr>`;
        }
        return mainRow + detailRow;
    }).join('');

    const rankHeader   = isCross ? '<th style="padding:10px 12px;text-align:center;width:42px">#</th>' : '';
    const branchHeader = isCross ? '<th style="padding:10px 12px">Filiale</th>'                       : '';

    tableEl.innerHTML = `
        <div style="background:white;border:1px solid #e2e8f0;border-radius:9px;overflow:hidden">
            <table style="width:100%;border-collapse:collapse">
                <thead>
                    <tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0;font-size:12px;color:#475569;text-align:left">
                        ${rankHeader}
                        <th style="padding:10px 12px;width:24px"></th>
                        <th style="padding:10px 12px">Mitarbeiter:in</th>
                        <th style="padding:10px 12px">Personal-Nr</th>
                        ${branchHeader}
                        <th style="padding:10px 12px">Modell</th>
                        <th style="padding:10px 12px;text-align:right">Krank (Tage / Fälle)</th>
                        <th style="padding:10px 12px;text-align:right">Unfall (Tage / Fälle)</th>
                        <th style="padding:10px 12px;text-align:right">Mutter / Vater (Tage / Fälle)</th>
                        <th style="padding:10px 12px;text-align:right">Total Tage</th>
                    </tr>
                </thead>
                <tbody>${rowsHtml}</tbody>
            </table>
        </div>
        <div style="margin-top:10px;font-size:11.5px;color:#94a3b8;line-height:1.5">
            Tage = Kalendertage des Absenz-Zeitraums, beschnitten aufs Berichtsjahr (eine Krankheit Dez–Feb zählt im Jahres-Report nur die Tage, die ins Jahr fallen). Sortiert nach Total-Tage absteigend${isCross ? ' — Rang #1 hat den grössten Ausfall' : ''}.
        </div>`;
}

function arToggleDetail(empId) {
    if (_arExpanded.has(empId)) _arExpanded.delete(empId);
    else                         _arExpanded.add(empId);
    // Re-render mit den vorhandenen Zeilen — Summary-Werte aus _arRows neu berechnen.
    const totalEmployees = _arRows.length;
    const totalCases     = _arRows.reduce((s, r) => s + (r.totalFaelle || 0), 0);
    const totalDays      = _arRows.reduce((s, r) => s + (r.totalTage   || 0), 0);
    const totalBranches  = _arMode === 'cross'
        ? new Set(_arRows.map(r => r.companyProfileId).filter(x => x != null)).size
        : undefined;
    renderAbsenceReport({ year: _arYear, totalEmployees, totalCases, totalDays, totalBranches });
}

// Server-seitiger Export — XLSX und PDF werden vom Backend generiert
// (NPOI / QuestPDF) und als Blob zum Download geliefert.
async function arExportXlsx() { return _arExport('xlsx'); }
async function arExportPdf()  { return _arExport('pdf'); }

async function _arExport(format) {
    if (!_arRows.length) return;
    const year = _arYear;
    let url;
    if (_arMode === 'branch') {
        const branchId = (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId)
                           ? String(fixedCompanyProfileId) : '';
        if (!branchId) return;
        url = `/api/reports/absences/branch/${branchId}/${format}?year=${year}`;
    } else {
        url = `/api/reports/absences/cross-branch/${format}?year=${year}`;
    }
    try {
        const r = await fetch(url, { headers: { 'Authorization': `Bearer ${authToken}` } });
        if (!r.ok) {
            alert(`Export-Fehler: HTTP ${r.status}`);
            return;
        }
        const blob = await r.blob();
        // Dateiname aus Content-Disposition wenn vorhanden, sonst Fallback.
        const cd = r.headers.get('Content-Disposition') || '';
        const m = /filename\*?=(?:UTF-8'')?["']?([^;"']+)["']?/i.exec(cd);
        const suffix = _arMode === 'cross' ? 'alle-filialen' : 'filiale';
        const filename = m ? decodeURIComponent(m[1]) : `absenz-auswertung-${suffix}-${year}.${format}`;
        // PDF → Vorschaufenster; Excel/CSV → direkt speichern (entscheidet previewFileModal).
        await previewFileModal(blob, filename);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}
