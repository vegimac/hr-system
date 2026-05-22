// ════════════════════════════════════════════════════════════════════════
//  Kontoplan / Lohnart→Konten-Mapping (Walter-Vorgabe 22.05.2026)
//  Zeigt die Tabelle lohn_konto_mapping (Mirus/McD-Buchungsschema). Grundlage
//  für das Fibu-Journal (Etappe 2) und später den Abacus-Export.
//  Soll-/Gegenkonto + Bezeichnung sind inline korrigierbar (PUT).
// ════════════════════════════════════════════════════════════════════════

let _kpRows = [];

const KP_KST_COLOR = { '100':'#dbeafe', '200':'#fef3c7', '300':'#ede9fe', '400':'#fae8ff' };

async function kpInit() {
    const cont = document.getElementById('kpContainer');
    if (cont) cont.innerHTML = '<div style="padding:24px;color:#94a3b8;font-size:13px">Lade Kontoplan…</div>';
    try {
        const r = await fetch('/api/lohn-konto-mapping', { headers: ah() });
        if (!r.ok) {
            cont.innerHTML = `<div style="padding:24px;color:#dc2626;font-size:13px">Konnte Kontoplan nicht laden (HTTP ${r.status}). Wurde die Migration <code>add_lohn_konto_mapping.sql</code> ausgeführt?</div>`;
            return;
        }
        _kpRows = await r.json() || [];
        kpRender();
    } catch (e) {
        if (cont) cont.innerHTML = `<div style="padding:24px;color:#dc2626;font-size:13px">Verbindungsfehler: ${e.message}</div>`;
    }
}

function kpRender() {
    const cont = document.getElementById('kpContainer');
    if (!cont) return;
    const q   = (document.getElementById('kpSearch')?.value || '').toLowerCase().trim();
    const kst = document.getElementById('kpKstFilter')?.value || '';

    let rows = _kpRows;
    if (kst) rows = rows.filter(m => String(m.kostenstelleNr || '') === kst);
    if (q) rows = rows.filter(m =>
        String(m.position).includes(q) ||
        (m.fibukonto || '').toLowerCase().includes(q) ||
        (m.gegenkonto || '').toLowerCase().includes(q) ||
        (m.bezeichnung || '').toLowerCase().includes(q) ||
        (m.kostenstelleName || '').toLowerCase().includes(q));

    const info = document.getElementById('kpInfo');
    if (info) info.textContent = `${rows.length} Buchungsregeln`;

    if (!rows.length) {
        cont.innerHTML = '<div style="padding:24px;color:#94a3b8;font-size:13px">Keine Einträge.</div>';
        return;
    }

    // Nach Position gruppieren
    const groups = {};
    rows.forEach(m => { (groups[m.position] ||= []).push(m); });

    const esc = s => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
    // EINE Tabelle mit festen Spaltenbreiten (table-layout:fixed) → alle Spalten
    // fluchten über alle Lohnarten hinweg untereinander (Walter-Vorgabe 22.05.2026).
    // Die Lohnart-Überschrift ist eine volle Zeile (colSpan) dazwischen.
    const th = (txt, w, right) => `<th style="padding:8px 10px;font-weight:600;color:#64748b;font-size:11px;text-align:${right ? 'right' : 'left'}${w ? `;width:${w}px` : ''}">${txt}</th>`;
    let body = '';
    Object.keys(groups).sort((a,b) => Number(a)-Number(b)).forEach(pos => {
        body += `<tr><td colspan="7" style="background:#f1f5f9;padding:8px 12px;font-weight:700;font-size:12.5px;color:#334155;border-top:1px solid #e2e8f0">Lohnart ${esc(pos)}</td></tr>`;
        groups[pos].forEach(m => {
            const kstBg = KP_KST_COLOR[String(m.kostenstelleNr || '')] || '#f1f5f9';
            const kstLabel = m.kostenstelleNr
                ? `<span style="background:${kstBg};padding:1px 7px;border-radius:7px;font-size:11px;font-weight:600;white-space:nowrap">${esc(m.kostenstelleNr)} ${esc(m.kostenstelleName || '')}</span>`
                : '<span style="color:#94a3b8;font-size:11px">alle</span>';
            const vorm = m.isVormonat ? '<span style="color:#b45309;font-size:10.5px;font-weight:600">Vormonat</span>' : '';
            body += `<tr data-id="${m.id}" style="border-top:1px solid #f1f5f9">
                <td style="padding:6px 10px;color:#64748b">${m.subPosition ?? '—'}</td>
                <td style="padding:6px 10px">${kstLabel}</td>
                <td style="padding:6px 10px"><span class="kp-bez">${esc(m.bezeichnung)}</span></td>
                <td style="padding:6px 10px;font-family:monospace;font-weight:700;color:#166534"><span class="kp-soll">${esc(m.fibukonto)}</span></td>
                <td style="padding:6px 10px;font-family:monospace;font-weight:700;color:#b91c1c"><span class="kp-gegen">${esc(m.gegenkonto)}</span></td>
                <td style="padding:6px 10px">${vorm}</td>
                <td style="padding:6px 10px;text-align:right"><button class="btn-link" style="font-size:11.5px;color:#3b82f6;background:none;border:none;cursor:pointer" onclick="kpEdit(${m.id})">✎ ändern</button></td>
            </tr>`;
        });
    });
    cont.innerHTML = `<div style="border:1px solid #e2e8f0;border-radius:10px;overflow:hidden">
        <table style="width:100%;border-collapse:collapse;font-size:12.5px;table-layout:fixed">
            <thead><tr style="background:#f8fafc;border-bottom:1px solid #e2e8f0">
                ${th('SubPos',62)}${th('Kostenstelle',172)}${th('Bezeichnung')}${th('Soll',74)}${th('Gegen',74)}${th('Typ',120)}${th('Aktion',92,true)}
            </tr></thead>
            <tbody>${body}</tbody>
        </table></div>`;
}

