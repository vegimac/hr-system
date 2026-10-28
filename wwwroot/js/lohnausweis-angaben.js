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

function laStil() {
    if (document.getElementById('laAngabenStil')) return;
    const s = document.createElement('style');
    s.id = 'laAngabenStil';
    s.textContent = `
        #laAngabenOverlay { padding: 18px !important; align-items: center !important; }
        #laAngabenOverlay .la-blatt {
            width: min(1240px, calc(100vw - 36px));
            height: calc(100vh - 36px);
            max-height: none;
            display: flex; flex-direction: column; overflow: hidden;
            padding: 0; border-radius: 22px; box-sizing: border-box;
        }
        #laAngabenOverlay .modal-hd { background: transparent; padding: 22px 32px 12px; border-bottom: 1px solid rgba(60,55,48,0.08); }
        #laAngabenOverlay .la-unter { font-size: 13.5px; line-height: 1.45; color: #646464; font-weight: 500; margin-top: 4px; }
        #laAngabenOverlay .modal-body { padding: 16px 32px 28px; }
        #laAngabenOverlay .modal-ft { padding: 14px 32px; background: rgba(255,255,255,0.28); }
        #laAngabenOverlay .la-raster { display: grid; grid-template-columns: 1fr 1fr; gap: 14px; align-items: start; }
        #laAngabenOverlay .la-karte {
            background: rgba(255,255,255,0.48);
            border: 1px solid rgba(255,255,255,0.78);
            border-radius: 16px;
            padding: 16px 16px 14px;
            box-shadow: inset 0 1px 0 rgba(255,255,255,0.85), 0 8px 18px rgba(60,55,48,0.05);
        }
        #laAngabenOverlay .la-breit { grid-column: 1 / -1; }
        #laAngabenOverlay .la-kopf { display: flex; gap: 12px; align-items: flex-start; }
        #laAngabenOverlay .la-badge {
            flex: 0 0 auto; min-width: 38px; height: 32px; padding: 0 8px;
            border-radius: 10px; background: #3f3f3f; color: #fff;
            font-size: 12px; font-weight: 700; letter-spacing: 0.02em;
            display: flex; align-items: center; justify-content: center;
        }
        #laAngabenOverlay .la-titel { margin: 1px 0 4px; font-size: 15px; font-weight: 700; color: #3f3f3f; }
        #laAngabenOverlay .la-text { margin: 0; font-size: 13px; line-height: 1.5; color: #646464; }
        #laAngabenOverlay .la-satz { margin: 8px 0 0; font-size: 12.5px; line-height: 1.4; color: #8b8b8b; }
        #laAngabenOverlay .la-feld { margin-top: 12px; }
        #laAngabenOverlay .la-feld .lqsel-wrap { display: block; width: 100%; }
        #laAngabenOverlay .la-feld .f-input,
        #laAngabenOverlay .la-feld select { width: 100%; box-sizing: border-box; }
        #laAngabenOverlay .la-zeile { display: grid; grid-template-columns: 1fr 160px; gap: 10px; margin-top: 10px; }
        #laAngabenOverlay .la-checks { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-top: 10px; }
        #laAngabenOverlay .la-check {
            display: flex; gap: 8px; align-items: flex-start;
            padding: 10px 12px; border-radius: 12px;
            border: 1px solid rgba(60,55,48,0.16);
            background: rgba(255,255,255,0.4);
            font-size: 13px; line-height: 1.35; color: #3f3f3f; cursor: pointer;
        }
        #laAngabenOverlay .la-check input { margin-top: 2px; }
        #laAngabenOverlay .la-hinweis {
            grid-column: 1 / -1; padding: 12px 14px; border-radius: 12px;
            background: rgba(146,64,14,0.08); color: #92400e; font-size: 13px; line-height: 1.45;
        }
        #laAngabenOverlay .la-speichern {
            background: #3f3f3f; color: #fff; border: none; border-radius: 12px;
            padding: 9px 18px; font-size: 14px; font-weight: 600; cursor: pointer;
        }
        @media (max-width: 900px) {
            #laAngabenOverlay .la-raster,
            #laAngabenOverlay .la-checks,
            #laAngabenOverlay .la-zeile { grid-template-columns: 1fr; }
        }
        body.theme-dark #laAngabenOverlay .la-karte { background: rgba(255,255,255,0.06); border-color: rgba(255,255,255,0.12); }
        body.theme-dark #laAngabenOverlay .la-titel { color: #f3f0ea; }
        body.theme-dark #laAngabenOverlay .la-text,
        body.theme-dark #laAngabenOverlay .la-unter { color: #c8c2b8; }
        body.theme-dark #laAngabenOverlay .la-check { color: #f3f0ea; background: rgba(255,255,255,0.05); }
    `;
    document.head.appendChild(s);
}

