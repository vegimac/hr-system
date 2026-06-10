// ══════════════════════════════════════════════════════════════════════
// family-children-import.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// FAMILIENZULAGEN-KONTROLLE-IMPORT (Mirus XLS)
// ──────────────────────────────────────────────
// Liest Mirus-Familienzulagen-Kontroll-XLS, legt Kinder pro MA an mit
// Allowance-Eintrag (Bis-Datum aus Datei, Betrag aus Filial-Kanton-Tarif).
let _fciImportFile = null;

// Beim Aufruf der Page: Beginn-Datum-Feld auf heute vorausfüllen, damit der
// User in 99% der Fälle (Stichtag-Import) nichts mehr eintippen muss.
// (Walter-Vorgabe 13.05.2026: das Feld bleibt — aber mit sinnvollem Default.)
document.addEventListener('DOMContentLoaded', () => {
    const fld = document.getElementById('fciImportValidFrom');
    if (fld && !fld.value) fld.value = new Date().toISOString().slice(0, 10);
});

async function fciImportPreview() {
    const inp = document.getElementById('fciImportFileInput');
    const alertEl = document.getElementById('fciImportAlert');
    alertEl.innerHTML = '';
    if (!inp.files || inp.files.length === 0) {
        showPageAlert('fciImportAlert', 'Bitte eine .xls-Datei wählen.', 'error');
        return;
    }
    const validFrom = document.getElementById('fciImportValidFrom').value;
    if (!validFrom) {
        showPageAlert('fciImportAlert', 'Bitte Beginn-Datum für die Familienzulagen-Einträge angeben.', 'error');
        return;
    }
    _fciImportFile = inp.files[0];

    const fd = new FormData();
    fd.append('file', _fciImportFile);
    fd.append('validFrom', validFrom);

    try {
        const r = await fetch('/api/imports/family-children/preview', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            showPageAlert('fciImportAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        const data = await r.json();
        renderFciImportPreview(data);
        document.getElementById('fciImportCommitBtn').disabled = data.insertable === 0;
    } catch (e) {
        showPageAlert('fciImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    }
}

function renderFciImportPreview(data) {
    const summary = document.getElementById('fciImportSummary');
    const preview = document.getElementById('fciImportPreview');
    summary.innerHTML = `
        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:10px">
            <div style="background:#dbeafe;border:1px solid #93c5fd;border-radius:8px;padding:12px 14px;color:#1e40af">
                <div style="font-size:24px;font-weight:700">${data.totalRows}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Kinder total</div>
            </div>
            <div style="background:#dcfce7;border:1px solid #86efac;border-radius:8px;padding:12px 14px;color:#166534">
                <div style="font-size:24px;font-weight:700">${data.insertable}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Wird angelegt</div>
            </div>
            <div style="background:#fef3c7;border:1px solid #fcd34d;border-radius:8px;padding:12px 14px;color:#854d0e">
                <div style="font-size:24px;font-weight:700">${data.duplicates}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Schon vorhanden</div>
            </div>
            <div style="background:#fee2e2;border:1px solid #fca5a5;border-radius:8px;padding:12px 14px;color:#991b1b">
                <div style="font-size:24px;font-weight:700">${data.skipped}</div>
                <div style="font-size:11.5px;font-weight:600;text-transform:uppercase">Übersprungen</div>
            </div>
        </div>
    `;
    const fmtDate = d => d ? new Date(d).toLocaleDateString('de-CH') : '–';
    const statusBadge = s => {
        const m = {
            OK:           ['#dcfce7', '#166534', 'OK'],
            DUPLICATE:    ['#fef3c7', '#854d0e', 'Bereits vorhanden'],
            NO_EMPLOYEE:  ['#fee2e2', '#991b1b', 'MA nicht gefunden'],
            NO_DATE:      ['#fef3c7', '#854d0e', 'Kein Bis-Datum'],
            NO_TARIF:     ['#fef3c7', '#854d0e', 'Kein Tarif']
        };
        const v = m[s] || ['#f1f5f9', '#475569', s];
        return `<span style="background:${v[0]};color:${v[1]};padding:2px 8px;border-radius:6px;font-size:11px;font-weight:600">${v[2]}</span>`;
    };
    preview.innerHTML = `
        <div class="card" style="padding:0;overflow:hidden">
            <table style="width:100%;border-collapse:collapse;font-size:13px">
                <thead style="background:#f8fafc">
                    <tr>
                        <th style="text-align:left;padding:8px 12px;font-size:11px;color:#475569;text-transform:uppercase">Pers.Nr.</th>
                        <th style="text-align:left;padding:8px 12px;font-size:11px;color:#475569;text-transform:uppercase">MA</th>
                        <th style="text-align:left;padding:8px 12px;font-size:11px;color:#475569;text-transform:uppercase">Kind</th>
                        <th style="text-align:left;padding:8px 12px;font-size:11px;color:#475569;text-transform:uppercase">Geburtsdatum</th>
                        <th style="text-align:left;padding:8px 12px;font-size:11px;color:#475569;text-transform:uppercase">Geplante Zulagen</th>
                        <th style="text-align:left;padding:8px 12px;font-size:11px;color:#475569;text-transform:uppercase">Status</th>
                    </tr>
                </thead>
                <tbody>
                    ${data.rows.map(r => {
                        // Walter-Vorgabe 07.06.2026: pro Kind bis zu 2 Zulagen
                        // (KZ + AZ). Anzeige als kompakte Pille-Liste.
                        const plans = r.plannedAllowances || [];
                        const plansHtml = plans.length === 0
                            ? '<span style="color:#94a3b8">–</span>'
                            : plans.map(p => {
                                const bg = p.type === 'AZ' ? '#dbeafe' : '#dcfce7';
                                const fg = p.type === 'AZ' ? '#1e40af' : '#166534';
                                return `<div style="margin-bottom:3px"><span style="background:${bg};color:${fg};padding:1px 8px;border-radius:9px;font-size:11px;font-weight:700">${p.type}</span> ${fmtDate(p.validFrom)} – ${fmtDate(p.validTo)} · <b>${Number(p.monthlyAmount).toFixed(2)}</b></div>`;
                              }).join('');
                        return `
                        <tr style="border-top:1px solid #f1f5f9">
                            <td style="padding:7px 12px;color:#64748b">${r.employeeNumber}</td>
                            <td style="padding:7px 12px;color:#0f172a">${r.employeeName}</td>
                            <td style="padding:7px 12px"><b>${r.childFirstName}</b> ${r.childLastName}</td>
                            <td style="padding:7px 12px">${fmtDate(r.dateOfBirth)}</td>
                            <td style="padding:7px 12px">${plansHtml}</td>
                            <td style="padding:7px 12px">
                                ${statusBadge(r.status)}
                                ${r.note ? `<div style="font-size:11px;color:#94a3b8;margin-top:2px">${r.note}</div>` : ''}
                            </td>
                        </tr>`;
                    }).join('')}
                </tbody>
            </table>
        </div>
    `;
}

async function fciImportCommit() {
    if (!_fciImportFile) {
        showPageAlert('fciImportAlert', 'Erst Datei analysieren.', 'error');
        return;
    }
    const validFrom = document.getElementById('fciImportValidFrom').value;
    if (!validFrom) {
        showPageAlert('fciImportAlert', 'Bitte Beginn-Datum angeben.', 'error');
        return;
    }
    if (!confirm('Familienzulagen jetzt importieren?\n\nKinder werden pro MA angelegt mit Geburtsdatum und Bis-Datum aus der Datei. Bestehende Kinder werden NICHT verdoppelt.')) return;

    const btn = document.getElementById('fciImportCommitBtn');
    btn.disabled = true; btn.textContent = 'Importiere...';

    const fd = new FormData();
    fd.append('file', _fciImportFile);
    fd.append('validFrom', validFrom);

    try {
        const r = await fetch('/api/imports/family-children/commit', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken },
            body: fd
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            showPageAlert('fciImportAlert', 'Fehler: ' + (j.error || r.status), 'error');
            return;
        }
        showPageAlert('fciImportAlert',
            `✓ Import erfolgreich. ${j.childrenAdded} Kinder angelegt, ${j.allowancesAdded} Familienzulagen-Einträge erstellt, ${j.duplicates} bereits vorhanden, ${j.skipped} übersprungen. Fenster wird in 2 Sekunden geschlossen…`,
            'success');
        // Walter-Vorgabe 13.05.2026: nach erfolgreichem Import zurück zur Übersicht.
        setTimeout(() => { if (typeof showPage === 'function') showPage('admin-hub'); }, 2000);
    } catch (e) {
        showPageAlert('fciImportAlert', 'Netzwerkfehler: ' + e.message, 'error');
    } finally {
        btn.disabled = false; btn.textContent = 'Import bestätigen';
    }
}


