// ════════════════════════════════════════════════════════════════════════
//  Kontoplan / Lohnart→Konten-Mapping (Walter-Vorgabe 22.05.2026)
//  Zeigt die Tabelle lohn_konto_mapping (Mirus/McD-Buchungsschema). Grundlage
//  für das Fibu-Journal (Etappe 2) und später den Abacus-Export.
//  Soll-/Gegenkonto + Bezeichnung sind inline korrigierbar (PUT).
//  Spaltenköpfe sitzen im fixen Kopf (HTML); hier nur Datenzeilen (22.07.2026).
// ════════════════════════════════════════════════════════════════════════

let _kpRows = [];

const KP_KST_COLOR = { '100':'#ece9e2', '200':'#fef3c7', '300':'#ede9fe', '400':'#fae8ff' };

async function kpInit() {
    const tbody = document.getElementById('kpTableBody');
    if (tbody) tbody.innerHTML = '<tr><td colspan="7" style="padding:24px;color:#94a3b8;font-size:13px;text-align:center">Lade Kontoplan…</td></tr>';
    try {
        const r = await fetch('/api/lohn-konto-mapping', { headers: ah() });
        if (!r.ok) {
            if (tbody) tbody.innerHTML = `<tr><td colspan="7" style="padding:24px;color:#dc2626;font-size:13px">Konnte Kontoplan nicht laden (HTTP ${r.status}). Wurde die Migration <code>add_lohn_konto_mapping.sql</code> ausgeführt?</td></tr>`;
            return;
        }
        _kpRows = await r.json() || [];
        kpRender();
    } catch (e) {
        if (tbody) tbody.innerHTML = `<tr><td colspan="7" style="padding:24px;color:#dc2626;font-size:13px">Verbindungsfehler: ${e.message}</td></tr>`;
    }
}

function kpRender() {
    const tbody = document.getElementById('kpTableBody');
    if (!tbody) return;
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
        tbody.innerHTML = '<tr><td colspan="7" style="padding:24px;color:#94a3b8;font-size:13px;text-align:center">Keine Einträge.</td></tr>';
        return;
    }

    // Nach Position gruppieren
    const groups = {};
    rows.forEach(m => { (groups[m.position] ||= []).push(m); });

    const esc = s => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
    let body = '';
    Object.keys(groups).sort((a,b) => Number(a)-Number(b)).forEach(pos => {
        body += `<tr><td colspan="7" style="background:#f1f5f9;padding:8px 12px;font-weight:700;font-size:12.5px;color:#334155;border-top:1px solid #e2e8f0">Lohnart ${esc(pos)}</td></tr>`;
        groups[pos].forEach(m => {
            const kstBg = KP_KST_COLOR[String(m.kostenstelleNr || '')] || '#f1f5f9';
            const kstLabel = m.kostenstelleNr
                ? `<span style="background:${kstBg};padding:1px 7px;border-radius:7px;font-size:11px;font-weight:600;white-space:nowrap">${esc(m.kostenstelleNr)} ${esc(m.kostenstelleName || '')}</span>`
                : '<span style="color:#94a3b8;font-size:11px">alle</span>';
            const vorm = m.isVormonat ? '<span style="color:#b45309;font-size:10.5px;font-weight:600">Vormonat</span>' : '';
            // MWST-Konfiguration für den Abacus-Export (Treuhänder 05.08.2026):
            // Badge «MWST 1067 / 200» wenn gesetzt — editierbar via kpEdit.
            const pz = (m.mwstProzent != null && Number(m.mwstProzent) > 0) ? ` · ${Number(m.mwstProzent)}%` : '';
            const mwst = m.mwstKonto
                ? `<span style="background:#ecfdf5;color:#047857;padding:1px 7px;border-radius:7px;font-size:10.5px;font-weight:600;white-space:nowrap" title="Abacus: TaxAccount ${esc(m.mwstKonto)}, TaxCode ${esc(m.mwstCode)}${pz}">MWST ${esc(m.mwstKonto)} / ${esc(m.mwstCode)}${pz}</span>`
                : '';
            body += `<tr data-id="${m.id}" style="border-top:1px solid #f1f5f9">
                <td style="padding:6px 10px;color:#64748b">${m.subPosition ?? '—'}</td>
                <td style="padding:6px 10px">${kstLabel}</td>
                <td style="padding:6px 10px"><span class="kp-bez">${esc(m.bezeichnung)}</span></td>
                <td style="padding:6px 10px;font-family:monospace;font-weight:700;color:#166534"><span class="kp-soll">${esc(m.fibukonto)}</span></td>
                <td style="padding:6px 10px;font-family:monospace;font-weight:700;color:#b91c1c"><span class="kp-gegen">${esc(m.gegenkonto)}</span></td>
                <td class="kp-extra" style="padding:6px 10px">${vorm}${vorm && mwst ? ' ' : ''}${mwst}</td>
                <td style="padding:6px 10px;text-align:right">
                    <div style="position:relative;display:inline-block">
                        <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'kp-${m.id}')" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="dokMenu-kp-${m.id}">
                            <button class="dok-menu-item" onclick="dokCloseAllMenus();kpEdit(${m.id})">Bearbeiten</button>
                        </div>
                    </div>
                </td>
            </tr>`;
        });
    });
    tbody.innerHTML = body;
}

