// ── Versicherungs-Codes am Mitarbeiter (Walter 07.09.2026, Swissdec-Lösungen) ──
// UVG (A/B/P …), UVG-Zusatz und KTG (10/11/12), BVG (11/21/22/K2010 oder Fixbetrag).
// Ohne Eintrag gilt der Standard der SV-Sätze (Zeile mit ★). API:
// /api/employees/{id}/versicherung-codes  (GET liefert pro Art effektiven Code + Optionen)
let _vcData = null;
const VC_ARTEN = { UVG: 'UVG (Unfall)', UVGZ: 'UVG-Zusatz', KTG: 'KTG', BVG: 'BVG', AHV: 'AHV/ALV' };

function vcUvgName(id) {
    const v = (_vcData?.uvgVersicherer || []).find(x => x.id === id);
    return v ? v.bezeichnung : '';
}
function vcUvgSelectHtml(selId) {
    const liste = _vcData?.uvgVersicherer || [];
    const opts = liste.map(v => `<option value="${v.id}" ${Number(v.id) === Number(selId) ? 'selected' : ''}>${esc(v.bezeichnung)}${v.kassennummer ? ' · ' + esc(v.kassennummer) : ''}</option>`).join('');
    const leer = liste.length > 1
        ? '– bitte den Versicherer wählen –'
        : (liste.length ? '– nicht einzeln zugewiesen –' : '– der Filiale ist kein UVG-Versicherer zugeordnet –');
    return `<option value="">${leer}</option>` + opts;
}

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
        const uvgV = a.art === 'UVG' && a.explizit?.empfaengerId
            ? `<div style="font-size:11.5px;color:#475569">UVG-Versicherer · ${esc(vcUvgName(a.explizit.empfaengerId) || ('Nr. ' + a.explizit.empfaengerId))}</div>` : '';
        const fix = a.explizit && (a.explizit.beitragFixAn || a.explizit.beitragFixAg)
            ? `<div style="font-size:11.5px;color:#475569">Fixbetrag/Mt.: AN ${Number(a.explizit.beitragFixAn || 0).toFixed(2)} · AG ${Number(a.explizit.beitragFixAg || 0).toFixed(2)}</div>` : '';
        const bvgInfo = a.art === 'BVG' && a.explizit && (a.explizit.bvgEintrittsgrund || a.explizit.bvgVollArbeitsfaehig != null || a.explizit.bvgBasisManuell)
            ? `<div style="font-size:11.5px;color:#475569">${[a.explizit.bvgEintrittsgrund === 'entryCompany' ? 'Firmeneintritt' : a.explizit.bvgEintrittsgrund === 'interruptionOfEmployment' ? 'Wiedereintritt' : a.explizit.bvgEintrittsgrund,
                 a.explizit.bvgVollArbeitsfaehig === true ? 'voll arbeitsfähig' : a.explizit.bvgVollArbeitsfaehig === false ? 'nicht voll arbeitsfähig' : null,
                 a.explizit.bvgBasisManuell ? 'Basis manuell ' + Number(a.explizit.bvgBasisManuell).toLocaleString('de-CH') : null].filter(Boolean).join(' · ')}</div>` : '';
        const zeit = a.explizit ? `<div style="font-size:11.5px;color:#64748b">${istVerzicht ? 'Wunsch ab' : 'ab'} ${fmt(a.explizit.validFrom)}${a.explizit.validTo ? ' bis ' + fmt(a.explizit.validTo) : ''}${a.explizit.bemerkung ? ' · ' + esc(a.explizit.bemerkung) : ''}</div>` : '';
        const darf = !!d.darfVerzicht;
        const v = d.verzicht || {};
        const neuLabel = v.gesperrt ? `Ab 1.1.${v.naechstesJahr} Verzicht` : 'Verzicht Freibetrag';
        const verzichtNeu = darf
            ? `<button class="btn-emp-edit" onclick="vcOpenVerzichtModal()">${neuLabel}</button> `
            : '';
        const btns = a.explizit
            ? `${istVerzicht && !darf ? '' : `<button class="btn-emp-edit" onclick="${istVerzicht ? `vcOpenVerzichtModal(${a.explizit.id})` : `vcOpenModal(${a.explizit.id})`}">Bearbeiten</button> `}<button class="btn-emp-del" onclick="vcDelete(${a.explizit.id})">Löschen</button>`
            : a.art === 'AHV'
                ? `${verzichtNeu}<button class="btn-emp-edit" onclick="vcOpenModal(null, 'AHV', 'SONDERFALL')">Sonderfall</button>`
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
            ${uvgV}${fix}${bvgInfo}${zeit}
            ${a.art === 'AHV' && d.verzichtHinweis ? `<div style="font-size:11.5px;color:#64748b;line-height:1.4;margin-top:4px">${esc(d.verzichtHinweis)}</div>` : ''}
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
                    <button class="btn-emp-edit" onclick="vcReaktivieren(${e.id})">Wieder öffnen</button>
                    ${e.code === 'VERZICHT' && !d.darfVerzicht ? '' : `<button class="btn-emp-edit" onclick="${e.code === 'VERZICHT' ? `vcOpenVerzichtModal(${e.id})` : `vcOpenModal(${e.id})`}">Details</button>`}
                    <button class="btn-emp-del" onclick="vcDelete(${e.id})">Löschen</button>
                </span>
            </div>
            ${detail}
        </div>`;
    }).join('');
    el.innerHTML = rows + (histHtml ? `<div id="vcHistWrap" style="display:none">${histHtml}</div>` : '');
    if (typeof zulHistFillSlot === 'function') zulHistFillSlot('vcHistPillSlot', hist.length, 'vcHistWrap', 'vcHistPill');
}

function vcDarfVerzicht() {
    return !!_vcData?.darfVerzicht;
}
function vcAhvOptionen(arten, sel) {
    const opts = (arten?.optionen || [])
        .filter(o => vcDarfVerzicht() || o.code !== 'VERZICHT')
        .map(o => `<option value="${esc(o.code)}" ${o.code === sel ? 'selected' : ''}>${esc(o.code)} · ${esc(o.name)}${o.istStandard ? ' ★ Standard' : ''}</option>`).join('');
    return opts;
}
function vcOpenVerzichtModal(entryId) {
    if (!selectedEmployeeId || !_vcData) return;
    if (!vcDarfVerzicht()) return;
    const entry = entryId ? (_vcData.eintraege || []).find(e => e.id === entryId) : null;
    const v = _vcData.verzicht || {};
    const vonWert = entry?.validFrom
        ? entry.validFrom.slice(0, 10)
        : (v.gesperrt ? `${v.naechstesJahr}-01-01` : (v.ersterTag || ''));
    const endeJahr = (v.gesperrt && entry) ? v.jahr : (vonWert.slice(0, 4) || v.jahr);
    const ende = `${endeJahr}-12-31`;
    const bis = entry?.validTo ? entry.validTo.slice(0, 10) : '';
    const bisFremd = bis && bis !== ende
        ? `<option value="${bis}" selected>endet am ${bis.slice(8, 10)}.${bis.slice(5, 7)}.${bis.slice(0, 4)}</option>` : '';
    const vonGesperrt = v.gesperrt ? 'readonly' : '';
    const html = `
    <div id="vcModal" class="vc-wunsch-overlay" style="position:fixed;inset:0;background:rgba(60,55,48,.28);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('vcModal').remove()">
      <div class="ma-modal-box vc-wunsch-box">
        <div class="ma-modal-head">
            <div>
                <div class="ma-modal-title">MA wünscht Verzicht Freibetrag</div>
                <div class="ma-modal-sub">Gilt für das Kalenderjahr · Änderung ab 1. Januar</div>
            </div>
            <button class="ma-modal-close" onclick="document.getElementById('vcModal').remove()">✕</button>
        </div>
        <div class="ma-modal-body">
            <p class="vc-wunsch-lead">${esc(_vcData.verzichtHinweis || '')}</p>
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
                    <input type="date" id="vcVon" class="ma-input" value="${vonWert}" ${vonGesperrt}>
                </div>
                <div class="ma-field">
                    <div class="ma-field-label">Weiterführung</div>
                    <select id="vcBisWahl" class="ma-input">
                        <option value="" ${bis && bis !== ende ? '' : 'selected'}>läuft weiter, auch nächstes Jahr</option>
                        ${bisFremd}
                        ${v.naechstesGesperrt ? '' : `<option value="${ende}" ${bis === ende ? 'selected' : ''}>endet am 31.12.${endeJahr} — ab 1.1.${Number(endeJahr) + 1} mit Freibetrag</option>`}
                    </select>
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

