// ══════════════════════════════════════════════════════════════════════
// dok-protokoll.js — Upload-Protokoll für Buchhaltung / Bommer
// (Walter-Vorgabe 22.07.2026)
//
// Zeigt wer wann welches Dokument in die MA-Akte hochgeladen hat.
// Seit Unterlagen direkt in OneCrew landen (nicht mehr über BommerBox),
// braucht die Buchhaltung diese Übersicht + CSV-Export.
//
// Filiale: NUR der globale Sidebar-Selektor (Walter 22.07.2026).
//   • konkrete Filiale → nur diese
//   • «Alle Filialen» (nur admin/superuser) → alle erlaubten
//   • buchhaltung/user: Sidebar zeigt nur zugeteilte Filialen; Server
//     prüft zusätzlich hart über user_branch_access.
// ══════════════════════════════════════════════════════════════════════

let _dpRows = [];

function dpEsc(s) {
    if (typeof esc === 'function') return esc(s);
    return String(s ?? '').replace(/[&<>"']/g, c => (
        { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]
    ));
}

function dpInit() {
    const today = new Date();
    const monthAgo = new Date(today.getTime() - 30 * 86400000);
    const iso = d => d.toISOString().slice(0, 10);
    const fromEl = document.getElementById('dpFrom');
    const toEl = document.getElementById('dpTo');
    if (fromEl && !fromEl.value) fromEl.value = iso(monthAgo);
    if (toEl && !toEl.value) toEl.value = iso(today);

    dpSyncBranchLabel();
    dpLoad();
}

function dpSyncBranchLabel() {
    const lbl = document.getElementById('dpBranchLabel');
    if (!lbl) return;
    if (!fixedCompanyProfileId) {
        lbl.textContent = 'Alle Filialen';
        return;
    }
    const b = (typeof allBranches !== 'undefined' ? allBranches : [])
        .find(x => x.id === fixedCompanyProfileId);
    lbl.textContent = b
        ? `${b.restaurantCode ? b.restaurantCode + ' – ' : ''}${b.branchName || b.companyName}`
        : 'Alle Filialen';
}

function dpBuildParams() {
    const p = new URLSearchParams();
    const from = document.getElementById('dpFrom')?.value;
    const to = document.getElementById('dpTo')?.value;
    const q = document.getElementById('dpSearch')?.value?.trim();
    const lim = document.getElementById('dpLimit')?.value || '500';
    if (from) p.set('from', from);
    if (to) p.set('to', to);
    if (q) p.set('q', q);
    p.set('limit', lim);
    // Nur wenn Sidebar eine konkrete Filiale hat — sonst Server = erlaubte Filialen.
    if (fixedCompanyProfileId)
        p.set('companyProfileId', String(fixedCompanyProfileId));
    return p;
}

