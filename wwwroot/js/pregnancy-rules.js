// ══════════════════════════════════════════════════════════════════════
// pregnancy-rules.js — Mutterschafts-Regeln-Pflege (admin)
// ──────────────────────────────────────────────────────────────────────
// Variante B (Walter 10.06.2026): Phasen-Ende + Lohn/Staffel-Felder.
// ══════════════════════════════════════════════════════════════════════

let _prAllRules = [];

async function prInit() {
    await prLoad();
}

async function prLoad() {
    const tbody = document.getElementById('prTableBody');
    if (!tbody) return;
    tbody.innerHTML = '<tr><td colspan="11" style="padding:24px;text-align:center;color:#94a3b8">Lade…</td></tr>';
    try {
        const r = await fetch('/api/pregnancy-rules', { headers: ah() });
        if (!r.ok) {
            tbody.innerHTML = '<tr><td colspan="11" style="padding:24px;color:#dc2626">Fehler beim Laden (' + r.status + ')</td></tr>';
            return;
        }
        _prAllRules = await r.json();
        prRender();
    } catch (e) {
        tbody.innerHTML = '<tr><td colspan="11" style="padding:24px;color:#dc2626">Verbindungsfehler: ' + e.message + '</td></tr>';
    }
}

const _prBasisLabel = { ET:'Errechneter Termin', GEBURT:'Geburt', MELDUNG:'Meldedatum' };

function prRender() {
    const tbody = document.getElementById('prTableBody');
    if (!tbody) return;
    if (!_prAllRules.length) {
        tbody.innerHTML = '<tr><td colspan="11" style="padding:24px;text-align:center;color:#94a3b8;font-style:italic">Keine Regeln — mit „+ Neue Regel" eine erste anlegen.</td></tr>';
        return;
    }
    tbody.innerHTML = _prAllRules.map(r => {
        const startTxt = prFmtOffset(r.offsetMonate, r.offsetWochen, r.richtung);
        const endeTxt  = (r.basisEnde || r.offsetEndeMonate != null || r.offsetEndeWochen != null)
            ? `<div style="font-size:11px;color:#64748b">bis ${prFmtOffset(r.offsetEndeMonate || 0, r.offsetEndeWochen || 0, r.richtungEnde || r.richtung)} ${r.basisEnde && r.basisEnde !== r.berechnungBasis ? '(' + _prBasisLabel[r.basisEnde] + ')' : ''}</div>`
            : '';
        const lohnTxt = [
            r.lohnersatzPct != null ? `${r.lohnersatzPct}%` : null,
            r.maxBetragProTag != null ? `max. CHF ${Number(r.maxBetragProTag).toFixed(0)}/Tag` : null
        ].filter(Boolean).join(' · ');
        const staffel = r.staffelText
            ? `<div style="font-size:10.5px;color:#94a3b8;margin-top:3px;font-style:italic">${prEsc(r.staffelText)}</div>`
            : '';
        const verbotPill = r.istArbeitsverbot
            ? '<span style="background:#fee2e2;color:#991b1b;font-size:11px;font-weight:600;padding:2px 8px;border-radius:8px">Arbeitsverbot</span>'
            : '<span style="color:#94a3b8;font-size:12px">–</span>';
        const statusPill = r.aktiv
            ? '<span style="background:#dcfce7;color:#166534;font-size:11px;font-weight:600;padding:2px 8px;border-radius:8px">Aktiv</span>'
            : '<span style="background:#f1f5f9;color:#64748b;font-size:11px;padding:2px 8px;border-radius:8px">Inaktiv</span>';
        return `<tr style="border-bottom:1px solid #f1f5f9;${r.aktiv ? '' : 'opacity:0.55;'}">
            <td style="padding:10px 14px;text-align:center;color:#64748b;font-variant-numeric:tabular-nums">${r.sortOrder}</td>
            <td style="padding:10px 14px"><code style="font-size:12px;font-weight:600;background:#f1f5f9;padding:2px 6px;border-radius:4px">${prEsc(r.code)}</code></td>
            <td style="padding:10px 14px;font-weight:500;color:#1e293b">${prEsc(r.bezeichnung)}${staffel}</td>
            <td style="padding:10px 14px;color:#64748b;font-size:12.5px">${prEsc(r.beschreibung || '')}</td>
            <td style="padding:10px 14px;color:#64748b;font-size:12px;white-space:nowrap">${prEsc(r.gesetz || '–')}</td>
            <td style="padding:10px 14px;color:#475569;font-size:12.5px;white-space:nowrap">
                <div>${_prBasisLabel[r.berechnungBasis] || r.berechnungBasis}</div>
                <div style="font-size:11px;color:#64748b;margin-top:2px">${startTxt}</div>
                ${endeTxt}
            </td>
            <td style="padding:10px 14px;color:#0f172a;font-size:12.5px;white-space:nowrap;font-variant-numeric:tabular-nums">${lohnTxt || '<span style="color:#cbd5e1">–</span>'}</td>
            <td style="padding:10px 14px">${verbotPill} ${statusPill}</td>
            <td style="padding:10px 14px;width:1%;text-align:right;white-space:nowrap">
                <div class="dok-menu-wrap" style="display:inline-block">
                    <button class="dok-menu-btn" onclick="prToggleMenu(event, ${r.id})" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="prMenu-${r.id}">
                        <button class="dok-menu-item" onclick="prOpenEdit(${r.id})">Bearbeiten</button>
                        <button class="dok-menu-item" onclick="prToggleActive(${r.id})">${r.aktiv ? 'Deaktivieren' : 'Aktivieren'}</button>
                        <button class="dok-menu-item danger" onclick="prDelete(${r.id})">Löschen</button>
                    </div>
                </div>
            </td>
        </tr>`;
    }).join('');
}