const UVG_ZIFFERN = [
    ['0', '0 — nicht versichert'],
    ['1', '1 — BU und NBU, MA zahlt NBU'],
    ['2', '2 — BU und NBU, AG zahlt NBU'],
    ['3', '3 — nur BU']
];

function vcUvgCodeTeile(code) {
    const c = (code || '').trim().toUpperCase();
    if (!c || c[0] < 'A' || c[0] > 'Z') return { teil: '', ziffer: '' };
    const z = c.length >= 2 ? c[1] : '';
    return { teil: c[0], ziffer: ['0', '1', '2', '3'].includes(z) ? z : '' };
}

function vcUvgTeilListe(artObj) {
    const liste = artObj?.betriebsteile;
    if (liste && liste.length) return liste;
    const seen = new Map();
    (artObj?.optionen || []).forEach(o => {
        const c = (o.code || '').trim().toUpperCase();
        if (c && c[0] >= 'A' && c[0] <= 'Z' && !seen.has(c[0])) seen.set(c[0], o.name || '');
    });
    return [...seen.entries()].map(([code, name]) => ({ code, name }));
}

function vcUvgTeilHtml(artObj, gewaehlt) {
    const opts = vcUvgTeilListe(artObj).map(t => {
        const name = String(t.name || '').split('—')[0].split('–')[0].trim();
        const label = name && name.toUpperCase() !== t.code ? `${t.code} · ${name}` : t.code;
        return `<option value="${t.code}" ${t.code === gewaehlt ? 'selected' : ''}>${esc(label)}</option>`;
    }).join('');
    if (!opts) return '<option value="">– kein Betriebsteil in den SV-Sätzen –</option>';
    return `<option value="">– Betriebsteil –</option>${opts}`;
}