function kpEdit(id) {
    const m = _kpRows.find(x => x.id === id);
    if (!m) return;
    const row = document.querySelector(`#kpContainer tr[data-id="${id}"]`);
    if (!row) return;
    const esc = s => String(s ?? '').replace(/"/g,'&quot;');
    row.querySelector('.kp-soll').innerHTML  = `<input id="kpSoll_${id}"  value="${esc(m.fibukonto)}"  style="width:60px;padding:2px 4px;border:1px solid #93c5fd;border-radius:5px;font-family:monospace">`;
    row.querySelector('.kp-gegen').innerHTML = `<input id="kpGegen_${id}" value="${esc(m.gegenkonto)}" style="width:60px;padding:2px 4px;border:1px solid #93c5fd;border-radius:5px;font-family:monospace">`;
    row.querySelector('.kp-bez').innerHTML   = `<input id="kpBez_${id}" value="${esc(m.bezeichnung)}" style="width:100%;min-width:180px;padding:2px 4px;border:1px solid #93c5fd;border-radius:5px">`;
    const actionCell = row.querySelector('td:last-child');
    actionCell.innerHTML = `<button class="btn-link" style="font-size:11.5px;color:#16a34a;background:none;border:none;cursor:pointer" onclick="kpSave(${id})">✓ speichern</button>
        <button class="btn-link" style="font-size:11.5px;color:#94a3b8;background:none;border:none;cursor:pointer" onclick="kpRender()">✕</button>`;
}

async function kpSave(id) {
    const fibukonto  = document.getElementById(`kpSoll_${id}`)?.value.trim();
    const gegenkonto = document.getElementById(`kpGegen_${id}`)?.value.trim();
    const bezeichnung = document.getElementById(`kpBez_${id}`)?.value.trim();
    if (!fibukonto || !gegenkonto) { alert('Soll- und Gegenkonto sind Pflicht.'); return; }
    try {
        const r = await fetch(`/api/lohn-konto-mapping/${id}`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ fibukonto, gegenkonto, bezeichnung })
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('Speichern fehlgeschlagen: ' + (j.error || `HTTP ${r.status}`));
            return;
        }
        const m = _kpRows.find(x => x.id === id);
        if (m) { m.fibukonto = fibukonto; m.gegenkonto = gegenkonto; m.bezeichnung = bezeichnung; }
        kpRender();
        if (typeof showToast === 'function') showToast('Konto gespeichert ✓', 'success');
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}
