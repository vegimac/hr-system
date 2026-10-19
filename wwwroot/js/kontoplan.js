// ════════════════════════════════════════════════════════════════════════
//  Kontoplan (Fibu) — Swissdec-Lohnart × Vertrags-Modell → Soll/Haben/MWST
//  (Walter 19.10.2026: keine Kostenstellen-Pflege; Abacus nur Konten)
// ════════════════════════════════════════════════════════════════════════

let _kpRows = [];

const KP_MODELL = {
    CREW: { label: 'Crew / MTP', bg: '#ece9e2' },
    FLEX: { label: 'Flex', bg: '#fef3c7' },
    MGMT: { label: 'Management', bg: '#ede9fe' }
};

async function kpInit() {
    const tbody = document.getElementById('kpTableBody');
    if (tbody) tbody.innerHTML = '<tr><td colspan="7" style="padding:24px;color:#94a3b8;font-size:13px;text-align:center">Lade Kontoplan…</td></tr>';
    try {
        const r = await fetch('/api/lohn-konto-mapping', { headers: ah() });
        if (!r.ok) {
            if (tbody) tbody.innerHTML = `<tr><td colspan="7" style="padding:24px;color:#dc2626;font-size:13px">Konnte Kontoplan nicht laden (HTTP ${r.status}).</td></tr>`;
            return;
        }
        _kpRows = await r.json() || [];
        kpRender();
    } catch (e) {
        if (tbody) tbody.innerHTML = `<tr><td colspan="7" style="padding:24px;color:#dc2626;font-size:13px">Verbindungsfehler: ${e.message}</td></tr>`;
    }
}

function kpGruppeKey(m) {
    return m.lohnartCode || String(m.position);
}

function kpRender() {
    const tbody = document.getElementById('kpTableBody');
    if (!tbody) return;
    const q = (document.getElementById('kpSearch')?.value || '').toLowerCase().trim();
    const modell = document.getElementById('kpModellFilter')?.value || '';

    let rows = _kpRows;
    if (modell) rows = rows.filter(m => String(m.modell || '') === modell);
    if (q) rows = rows.filter(m =>
        String(m.position).includes(q) ||
        (m.lohnartCode || '').toLowerCase().includes(q) ||
        (m.fibukonto || '').toLowerCase().includes(q) ||
        (m.gegenkonto || '').toLowerCase().includes(q) ||
        (m.bezeichnung || '').toLowerCase().includes(q) ||
        (m.modell || '').toLowerCase().includes(q));

    const info = document.getElementById('kpInfo');
    if (info) info.textContent = `${rows.length} Buchungsregeln`;

    if (!rows.length) {
        tbody.innerHTML = '<tr><td colspan="7" style="padding:24px;color:#94a3b8;font-size:13px;text-align:center">Keine Einträge.</td></tr>';
        return;
    }

    const groups = {};
    rows.forEach(m => { (groups[kpGruppeKey(m)] ||= []).push(m); });

    const esc = s => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
    let body = '';
    Object.keys(groups).sort((a, b) => {
        const na = Number(a), nb = Number(b);
        if (!Number.isNaN(na) && !Number.isNaN(nb)) return na - nb;
        return String(a).localeCompare(String(b), 'de');
    }).forEach(key => {
        const first = groups[key][0];
        const titel = first.lohnartCode
            ? `Lohnart ${esc(first.lohnartCode)}`
            : `Pos. ${esc(first.position)}`;
        body += `<tr><td colspan="7" style="background:#f1f5f9;padding:8px 12px;font-weight:700;font-size:12.5px;color:#334155;border-top:1px solid #e2e8f0">${titel}</td></tr>`;
        groups[key].forEach(m => {
            const meta = KP_MODELL[m.modell] || null;
            const modellLabel = meta
                ? `<span style="background:${meta.bg};padding:1px 7px;border-radius:7px;font-size:11px;font-weight:600;white-space:nowrap">${esc(meta.label)}</span>`
                : '<span style="color:#94a3b8;font-size:11px">alle</span>';
            const vorm = m.isVormonat ? '<span style="color:#b45309;font-size:10.5px;font-weight:600">Vormonat</span>' : '';
            const pz = (m.mwstProzent != null && Number(m.mwstProzent) > 0) ? ` · ${Number(m.mwstProzent)}%` : '';
            const mwst = m.mwstKonto
                ? `<span style="background:#ecfdf5;color:#047857;padding:1px 7px;border-radius:7px;font-size:10.5px;font-weight:600;white-space:nowrap" title="Abacus TaxAccount/TaxCode">MWST ${esc(m.mwstKonto)} / ${esc(m.mwstCode)}${pz}</span>`
                : '';
            body += `<tr data-id="${m.id}" style="border-top:1px solid #f1f5f9">
                <td style="padding:6px 10px;color:#64748b;font-family:monospace">${esc(m.lohnartCode || m.position)}${m.subPosition != null ? '.' + m.subPosition : ''}</td>
                <td style="padding:6px 10px">${modellLabel}</td>
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
    const extra = row.querySelector('.kp-extra');
    if (extra) {
        extra.innerHTML = `<input id="kpBez_${id}" value="${esc(m.bezeichnung)}" style="width:160px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-size:12px" placeholder="Bezeichnung">
            <button onclick="kpSave(${id})" style="margin-left:6px;padding:3px 10px;border-radius:8px;border:0;background:#3f3f3f;color:#fff;font-size:12px;cursor:pointer">Speichern</button>
            <button onclick="kpRender()" style="margin-left:4px;padding:3px 10px;border-radius:8px;border:1px solid #d0c8b8;background:#fff;font-size:12px;cursor:pointer">Abbrechen</button>`;
    }
}

async function kpSave(id) {
    const fibukonto = document.getElementById(`kpSoll_${id}`)?.value?.trim();
    const gegenkonto = document.getElementById(`kpGegen_${id}`)?.value?.trim();
    const bezeichnung = document.getElementById(`kpBez_${id}`)?.value?.trim();
    const m = _kpRows.find(x => x.id === id);
    if (!m || !fibukonto || !gegenkonto) return;
    const r = await fetch(`/api/lohn-konto-mapping/${id}`, {
        method: 'PUT',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({
            fibukonto, gegenkonto, bezeichnung,
            mwstKonto: m.mwstKonto, mwstCode: m.mwstCode, mwstProzent: m.mwstProzent
        })
    });
    if (!r.ok) {
        const err = await r.json().catch(() => ({}));
        alert(err.error || `Speichern fehlgeschlagen (${r.status})`);
        return;
    }
    const saved = await r.json();
    Object.assign(m, saved);
    kpRender();
}
