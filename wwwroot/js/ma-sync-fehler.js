// ══════════════════════════════════════════════════════════════════════
// ma-sync-fehler.js — Fehlerliste MA-Stammdaten-Sync (Walter 01.09.2026)
// ──────────────────────────────────────────────────────────────────────
// Backend: /api/ma-sync-fehler (GET, POST {id}/erledigt)
//
// Zeigt die Verträge, die der Nachtlauf NICHT importiert hat, weil sie in
// easy@work widersprüchlich erfasst sind:
//   FIX  → nie Stunden, immer Prozent; nur 50/60/70/80/90/100
//   FLEX → 17 Stunden pro Woche
//   MTP  → 18–40 Stunden pro Woche
//
// Ohne diese Liste wären blockierte Verträge unsichtbar — deshalb ist sie
// nicht Zierde, sondern Voraussetzung dafür, dass blockiert werden darf.
// ══════════════════════════════════════════════════════════════════════

async function msfLoad(alle) {
    const box = document.getElementById('msfBox');
    if (!box) return;
    box.innerHTML = '<div style="color:#64748b;font-size:13px">Lade…</div>';
    try {
        const r = await fetch('/api/ma-sync-fehler' + (alle ? '?alle=true' : ''), {
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (!r.ok) {
            box.innerHTML = '<div style="color:#991b1b;font-size:13px">Fehler beim Laden: '
                          + escapeHtml(await r.text() || String(r.status)) + '</div>';
            return;
        }
        const d = await r.json();
        msfRender(d, alle);
    } catch (e) {
        box.innerHTML = '<div style="color:#991b1b;font-size:13px">Netzwerkfehler: '
                      + escapeHtml(e.message) + '</div>';
    }
}

function msfRender(d, alle) {
    const box = document.getElementById('msfBox');
    if (!d.zeilen || d.zeilen.length === 0) {
        box.innerHTML = '<div style="background:#f0fdf4;border:1px solid #86efac;border-radius:10px;'
                      + 'padding:12px 15px;color:#166534;font-size:13px">'
                      + '✓ Keine offenen Erfassungsfehler aus dem letzten Sync-Lauf.</div>';
        return;
    }

    const rows = d.zeilen.map(z => `
        <tr style="border-top:1px solid #e2e8f0">
            <td style="padding:8px 10px;white-space:nowrap;font-weight:600">${escapeHtml(z.employeeNumber || '—')}</td>
            <td style="padding:8px 10px;font-size:12.5px;color:#64748b;white-space:nowrap">${escapeHtml(z.filiale)}</td>
            <td style="padding:8px 10px;font-size:12.5px;line-height:1.5">${escapeHtml(z.reason)}</td>
            <td style="padding:8px 10px;text-align:right;white-space:nowrap">
                ${z.erledigt
                    ? '<span style="color:#16a34a;font-size:12px">✓ erledigt</span>'
                    : `<button onclick="msfErledigt(${z.id}, ${alle ? 'true' : 'false'})"
                         style="padding:5px 11px;border:1px solid #cbd5e1;border-radius:7px;background:#f8fafc;
                                cursor:pointer;font-size:12px;color:#475569">erledigt</button>`}
            </td>
        </tr>`).join('');

    box.innerHTML = `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-radius:10px;padding:11px 15px;
                    color:#991b1b;font-size:12.5px;margin-bottom:12px">
            <strong>⚠️ ${d.anzahl} Vertrag/Verträge wurden NICHT importiert.</strong>
            Bitte in easy@work korrigieren — der nächste Sync übernimmt sie dann automatisch.
        </div>
        <div style="overflow-x:auto">
        <table style="width:100%;border-collapse:collapse">
            <thead><tr style="background:#f8fafc">
                <th style="text-align:left;padding:7px 10px;font-size:11.5px;color:#64748b">Pers.-Nr.</th>
                <th style="text-align:left;padding:7px 10px;font-size:11.5px;color:#64748b">Filiale</th>
                <th style="text-align:left;padding:7px 10px;font-size:11.5px;color:#64748b">Was ist falsch</th>
                <th></th>
            </tr></thead>
            <tbody>${rows}</tbody>
        </table>
        </div>`;
}

async function msfErledigt(id, alle) {
    try {
        const r = await fetch('/api/ma-sync-fehler/' + id + '/erledigt', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + localStorage.hrToken }
        });
        if (!r.ok) { showToast('Konnte nicht abgehakt werden.', 'error'); return; }
        await msfLoad(alle);
    } catch (e) {
        showToast('Netzwerkfehler: ' + e.message, 'error');
    }
}