function vcUvgZifferHtml(gewaehlt) {
    const ok = ['0', '1', '2', '3'].includes(gewaehlt) ? gewaehlt : '';
    return `<option value="">– Ziffer –</option>` + UVG_ZIFFERN.map(([v, l]) =>
        `<option value="${v}" ${v === ok ? 'selected' : ''}>${l}</option>`).join('');
}

function vcOpenModal(entryId, artVorgabe, codeVorgabe) {
    if (!selectedEmployeeId || !_vcData) return;
    const entry = entryId ? (_vcData.eintraege || []).find(e => e.id === entryId) : null;
    const art = entry?.art || artVorgabe || 'UVG';
    const codeSel = entry?.code || codeVorgabe || '';
    if (codeSel === 'VERZICHT') {
        if (!vcDarfVerzicht()) return;
        vcOpenVerzichtModal(entryId);
        return;
    }
    const istVerzicht = codeSel === 'VERZICHT';
    const titel = istVerzicht
        ? (entry ? 'Verzicht Freibetrag bearbeiten' : 'MA wünscht Verzicht auf AHV-Freibetrag')
        : (entry ? 'Versicherungs-Code bearbeiten' : 'Abweichenden Versicherungs-Code erfassen');
    const hinweis = istVerzicht
        ? 'Nur auf ausdrücklichen Wunsch des MA. Der Verzicht gilt ab dem eingetragenen Datum, wird aber erst wirksam, wenn das Referenzalter erreicht ist (Monat danach). Vorher ändert er nichts am Lohn. Dann AHV auf dem vollen Lohn, ohne Abzug der 1’400.'
        : 'Nur erfassen, wenn der MA vom Standard abweicht. Beim UVG: Betriebsteil aus den SV-Sätzen und die Ziffer 0, 1, 2 oder 3. Bei UVGZ, KTG und BVG den Lösungs-Code. Ein neuer Eintrag beendet den bisherigen derselben Art automatisch am Vortag.';
    const optHtml = (a, sel) => {
        const arten = _vcData.arten.find(x => x.art === a);
        return (a === 'AHV' || a === 'UVG' ? '' : `<option value="">– kein Code (nur Fixbetrag) –</option>`) + vcAhvOptionen(arten, sel);
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
                <div class="ma-field" id="vcCodeFeld" style="${art === 'UVG' ? 'display:none' : ''}">
                    <div class="ma-field-label">${istVerzicht ? 'Art des Wunsches' : 'Code *'}</div>
                    <select id="vcCode" class="ma-input" ${istVerzicht ? 'disabled' : ''}>${optHtml(art, codeSel)}</select>
                </div>
                <div class="ma-field" id="vcUvgCodeFeld" style="${art === 'UVG' ? '' : 'display:none'}">
                    <div class="ma-field-label">UVG-Code *</div>
                    <div style="display:grid;grid-template-columns:1fr 1.4fr;gap:8px">
                        <select id="vcUvgTeil" class="ma-input">${vcUvgTeilHtml(_vcData.arten.find(x => x.art === 'UVG'), vcUvgCodeTeile(art === 'UVG' ? codeSel : '').teil)}</select>
                        <select id="vcUvgZiffer" class="ma-input">${vcUvgZifferHtml(vcUvgCodeTeile(art === 'UVG' ? codeSel : '').ziffer)}</select>
                    </div>
                    <div style="font-size:11px;color:#8b8b8b;margin-top:3px">Zweite Stelle nur 0, 1, 2 oder 3. 0 braucht keinen Satz in den SV-Sätzen.</div>
                </div>
            </div>
            <div id="vcUvgBox" style="${art === 'UVG' ? '' : 'display:none'};margin-top:8px">
                <div class="ma-field">
                    <div class="ma-field-label">UVG-Versicherer${(_vcData?.uvgVersicherer || []).length > 1 ? ' *' : ''}</div>
                    <select id="vcUvg" class="ma-input">${vcUvgSelectHtml(entry?.empfaengerId || '')}</select>
                    <div style="font-size:11px;color:#8b8b8b;margin-top:3px">${(_vcData?.uvgVersicherer || []).length > 1
                        ? 'Die Filiale hat mehrere UVG-Versicherer. Diese Person braucht einen davon.'
                        : 'Mehrere Versicherer der Filiale können verschiedenen Personen zugewiesen werden. Leer = der einzige offene UVG der Filiale.'}</div>
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
    sel.innerHTML = (art === 'AHV' || art === 'UVG' ? '' : `<option value="">– kein Code (nur Fixbetrag) –</option>`) +
        vcAhvOptionen(arten, '');
    document.getElementById('vcFixBox').style.display = art === 'BVG' ? '' : 'none';
    const istUvg = art === 'UVG';
    const codeFeld = document.getElementById('vcCodeFeld');
    const uvgCode = document.getElementById('vcUvgCodeFeld');
    if (codeFeld) codeFeld.style.display = istUvg ? 'none' : '';
    if (uvgCode) uvgCode.style.display = istUvg ? '' : 'none';
    const uvg = document.getElementById('vcUvgBox');
    if (uvg) uvg.style.display = istUvg ? '' : 'none';
    if (istUvg) {
        const teil = document.getElementById('vcUvgTeil');
        const ziffer = document.getElementById('vcUvgZiffer');
        if (teil) teil.innerHTML = vcUvgTeilHtml(arten, teil.value || '');
        if (ziffer) ziffer.innerHTML = vcUvgZifferHtml(ziffer.value || '');
    }
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
        validTo: document.getElementById('vcBisWahl')?.value || null,
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
    let code = document.getElementById('vcCode').value || null;
    if (art === 'UVG') {
        const teil = document.getElementById('vcUvgTeil')?.value || '';
        const ziffer = document.getElementById('vcUvgZiffer')?.value || '';
        code = teil && ziffer !== '' ? teil + ziffer : null;
    }
    const von = document.getElementById('vcVon').value;
    const bis = document.getElementById('vcBis').value || null;
    const fixAn = parseFloat(document.getElementById('vcFixAn')?.value) || null;
    const fixAg = parseFloat(document.getElementById('vcFixAg')?.value) || null;
    if (!von) { alert('Bitte «Gültig ab» eintragen.'); return; }
    if (art === 'UVG' && (_vcData?.uvgVersicherer || []).length > 1 && !(parseInt(document.getElementById('vcUvg')?.value, 10) > 0)) {
        alert('Die Filiale hat mehrere UVG-Versicherer. Bitte den zuständigen der Person zuweisen.');
        return;
    }
    if (!code && !(art === 'BVG' && (fixAn || fixAg))) {
        alert(art === 'UVG'
            ? 'UVG-Code fehlt. Betriebsteil und Ziffer 0, 1, 2 oder 3 angeben.'
            : (art === 'BVG' ? 'Code oder festen Beitrag angeben.' : 'Bitte einen Code wählen.'));
        return;
    }
    const fitV = document.getElementById('vcBvgFit')?.value;
    const dto = { art, code, validFrom: von, validTo: bis, beitragFixAn: fixAn, beitragFixAg: fixAg,
                  bemerkung: document.getElementById('vcBem').value.trim() || null,
                  bvgEintrittsgrund: art === 'BVG' ? (document.getElementById('vcBvgGrund')?.value || null) : null,
                  bvgVollArbeitsfaehig: art === 'BVG' && fitV ? fitV === '1' : null,
                  bvgBasisManuell: art === 'BVG' ? (parseFloat(document.getElementById('vcBvgBasis')?.value) || null) : null,
                  zusaetzlich: !!document.getElementById('vcZusaetzlich')?.checked,
                  empfaengerId: art === 'UVG' ? (parseInt(document.getElementById('vcUvg')?.value, 10) || null) : null };
    const url = id ? `/api/employees/${selectedEmployeeId}/versicherung-codes/${id}` : `/api/employees/${selectedEmployeeId}/versicherung-codes`;
    const res = await fetch(url, { method: id ? 'PUT' : 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(dto) });
    if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { const b = await res.clone().json().catch(() => ({})); alert(b.message || 'Fehler beim Speichern.'); return; }
    document.getElementById('vcModal')?.remove();
    vcLoad(selectedEmployeeId);
}

async function vcReaktivieren(id) {
    const e = (_vcData?.eintraege || []).find(x => x.id === id);
    const art = VC_ARTEN[e?.art] || e?.art || 'Dieser Eintrag';
    const ab = e?.validFrom ? formatDate(e.validFrom) : '';
    const text = ab
        ? `${art} ab ${ab} gilt wieder ohne Enddatum.`
        : `${art} gilt wieder ohne Enddatum.`;
    if (!(await liquidConfirm(text, { title: 'Wieder öffnen', yesLabel: 'Öffnen', noLabel: 'Abbrechen' }))) return;
    const res = await fetch(`/api/employees/${selectedEmployeeId}/versicherung-codes/${id}/wieder-oeffnen`, { method: 'POST', headers: ah() });
    if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { const b = await res.clone().json().catch(() => ({})); alert(b.message || 'Konnte nicht geöffnet werden.'); return; }
    vcLoad(selectedEmployeeId);
}

async function vcDelete(id) {
    if (!(await liquidConfirm('Diesen Eintrag löschen? Endete eine frühere Versicherung am Vortag, gilt sie wieder. Sonst gilt der Standard.'))) return;
    const res = await fetch(`/api/employees/${selectedEmployeeId}/versicherung-codes/${id}`, { method: 'DELETE', headers: ah() });
    if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
    if (!res.ok) { const b = await res.clone().json().catch(() => ({})); alert(b.message || 'Fehler beim Löschen.'); return; }
    vcLoad(selectedEmployeeId);
}