function laTri(id, wert, firmaJa) {
    const v = wert === true ? 'ja' : wert === false ? 'nein' : '';
    const stand = firmaJa ? 'ja' : 'nein';
    return `<div class="la-feld"><select id="${id}" class="f-input">
            <option value=""${v === '' ? ' selected' : ''}>wie Unternehmen (${stand})</option>
            <option value="ja"${v === 'ja' ? ' selected' : ''}>Ja, für diese Person</option>
            <option value="nein"${v === 'nein' ? ' selected' : ''}>Nein, für diese Person</option>
        </select></div>`;
}

function laModus(id, wert) {
    const v = wert || '';
    return `<div class="la-feld"><select id="${id}" class="f-input" onchange="laModusZeigen()">
        <option value=""${v === '' ? ' selected' : ''}>wie Unternehmen</option>
        <option value="EIGEN"${v === 'EIGEN' ? ' selected' : ''}>eigene Angabe</option>
        <option value="KEIN"${v === 'KEIN' ? ' selected' : ''}>keine, auch wenn das Unternehmen eine hat</option>
    </select></div>`;
}

function laKarte(badge, titel, text, inhalt, breit) {
    return `<article class="la-karte${breit ? ' la-breit' : ''}">
        <div class="la-kopf">
            <span class="la-badge">${badge}</span>
            <div>
                <h3 class="la-titel">${titel}</h3>
                <p class="la-text">${text}</p>
            </div>
        </div>
        ${inhalt}
    </article>`;
}

function laTriWert(id) {
    const v = document.getElementById(id)?.value;
    if (v === 'ja') return true;
    if (v === 'nein') return false;
    return null;
}

