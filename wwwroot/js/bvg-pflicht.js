// ── BVG-Versicherungspflicht pro Person (Walter 04.10.2026) ──
// Versichert ja/nein ab/bis. Massgebend ist der erwartete Jahreslohn inkl. 13. ML
// (Schwelle aus den SV-Sätzen), nicht der einzelne Monat. Ohne Eintrag prüft der
// Lohn wie bisher jeden Monat einzeln. API: /api/employees/{id}/bvg-pflicht
let _bpData = null;

async function bpLoad(employeeId) {
    const el = document.getElementById('bpContent');
    if (!el || !employeeId) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch(`/api/employees/${employeeId}/bvg-pflicht`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        _bpData = await r.json();
        bpRender(el);
    } catch (_) { el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>'; }
}

function bpPille(text, bg, fg) {
    return `<span style="background:${bg};color:${fg};font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px">${text}</span>`;
}

function bpQuelleText(q) {
    return q === 'VORSCHLAG' ? 'aus Vorschlag' : q === 'MIRUS' ? 'aus Mirus' : 'von Hand';
}

function bpRender(el) {
    const d = _bpData; if (!d) return;
    const fmt = (x) => x ? formatDate(x) : '';
    const a = d.aktuell;
    const stand = a
        ? `<b>${a.versichert ? 'Versichert' : 'Nicht versichert'}</b> <span style="color:#64748b">ab ${fmt(a.gueltigAb)}${a.gueltigBis ? ' bis ' + fmt(a.gueltigBis) : ''} · ${bpQuelleText(a.quelle)}</span>`
        : '<b>Nicht festgelegt</b> <span style="color:#64748b">· der Lohn prüft jeden Monat einzeln (Monatslohn × 12)</span>';
    const standPille = a
        ? (a.versichert ? bpPille('VERSICHERT', '#dcfce7', '#166534') : bpPille('NICHT VERSICHERT', '#f1f5f9', '#475569'))
        : bpPille('OFFEN', '#fef3c7', '#92400e');
    const btns = a
        ? `<button class="btn-emp-edit" onclick="bpOpenModal(${a.id})">Bearbeiten</button> <button class="btn-emp-del" onclick="bpDelete(${a.id})">Löschen</button>`
        : '';

    const v = d.vorschlag;
    let vorschlagHtml = '';
    if (v) {
        const vTxt = v.versichert === true ? 'versichert' : v.versichert === false ? 'nicht versichert' : 'noch kein Vorschlag';
        const knopf = v.abweichend
            ? `<button class="btn-emp-edit" onclick="bpVorschlagUebernehmen()">Vorschlag übernehmen</button>` : '';
        vorschlagHtml = `<div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-top:6px;font-size:12px;color:#475569">
            <span>Vorschlag: <b>${vTxt}</b>${v.abweichend ? ' ' + bpPille('WEICHT AB', '#fef3c7', '#92400e') : ''}</span>
            <span style="color:#64748b">${esc(v.grundlage || '')}</span>
            <span style="margin-left:auto">${knopf}</span>
        </div>`;
    }

    const card = `<div class="emp-family-card" style="border-left:3px solid ${a ? '#cbd5e1' : '#d97706'};margin-bottom:6px">
        <div class="emp-family-card-head">
            <div style="display:flex;gap:14px;align-items:center;flex-wrap:wrap">
                <div style="min-width:110px;font-weight:600">BVG</div>
                <div>${stand}</div>
                ${standPille}
            </div>
            <div style="display:flex;gap:6px">${btns}</div>
        </div>
        ${vorschlagHtml}
        ${a && (d.eintraege || []).find(e => e.id === a.id)?.bemerkung ? `<div style="font-size:11.5px;color:#64748b">${esc(d.eintraege.find(e => e.id === a.id).bemerkung)}</div>` : ''}
    </div>`;

    const hist = (d.eintraege || []).filter(e => !e.isCurrent);
    const histHtml = hist.map(e => `<div style="font-size:12px;color:#3f3f3f;padding:8px 4px;border-bottom:1px solid rgba(60,55,48,0.10)">
        <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
            <span><b>${e.versichert ? 'Versichert' : 'Nicht versichert'}</b> · ${fmt(e.gueltigAb)} – ${e.gueltigBis ? fmt(e.gueltigBis) : 'offen'} · ${bpQuelleText(e.quelle)}${e.bemerkung ? ' · ' + esc(e.bemerkung) : ''}</span>
            <span style="margin-left:auto;display:flex;gap:6px">
                <button class="btn-emp-edit" onclick="bpOpenModal(${e.id})">Details</button>
                <button class="btn-emp-del" onclick="bpDelete(${e.id})">Löschen</button>
            </span>
        </div>
    </div>`).join('');
    el.innerHTML = card + (histHtml ? `<div id="bpHistWrap" style="display:none">${histHtml}</div>` : '');
    if (typeof zulHistFillSlot === 'function') zulHistFillSlot('bpHistPillSlot', hist.length, 'bpHistWrap', 'bpHistPill');
}

function bpVorschlagUebernehmen() {
    const v = _bpData?.vorschlag;
    if (!v || v.versichert == null) return;
    const heute = new Date();
    const ersterDesMonats = `${heute.getFullYear()}-${String(heute.getMonth() + 1).padStart(2, '0')}-01`;
    bpOpenModal(null, { versichert: v.versichert, gueltigAb: ersterDesMonats, quelle: 'VORSCHLAG', jahreslohn: v.jahreslohn, bemerkung: v.grundlage });
}

function bpOpenModal(entryId, vorgabe) {
    if (!selectedEmployeeId || !_bpData) return;
    const e = entryId ? (_bpData.eintraege || []).find(x => x.id === entryId) : null;
    const w = e || vorgabe || {};
    const vers = w.versichert !== false;
    const html = `
    <div id="bpModal" style="position:fixed;inset:0;background:rgba(60,55,48,.28);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('bpModal').remove()">
      <div class="ma-modal-box narrow">
        <div class="ma-modal-head">
            <div>
                <div class="ma-modal-title">${e ? 'BVG-Versicherungspflicht bearbeiten' : 'BVG-Versicherungspflicht festlegen'}</div>
                <div class="ma-modal-sub">Gilt in jedem Monat — auch bei Krankheit oder wenig Stunden</div>
            </div>
            <button class="ma-modal-close" onclick="document.getElementById('bpModal').remove()">✕</button>
        </div>
        <div class="ma-modal-body">
            <input type="hidden" id="bpId" value="${e?.id ?? ''}">
            <input type="hidden" id="bpQuelle" value="${esc(w.quelle || 'HAND')}">
            <input type="hidden" id="bpJahreslohn" value="${w.jahreslohn ?? ''}">
            <div class="ma-grid cols-3">
                <div class="ma-field">
                    <div class="ma-field-label">Pensionskasse *</div>
                    <select id="bpVersichert" class="ma-input">
                        <option value="1" ${vers ? 'selected' : ''}>versichert</option>
                        <option value="0" ${!vers ? 'selected' : ''}>nicht versichert</option>
                    </select>
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Gültig ab *</div>
                    <input type="date" id="bpVon" class="ma-input" value="${w.gueltigAb ? String(w.gueltigAb).slice(0, 10) : ''}">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Gültig bis <span class="opt">(leer = offen)</span></div>
                    <input type="date" id="bpBis" class="ma-input" value="${w.gueltigBis ? String(w.gueltigBis).slice(0, 10) : ''}">
                </div>
            </div>
            <div class="ma-grid cols-1" style="margin-top:8px">
                <div class="ma-field">
                    <div class="ma-field-label">Bemerkung <span class="opt">(optional)</span></div>
                    <input id="bpBem" class="ma-input" value="${esc(w.bemerkung || '')}">
                </div>
            </div>
            <div id="bpErr" class="vc-wunsch-err"></div>
        </div>
        <div class="ma-modal-foot">
            <button type="button" class="btn btn-outline" onclick="document.getElementById('bpModal').remove()">Abbrechen</button>
            <button type="button" class="btn btn-primary" onclick="bpSave()">Speichern</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

async function bpSave() {
    const err = document.getElementById('bpErr');
    const zeige = (t) => { if (err) { err.textContent = t; err.classList.add('show'); } };
    if (err) err.classList.remove('show');
    const von = document.getElementById('bpVon')?.value;
    if (!von) { zeige('Bitte «Gültig ab» eintragen.'); return; }
    const id = document.getElementById('bpId')?.value;
    const dto = {
        versichert: document.getElementById('bpVersichert')?.value === '1',
        gueltigAb: von,
        gueltigBis: document.getElementById('bpBis')?.value || null,
        quelle: document.getElementById('bpQuelle')?.value || 'HAND',
        jahreslohn: parseFloat(document.getElementById('bpJahreslohn')?.value) || null,
        bemerkung: (document.getElementById('bpBem')?.value || '').trim() || null
    };
    const url = id ? `/api/employees/${selectedEmployeeId}/bvg-pflicht/${id}` : `/api/employees/${selectedEmployeeId}/bvg-pflicht`;
    const res = await fetch(url, { method: id ? 'PUT' : 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(dto) });
    if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { const b = await res.clone().json().catch(() => ({})); zeige(b.message || 'Fehler beim Speichern.'); return; }
    document.getElementById('bpModal')?.remove();
    bpLoad(selectedEmployeeId);
}

async function bpDelete(id) {
    if (!(await liquidConfirm('Diesen BVG-Eintrag löschen? Ohne Eintrag prüft der Lohn wieder jeden Monat einzeln.'))) return;
    const res = await fetch(`/api/employees/${selectedEmployeeId}/bvg-pflicht/${id}`, { method: 'DELETE', headers: ah() });
    if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { const b = await res.clone().json().catch(() => ({})); alert(b.message || 'Fehler beim Löschen.'); return; }
    bpLoad(selectedEmployeeId);
}
