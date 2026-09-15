// ── Versicherungs-Codes am Mitarbeiter (Walter 07.09.2026, Swissdec-Lösungen) ──
// UVG (A/B/P …), UVG-Zusatz und KTG (10/11/12), BVG (11/21/22/K2010 oder Fixbetrag).
// Ohne Eintrag gilt der Standard der SV-Sätze (Zeile mit ★). API:
// /api/employees/{id}/versicherung-codes  (GET liefert pro Art effektiven Code + Optionen)
let _vcData = null;
const VC_ARTEN = { UVG: 'UVG (Unfall)', UVGZ: 'UVG-Zusatz', KTG: 'KTG', BVG: 'BVG', AHV: 'AHV/ALV' };

async function vcLoad(employeeId) {
    const el = document.getElementById('vcContent');
    if (!el || !employeeId) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen…</span></div>';
    try {
        const r = await fetch(`/api/employees/${employeeId}/versicherung-codes`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        _vcData = await r.json();
        vcRender(el);
    } catch (_) { el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>'; }
}

function vcRender(el) {
    const d = _vcData; if (!d) return;
    const fmt = (x) => x ? formatDate(x) : '';
    const rows = d.arten.map(a => {
        const opt = (a.optionen || []).find(o => o.code === a.effektiverCode);
        const istVerzicht = (a.explizit?.code || '') === 'VERZICHT';
        const codeTxt = istVerzicht
            ? `<b>MA wünscht Verzicht Freibetrag</b>`
            : (a.effektiverCode
                ? `<b>${esc(a.effektiverCode)}</b>${opt ? ` <span style="color:#64748b">· ${esc(opt.name)}</span>` : ''}`
                : '<span style="color:#94a3b8">–</span>')
            + ((a.weitereCodes || []).length && !istVerzicht ? ` <span style="color:#64748b">+ ${a.weitereCodes.map(esc).join(', ')}</span>` : '');
        const herk = istVerzicht ? '<span style="background:#fef3c7;color:#92400e;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px">WUNSCH MA</span>'
                   : a.herkunft === 'manuell' ? '<span style="background:#fef3c7;color:#92400e;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px">MANUELL</span>'
                   : a.herkunft === 'standard' ? '<span style="background:#dcfce7;color:#166534;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px">STANDARD</span>'
                   : a.herkunft === 'beitragspflichtig' ? '<span style="background:#dcfce7;color:#166534;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px">BEITRAGSPFLICHTIG</span>'
                   : `<span style="color:#94a3b8;font-size:11px">${esc(a.herkunft)}</span>`;
        const fix = a.explizit && (a.explizit.beitragFixAn || a.explizit.beitragFixAg)
            ? `<div style="font-size:11.5px;color:#475569">Fixbetrag/Mt.: AN ${Number(a.explizit.beitragFixAn || 0).toFixed(2)} · AG ${Number(a.explizit.beitragFixAg || 0).toFixed(2)}</div>` : '';
        const bvgInfo = a.art === 'BVG' && a.explizit && (a.explizit.bvgEintrittsgrund || a.explizit.bvgVollArbeitsfaehig != null || a.explizit.bvgBasisManuell)
            ? `<div style="font-size:11.5px;color:#475569">${[a.explizit.bvgEintrittsgrund === 'entryCompany' ? 'Firmeneintritt' : a.explizit.bvgEintrittsgrund === 'interruptionOfEmployment' ? 'Wiedereintritt' : a.explizit.bvgEintrittsgrund,
                 a.explizit.bvgVollArbeitsfaehig === true ? 'voll arbeitsfähig' : a.explizit.bvgVollArbeitsfaehig === false ? 'nicht voll arbeitsfähig' : null,
                 a.explizit.bvgBasisManuell ? 'Basis manuell ' + Number(a.explizit.bvgBasisManuell).toLocaleString('de-CH') : null].filter(Boolean).join(' · ')}</div>` : '';
        const zeit = a.explizit ? `<div style="font-size:11.5px;color:#64748b">${istVerzicht ? 'Wunsch ab' : 'ab'} ${fmt(a.explizit.validFrom)}${a.explizit.validTo ? ' bis ' + fmt(a.explizit.validTo) : ''}${a.explizit.bemerkung ? ' · ' + esc(a.explizit.bemerkung) : ''}${istVerzicht ? ' · greift erst nach Referenzalter' : ''}</div>` : '';
        const btns = a.explizit
            ? `<button class="btn-emp-edit" onclick="${istVerzicht ? `vcOpenVerzichtModal(${a.explizit.id})` : `vcOpenModal(${a.explizit.id})`}">Bearbeiten</button> <button class="btn-emp-del" onclick="vcDelete(${a.explizit.id})">Löschen</button>`
            : a.art === 'AHV'
                ? `<button class="btn-emp-edit" onclick="vcOpenVerzichtModal()">Verzicht Freibetrag</button> <button class="btn-emp-edit" onclick="vcOpenModal(null, 'AHV', 'SONDERFALL')">Sonderfall</button>`
                : `<button class="btn-emp-edit" onclick="vcOpenModal(null, '${a.art}')">Abweichung</button>`;
        return `<div class="emp-family-card" style="border-left:3px solid ${a.herkunft === 'manuell' ? '#d97706' : '#cbd5e1'};margin-bottom:6px">
            <div class="emp-family-card-head">
                <div style="display:flex;gap:14px;align-items:center;flex-wrap:wrap">
                    <div style="min-width:110px;font-weight:600">${VC_ARTEN[a.art] || a.art}</div>
                    <div>${codeTxt}</div>
                    ${herk}
                </div>
                <div style="display:flex;gap:6px">${btns}</div>
            </div>
            ${fix}${bvgInfo}${zeit}
        </div>`;
    }).join('');
    // History: beendete Einträge — mit Fixbetrag/Basis, sonst sieht man nur Daten
    const heute = new Date().toISOString().slice(0, 10);
    const hist = (d.eintraege || []).filter(e => e.validTo && String(e.validTo).slice(0, 10) < heute)
        .slice().sort((a, b) => String(b.validFrom || '').localeCompare(String(a.validFrom || '')) || (b.id - a.id));
    const histHtml = hist.map(e => {
        const teile = [];
        if (e.beitragFixAn || e.beitragFixAg)
            teile.push(`Fix AN ${Number(e.beitragFixAn || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} / AG ${Number(e.beitragFixAg || 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`);
        if (e.bvgBasisManuell)
            teile.push('Basis manuell ' + Number(e.bvgBasisManuell).toLocaleString('de-CH'));
        if (e.bvgEintrittsgrund === 'entryCompany') teile.push('Firmeneintritt');
        else if (e.bvgEintrittsgrund === 'interruptionOfEmployment') teile.push('Wiedereintritt');
        else if (e.bvgEintrittsgrund) teile.push(e.bvgEintrittsgrund);
        if (e.bvgVollArbeitsfaehig === true) teile.push('voll arbeitsfähig');
        else if (e.bvgVollArbeitsfaehig === false) teile.push('nicht voll arbeitsfähig');
        if (e.bemerkung) teile.push(e.bemerkung);
        const detail = teile.length ? `<div style="font-size:11.5px;color:#64748b;margin-top:2px">${teile.map(esc).join(' · ')}</div>` : '';
        return `<div style="font-size:12px;color:#3f3f3f;padding:8px 4px;border-bottom:1px solid rgba(60,55,48,0.10)">
            <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                <span>${VC_ARTEN[e.art] || e.art} · <b>${esc(e.code === 'VERZICHT' ? 'Verzicht Freibetrag (Wunsch MA)' : (e.code || 'Fix'))}</b> · ${fmt(e.validFrom)} – ${fmt(e.validTo)}</span>
                <span style="margin-left:auto;display:flex;gap:6px">
                    <button class="btn-emp-edit" onclick="${e.code === 'VERZICHT' ? `vcOpenVerzichtModal(${e.id})` : `vcOpenModal(${e.id})`}">Details</button>
                    <button class="btn-emp-del" onclick="vcDelete(${e.id})">Löschen</button>
                </span>
            </div>
            ${detail}
        </div>`;
    }).join('');
    el.innerHTML = rows + (histHtml ? `<div id="vcHistWrap" style="display:none">${histHtml}</div>` : '');
    if (typeof zulHistFillSlot === 'function') zulHistFillSlot('vcHistPillSlot', hist.length, 'vcHistWrap', 'vcHistPill');
}

function vcOpenVerzichtModal(entryId) {
    if (!selectedEmployeeId || !_vcData) return;
    const entry = entryId ? (_vcData.eintraege || []).find(e => e.id === entryId) : null;
    const html = `
    <div id="vcModal" class="vc-wunsch-overlay" style="position:fixed;inset:0;background:rgba(60,55,48,.28);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('vcModal').remove()">
      <div class="ma-modal-box vc-wunsch-box">
        <div class="ma-modal-head">
            <div>
                <div class="ma-modal-title">MA wünscht Verzicht Freibetrag</div>
                <div class="ma-modal-sub">Nur nach ausdrücklichem Wunsch · erst ab Referenzalter wirksam</div>
            </div>
            <button class="ma-modal-close" onclick="document.getElementById('vcModal').remove()">✕</button>
        </div>
        <div class="ma-modal-body">
            <p class="vc-wunsch-lead">Nach dem Referenzalter zieht OneCrew automatisch CHF 1’400 vom AHV-Lohn ab. Will der MA das nicht, erfassen Sie den Wunsch hier — mit Datum.</p>
            <div class="vc-wunsch-cmp">
                <div class="vc-wunsch-tile">
                    <h4>Standard</h4>
                    <p>Freibetrag 1’400/Mt.<br>weniger AHV</p>
                </div>
                <div class="vc-wunsch-tile on">
                    <h4>Wunsch des MA</h4>
                    <p>AHV auf dem vollen Lohn<br>kein Freibetrag</p>
                </div>
            </div>
            <input type="hidden" id="vcId" value="${entry?.id ?? ''}">
            <input type="hidden" id="vcArt" value="AHV">
            <input type="hidden" id="vcCode" value="VERZICHT">
            <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">Wunsch ab *</div>
                    <input type="date" id="vcVon" class="ma-input" value="${entry?.validFrom ? entry.validFrom.slice(0, 10) : ''}">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Wunsch bis <span class="opt">(leer = offen)</span></div>
                    <input type="date" id="vcBis" class="ma-input" value="${entry?.validTo ? entry.validTo.slice(0, 10) : ''}">
                </div>
            </div>
            <div class="ma-grid cols-1" style="margin-top:8px">
                <div class="ma-field">
                    <div class="ma-field-label">Bemerkung <span class="opt">(optional)</span></div>
                    <input id="vcBem" class="ma-input" value="${esc(entry?.bemerkung || '')}" placeholder="z. B. schriftlicher Wunsch vom …">
                </div>
            </div>
            <div id="vcWunschErr" class="vc-wunsch-err">Bitte das Datum «Wunsch ab» eintragen.</div>
        </div>
        <div class="ma-modal-foot">
            <button type="button" class="btn btn-outline" onclick="document.getElementById('vcModal').remove()">Abbrechen</button>
            <button type="button" class="btn btn-primary" onclick="vcSaveVerzicht()">Speichern</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

function vcOpenModal(entryId, artVorgabe, codeVorgabe) {
    if (!selectedEmployeeId || !_vcData) return;
    const entry = entryId ? (_vcData.eintraege || []).find(e => e.id === entryId) : null;
    const art = entry?.art || artVorgabe || 'UVG';
    const codeSel = entry?.code || codeVorgabe || '';
    if (codeSel === 'VERZICHT') { vcOpenVerzichtModal(entryId); return; }
    const istVerzicht = codeSel === 'VERZICHT';
    const titel = istVerzicht
        ? (entry ? 'Verzicht Freibetrag bearbeiten' : 'MA wünscht Verzicht auf AHV-Freibetrag')
        : (entry ? 'Versicherungs-Code bearbeiten' : 'Abweichenden Versicherungs-Code erfassen');
    const hinweis = istVerzicht
        ? 'Nur auf ausdrücklichen Wunsch des MA. Der Verzicht gilt ab dem eingetragenen Datum, wird aber erst wirksam, wenn das Referenzalter erreicht ist (Monat danach). Vorher ändert er nichts am Lohn. Dann AHV auf dem vollen Lohn, ohne Abzug der 1’400.'
        : 'Nur erfassen, wenn der MA vom Standard abweicht (z.B. Büro → UVG B, Kader → BVG K2010). Die wählbaren Codes kommen aus den SV-Sätzen (System → SV-Sätze, Spalte Lösungs-Code). Ein neuer Eintrag beendet den bisherigen derselben Art automatisch am Vortag.';
    const optHtml = (a, sel) => {
        const arten = _vcData.arten.find(x => x.art === a);
        const opts = (arten?.optionen || []).map(o => `<option value="${esc(o.code)}" ${o.code === sel ? 'selected' : ''}>${esc(o.code)} · ${esc(o.name)}${o.istStandard ? ' ★ Standard' : ''}</option>`).join('');
        return (a === 'AHV' ? '' : `<option value="">– kein Code (nur Fixbetrag) –</option>`) + opts;
    };
    const html = `
    <div id="vcModal" style="position:fixed;inset:0;background:rgba(15,23,42,.55);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('vcModal').remove()">
      <div class="ma-modal-box narrow">
        <div class="ma-modal-head">
            <div class="ma-modal-title">${titel}</div>
            <button class="ma-modal-close" onclick="document.getElementById('vcModal').remove()">✕</button>
        </div>
        <div class="ma-modal-body">
            <div style="background:#f6f3ee;border:1px solid #e7e1d8;padding:10px 12px;border-radius:8px;font-size:12px;color:#3f4d5e;margin-bottom:10px;line-height:1.5">
                ${hinweis}
            </div>
            <input type="hidden" id="vcId" value="${entry?.id ?? ''}">
            <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">Versicherung *</div>
                    <select id="vcArt" class="ma-input" onchange="vcArtChanged()" ${entry || istVerzicht ? 'disabled' : ''}>
                        ${Object.entries(VC_ARTEN).map(([k, v]) => `<option value="${k}" ${k === art ? 'selected' : ''}>${v}</option>`).join('')}
                    </select>
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">${istVerzicht ? 'Art des Wunsches' : 'Code *'}</div>
                    <select id="vcCode" class="ma-input" ${istVerzicht ? 'disabled' : ''}>${optHtml(art, codeSel)}</select>
                </div>
            </div>
            <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">${istVerzicht ? 'MA wünscht Verzicht Freibetrag ab *' : 'Gültig ab *'}</div>
                    <input type="date" id="vcVon" class="ma-input" value="${entry?.validFrom ? entry.validFrom.slice(0, 10) : ''}">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">${istVerzicht ? 'Wunsch bis' : 'Gültig bis'} <span class="opt">(leer = offen)</span></div>
                    <input type="date" id="vcBis" class="ma-input" value="${entry?.validTo ? entry.validTo.slice(0, 10) : ''}">
                </div>
            </div>
            <div id="vcFixBox" style="${art === 'BVG' ? '' : 'display:none'}">
              <div class="ma-grid cols-2">
                <div class="ma-field">
                    <div class="ma-field-label">BVG-Beitrag fix AN / Mt. <span class="opt">(CHF, ersetzt Prozent)</span></div>
                    <input type="number" step="0.05" min="0" id="vcFixAn" class="ma-input" value="${entry?.beitragFixAn ?? ''}">
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">BVG-Beitrag fix AG / Mt. <span class="opt">(CHF)</span></div>
                    <input type="number" step="0.05" min="0" id="vcFixAg" class="ma-input" value="${entry?.beitragFixAg ?? ''}">
                </div>
              </div>
              <div class="emp-section-title" style="margin-top:6px">BVG-Eintrittsmeldung</div>
              <div class="ma-grid cols-3">
                <div class="ma-field">
                    <div class="ma-field-label">Eintrittsgrund</div>
                    <select id="vcBvgGrund" class="ma-input">
                        <option value="">–</option>
                        <option value="entryCompany" ${entry?.bvgEintrittsgrund === 'entryCompany' ? 'selected' : ''}>Firmeneintritt</option>
                        <option value="interruptionOfEmployment" ${entry?.bvgEintrittsgrund === 'interruptionOfEmployment' ? 'selected' : ''}>Wiedereintritt nach Unterbruch</option>
                        <option value="others" ${entry?.bvgEintrittsgrund === 'others' ? 'selected' : ''}>Anderer Grund (z.B. Lohnschwelle erreicht)</option>
                    </select>
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Bei Eintritt voll arbeitsfähig</div>
                    <select id="vcBvgFit" class="ma-input">
                        <option value="">–</option>
                        <option value="1" ${entry?.bvgVollArbeitsfaehig === true ? 'selected' : ''}>ja</option>
                        <option value="0" ${entry?.bvgVollArbeitsfaehig === false ? 'selected' : ''}>nein</option>
                    </select>
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">BVG-Basis manuell / Jahr <span class="opt">(CHF, gemeldete Basis)</span></div>
                    <input type="number" step="1" min="0" id="vcBvgBasis" class="ma-input" value="${entry?.bvgBasisManuell ?? ''}">
                </div>
              </div>
            </div>
            <div class="ma-grid cols-1">
                <div class="ma-field">
                    <div class="ma-field-label">Bemerkung <span class="opt">(optional)</span></div>
                    <input id="vcBem" class="ma-input" value="${esc(entry?.bemerkung || '')}">
                </div>
            </div>
            ${entry ? '' : `<label style="display:flex;align-items:center;gap:8px;font-size:12.5px;margin-top:6px"><input type="checkbox" id="vcZusaetzlich"> Zusätzlich zum bisherigen Code (z.B. KTG 11 + 12 Überschusslohn) — bisherige Codes bleiben</label>`}
        </div>
        <div class="ma-modal-foot">
            <button class="btn btn-outline" onclick="document.getElementById('vcModal').remove()">Abbrechen</button>
            <button class="btn btn-primary" onclick="vcSave()">Speichern</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

function vcArtChanged() {
    const art = document.getElementById('vcArt').value;
    const arten = _vcData.arten.find(x => x.art === art);
    const sel = document.getElementById('vcCode');
    sel.innerHTML = (art === 'AHV' ? '' : `<option value="">– kein Code (nur Fixbetrag) –</option>`) +
        (arten?.optionen || []).map(o => `<option value="${esc(o.code)}">${esc(o.code)} · ${esc(o.name)}${o.istStandard ? ' ★ Standard' : ''}</option>`).join('');
    document.getElementById('vcFixBox').style.display = art === 'BVG' ? '' : 'none';
}

async function vcSaveVerzicht() {
    const err = document.getElementById('vcWunschErr');
    if (err) err.classList.remove('show');
    const von = document.getElementById('vcVon')?.value;
    if (!von) { if (err) err.classList.add('show'); return; }
    const id = document.getElementById('vcId')?.value;
    const dto = {
        art: 'AHV', code: 'VERZICHT',
        validFrom: von,
        validTo: document.getElementById('vcBis')?.value || null,
        bemerkung: (document.getElementById('vcBem')?.value || '').trim() || null
    };
    const url = id ? `/api/employees/${selectedEmployeeId}/versicherung-codes/${id}` : `/api/employees/${selectedEmployeeId}/versicherung-codes`;
    const res = await fetch(url, { method: id ? 'PUT' : 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(dto) });
    if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { const b = await res.clone().json().catch(() => ({})); if (err) { err.textContent = b.message || 'Fehler beim Speichern.'; err.classList.add('show'); } return; }
    document.getElementById('vcModal')?.remove();
    vcLoad(selectedEmployeeId);
}

async function vcSave() {
    const id = document.getElementById('vcId').value;
    const art = document.getElementById('vcArt').value;
    const code = document.getElementById('vcCode').value || null;
    const von = document.getElementById('vcVon').value;
    const bis = document.getElementById('vcBis').value || null;
    const fixAn = parseFloat(document.getElementById('vcFixAn')?.value) || null;
    const fixAg = parseFloat(document.getElementById('vcFixAg')?.value) || null;
    if (!von) { alert('Bitte «Gültig ab» eintragen.'); return; }
    if (!code && !(art === 'BVG' && (fixAn || fixAg))) { alert(art === 'BVG' ? 'Code oder festen Beitrag angeben.' : 'Bitte einen Code wählen.'); return; }
    const fitV = document.getElementById('vcBvgFit')?.value;
    const dto = { art, code, validFrom: von, validTo: bis, beitragFixAn: fixAn, beitragFixAg: fixAg,
                  bemerkung: document.getElementById('vcBem').value.trim() || null,
                  bvgEintrittsgrund: art === 'BVG' ? (document.getElementById('vcBvgGrund')?.value || null) : null,
                  bvgVollArbeitsfaehig: art === 'BVG' && fitV ? fitV === '1' : null,
                  bvgBasisManuell: art === 'BVG' ? (parseFloat(document.getElementById('vcBvgBasis')?.value) || null) : null,
                  zusaetzlich: !!document.getElementById('vcZusaetzlich')?.checked };
    const url = id ? `/api/employees/${selectedEmployeeId}/versicherung-codes/${id}` : `/api/employees/${selectedEmployeeId}/versicherung-codes`;
    const res = await fetch(url, { method: id ? 'PUT' : 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(dto) });
    if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { const b = await res.clone().json().catch(() => ({})); alert(b.message || 'Fehler beim Speichern.'); return; }
    document.getElementById('vcModal')?.remove();
    vcLoad(selectedEmployeeId);
}

async function vcDelete(id) {
    if (!(await liquidConfirm('Diesen Versicherungs-Code löschen? Der MA fällt dann auf den Standard zurück.'))) return;
    const res = await fetch(`/api/employees/${selectedEmployeeId}/versicherung-codes/${id}`, { method: 'DELETE', headers: ah() });
    if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { const b = await res.clone().json().catch(() => ({})); alert(b.message || 'Fehler beim Löschen.'); return; }
    vcLoad(selectedEmployeeId);
}