function laOverlay() {
    laStil();
    let el = document.getElementById('laAngabenOverlay');
    if (el) return el;
    el = document.createElement('div');
    el.id = 'laAngabenOverlay';
    el.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(40,36,30,0.42);z-index:3200;align-items:center;justify-content:center;overflow:hidden';
    el.innerHTML = `<div class="modal la-blatt" onclick="event.stopPropagation()">
        <div class="modal-hd" style="flex:0 0 auto">
            <div>
                <div class="modal-hd-title" id="laAngabenTitel">Lohnausweis</div>
                <div class="la-unter" id="laAngabenUnter"></div>
            </div>
            <button type="button" class="modal-x" onclick="laAngabenSchliessen()">×</button>
        </div>
        <div class="modal-body" id="laAngabenBody" style="flex:1 1 auto;overflow:auto"></div>
        <div class="modal-ft" style="flex:0 0 auto">
            <button type="button" class="btn btn-outline" onclick="laAngabenSchliessen()">Abbrechen</button>
            <button type="button" id="laAngabenSave" class="la-speichern">Speichern</button>
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
    document.getElementById('laAngabenUnter').textContent =
        'Was hier leer bleibt, gilt wie bei der Filiale' + (d.firmaName ? ' ' + d.firmaName : '') + '. «Einer von … Lohnausweisen» setzt das Programm selbst, sobald dieselbe Person im Jahr bei zwei Arbeitgebern Lohn hat.';
    document.getElementById('laAngabenBody').innerHTML = laPersonHtml(p, f, d.hinweis);
    document.getElementById('laAngabenSave').onclick = () => laPersonSpeichern(employeeId);
    laModusZeigen();
    ov.style.display = 'flex';
}

function laDatum(iso) {
    if (!iso || String(iso).length < 10) return '';
    const s = String(iso);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}

function laFirmaStand(text) {
    return `<p class="la-satz">${laEsc(text)}</p>`;
}

function laGrantFelder(modusId, datumId, kantonId, datum, kanton) {
    return `<div data-la-wenn="${modusId}=EIGEN" class="la-zeile">
        <label class="f-label">Datum der Genehmigung<input type="date" id="${datumId}" class="f-input" value="${laEsc(datum || '')}"></label>
        <label class="f-label">Kanton${laKantonSelect(kantonId, kanton)}</label>
    </div>`;
}

function laPersonHtml(p, f, hinweis) {
    const firmaGrant = (datum, kanton) => datum && kanton
        ? 'Filiale: Kanton ' + kanton + ', ' + laDatum(datum)
        : 'Filiale: keine Genehmigung hinterlegt';
    const art = `<div class="la-feld"><select id="laArt" class="f-input">
            <option value="LOHN"${p.art !== 'RENTE' ? ' selected' : ''}>Lohnausweis (Buchstabe A)</option>
            <option value="RENTE"${p.art === 'RENTE' ? ' selected' : ''}>Rentenbescheinigung (Buchstabe B)</option>
        </select></div>`;
    return `<div class="la-raster">
        ${hinweis ? `<div class="la-hinweis">${laEsc(hinweis)}</div>` : ''}
        ${laKarte('A/B', 'Lohn oder Rente',
            'Buchstabe A ist der normale Lohnausweis. Buchstabe B ist die Rentenbescheinigung. Die Beträge kommen aus den Lohnabrechnungen. Schaub zahlt keine Renten; das Feld ist für den Fall, dass ein Ausweis eine Rente bescheinigt.',
            art, true)}
        ${laKarte('F', 'Unentgeltliche Beförderung',
            'Ziffer F wird angekreuzt, wenn der Arbeitgeber die Fahrt zwischen Wohn- und Arbeitsort bezahlt, zum Beispiel mit einem Firmenbus. Bei einem selbst bezahlten Arbeitsweg bleibt das Kreuz weg.',
            laTri('laBoxF', p.boxF, !!f.boxF))}
        ${laKarte('G', 'Kantine oder Lunch-Checks',
            'Ziffer G wird angekreuzt, wenn der Arbeitgeber die Verpflegung bezahlt. Zahlt die Person einen Anteil selbst, bleibt das Kreuz weg. Bei Schaub ist das der Normalfall, weil die Crew die Hälfte selbst zahlt.',
            laTri('laBoxG', p.boxG, !!f.boxG))}
        ${laKarte('14', 'Nicht bewertete Nebenleistungen',
            'Gehaltsnebenleistungen, die nicht in Franken aufgerechnet werden. Der Text steht in Ziffer 14, zum Beispiel «stark verbilligte Abgabe eines Personenwagens».',
            laFirmaStand('Filiale: ' + (f.ziffer14 || 'kein Text'))
            + laModus('laZ14Modus', p.ziffer14Modus)
            + `<div data-la-wenn="laZ14Modus=EIGEN" class="la-feld"><textarea id="laZ14Text" class="f-input" rows="3" placeholder="z. B. stark verbilligte Abgabe eines Personenwagens">${laEsc(p.ziffer14Text || '')}</textarea></div>`,
            true)}
        ${laKarte('15', 'Expatriate-Ruling',
            'Genehmigung der kantonalen Steuerverwaltung. Auf dem Ausweis steht «Expatriateruling durch Kanton … am … genehmigt».',
            laFirmaStand(firmaGrant(f.expatDatum, f.expatKanton))
            + laModus('laExpatModus', p.expatModus)
            + laGrantFelder('laExpatModus', 'laExpatDatum', 'laExpatKanton', p.expatDatum, p.expatKanton))}
        ${laKarte('2.2', 'Geschäftswagen, Kosten abklären',
            'Übernimmt die Person einen grossen Teil der Kosten am Geschäftsfahrzeug, steht in Ziffer 2.2 kein Betrag. Dafür der Satz «Privatanteil Geschäftsfahrzeug im Veranlagungsverfahren abzuklären».',
            laTri('laWagenAb', p.wagenAbklaeren, !!f.wagenAbklaeren))}
        ${laKarte('2.2', 'Geschäftswagen, 0,9 Prozent',
            'Zahlt die Person mindestens 0,9 Prozent des Kaufpreises selbst, steht «Privatanteil wird vom Arbeitnehmer bezahlt».',
            laTri('laWagenMin', p.wagenMinimal, !!f.wagenMinimal))}
        ${laKarte('13', 'Spesenreglement genehmigt',
            'Genehmigung der kantonalen Steuerverwaltung. Auf dem Ausweis steht «Spesenreglement durch Kanton … am … genehmigt». Liegt sie vor, entfällt das Kreuz in Ziffer 13.1.1.',
            laFirmaStand(firmaGrant(f.spesenDatum, f.spesenKanton))
            + laModus('laSpesenModus', p.spesenModus)
            + laGrantFelder('laSpesenModus', 'laSpesenDatum', 'laSpesenKanton', p.spesenDatum, p.spesenKanton))}
        ${laKarte('13.1.1', 'Randziffer 52',
            'Kreuz in Ziffer 13.1.1, wenn die Spesen den Bedingungen von Randziffer 52 entsprechen. Ein genehmigtes Spesenreglement verdrängt dieses Kreuz.',
            laTri('laRz52', p.rz52, !!f.rz52))}
        ${laKarte('15', 'Verkehrswert der Beteiligung',
            'Der Kanton hat den Verkehrswert der Mitarbeiterbeteiligung genehmigt. Auf dem Ausweis steht «Verkehrswert durch Kanton … am … genehmigt».',
            laFirmaStand(firmaGrant(f.wertDatum, f.wertKanton))
            + laModus('laWertModus', p.wertModus)
            + laGrantFelder('laWertModus', 'laWertDatum', 'laWertKanton', p.wertDatum, p.wertKanton))}
        ${laKarte('15', 'Beteiligung noch ohne Einkommen',
            'Mitarbeiterbeteiligung, aus der noch kein steuerbares Einkommen entsteht. Mehrere Gründe sind möglich. Auf dem Ausweis steht «Mitarbeiterbeteiligung ohne steuerbares Einkommen. Grund: …».',
            laModus('laBetModus', p.beteiligungModus)
            + `<div data-la-wenn="laBetModus=EIGEN" class="la-checks">
                ${laCheck('laBetG', 'Gesperrte Mitarbeiteroptionen', p.beteiligungGesperrt)}
                ${laCheck('laBetN', 'Nicht börsenkotierte Mitarbeiteroptionen', p.beteiligungNichtKotiert)}
                ${laCheck('laBetA', 'Anwartschaften auf Mitarbeiteraktien', p.beteiligungAnwartschaft)}
                ${laCheck('laBetU', 'Unechte Mitarbeiterbeteiligungen', p.beteiligungUnecht)}
            </div>`,
            true)}
        ${laKarte('15', 'Kinderzulagen der Ausgleichskasse',
            'Nur diese Person. Die Zulagen stehen nicht im Lohnausweis, weil die Ausgleichskasse sie direkt auszahlt. Satz: «Kinderzulagen im LA nicht enthalten. Auszahlung durch Ausgleichskasse».',
            `<label class="la-check" style="margin-top:12px"><input type="checkbox" id="laKinder"${p.kinderzulageAhv ? ' checked' : ''}> Direkt durch die Ausgleichskasse ausbezahlt</label>`)}
        ${laKarte('15', 'Umzugskosten',
            'Nur diese Person und nur im angegebenen Jahr. Berufsbedingte Umzugskosten, die der Arbeitgeber bezahlt. Satz: «Umzugskosten von CHF … bezahlt».',
            `<div class="la-zeile">
                <label class="f-label">Betrag CHF<input id="laUmzugBetrag" class="f-input" type="number" step="0.05" min="0" value="${p.umzugBetrag ?? ''}"></label>
                <label class="f-label">Jahr des Lohnausweises<input id="laUmzugJahr" class="f-input" type="number" value="${p.umzugJahr ?? ''}"></label>
            </div>`)}
        ${laKarte('4', 'Besoldungsnachgenuss',
            'Nur diese Person und nur im angegebenen Jahr. Lohn, der nach dem Tod an eine begünstigte Person weitergeht. Der Betrag kommt in Ziffer 4 und zusätzlich auf Brutto und Netto. Name und Adresse stehen in Ziffer 15: «Besoldungsnachgenuss an Name, Vorname, Strasse, PLZ Ort». Nur eintragen, wenn der Betrag nicht schon im Lohnlauf steckt.',
            `<label class="la-check" style="margin-top:12px"><input type="checkbox" id="laNach"${p.nachgenuss ? ' checked' : ''} onchange="laModusZeigen()"> Besoldungsnachgenuss für diese Person erfassen</label>
            <div data-la-wenn="laNach=on" id="laNachBlock" class="la-checks">
                <label class="f-label">Betrag CHF<input id="laNachBetrag" class="f-input" type="number" step="0.05" min="0" value="${p.nachgenussBetrag ?? ''}"></label>
                <label class="f-label">Jahr<input id="laNachJahr" class="f-input" type="number" value="${p.nachgenussJahr ?? ''}"></label>
                <label class="f-label">Name<input id="laNachName" class="f-input" value="${laEsc(p.nachgenussName || '')}"></label>
                <label class="f-label">Vorname<input id="laNachVorname" class="f-input" value="${laEsc(p.nachgenussVorname || '')}"></label>
                <label class="f-label" style="grid-column:1 / -1">Strasse<input id="laNachStrasse" class="f-input" value="${laEsc(p.nachgenussStrasse || '')}"></label>
                <label class="f-label">PLZ<input id="laNachPlz" class="f-input" value="${laEsc(p.nachgenussPlz || '')}"></label>
                <label class="f-label">Ort<input id="laNachOrt" class="f-input" value="${laEsc(p.nachgenussOrt || '')}"></label>
            </div>`,
            true)}
    </div>`;
}

function laCheck(id, label, on) {
    return `<label class="la-check"><input type="checkbox" id="${id}"${on ? ' checked' : ''}> ${label}</label>`;
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
    document.getElementById('laAngabenUnter').textContent =
        'Gilt für alle Personen dieser Filiale, solange bei der Person nichts Abweichendes steht. Box F und G sind dieselben Schalter wie in den Stammdaten.';
    document.getElementById('laAngabenBody').innerHTML = laFirmaHtml(f);
    document.getElementById('laAngabenSave').onclick = () => laFirmaSpeichern(companyProfileId);
    laFirmaHinweisHaengen();
    ov.style.display = 'flex';
}

const laSpesenSatz = 'Genehmigtes Spesenreglement und die Angabe RZ 52 sind beide gesetzt. '
    + 'Auf dem Lohnausweis gilt das Reglement; das Kreuz in Ziffer 13.1.1 entfällt '
    + '(Wegleitung 2026, Randziffer 65).';

function laFirmaSpesenKollision() {
    const datum = document.getElementById('laFSpesenDatum')?.value;
    const kanton = document.getElementById('laFSpesenKanton')?.value;
    const rz = document.getElementById('laFRz52')?.checked;
    return !!(datum && kanton && rz);
}

function laFirmaHinweisZeigen() {
    const box = document.getElementById('laFirmaHinweis');
    if (!box) return;
    const an = laFirmaSpesenKollision();
    box.style.display = an ? '' : 'none';
    box.textContent = an ? laSpesenSatz : '';
}

function laFirmaHinweisHaengen() {
    ['laFSpesenDatum', 'laFSpesenKanton', 'laFRz52'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', laFirmaHinweisZeigen);
    });
    laFirmaHinweisZeigen();
}

function laFirmaGrant(datumId, kantonId, datum, kanton) {
    return `<div class="la-zeile">
        <label class="f-label">Datum der Genehmigung<input type="date" id="${datumId}" class="f-input" value="${laEsc(datum || '')}"></label>
        <label class="f-label">Kanton${laKantonSelect(kantonId, kanton)}</label>
    </div>`;
}

function laFirmaHtml(f) {
    return `<div class="la-raster">
        <div id="laFirmaHinweis" class="la-hinweis" style="display:none"></div>
        ${laKarte('F', 'Unentgeltliche Beförderung',
            'Ziffer F für alle Personen dieser Filiale, wenn der Arbeitgeber die Fahrt zwischen Wohn- und Arbeitsort bezahlt.',
            `<label class="la-check" style="margin-top:12px"><input type="checkbox" id="laFBoxF"${f.boxF ? ' checked' : ''}> Ziffer F ankreuzen</label>`)}
        ${laKarte('G', 'Kantine oder Lunch-Checks',
            'Ziffer G für alle, wenn der Arbeitgeber die Verpflegung bezahlt. Bei einem Eigenanteil der Personen bleibt das Kreuz weg.',
            `<label class="la-check" style="margin-top:12px"><input type="checkbox" id="laFBoxG"${f.boxG ? ' checked' : ''}> Ziffer G ankreuzen</label>`)}
        ${laKarte('14', 'Nicht bewertete Nebenleistungen',
            'Text in Ziffer 14 für alle Personen, zum Beispiel «stark verbilligte Abgabe eines Personenwagens». Eine Person kann ihn durch einen eigenen Text ersetzen oder unterdrücken.',
            `<div class="la-feld"><textarea id="laF14" class="f-input" rows="3">${laEsc(f.ziffer14 || '')}</textarea></div>`,
            true)}
        ${laKarte('15', 'Expatriate-Ruling',
            'Genehmigung der kantonalen Steuerverwaltung. Auf dem Ausweis steht «Expatriateruling durch Kanton … am … genehmigt».',
            laFirmaGrant('laFExpatDatum', 'laFExpatKanton', f.expatDatum, f.expatKanton))}
        ${laKarte('2.2', 'Geschäftswagen, Kosten abklären',
            'Für alle: «Privatanteil Geschäftsfahrzeug im Veranlagungsverfahren abzuklären». In Ziffer 2.2 steht dann kein Betrag.',
            `<label class="la-check" style="margin-top:12px"><input type="checkbox" id="laFWagenAb"${f.wagenAbklaeren ? ' checked' : ''}> Satz auf den Lohnausweis setzen</label>`)}
        ${laKarte('2.2', 'Geschäftswagen, 0,9 Prozent',
            'Für alle: «Privatanteil wird vom Arbeitnehmer bezahlt», wenn mindestens 0,9 Prozent des Kaufpreises selbst bezahlt werden.',
            `<label class="la-check" style="margin-top:12px"><input type="checkbox" id="laFWagenMin"${f.wagenMinimal ? ' checked' : ''}> Satz auf den Lohnausweis setzen</label>`)}
        ${laKarte('13', 'Spesenreglement genehmigt',
            'Genehmigung der kantonalen Steuerverwaltung. Satz: «Spesenreglement durch Kanton … am … genehmigt». Danach entfällt das Kreuz in Ziffer 13.1.1.',
            laFirmaGrant('laFSpesenDatum', 'laFSpesenKanton', f.spesenDatum, f.spesenKanton))}
        ${laKarte('13.1.1', 'Randziffer 52',
            'Kreuz in Ziffer 13.1.1 für alle, wenn die Spesen den Bedingungen von Randziffer 52 entsprechen und kein genehmigtes Reglement vorliegt.',
            `<label class="la-check" style="margin-top:12px"><input type="checkbox" id="laFRz52"${f.rz52 ? ' checked' : ''}> Kreuz in Ziffer 13.1.1 setzen</label>`)}
        ${laKarte('15', 'Verkehrswert der Beteiligung',
            'Der Kanton hat den Verkehrswert genehmigt. Satz: «Verkehrswert durch Kanton … am … genehmigt».',
            laFirmaGrant('laFWertDatum', 'laFWertKanton', f.wertDatum, f.wertKanton))}
        ${laKarte('15', 'Beteiligung noch ohne Einkommen',
            'Gründe, warum aus der Mitarbeiterbeteiligung noch kein steuerbares Einkommen entsteht. Mehrere sind möglich. Eine Person kann die Liste für sich ersetzen.',
            `<div class="la-checks">
                ${laCheck('laFBetG', 'Gesperrte Mitarbeiteroptionen', f.betGesperrt)}
                ${laCheck('laFBetN', 'Nicht börsenkotierte Mitarbeiteroptionen', f.betNichtKotiert)}
                ${laCheck('laFBetA', 'Anwartschaften auf Mitarbeiteraktien', f.betAnwartschaft)}
                ${laCheck('laFBetU', 'Unechte Mitarbeiterbeteiligungen', f.betUnecht)}
            </div>`,
            true)}
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
    if (typeof laFirmaAnsichtAktualisieren === 'function')
        laFirmaAnsichtAktualisieren(companyProfileId, body.boxF, body.boxG);
    if (laFirmaSpesenKollision()) alert(laSpesenSatz);
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