function prFmtOffset(monate, wochen, richtung) {
    const parts = [];
    if (monate) parts.push(`${Math.abs(monate)} Mt.`);
    if (wochen) parts.push(`${Math.abs(wochen)} Wo.`);
    if (!parts.length) return '0';
    return `${richtung === 'NACHHER' ? '+ ' : '− '}${parts.join(' + ')}`;
}

function prEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

function prToggleMenu(event, id) {
    event.stopPropagation();
    const menu = document.getElementById(`prMenu-${id}`);
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (!wasOpen && menu) {
        menu.classList.add('show');
        setTimeout(() => {
            document.addEventListener('click', () => {
                document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
            }, { once: true });
        }, 10);
    }
}

function prOpenNew() {
    prFillForm({});
    document.getElementById('prEditId').value = '';
    document.getElementById('prEditCode').readOnly = false;
    document.getElementById('prEditCode').value = '';
    document.getElementById('prEditTitle').textContent = 'Neue Mutterschafts-Regel';
    document.getElementById('prEditModal').style.display = 'flex';
}

function prOpenEdit(id) {
    const rule = _prAllRules.find(r => r.id === id);
    if (!rule) return;
    prFillForm(rule);
    document.getElementById('prEditId').value = rule.id;
    document.getElementById('prEditCode').readOnly = true;   // Code nicht änderbar nach Erstellung
    document.getElementById('prEditCode').value = rule.code;
    document.getElementById('prEditTitle').textContent = 'Regel bearbeiten';
    document.getElementById('prEditModal').style.display = 'flex';
}