function kpEdit(id) {
    const m = _kpRows.find(x => x.id === id);
    if (!m) return;
    const row = document.querySelector(`#kpTableBody tr[data-id="${id}"]`);
    if (!row) return;
    const esc = s => String(s ?? '').replace(/"/g,'&quot;');
    row.querySelector('.kp-soll').innerHTML  = `<input id="kpSoll_${id}"  value="${esc(m.fibukonto)}"  style="width:60px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-family:monospace">`;
    row.querySelector('.kp-gegen').innerHTML = `<input id="kpGegen_${id}" value="${esc(m.gegenkonto)}" style="width:60px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-family:monospace">`;
    row.querySelector('.kp-bez').innerHTML   = `<input id="kpBez_${id}" value="${esc(m.bezeichnung)}" style="width:100%;min-width:180px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px">`;
    // MWST-Konfiguration (Abacus): beide Felder oder beide leer.
    const extra = row.querySelector('.kp-extra');
    if (extra) extra.innerHTML = `
        <span style="font-size:10.5px;color:#64748b">MWST-Kto</span>
        <input id="kpMwstKto_${id}" value="${esc(m.mwstKonto || '')}" placeholder="1067" style="width:52px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-family:monospace">
        <span style="font-size:10.5px;color:#64748b">Code</span>
        <input id="kpMwstCode_${id}" value="${esc(m.mwstCode || '')}" placeholder="200" style="width:44px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-family:monospace">
        <span style="font-size:10.5px;color:#64748b">%</span>
        <input id="kpMwstPz_${id}" value="${m.mwstProzent != null ? Number(m.mwstProzent) : ''}" placeholder="0" style="width:38px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-family:monospace">`;
    const actionCell = row.lastElementChild;
    actionCell.innerHTML = `<button class="btn-link" style="font-size:11.5px;color:#16a34a;background:none;border:none;cursor:pointer" onclick="kpSave(${id})">✓ speichern</button>
        <button class="btn-link" style="font-size:11.5px;color:#94a3b8;background:none;border:none;cursor:pointer" onclick="kpRender()">✕</button>`;
}

async function kpSave(id) {
    const fibukonto  = document.getElementById(`kpSoll_${id}`)?.value.trim();
    const gegenkonto = document.getElementById(`kpGegen_${id}`)?.value.trim();
    const bezeichnung = document.getElementById(`kpBez_${id}`)?.value.trim();
    const mwstKonto  = document.getElementById(`kpMwstKto_${id}`)?.value.trim() || '';
    const mwstCode   = document.getElementById(`kpMwstCode_${id}`)?.value.trim() || '';
    const pzRaw      = (document.getElementById(`kpMwstPz_${id}`)?.value || '').trim().replace(',', '.');
    const mwstProzent = pzRaw === '' ? null : parseFloat(pzRaw);
    if (!fibukonto || !gegenkonto) { alert('Soll- und Gegenkonto sind Pflicht.'); return; }
    if ((mwstKonto === '') !== (mwstCode === '')) {
        alert('MWST-Konto und MWST-Code entweder beide ausfüllen oder beide leer lassen.');
        return;
    }
    if (pzRaw !== '' && !Number.isFinite(mwstProzent)) { alert('MWST-Prozent ist keine gültige Zahl.'); return; }
    try {
        const r = await fetch(`/api/lohn-konto-mapping/${id}`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ fibukonto, gegenkonto, bezeichnung, mwstKonto, mwstCode, mwstProzent })
        });
        if (!r.ok) {
            const j = await r.json().catch(() => ({}));
            alert('Speichern fehlgeschlagen: ' + (j.error || `HTTP ${r.status}`));
            return;
        }
        const m = _kpRows.find(x => x.id === id);
        if (m) {
            m.fibukonto = fibukonto; m.gegenkonto = gegenkonto; m.bezeichnung = bezeichnung;
            m.mwstKonto = mwstKonto || null; m.mwstCode = mwstCode || null;
            m.mwstProzent = mwstKonto ? (mwstProzent ?? 0) : null;
        }
        kpRender();
        if (typeof showToast === 'function') showToast('Konto gespeichert ✓', 'success');
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}