async function dpLoad() {
    dpSyncBranchLabel();
    const mount = document.getElementById('dpResults');
    const info = document.getElementById('dpInfo');
    if (mount) mount.innerHTML = '<div style="padding:28px;text-align:center;color:#94a3b8">Lade…</div>';
    try {
        const r = await fetch('/api/documents/upload-protocol?' + dpBuildParams().toString(), { headers: ah() });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try {
                const j = await r.json();
                if (j?.message) msg = j.message;
                else if (j?.error) msg = j.error;
                else if (j?.title) msg = j.title + (j.detail ? ': ' + j.detail : '');
            } catch (_) {}
            if (mount) mount.innerHTML = `<div style="padding:28px;text-align:center;color:#dc2626">${dpEsc(msg)}</div>`;
            return;
        }
        const data = await r.json();
        _dpRows = data.items || [];
        if (info) info.textContent = `${_dpRows.length} Einträge`;
        dpRender();
    } catch (e) {
        if (mount) mount.innerHTML = `<div style="padding:28px;text-align:center;color:#dc2626">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function dpFmt(iso) {
    if (!iso) return '—';
    const d = new Date(iso);
    if (isNaN(d)) return iso;
    return d.toLocaleString('de-CH', {
        day: '2-digit', month: '2-digit', year: 'numeric',
        hour: '2-digit', minute: '2-digit'
    });
}

function dpFmtSize(n) {
    if (n == null) return '—';
    if (n < 1024) return n + ' B';
    if (n < 1024 * 1024) return (n / 1024).toFixed(1) + ' KB';
    return (n / (1024 * 1024)).toFixed(1) + ' MB';
}

function dpRender() {
    const mount = document.getElementById('dpResults');
    if (!mount) return;
    if (!_dpRows.length) {
        mount.innerHTML = '<div style="padding:28px;text-align:center;color:#94a3b8">Keine Uploads im gewählten Zeitraum.</div>';
        return;
    }
    const rows = _dpRows.map(r => {
        const ma = `${r.firstName || ''} ${r.lastName || ''}`.trim() || '—';
        return `<tr>
            <td style="white-space:nowrap;color:#64748b;font-size:12.5px">${dpEsc(dpFmt(r.hochgeladenAm))}</td>
            <td style="font-weight:600">${dpEsc(r.hochgeladenVon || '—')}</td>
            <td>
                <div style="font-weight:600">${dpEsc(ma)}</div>
                <div style="font-size:11px;color:#94a3b8">${dpEsc(r.employeeNumber || '')}</div>
            </td>
            <td><span class="badge b-code" style="font-size:10px">${dpEsc(r.branchCode || '—')}</span></td>
            <td>
                <div style="font-size:12.5px">${dpEsc(r.kategorie || '')}</div>
                <div style="font-size:11px;color:#64748b">${dpEsc(r.dokumentTyp || '')}</div>
            </td>
            <td>
                <div style="font-size:12.5px;word-break:break-word">${dpEsc(r.filename || '')}</div>
                <div style="font-size:11px;color:#94a3b8">${dpEsc(dpFmtSize(r.groesseBytes))}${r.bemerkung ? ' · ' + dpEsc(r.bemerkung) : ''}</div>
            </td>
            <td style="text-align:right;white-space:nowrap">
                <button class="btn btn-sm btn-outline" onclick="dpOpenEmployee(${r.employeeId})" title="Mitarbeiter öffnen">MA</button>
            </td>
        </tr>`;
    }).join('');

    mount.innerHTML = `
    <div class="card" style="padding:0;overflow:auto">
      <table style="width:100%;border-collapse:collapse;font-size:13px">
        <thead>
          <tr style="background:#f1f5f9;border-bottom:1px solid #e2e8f0">
            <th style="padding:10px 12px;text-align:left;font-size:11px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Datum</th>
            <th style="padding:10px 12px;text-align:left;font-size:11px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Hochgeladen von</th>
            <th style="padding:10px 12px;text-align:left;font-size:11px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Mitarbeiter</th>
            <th style="padding:10px 12px;text-align:left;font-size:11px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Filiale</th>
            <th style="padding:10px 12px;text-align:left;font-size:11px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Kategorie / Typ</th>
            <th style="padding:10px 12px;text-align:left;font-size:11px;font-weight:700;color:#64748b;text-transform:uppercase;letter-spacing:.04em">Datei</th>
            <th style="padding:10px 12px"></th>
          </tr>
        </thead>
        <tbody>${rows}</tbody>
      </table>
    </div>`;
}

function dpOpenEmployee(empId) {
    if (!empId) return;
    window.activeEmpId = empId;
    if (typeof showPage === 'function') showPage('mitarbeiter');
}

async function dpExportCsv() {
    try {
        const p = dpBuildParams();
        p.delete('limit');
        const r = await fetch('/api/documents/upload-protocol/export?' + p.toString(), { headers: ah() });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); if (j?.error || j?.message) msg = j.error || j.message; } catch (_) {}
            alert('Export fehlgeschlagen: ' + msg);
            return;
        }
        const blob = await r.blob();
        const filename = `dokument-upload-protokoll_${new Date().toISOString().slice(0, 10)}.csv`;
        if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, filename);
        else {
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = filename; a.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        }
    } catch (e) {
        alert('Export fehlgeschlagen: ' + e.message);
    }
}