function prFillForm(r) {
    document.getElementById('prEditBezeichnung').value      = r.bezeichnung      || '';
    document.getElementById('prEditBeschreibung').value     = r.beschreibung     || '';
    document.getElementById('prEditGesetz').value           = r.gesetz           || '';
    document.getElementById('prEditBasis').value            = r.berechnungBasis  || 'ET';
    document.getElementById('prEditMonate').value           = r.offsetMonate ?? 0;
    document.getElementById('prEditWochen').value           = r.offsetWochen ?? 0;
    document.getElementById('prEditRichtung').value         = r.richtung         || 'VORHER';
    document.getElementById('prEditBasisEnde').value        = r.basisEnde        || '';
    document.getElementById('prEditMonateEnde').value       = r.offsetEndeMonate ?? '';
    document.getElementById('prEditWochenEnde').value       = r.offsetEndeWochen ?? '';
    document.getElementById('prEditRichtungEnde').value     = r.richtungEnde     || '';
    document.getElementById('prEditLohn').value             = r.lohnersatzPct    ?? '';
    document.getElementById('prEditMaxBetrag').value        = r.maxBetragProTag  ?? '';
    document.getElementById('prEditStaffel').value          = r.staffelText      || '';
    document.getElementById('prEditVerbot').checked         = !!r.istArbeitsverbot;
    document.getElementById('prEditSortOrder').value        = r.sortOrder ?? 99;
    document.getElementById('prEditAktiv').checked          = r.aktiv === undefined ? true : !!r.aktiv;
}

function prCloseEdit() {
    document.getElementById('prEditModal').style.display = 'none';
}

async function prSaveEdit() {
    const id = document.getElementById('prEditId').value;
    const isNew = !id;
    const numOrNull = v => { v = String(v).trim(); return v === '' ? null : Number(v); };
    const txtOrNull = v => { v = String(v).trim(); return v === '' ? '' : v; };  // leer → "" zum Löschen
    const dto = {
        code:             isNew ? document.getElementById('prEditCode').value.trim().toUpperCase() : undefined,
        bezeichnung:      document.getElementById('prEditBezeichnung').value.trim(),
        beschreibung:     document.getElementById('prEditBeschreibung').value.trim(),
        gesetz:           document.getElementById('prEditGesetz').value.trim(),
        berechnungBasis:  document.getElementById('prEditBasis').value,
        offsetMonate:     parseInt(document.getElementById('prEditMonate').value) || 0,
        offsetWochen:     parseInt(document.getElementById('prEditWochen').value) || 0,
        richtung:         document.getElementById('prEditRichtung').value,
        basisEnde:        txtOrNull(document.getElementById('prEditBasisEnde').value),
        offsetEndeMonate: numOrNull(document.getElementById('prEditMonateEnde').value),
        offsetEndeWochen: numOrNull(document.getElementById('prEditWochenEnde').value),
        richtungEnde:     txtOrNull(document.getElementById('prEditRichtungEnde').value),
        lohnersatzPct:    numOrNull(document.getElementById('prEditLohn').value),
        maxBetragProTag:  numOrNull(document.getElementById('prEditMaxBetrag').value),
        staffelText:      txtOrNull(document.getElementById('prEditStaffel').value),
        istArbeitsverbot: document.getElementById('prEditVerbot').checked,
        sortOrder:        parseInt(document.getElementById('prEditSortOrder').value) || 99,
        aktiv:            document.getElementById('prEditAktiv').checked,
    };
    if (isNew && !dto.code) { alert('Code ist Pflicht.'); return; }
    if (!dto.bezeichnung)   { alert('Bezeichnung ist Pflicht.'); return; }
    const url    = isNew ? '/api/pregnancy-rules' : `/api/pregnancy-rules/${id}`;
    const method = isNew ? 'POST' : 'PUT';
    const r = await fetch(url, {
        method,
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify(dto)
    });
    if (!r.ok) {
        let t = await r.text();
        try { const j = JSON.parse(t); if (j.error) t = j.error; } catch {}
        alert('Fehler: ' + t);
        return;
    }
    prCloseEdit();
    await prLoad();
}

async function prToggleActive(id) {
    const rule = _prAllRules.find(r => r.id === id);
    if (!rule) return;
    const r = await fetch(`/api/pregnancy-rules/${id}`, {
        method: 'PUT',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ aktiv: !rule.aktiv })
    });
    if (!r.ok) { alert('Fehler: ' + await r.text()); return; }
    await prLoad();
}

async function prDelete(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const rule = _prAllRules.find(r => r.id === id);
    if (!rule) return;
    if (!confirm(`Regel „${rule.code}" wirklich löschen?`)) return;
    const r = await fetch(`/api/pregnancy-rules/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { alert('Fehler: ' + await r.text()); return; }
    await prLoad();
}
