// Lohnausweis-Angaben (Swissdec Basistest 03_05, Wegleitung 2026).
// Person: Mitarbeiter → Zulagen. Unternehmen: Filial-Stammdaten.

const LA_KANTONE = ['AG','AI','AR','BE','BL','BS','FR','GE','GL','GR','JU','LU','NE','NW','OW','SG','SH','SO','SZ','TG','TI','UR','VD','VS','ZG','ZH'];

function laEsc(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

function laKantonSelect(id, wert) {
    const opts = ['<option value="">—</option>']
        .concat(LA_KANTONE.map(k => `<option value="${k}"${wert === k ? ' selected' : ''}>${k}</option>`));
    return `<select id="${id}" class="f-input">${opts.join('')}</select>`;
}

function laTri(id, label, wert, firmaJa) {
    const v = wert === true ? 'ja' : wert === false ? 'nein' : '';
    const stand = firmaJa ? 'ja' : 'nein';
    return `<label class="f-label" style="display:block;margin:8px 0">
        <span style="display:block;margin-bottom:4px">${label}</span>
        <select id="${id}" class="f-input">
            <option value=""${v === '' ? ' selected' : ''}>wie Unternehmen (${stand})</option>
            <option value="ja"${v === 'ja' ? ' selected' : ''}>Ja</option>
            <option value="nein"${v === 'nein' ? ' selected' : ''}>Nein</option>
        </select>
    </label>`;
}

function laModus(id, wert) {
    const v = wert || '';
    return `<select id="${id}" class="f-input" onchange="laModusZeigen()">
        <option value=""${v === '' ? ' selected' : ''}>wie Unternehmen</option>
        <option value="EIGEN"${v === 'EIGEN' ? ' selected' : ''}>eigene Angabe</option>
        <option value="KEIN"${v === 'KEIN' ? ' selected' : ''}>keine</option>
    </select>`;
}

function laTriWert(id) {
    const v = document.getElementById(id)?.value;
    if (v === 'ja') return true;
    if (v === 'nein') return false;
    return null;
}

function laOverlay() {
    let el = document.getElementById('laAngabenOverlay');
    if (el) return el;
    el = document.createElement('div');
    el.id = 'laAngabenOverlay';
    el.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(0,0,0,0.45);z-index:3200;align-items:flex-start;justify-content:center;overflow-y:auto;padding:28px 16px';
    el.innerHTML = `<div class="modal" style="width:min(920px,96vw);max-height:min(90vh,920px);display:flex;flex-direction:column;overflow:hidden;padding:0" onclick="event.stopPropagation()">
        <div class="modal-hd" style="flex:0 0 auto">
            <div class="modal-hd-title" id="laAngabenTitel">Lohnausweis</div>
            <button type="button" class="modal-x" onclick="laAngabenSchliessen()">×</button>
        </div>
        <div class="modal-body" id="laAngabenBody" style="flex:1 1 auto;overflow:auto"></div>
        <div class="modal-ft" style="flex:0 0 auto">
            <button type="button" class="btn btn-outline" onclick="laAngabenSchliessen()">Abbrechen</button>
            <button type="button" id="laAngabenSave" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 16px;cursor:pointer">Speichern</button>
        </div>
    </div>`;
    el.addEventListener('click', e => { if (e.target === el) laAngabenSchliessen(); });
    document.body.appendChild(el);
    return el;
}

function laAngabenSchliessen() {
    const el = document.getElementById('laAngabenOverlay');
    if (el) el.style.display = 'none';
}

async function laPersonKarte(employeeId) {
    const box = document.getElementById('laAngabenKarte');
    if (!box || !employeeId) return;
    box.innerHTML = '<div class="emp-placeholder"><span>Lohnausweis wird geladen…</span></div>';
    try {
        const r = await fetch(`/api/employees/${employeeId}/lohnausweis-angaben`, { headers: ah() });
        if (!r.ok) { box.innerHTML = ''; return; }
        const d = await r.json();
        const p = d.person || {};
        const art = p.art === 'RENTE' ? 'Rentenbescheinigung' : 'Lohnausweis';
        const extras = [];
        if (p.boxF != null) extras.push('F ' + (p.boxF ? 'ja' : 'nein'));
        if (p.boxG != null) extras.push('G ' + (p.boxG ? 'ja' : 'nein'));
        if (p.kinderzulageAhv) extras.push('Kinderzulagen Ausgleichskasse');
        if (p.nachgenuss) extras.push('Besoldungsnachgenuss');
        if (p.umzugBetrag) extras.push('Umzugskosten');
        box.innerHTML = `
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:0">
                <span>Lohnausweis</span>
                <button type="button" class="btn-emp-add" onclick="laPersonOeffnen(${employeeId})">Angaben</button>
            </div>
            <div style="font-size:13px;color:#3f3f3f;margin:0 0 14px;line-height:1.45">
                ${laEsc(art)}${extras.length ? ' · ' + laEsc(extras.join(' · ')) : ''}
                <div style="font-size:12px;color:#8b8b8b;margin-top:2px">Nicht gesetzte Punkte gelten wie beim Unternehmen${d.firmaName ? ' (' + laEsc(d.firmaName) + ')' : ''}.</div>
            </div>`;
    } catch {
        box.innerHTML = '';
    }
}

async function laPersonOeffnen(employeeId) {
    const r = await fetch(`/api/employees/${employeeId}/lohnausweis-angaben`, { headers: ah() });
    if (!r.ok) { alert('Lohnausweis-Angaben konnten nicht geladen werden.'); return; }
    const d = await r.json();
    const p = d.person || {};
    const f = d.firma || {};
    const ov = laOverlay();
    document.getElementById('laAngabenTitel').textContent = 'Lohnausweis der Person';
    document.getElementById('laAngabenBody').innerHTML = laPersonHtml(p, f, d.hinweis);
    document.getElementById('laAngabenSave').onclick = () => laPersonSpeichern(employeeId);
    laModusZeigen();
    ov.style.display = 'flex';
}

function laPersonHtml(p, f, hinweis) {
    const grant = (titel, modusId, datumId, kantonId, modus, datum, kanton, firmaText) => `
        <div style="margin:12px 0;padding-top:8px;border-top:1px solid rgba(60,55,48,0.12)">
            <div style="font-weight:700;font-size:13px;margin-bottom:4px">${titel}</div>
            <div style="font-size:12px;color:#8b8b8b;margin-bottom:6px">${laEsc(firmaText)}</div>
            ${laModus(modusId, modus)}
            <div data-la-wenn="${modusId}=EIGEN" style="display:grid;grid-template-columns:1fr 140px;gap:8px;margin-top:8px">
                <label class="f-label">Datum<input type="date" id="${datumId}" class="f-input" value="${laEsc(datum || '')}"></label>
                <label class="f-label">Kanton${laKantonSelect(kantonId, kanton)}</label>
            </div>
        </div>`;
    const firmaGrant = (datum, kanton) => datum && kanton ? `Unternehmen: Kanton ${kanton}, ${datum.split('-').reverse().join('.')}` : 'Unternehmen: keine Angabe';
    return `
        ${hinweis ? `<div style="font-size:12.5px;color:#92400e;margin-bottom:10px">${laEsc(hinweis)}</div>` : ''}
        <label class="f-label">Art
            <select id="laArt" class="f-input">
                <option value="LOHN"${p.art !== 'RENTE' ? ' selected' : ''}>Lohnausweis (Buchstabe A)</option>
                <option value="RENTE"${p.art === 'RENTE' ? ' selected' : ''}>Rentenbescheinigung (Buchstabe B)</option>
            </select>
        </label>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
            ${laTri('laBoxF', 'Ziffer F — unentgeltliche Beförderung', p.boxF, !!f.boxF)}
            ${laTri('laBoxG', 'Ziffer G — Kantine / Lunch-Checks', p.boxG, !!f.boxG)}
        </div>
        <div style="margin-top:12px">
            <div style="font-weight:700;font-size:13px">Ziffer 14 — nicht bewertete Gehaltsnebenleistungen</div>
            <div style="font-size:12px;color:#8b8b8b;margin:4px 0">Unternehmen: ${laEsc(f.ziffer14 || 'kein Text')}</div>
            ${laModus('laZ14Modus', p.ziffer14Modus)}
            <div data-la-wenn="laZ14Modus=EIGEN" style="margin-top:8px">
                <textarea id="laZ14Text" class="f-input" rows="2" placeholder="z. B. stark verbilligte Abgabe eines Personenwagens">${laEsc(p.ziffer14Text || '')}</textarea>
            </div>
        </div>
        ${grant('Expatriate-Ruling', 'laExpatModus', 'laExpatDatum', 'laExpatKanton', p.expatModus, p.expatDatum, p.expatKanton, firmaGrant(f.expatDatum, f.expatKanton))}
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:8px">
            ${laTri('laWagenAb', 'Geschäftswagen: beträchtliche Kosten, Privatanteil abklären', p.wagenAbklaeren, !!f.wagenAbklaeren)}
            ${laTri('laWagenMin', 'Geschäftswagen: Arbeitnehmer zahlt mind. 0,9 %', p.wagenMinimal, !!f.wagenMinimal)}
        </div>
        ${grant('Spesenreglement genehmigt', 'laSpesenModus', 'laSpesenDatum', 'laSpesenKanton', p.spesenModus, p.spesenDatum, p.spesenKanton, firmaGrant(f.spesenDatum, f.spesenKanton))}
        ${laTri('laRz52', 'Ziffer 13.1.1 — Bedingungen von Randziffer 52 eingehalten', p.rz52, !!f.rz52)}
        ${grant('Verkehrswert Mitarbeiterbeteiligungen', 'laWertModus', 'laWertDatum', 'laWertKanton', p.wertModus, p.wertDatum, p.wertKanton, firmaGrant(f.wertDatum, f.wertKanton))}
        <div style="margin-top:12px;padding-top:8px;border-top:1px solid rgba(60,55,48,0.12)">
            <div style="font-weight:700;font-size:13px">Mitarbeiterbeteiligungen noch ohne steuerbares Einkommen</div>
            <div style="font-size:12px;color:#8b8b8b;margin:4px 0 6px">Mehrere Gründe möglich.</div>
            ${laModus('laBetModus', p.beteiligungModus)}
            <div data-la-wenn="laBetModus=EIGEN" style="display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-top:8px">
                ${laCheck('laBetG', 'Gesperrte Mitarbeiteroptionen', p.beteiligungGesperrt)}
                ${laCheck('laBetN', 'Nicht börsenkotierte Mitarbeiteroptionen', p.beteiligungNichtKotiert)}
                ${laCheck('laBetA', 'Anwartschaften auf Mitarbeiteraktien', p.beteiligungAnwartschaft)}
                ${laCheck('laBetU', 'Unechte Mitarbeiterbeteiligungen', p.beteiligungUnecht)}
            </div>
        </div>
        <label style="display:flex;gap:8px;align-items:flex-start;margin:12px 0;font-size:13px">
            <input type="checkbox" id="laKinder"${p.kinderzulageAhv ? ' checked' : ''}>
            <span>Kinderzulagen direkt durch die Ausgleichskasse ausbezahlt</span>
        </label>
        <div style="display:grid;grid-template-columns:1fr 140px;gap:8px">
            <label class="f-label">Umzugskosten CHF<input id="laUmzugBetrag" class="f-input" type="number" step="0.05" min="0" value="${p.umzugBetrag ?? ''}"></label>
            <label class="f-label">Jahr<input id="laUmzugJahr" class="f-input" type="number" value="${p.umzugJahr ?? ''}"></label>
        </div>
        <div style="margin-top:12px;padding-top:8px;border-top:1px solid rgba(60,55,48,0.12)">
            <label style="display:flex;gap:8px;align-items:center;font-weight:700;font-size:13px">
                <input type="checkbox" id="laNach"${p.nachgenuss ? ' checked' : ''} onchange="laModusZeigen()">
                Besoldungsnachgenuss (Ziffer 4 und begünstigte Person)
            </label>
            <div data-la-wenn="laNach=on" id="laNachBlock" style="display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px">
                <label class="f-label">Betrag CHF<input id="laNachBetrag" class="f-input" type="number" step="0.05" min="0" value="${p.nachgenussBetrag ?? ''}"></label>
                <label class="f-label">Jahr<input id="laNachJahr" class="f-input" type="number" value="${p.nachgenussJahr ?? ''}"></label>
                <label class="f-label">Name<input id="laNachName" class="f-input" value="${laEsc(p.nachgenussName || '')}"></label>
                <label class="f-label">Vorname<input id="laNachVorname" class="f-input" value="${laEsc(p.nachgenussVorname || '')}"></label>
                <label class="f-label" style="grid-column:1 / -1">Strasse<input id="laNachStrasse" class="f-input" value="${laEsc(p.nachgenussStrasse || '')}"></label>
                <label class="f-label">PLZ<input id="laNachPlz" class="f-input" value="${laEsc(p.nachgenussPlz || '')}"></label>
                <label class="f-label">Ort<input id="laNachOrt" class="f-input" value="${laEsc(p.nachgenussOrt || '')}"></label>
            </div>
        </div>
        <div style="font-size:12px;color:#8b8b8b;margin-top:12px;line-height:1.4">Der Besoldungsnachgenuss-Betrag wird in Ziffer 4 geschrieben und zu Brutto und Netto gezählt. Nur eintragen, wenn er nicht schon im Lohnlauf steckt. Umzugskosten und Nachgenuss gelten nur im angegebenen Jahr. «Einer von … Lohnausweisen» setzt das Programm selbst, sobald dieselbe Person im Jahr bei zwei Arbeitgebern mit unterschiedlicher UID Lohn hat.</div>`;
}

function laCheck(id, label, on) {
    return `<label style="display:flex;gap:8px;align-items:flex-start;font-size:13px"><input type="checkbox" id="${id}"${on ? ' checked' : ''}> ${label}</label>`;
}

function laNum(id) {
    const v = document.getElementById(id)?.value;
    if (v === undefined || v === null || String(v).trim() === '') return null;
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
}

async function laPersonSpeichern(employeeId) {
    const body = {
        art: document.getElementById('laArt').value,
        boxF: laTriWert('laBoxF'),
        boxG: laTriWert('laBoxG'),
        ziffer14Modus: document.getElementById('laZ14Modus').value || null,
        ziffer14Text: document.getElementById('laZ14Text')?.value || null,
        expatModus: document.getElementById('laExpatModus').value || null,
        expatDatum: document.getElementById('laExpatDatum').value || null,
        expatKanton: document.getElementById('laExpatKanton').value || null,
        wagenAbklaeren: laTriWert('laWagenAb'),
        wagenMinimal: laTriWert('laWagenMin'),
        spesenModus: document.getElementById('laSpesenModus').value || null,
        spesenDatum: document.getElementById('laSpesenDatum').value || null,
        spesenKanton: document.getElementById('laSpesenKanton').value || null,
        rz52: laTriWert('laRz52'),
        wertModus: document.getElementById('laWertModus').value || null,
        wertDatum: document.getElementById('laWertDatum').value || null,
        wertKanton: document.getElementById('laWertKanton').value || null,
        beteiligungModus: document.getElementById('laBetModus').value || null,
        beteiligungGesperrt: !!document.getElementById('laBetG')?.checked,
        beteiligungNichtKotiert: !!document.getElementById('laBetN')?.checked,
        beteiligungAnwartschaft: !!document.getElementById('laBetA')?.checked,
        beteiligungUnecht: !!document.getElementById('laBetU')?.checked,
        kinderzulageAhv: !!document.getElementById('laKinder').checked,
        umzugBetrag: laNum('laUmzugBetrag'),
        umzugJahr: laNum('laUmzugJahr'),
        nachgenuss: !!document.getElementById('laNach').checked,
        nachgenussBetrag: laNum('laNachBetrag'),
        nachgenussJahr: laNum('laNachJahr'),
        nachgenussName: document.getElementById('laNachName').value,
        nachgenussVorname: document.getElementById('laNachVorname').value,
        nachgenussStrasse: document.getElementById('laNachStrasse').value,
        nachgenussPlz: document.getElementById('laNachPlz').value,
        nachgenussOrt: document.getElementById('laNachOrt').value,
    };
    const r = await fetch(`/api/employees/${employeeId}/lohnausweis-angaben`, {
        method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    if (!r.ok) {
        const err = await r.json().catch(() => ({}));
        alert(err.message || 'Speichern fehlgeschlagen.');
        return;
    }
    laAngabenSchliessen();
    laPersonKarte(employeeId);
}

async function laFirmaOeffnen(companyProfileId) {
    if (!companyProfileId) return;
    const r = await fetch(`/api/companyprofiles/${companyProfileId}/lohnausweis-angaben`, { headers: ah() });
    if (!r.ok) { alert('Die Unternehmensangaben konnten nicht geladen werden.'); return; }
    const f = await r.json();
    const ov = laOverlay();
    document.getElementById('laAngabenTitel').textContent = 'Lohnausweis für das Unternehmen';
    document.getElementById('laAngabenBody').innerHTML = laFirmaHtml(f);
    document.getElementById('laAngabenSave').onclick = () => laFirmaSpeichern(companyProfileId);
    ov.style.display = 'flex';
}

function laFirmaHtml(f) {
    const grant = (titel, datumId, kantonId, datum, kanton) => `
        <div style="margin:12px 0;padding-top:8px;border-top:1px solid rgba(60,55,48,0.12)">
            <div style="font-weight:700;font-size:13px;margin-bottom:6px">${titel}</div>
            <div style="display:grid;grid-template-columns:1fr 140px;gap:8px">
                <label class="f-label">Datum<input type="date" id="${datumId}" class="f-input" value="${laEsc(datum || '')}"></label>
                <label class="f-label">Kanton${laKantonSelect(kantonId, kanton)}</label>
            </div>
        </div>`;
    return `
        <div style="font-size:12.5px;color:#646464;margin-bottom:10px;line-height:1.4">Gilt für alle Personen dieser Filiale, solange bei der Person nichts Abweichendes steht. Box F und G sind dieselben Schalter wie im Block darüber.</div>
        <label style="display:flex;gap:8px;align-items:center;font-size:13px;margin-bottom:6px"><input type="checkbox" id="laFBoxF"${f.boxF ? ' checked' : ''}> Ziffer F — unentgeltliche Beförderung</label>
        <label style="display:flex;gap:8px;align-items:center;font-size:13px"><input type="checkbox" id="laFBoxG"${f.boxG ? ' checked' : ''}> Ziffer G — Kantine / Lunch-Checks</label>
        <label class="f-label" style="margin-top:10px">Ziffer 14 — Text für alle
            <textarea id="laF14" class="f-input" rows="2">${laEsc(f.ziffer14 || '')}</textarea>
        </label>
        ${grant('Expatriate-Ruling', 'laFExpatDatum', 'laFExpatKanton', f.expatDatum, f.expatKanton)}
        <label style="display:flex;gap:8px;align-items:flex-start;font-size:13px;margin-top:8px"><input type="checkbox" id="laFWagenAb"${f.wagenAbklaeren ? ' checked' : ''}> Privatanteil Geschäftswagen im Veranlagungsverfahren abklären</label>
        <label style="display:flex;gap:8px;align-items:flex-start;font-size:13px;margin-top:6px"><input type="checkbox" id="laFWagenMin"${f.wagenMinimal ? ' checked' : ''}> Arbeitnehmer übernimmt mind. 0,9 % des Kaufpreises</label>
        ${grant('Spesenreglement genehmigt', 'laFSpesenDatum', 'laFSpesenKanton', f.spesenDatum, f.spesenKanton)}
        <label style="display:flex;gap:8px;align-items:flex-start;font-size:13px"><input type="checkbox" id="laFRz52"${f.rz52 ? ' checked' : ''}> Randziffer 52 eingehalten (Kreuz in Ziffer 13.1.1)</label>
        ${grant('Verkehrswert Mitarbeiterbeteiligungen', 'laFWertDatum', 'laFWertKanton', f.wertDatum, f.wertKanton)}
        <div style="font-weight:700;font-size:13px;margin:12px 0 6px">Mitarbeiterbeteiligungen ohne steuerbares Einkommen</div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px">
            ${laCheck('laFBetG', 'Gesperrte Mitarbeiteroptionen', f.betGesperrt)}
            ${laCheck('laFBetN', 'Nicht börsenkotierte Mitarbeiteroptionen', f.betNichtKotiert)}
            ${laCheck('laFBetA', 'Anwartschaften auf Mitarbeiteraktien', f.betAnwartschaft)}
            ${laCheck('laFBetU', 'Unechte Mitarbeiterbeteiligungen', f.betUnecht)}
        </div>`;
}

async function laFirmaSpeichern(companyProfileId) {
    const body = {
        boxF: !!document.getElementById('laFBoxF').checked,
        boxG: !!document.getElementById('laFBoxG').checked,
        ziffer14: document.getElementById('laF14').value,
        expatDatum: document.getElementById('laFExpatDatum').value || null,
        expatKanton: document.getElementById('laFExpatKanton').value || null,
        wagenAbklaeren: !!document.getElementById('laFWagenAb').checked,
        wagenMinimal: !!document.getElementById('laFWagenMin').checked,
        spesenDatum: document.getElementById('laFSpesenDatum').value || null,
        spesenKanton: document.getElementById('laFSpesenKanton').value || null,
        rz52: !!document.getElementById('laFRz52').checked,
        wertDatum: document.getElementById('laFWertDatum').value || null,
        wertKanton: document.getElementById('laFWertKanton').value || null,
        betGesperrt: !!document.getElementById('laFBetG').checked,
        betNichtKotiert: !!document.getElementById('laFBetN').checked,
        betAnwartschaft: !!document.getElementById('laFBetA').checked,
        betUnecht: !!document.getElementById('laFBetU').checked,
    };
    const r = await fetch(`/api/companyprofiles/${companyProfileId}/lohnausweis-angaben`, {
        method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    if (!r.ok) {
        const err = await r.json().catch(() => ({}));
        alert(err.message || 'Speichern fehlgeschlagen.');
        return;
    }
    const f = document.getElementById('stmLohnausweisBoxF');
    const g = document.getElementById('stmLohnausweisBoxG');
    if (f) f.checked = body.boxF;
    if (g) g.checked = body.boxG;
    laAngabenSchliessen();
}

// Blöcke mit data-la-wenn ein- und ausblenden (Select-Wert oder Häkchen).
function laModusZeigen() {
    document.querySelectorAll('[data-la-wenn]').forEach(n => {
        const spec = n.getAttribute('data-la-wenn');
        const eq = spec.indexOf('=');
        const id = spec.slice(0, eq);
        const soll = spec.slice(eq + 1);
        const el = document.getElementById(id);
        let wert = '';
        if (el?.type === 'checkbox') wert = el.checked ? 'on' : '';
        else wert = el?.value || '';
        n.style.display = wert === soll ? '' : 'none';
    });
}
