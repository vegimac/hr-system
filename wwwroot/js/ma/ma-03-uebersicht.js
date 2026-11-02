// ═══════════ TAB «UEBERSICHT» (Etappe 2, Walter 17.07.2026) ═══════════
// Read-only Karten-Grid nach Prototyp: Personalien · Kontakt & Adresse ·
// Anstellung · Nachtarbeit · Vertraege · Dokumente. Jede Karte hat einen
// Sprung-Pfeil in ihren Fach-Tab — bearbeitet wird NUR dort. Daten kommen
// aus dem bereits geladenen selectedEmployee; nur die Dokumente werden
// nachgeladen (bestehender Endpoint by-employee).
function _ahvKasseHtml(emp) {
    const k = emp && emp.ahvKasse;
    if (!k || !k.text) return '';
    const cls = k.text === 'abgemeldet' ? 'weg' : (k.erledigt ? 'ok' : 'fehl');
    return `<span class="ov-ahv-kasse ${cls}">${esc(k.text)}</span>`;
}

function _ovCard(title, jumpTab, jumpTitle, bodyHtml, extraHeader = '') {
    const jump = jumpTab
        ? `<button class="ov-jump" title="${jumpTitle}" onclick="switchEmpTab('${jumpTab}')">→</button>`
        : '';
    return `<div class="ov-card">
        <div class="ov-card-h"><span>${title}</span><span style="display:flex;gap:8px;align-items:center">${extraHeader}${jump}</span></div>
        ${bodyHtml}
    </div>`;
}
function _ovF(label, value) {
    return `<div class="ov-f"><div class="ov-fl">${label}</div><div class="ov-fv">${value || '<span class="ov-empty">–</span>'}</div></div>`;
}

function landIstCh(roh) {
    const u = (roh || '').trim().toUpperCase();
    return !u || u === 'CH' || u === 'CHE' || u === 'SCHWEIZ' || u === 'SWITZERLAND'
        || u === 'SUISSE' || u === 'SVIZZERA';
}

/** Gespeicherter Wert = ISO-2. Anzeige im Menü = englisch, gross (SWITZERLAND). */
function maLandIso(roh) {
    const u = (roh || '').trim().toUpperCase();
    if (!u || u === 'CH' || u === 'CHE' || u === 'SCHWEIZ' || u === 'SWITZERLAND'
        || u === 'SUISSE' || u === 'SVIZZERA') return 'CH';
    return /^[A-Z]{2}$/.test(u) ? u : 'CH';
}

function maLandName(iso) {
    const code = maLandIso(iso);
    if (window.SwissUid && typeof SwissUid.landNameEn === 'function') return SwissUid.landNameEn(code);
    return code === 'CH' ? 'SWITZERLAND' : code;
}

function maLandSelectHtml(id, iso, extra) {
    const code = maLandIso(iso);
    let sel = (window.SwissUid && typeof SwissUid.landSelectHtml === 'function')
        ? SwissUid.landSelectHtml(id, code, '')
        : `<select id="${id}"><option value="${code}" selected>${maLandName(code)}</option></select>`;
    if (!sel.includes('value="' + code + '"'))
        sel = sel.replace(/>/, '><option value="' + code + '" data-kurz="' + code + '" selected>' + maLandName(code) + '</option>');
    sel = sel.replace(/<option value="([A-Z0-9]+)"/g, '<option value="$1" data-kurz="$1"');
    sel = sel.replace('<select ', '<select class="ov-softin ef-input" ' + (extra || '')
        + ' onchange="maLandGeaendert(\'' + id + '\')" ');
    return '<div class="ma-land-zeile">' + sel
        + '<span class="ma-land-en" id="' + id + '-en">' + maLandName(code) + '</span></div>';
}

function maLandGeaendert(id) {
    const el = document.getElementById(id);
    const iso = maLandIso(el && el.value);
    const name = document.getElementById(id + '-en');
    if (name) name.textContent = maLandName(iso);
    if (id === 'ov-land' && typeof ovLandGeaendert === 'function') ovLandGeaendert();
    if (id === 'ef-country' && typeof efLandChanged === 'function') efLandChanged();
    if (id === 'empAddr-country') {
        const kt = document.getElementById('empAddr-canton');
        if (kt && iso !== 'CH') kt.value = 'EX';
    }
}

function maHandErfasst(emp) {
    if (!emp) return false;
    if (emp._neu) return true;
    const easy = emp.easyAtWorkEmployeeId || emp.easyatworkEmployeeId;
    return !easy && !(emp.employments || []).length;
}

function ovLandGeaendert() {
    const land = (document.getElementById('ov-land')?.value || '').trim().toUpperCase();
    const kt = document.getElementById('ov-canton');
    if (kt && land && land !== 'CH' && land !== 'SCHWEIZ' && land !== 'SWITZERLAND') kt.value = 'EX';
    if (typeof ovDirty === 'function') ovDirty();
}

function heuteIsoLokal() {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function loadUebersichtTab() {
    const emp = selectedEmployee;
    const popup = !!(emp && emp._neu && document.getElementById('testNeuMaModal')?.classList.contains('offen'));
    const el = popup ? document.getElementById('testNeuMaBody') : document.getElementById('uebersichtContent');
    if (!el || !emp) return;
    if (popup) document.querySelectorAll('#uebersichtContent [id]').forEach(n => n.removeAttribute('id'));


    // ── Karte Personalien & Adresse (Walter 17.07.2026, 3 fixe Zeilen):
    //    Z1 Anrede·Briefanrede·Zivilstand·Geschlecht — Z2 Adresse (breit)
    //    ·Telefon 2 — Z3 Nationalität·ZEMIS·AHV·Speichern.
    //    Briefanrede/Telefon 2/ZEMIS sind DIREKT HIER editierbar: die
    //    ov-Inputs spiegeln ihre Werte beim Speichern in die (versteckten)
    //    ef-*-Inputs des Personal-Tabs und rufen saveEmpEdit() — derselbe
    //    geprüfte Save-Pfad, keine doppelten DOM-IDs. ──
    const adresse = [emp.street, [emp.zipCode, stripCityCantonSuffix(emp.city)].filter(Boolean).join(' ')].filter(Boolean).join(', ');
    // Edit-Felder im TEXT-Look (Walter 17.07.2026: grosse Input-Kaesten
    // zerstoerten die ruhige Karte): sehen aus wie Werte, nur mit dezenter
    // gestrichelter Unterlinie — erst bei Fokus wird der Rahmen sichtbar.
    const _ovE = (label, id, value, ph = '') => `
        <div class="ov-f"><div class="ov-fl">${label}</div>
        <input id="${id}" class="ov-editin" value="${esc(value)}" placeholder="${ph}" oninput="ovDirty()" title="Klicken zum Bearbeiten"></div>`;
    // Geschlecht kurz als «Sex» w/m/d (Walter 17.07.2026). Anrede redundant.
    // ── Personalien & Adresse: Z1 Ledigname·Briefanrede·Kurzname(ro)·Sex·
    //    Strasse·PLZ·Ort·Kanton(schmal)·Tel 2. Kurzname = easy@work Nickname,
    //    nur Anzeige (Walter 17.07.2026). ──
    const _rel = emp.religion || '';
    const _relOpt = (v, label) => `<option value="${v}" ${_rel === v ? 'selected' : ''}>${label}</option>`;
    const _pf = (label, valueHtml) => `
        <div class="ov-pf"><div class="ov-pfl">${label}</div><div class="ov-pfv">${valueHtml || '<span class="ov-empty">–</span>'}</div></div>`;
    const _pfE = (label, id, value, ph = '', type = 'text', w = 180) => `
        <div class="ov-pf"><div class="ov-pfl">${label}</div>
        <input id="${id}" class="ov-softin" style="width:${w}px" type="${type}" value="${type === 'date' ? toDateInput(value) : esc(value)}" placeholder="${ph}" ${type === 'date' ? 'onchange="ovDirty()"' : 'oninput="ovDirty()"'}></div>`;
    const _g = (emp.gender || '').toLowerCase();
    const gKurz2 = _g.startsWith('m') ? 'M' : (_g.startsWith('f') || _g === 'w' || _g === 'weiblich') ? 'W' : (emp.gender ? 'D' : null);
    const istCH = (emp.nationalityCode || '').toUpperCase() === 'CH';
    // Personalien & Adresse (Walter 19.07.2026):
    // Ein gemeinsames 5-Spalten-Raster über Z1–Z3:
    //   Strasse | PLZ | Ort+Kt. | Telefon 2 | AHV
    //   Z2 eigene Zeile (12.10.2026): Briefanrede | Kurzname | Sex | Konfession | Nationalität | Bewilligung | ZEMIS
    //   Zivilstand | seit | Ledigname | Familienbetreuung (2 Spalten)
    // Notfallkontakt: Anzeige lebt seit 25.08.2026 (v4) ausschliesslich im
    // MA-Kopf (🆘-Zeile unter dem Eintrittsdatum, Klick = Modal) — die
    // frühere Zusatz-Zeile in der Personalien-Karte ist raus (Walter).

    const hand = maHandErfasst(emp) || !!emp._testBearbeiten;
    const ahvRoh = (emp.ahvNumber ?? emp.socialSecurityNumber ?? '').trim();
    const ahvUnbekannt = ahvRoh.toLowerCase() === 'unbekannt';
    const ahvZiffern = ahvRoh.replace(/\D/g, '');
    const ahvText = ahvUnbekannt ? 'unbekannt'
        : (ahvZiffern.length === 13
            ? `${ahvZiffern.slice(0, 3)}.${ahvZiffern.slice(3, 7)}.${ahvZiffern.slice(7, 11)}.${ahvZiffern.slice(11)}`
            : ahvRoh);
    const ahvEdit = !!window._istTestinstanz;
    const neuZeile = emp._neu ? `
            <div class="ov-pers-neu">
            <div class="ov-pf"><div class="ov-pfl">Vorname</div>
            <input id="ov-firstName" class="ov-softin" value="${esc(emp.firstName || '')}" autocomplete="off" oninput="ovDirty()"></div>
            <div class="ov-pf"><div class="ov-pfl">Nachname</div>
            <input id="ov-lastName" class="ov-softin" value="${esc(emp.lastName || '')}" autocomplete="off" oninput="ovDirty()"></div>
            <div class="ov-pf"><div class="ov-pfl">Personalnummer</div>
            <input id="ov-nr" class="ov-softin" value="${esc(emp.employeeNumber || '')}" inputmode="numeric" autocomplete="off" oninput="ovNrPruefen()">
            <div id="ov-nr-hinweis" class="ov-nr-hinweis"></div></div>
            </div>` : '';
    const kPers = _ovCard(emp._neu ? 'Neuer Mitarbeiter' : 'Personalien & Adresse', null, '', `
        <div class="ov-pers-body">
        <div class="ov-pers-aligned">
            ${neuZeile}
            <div class="ov-pers-z1">
            ${hand ? `<div class="ov-pf ov-pf-street"><div class="ov-pfl">${_t('ma.field.street','Strasse')}</div>
                <input id="ov-street" class="ov-softin" value="${esc(emp.street)}" oninput="ovDirty()"></div>
            <div class="ov-adr-gruppe">
            <div class="ov-pf ov-pf-plz"><div class="ov-pfl">PLZ</div>
                <input id="ov-zip" class="ov-softin" value="${esc(emp.zipCode)}" inputmode="numeric" maxlength="10" oninput="ovDirty();if(this.value.replace(/\\D/g,'').length>=4)plzLookupGeneric(this.value,'ov-city','ov-canton',null,'ov-plz-hint','ov-land')" onblur="plzLookupGeneric(this.value,'ov-city','ov-canton',null,'ov-plz-hint','ov-land')">
                <div id="ov-plz-hint" style="font-size:11px;min-height:14px"></div></div>
            <div class="ov-pf ov-pf-city"><div class="ov-pfl">${_t('ma.field.city','Ort')}</div>
                <input id="ov-city" class="ov-softin" value="${esc(stripCityCantonSuffix(emp.city))}" oninput="ovDirty()"></div>
            <div class="ov-pf ov-pf-kt"><div class="ov-pfl">Kt.</div>
                ${renderKantonSelect('ov-canton', maLandIso(emp.country) !== 'CH' ? 'EX' : (emp.cantonCode || ''), 'onchange="ovDirty()"')}</div>
            <div class="ov-pf ov-pf-land"><div class="ov-pfl">Land</div>
                ${maLandSelectHtml('ov-land', emp.country || 'CH', '')}</div>
            </div>` : `
            <div class="ov-pf ov-pf-street" title="${esc(emp.street || '')}">
                <div class="ov-pfl">${_t('ma.field.street','Strasse')}</div>
                <div class="ov-pfv">${esc(emp.street) || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-adr-gruppe">
            <div class="ov-pf ov-pf-plz">
                <div class="ov-pfl">PLZ</div>
                <div class="ov-pfv">${esc(emp.zipCode) || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-pf ov-pf-city" title="${esc(stripCityCantonSuffix(emp.city) || '')}">
                <div class="ov-pfl">${_t('ma.field.city','Ort')}</div>
                <div class="ov-pfv">${esc(stripCityCantonSuffix(emp.city)) || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-pf ov-pf-kt">
                <div class="ov-pfl">Kt.</div>
                <div class="ov-pfv">${esc(maLandIso(emp.country) !== 'CH' ? 'EX' : (emp.cantonCode || '')) || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-pf ov-pf-land" title="${maLandIso(emp.country) !== 'CH' ? 'Wohnsitz im Ausland — QST-Kanton = Kanton der Filiale' : 'ISO-Code, dahinter der englische Ländername.'}">
                <div class="ov-pfl">Land</div>
                <div class="ov-pfv ma-land-zeile"><span class="ma-land-code">${esc(maLandIso(emp.country))}</span><span class="ma-land-en">${esc(maLandName(emp.country))}</span></div>
            </div>
            </div>`}
            <div class="ov-pf ov-pf-tel2"><div class="ov-pfl">Telefonnummer</div>
            <input id="ov-phone2" class="ov-softin" type="tel" value="${esc(emp.phone2)}" placeholder="+41 79 …" oninput="validatePhone(this);ovDirty()" onblur="validatePhoneBlur(this)"></div>
            <div class="ov-pf ov-pf-ahv">
                <div class="ov-pfl">AHV-Nummer${_ahvKasseHtml(emp)}</div>
                ${ahvEdit ? `<div class="ov-ahv-zeile">
                    <input id="ov-ahv" class="ov-softin ov-ahv-eingabe" inputmode="numeric" maxlength="16"
                        value="${ahvUnbekannt ? '' : esc(ahvText)}"
                        placeholder="756.7865.5342.46"
                        ${ahvUnbekannt ? 'disabled' : ''}
                        oninput="ahvMaskInput(this);ovDirty()" onblur="validateAhvField(this, true)">
                    <label class="ov-ahv-unb">
                        <input type="checkbox" id="ov-ahv-unbekannt" ${ahvUnbekannt ? 'checked' : ''} onchange="ovAhvUnbekannt()">
                        unbekannt
                    </label>
                    ${!ahvUnbekannt && ahvRoh ? directDocButton(emp.ahvKarteDokumentId, 'ahv_karte', 'ahv_card') : ''}
                </div>
                <div id="ov-ahv-status" style="font-size:11px;min-height:14px"></div>`
                : `<div class="ov-ahv-zeile">
                    <div class="ov-pfv ov-ahv-voll">${ahvText ? esc(ahvText) : '<span class="ov-empty">–</span>'}</div>
                    ${!ahvUnbekannt && ahvRoh ? directDocButton(emp.ahvKarteDokumentId, 'ahv_karte', 'ahv_card') : ''}
                </div>`}
            </div>
            </div>

            <div class="ov-pers-z2">
            <div class="ov-pers-z2a">
            <div class="ov-pf ov-pf-briefanrede"><div class="ov-pfl">${_t('ma.field.letterSalutation','Briefanrede')}</div>
            <input id="ov-letterSalutation" class="ov-softin" type="text" value="${esc(emp.letterSalutation)}" oninput="ovDirty()"></div>
            <div class="ov-pf ov-pf-kurzname" title="Kommt aus easy@work (Nickname) — hier nicht editierbar">
                <div class="ov-pfl">${_t('ma.field.shortName','Nickname')}</div>
                <div class="ov-pfv">${esc(emp.shortName) || '<span class="ov-empty">–</span>'}</div>
            </div>
            <div class="ov-pf ov-pf-sex">
                <div class="ov-pfl">Geschlecht</div>
                <select id="ov-gender" class="ov-softin" onchange="ovDirty()" title="M und F">
                    <option value="">–</option>
                    <option value="male" ${(_g === 'male' || _g === 'm' || _g === 'mann' || _g === 'männlich') ? 'selected' : ''}>M</option>
                    <option value="female" ${(_g === 'female' || _g === 'f' || _g === 'w' || _g === 'weiblich' || _g === 'frau') ? 'selected' : ''}>F</option>
                    ${_g === 'divers' || _g === 'd' ? '<option value="divers" selected>D</option>' : ''}
                </select>
            </div>
            <div class="ov-pf ov-pf-konf"><div class="ov-pfl">${_t('ma.field.religion','Konfession')}</div>
            <select id="ov-religion" class="ov-softin" onchange="ovDirty()">
                <option value="">–</option>
                ${_relOpt('evangelisch_reformiert', _t('ma.value.religion.evangelisch_reformiert','Evang.-reformiert'))}
                ${_relOpt('roemisch_katholisch', _t('ma.value.religion.roemisch_katholisch','Röm.-katholisch'))}
                ${_relOpt('christ_katholisch', _t('ma.value.religion.christ_katholisch','Christ-katholisch'))}
                ${_relOpt('israelitisch', _t('ma.value.religion.israelitisch','Israelitische Kultusgemeinde'))}
                ${_relOpt('andere', _t('ma.value.religion.andere','Andere'))}
                ${_relOpt('keine', _t('ma.value.religion.keine','Keine'))}
            </select></div>
            <div class="ov-pf ov-pf-lang">
                <div class="ov-pfl">Sprache</div>
                <select id="ov-lang" class="ov-softin" onchange="ovDirty()" title="DE, FR, IT, EN">
                    ${['de','fr','it','en'].map(c => `<option value="${c}" ${(emp.languageCode || 'de').toLowerCase() === c ? 'selected' : ''}>${c.toUpperCase()}</option>`).join('')}
                </select>
            </div>
            <div class="ov-pf ov-pf-geb">
                <div class="ov-pfl">Geburtsdatum</div>
                <input id="ov-dob" class="ov-softin" type="date" max="${heuteIsoLokal()}" value="${toDateInput(emp.dateOfBirth)}" onchange="ovDirty()">
            </div>
            </div>
            <div class="ov-pers-z2b">
            <div class="ov-pf ov-pf-mobil">
                <div class="ov-pfl">Mobilnummer</div>
                <input id="ov-mobile" class="ov-softin" type="tel" value="${esc(emp.phoneMobile)}" placeholder="+41 79 409 43 33" oninput="ovDirty()">
            </div>
            <div class="ov-pf ov-pf-mail">
                <div class="ov-pfl">E-Mail</div>
                <input id="ov-email" class="ov-softin" type="email" value="${esc(emp.email)}" title="${esc(emp.email)}" oninput="ovDirty()">
            </div>
            <div class="ov-pf ov-pf-nat">
                <div class="ov-pfl">${_t('ma.field.nationality','Nationalität')}</div>
                <div class="ov-nat-zeile">
                <select id="ov-nat" class="ov-softin" onchange="ovDirty()" title="ISO 3166, plus 11 unbekannt und 22 staatenlos">
                    <option value="${emp.nationalityId || ''}">${esc(emp.nationalityName || emp.nationalityCode || emp.nationality || '–')}</option>
                </select>
                ${directDocButton(emp.idPassDokumentId, 'id_pass', 'passport')}
                </div>
            </div>
            ${istCH
                ? ''
                // Letzte Bewilligung, read-only. Klick springt in den Bewilligungs-Tab.
                // Datum gleich gross wie die anderen Daten, mit führender Null (Walter 26.10.2026).
                : `<div class="ov-pf ov-pf-bew" title="Neueste Bewilligung — Klick öffnet den Tab «Bewilligung QST Bank»"
                        style="cursor:pointer" onclick="switchEmpTab('quellensteuer')">
                    <div class="ov-pfl">Bewilligung</div>
                    <div class="ov-pfv">${emp.permitType
                        ? `<b class="ov-bew-code">${esc(emp.permitType.code)}</b>${emp.permitExpiryDate ? `<span class="ov-bew-bis">bis <span class="ov-bew-datum">${(/^\d{4}-\d{2}-\d{2}/.test(String(emp.permitExpiryDate)) ? `${String(emp.permitExpiryDate).slice(8,10)}.${String(emp.permitExpiryDate).slice(5,7)}.${String(emp.permitExpiryDate).slice(0,4)}` : formatDate(emp.permitExpiryDate))}</span></span>` : ''}`
                        : '<span class="ov-empty" style="color:#b91c1c;font-weight:600">– keine erfasst –</span>'}</div>
                </div>`}
            ${istCH
                ? ''
                : `<div class="ov-pf ov-pf-zemis"><div class="ov-pfl">ZEMIS-Nr.</div>
            <input id="ov-zemisNumber" class="ov-softin" type="text" value="${esc(emp.zemisNumber)}" placeholder="${_t('ma.placeholder.zemis','z.B. 12345678.9')}" maxlength="14" oninput="ovDirty()"></div>`}
            </div>
            </div>

            <div class="ov-pers-z4">
            <div class="ov-pf ov-pf-zivil">
                <div class="ov-pfl">${_t('ma.field.maritalStatus','Zivilstand')}</div>
                <div class="ov-pfv">${emp._neu
                    ? `<select id="ov-zivilstand" class="ov-softin" onchange="ovDirty()">${zivOptionenHtml('')}</select>`
                    // Wechsel direkt hier (Walter 02.11.2026): neuer Zivilstand + «Zivilstand seit» als
                    // Gültig ab + Bemerkung → beim Speichern Historie-Eintrag, Vorgänger endet am Vortag.
                    : `<div class="ov-ziv-zeile"><select id="ov-zivilstand" class="ov-softin" data-alt="${esc(emp.zivilstand ?? emp.maritalStatus ?? '')}"
                        data-easy="${(emp.easyAtWorkEmployeeId || emp.easyatworkEmployeeId) ? '1' : ''}"
                        onchange="ovZivGeaendert()">${zivOptionenHtml(emp.zivilstand ?? emp.maritalStatus)}</select>
                    ${directDocButton(emp.zivilstandDokumentId, 'zivilstand', 'marriage_cert')}
                    <button type="button" class="qst-warum-btn" style="padding:2px 10px;font-size:11px;white-space:nowrap" title="Zivilstand-Historie — alle Einträge mit Gültig-ab" onclick="openZivilstandHistorie(${emp.id})">🕘 Historie</button></div>
                    <input id="ov-zivBemerkung" class="ov-softin" type="text" placeholder="Bemerkung zum Wechsel (optional), z.B. Heirat, Scheidungsurteil" style="display:none;margin-top:6px" oninput="ovDirty()">
                    <div id="ov-zivHinweis" style="display:none;margin-top:4px;font-size:11.5px;color:#92400e"></div>`}</div>
            </div>
            <div class="ov-pf ov-pf-seit"><div class="ov-pfl">${_t('ma.field.maritalSince','Zivilstand seit')}</div>
            <input id="ov-maritalStatusSince" class="ov-softin" type="date" value="${toDateInput(emp.maritalStatusSince)}" onchange="ovDirty()"></div>
            <div class="ov-pf ov-pf-ledig"><div class="ov-pfl">${_t('ma.field.maidenName','Ledigname')}</div>
            <input id="ov-maidenName" class="ov-softin" type="text" value="${esc(emp.maidenName)}" oninput="ovDirty()"></div>
            ${(() => {
                // Familienbetreuung (Walter 12.10.2026): nur aus easy@work, hier nur Anzeige.
                const _d = new Date();
                const heute = `${_d.getFullYear()}-${String(_d.getMonth() + 1).padStart(2, '0')}-${String(_d.getDate()).padStart(2, '0')}`;
                const von = (emp.familienbetreuungVon || '').slice(0, 10);
                const bis = (emp.familienbetreuungBis || '').slice(0, 10);
                const dmy = iso => `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}`;
                const zeitraum = emp.familienbetreuung && (von || bis)
                    ? ` <span style="font-size:12.5px;font-weight:400;color:#646464">${von ? dmy(von) : '…'} – ${bis ? dmy(bis) : 'offen'}</span>` : '';
                const lage = !emp.familienbetreuung ? ''
                    : (bis && bis < heute) ? ' <span style="font-size:11.5px;font-weight:400;color:#8b8b8b">(abgelaufen)</span>'
                    : (von && von > heute) ? ' <span style="font-size:11.5px;font-weight:400;color:#8b8b8b">(noch nicht gültig)</span>' : '';
                return `<div class="ov-pf ov-pf-fb" title="Kommt aus easy@work (Familienbetreuung) — dort pflegen, hier nicht editierbar">
                    <div class="ov-pfl">Familienbetreuung</div>
                    <div class="ov-pfv">${emp.familienbetreuung ? 'Ja' : 'Nein'}${zeitraum}${lage}</div>
                </div>`;
            })()}
            <div class="ov-pf ov-pf-schuh"><div class="ov-pfl">Schuhgrösse</div>
            <input id="ov-schuhgroesse" class="ov-softin" type="text" inputmode="decimal" value="${esc(emp.schuhgroesse)}" placeholder="42" maxlength="10" oninput="ovDirty()"></div>
            </div>
        </div>
        </div>`,
        `<button type="button" id="ovCancelBtn" class="ov-hbtn ov-savebtn" style="display:none" onclick="ovAbbrechen()">Abbrechen</button><button id="ovSaveBtn" class="ov-hbtn ov-hbtn-primary ov-savebtn" style="display:none" onclick="ovSave()">Speichern</button>`);

    // Weitere Adressen = eigene Box unten (Walter 02.08.2026).
    // Titelzeile: «Weitere Adressen» [(n)] · Beschreibung (Walter 02.08.2026).
    const kAddr = emp.isPayrollExcluded ? '' : _ovCard(
        `<span id="ovAddrCardTitle">${_t('ma.section.otherAddresses', 'Weitere Adressen')}</span>` +
        `<span id="ovAddrCardCount" class="ov-addr-count"></span>` +
        ` <span class="ov-addr-hint">${_t('ma.section.otherAddrHint', '(z.B. Wochenaufenthalt, Korrespondenz, Sozialamt — Hauptadresse oben)')}</span>` +
        ` <span id="ovAddrHistPill" style="margin-left:10px;display:inline-flex;vertical-align:middle"></span>`,
        null, '',
        `<div id="otherAddressesContent"></div>`,
        `<button type="button" class="ov-hbtn" style="padding:4px 12px;font-size:12px" onclick="openEmployeeAddressModal(null)">＋ ${_t('ma.btn.addAddress','Adresse hinzufügen')}</button>`);

    // ── Karte Anstellung (Walter 17.07.2026): L-GAV/<8h-Toggles +
    //    Kuendigungs-Daten mit Auto-Fristberechnung.
    //    Layout (Walter 19.07.2026): 2×2 Datumsfelder links, Toggles
    //    untereinander ganz rechts —
    //    Eintritt | Austritt | L-GAV
    //    Gekündigt am | Kündigung per | < 8 h / Wo. ──
    // Layout (Walter 02.08.2026): Probezeit nur noch als Badge im MA-Kopf.
    //   Zeile 1: Eintritt | Austritt | L-GAV (rechts)
    //   Zeile 2: Gekündigt am | Kündigung per | Kündigung durch | Austrittsgrund
    // < 8 h / Wo. gehört zum FLEX-Vertrag (Vertragsmaske), nicht zur Anstellung.
    const kAnst = _ovCard('Anstellung', null, '', `
        <div class="ov-anst-grid">
            <div class="ov-anst-top">
                <div class="ov-anst-daten">
                <div class="ov-pf ov-anst-datum"><div class="ov-pfl">${_t('ma.detail.entryDate','Eintritt')}</div><div class="ov-pfv">${emp.entryDate ? formatDate(emp.entryDate) : '<span class="ov-empty">–</span>'}</div></div>
                <div class="ov-pf ov-anst-datum"><div class="ov-pfl">${_t('ma.detail.exitDate','Austritt')}</div><div class="ov-pfv">${emp.exitDate ? formatDate(emp.exitDate) : '<span class="ov-empty">–</span>'}</div></div>
                ${_ovDienstalterRowHtml(emp)}
                </div>
                ${_ovArbeitszeugnisRowHtml(emp)}
                ${_ovKuendigungRowHtml(emp)}
                ${empSiehtLgav(emp)
                    ? `<div class="ov-pf ov-anst-tog"><div class="ov-pfl">L-GAV</div><div class="ov-pfv">${yesNoToggle('ov-lgavPflichtig', !!emp.lgavPflichtig)}</div></div>`
                    : ''}
            </div>
            <div class="ov-anst-kuend-row">
                <div class="ov-pf ov-anst-date ov-anst-kuend"><div class="ov-pfl">Gekündigt am</div>
                <input id="ov-kuendAm" class="ov-softin" type="date" value="${toDateInput(emp.kuendigungAusgesprochenAm)}" onchange="ovKuendAmChanged(${emp.id})"></div>
                <div class="ov-pf ov-anst-date ov-anst-kuend"><div class="ov-pfl">Kündigung per</div>
                <input id="ov-kuendPer" class="ov-softin" type="date" value="${toDateInput(emp.kuendigungPer)}" onchange="ovDirty()"></div>
                <div class="ov-pf ov-anst-date ov-anst-kuend"><div class="ov-pfl">Kündigung durch</div>
                <select id="ov-kuendDurch" class="ov-softin" onchange="ovDirty()">
                    <option value="">—</option>
                    <option value="AG"${(emp.kuendigungDurch || '').toUpperCase() === 'AG' ? ' selected' : ''}>durch uns</option>
                    <option value="AN"${(emp.kuendigungDurch || '').toUpperCase() === 'AN' ? ' selected' : ''}>durch Mitarbeiter</option>
                    <option value="GG"${(emp.kuendigungDurch || '').toUpperCase() === 'GG' ? ' selected' : ''}>gegenseitig</option>
                </select></div>
                <div class="ov-pf ov-anst-date ov-anst-kuend"><div class="ov-pfl">Austrittsgrund</div>
                <select id="ov-austrittsgrund" class="ov-softin" onchange="ovDirty()">${_austrittsgrundOptionsHtml(emp.austrittsgrund)}</select></div>
            </div>
        </div>`,
        `<button class="ov-hbtn ov-hbtn-primary ov-savebtn" style="display:none" onclick="ovSave()">Speichern</button>`);
    // ── Karte Nachtarbeit (Walter 17.07.2026): der VOLLE Funktions-Block
    //    aus dem frueheren Personal-Tab lebt jetzt HIER (einzige Instanz,
    //    keine DOM-ID-Dubletten): Status mit Ablauf-Warnung, Drucken-Buttons,
    //    Zeugnis-/Ausnahme-Anzeige, ⋮-Menue (Datum bearbeiten, verknuepfen,
    //    loesen) + Inline-Datum-Edit. ──
    // Pflicht-Badge: grün/rot (ArGV1 Art. 30). Layout in festen Zeilen
    // (Walter 19.07.2026), damit Badge/Nächte/Datum/Warnungen/Buttons
    // nicht je nach Inhalt springen.
    const kNacht = _ovCard('Nachtarbeit', null, '', `
            <div class="nw-card-body">
                <div id="nwView_${emp.id}" class="nw-layout">
                    <div class="nw-row nw-row1">
                        ${_nwDutyBadgeOnlyHtml(emp)}
                        ${_nwDutyCountHtml(emp)}
                        <div class="nw-col-menu">
                            <div class="dok-menu-wrap">
                                <button class="dok-menu-btn" onclick="nwToggleMenu(event, ${emp.id})" title="Aktionen">⋮</button>
                                <div class="dok-menu" id="nwMenu-${emp.id}">
                                    <button class="dok-menu-item" onclick="nwStartEdit(${emp.id})">Ausstellungsdatum bearbeiten</button>
                                    <button class="dok-menu-item" onclick="openAusweisDokuModal(${emp.id},'night_work_exam')">${emp.nightWorkExamDokumentId ? 'ArztZeug. ersetzen' : 'ArztZeug. verknüpfen'}</button>
                                    ${emp.nightWorkExamDokumentId ? `<button class="dok-menu-item" onclick="nwUnlinkDoku(${emp.id},'night_work_exam','Arztzeugnis')">ArztZeug. lösen</button>` : ''}
                                    <button class="dok-menu-item" onclick="openAusweisDokuModal(${emp.id},'night_work_ausnahme')">${emp.nightWorkAusnahmeDokumentId ? 'Ausn.Reg. ersetzen' : 'Ausn.Reg. verknüpfen'}</button>
                                    ${emp.nightWorkAusnahmeDokumentId ? `<button class="dok-menu-item" onclick="nwUnlinkDoku(${emp.id},'night_work_ausnahme','Ausnahmeregelung')">Ausn.Reg. lösen</button>` : ''}
                                </div>
                            </div>
                        </div>
                    </div>
                    <div class="nw-row nw-row2">
                        <div class="nw-dates" id="nwViewText_${emp.id}">${_nwViewTextHtml(emp.nightWorkExamIssued || (emp.nightWorkExamValidUntil ? _nwAddYears(emp.nightWorkExamValidUntil, -2) : null), emp.nightWorkExamValidUntil, emp.nightWorkExamMismatch, emp.nightWorkExamSollBis, emp.nightWorkExamDokumentId)}</div>
                    </div>
                    ${(() => {
                        // Fehlende Docs = rot; vollständig + gültig = grüne «Alles in Ordnung»
                        // (Walter 26.07.2026 — Alarm-Rot nur wenn wirklich etwas fehlt).
                        const status = _nwStatusChipsHtml(emp);
                        return status
                            ? `<div class="nw-row nw-row3"><div class="nw-warns">${status}</div></div>`
                            : '';
                    })()}
                    <div class="nw-row nw-row4 nw-actions">
                        <button class="nw-act-btn" onclick="openNachtEignungPdf(${emp.id})" title="Ärztliches Untersuchungsformular (SECO) drucken">🖨 Arztformular</button>
                        <button class="nw-act-btn" onclick="openNachtAusnahmePdf(${emp.id})" title="Ausnahmeregelung Tag-/Nachtarbeit drucken">🖨 Ausn. Reg.</button>
                        ${emp.nightWorkExamDokumentId
                            ? `<button class="nw-act-btn nw-act-view" onclick="qstOpenBefreiungsDok(${emp.id}, ${emp.nightWorkExamDokumentId})" title="Hinterlegtes Arztzeugnis anzeigen">👁 Arztzeugnis</button>`
                            : ''}
                        ${emp.nightWorkAusnahmeDokumentId
                            ? `<button class="nw-act-btn nw-act-view" onclick="qstOpenBefreiungsDok(${emp.id}, ${emp.nightWorkAusnahmeDokumentId})" title="Hinterlegte Ausnahmeregelung anzeigen">👁 Ausn. Reg.</button>`
                            : ''}
                    </div>
                </div>
                <div id="nwEdit_${emp.id}" class="nw-edit" style="display:none">
                    <span class="nw-edit-label">Ausgestellt</span>
                    <input type="date" id="nwDateInput_${emp.id}" value="${emp.nightWorkExamIssued ? String(emp.nightWorkExamIssued).slice(0,10) : (emp.nightWorkExamValidUntil ? _nwAddYears(emp.nightWorkExamValidUntil, -2) : '')}"
                           oninput="nwPreview(${emp.id}, this.value)"
                           title="Ausstellungsdatum des Arztzeugnisses"
                           class="nw-edit-input">
                    <span id="nwGueltigBis_${emp.id}">${_nwGueltigBisHtml(emp.nightWorkExamValidUntil, emp.nightWorkExamDokumentId)}</span>
                    <button onclick="nwSaveEdit(${emp.id})" class="nw-edit-save">Speichern</button>
                    <button onclick="nwCancelEdit(${emp.id})" class="nw-edit-cancel">Abbrechen</button>
                </div>
            </div>
`);

    // ── Karte Vertraege (alle — Personal-Tab entfernt, kein «Alle anzeigen») ──
    const contracts = (emp.employments || []).slice()
        .sort((a, b) => String(b.contractStartDate || '').localeCompare(String(a.contractStartDate || '')));
    const vRows = contracts.map(c => {
        const von = c.contractStartDate ? formatDate(c.contractStartDate) : '–';
        const bis = c.contractEndDate ? formatDate(c.contractEndDate) : 'offen';
        const pensum = empContractPensumText(c);
        const lohn = empContractWageText(c);
        const actions = _empContractActionsHtml(emp, c, contracts);
        // Punkt: offen/laufend = grün, beendet = grau (unabhängig von den Aktions-Buttons)
        const laufend = !_empContractIsEnded(c);
        const metaExtra = [pensum, empArbeitstageText(c), lohn].filter(Boolean).map(t => ' · ' + esc(t)).join('');
        const filCode = empContractBranchCode(c);
        const filHtml = filCode
            ? `<span class="ov-vfil" title="${esc(empContractBranchTitle(c))}">${esc(filCode)}</span>`
            : '';
        return `<div class="ov-vrow${laufend ? '' : ' archiv'}">
            <span class="ov-vdot${laufend ? ' g' : ''}"></span>
            <span class="emp-contract-model ${contractModelClass(c.employmentModel || '')}">${esc(modelDisplay(c.employmentModel || '–'))}</span>
            <span class="ov-vrole">${esc(c.jobTitle || c.jobGroupCode || 'Vertrag')}</span>
            ${filHtml}
            <span class="ov-vmeta">${von} – ${bis}${metaExtra}</span>
<!-- SMS-/Öffnungs-Status entfernt (Walter 10.08.2026): die Info lebt jetzt
     in der ONBOARDING-Auswertung im HR-Hub. loadOvVertragSms bleibt als
     toter Code erhalten, wird aber nicht mehr aufgerufen. -->
            ${actions}
        </div>`;
    }).join('') || '<div class="ov-empty" style="padding:4px 0">Keine Verträge vorhanden.</div>';
    // Feste 3-Zeilen-Hoehe (Walter 17.07.2026): 4 passten nicht ohne Masken-
    // Scroll. Mehr Verträge → nur die innere Liste scrollt, Maske bleibt fix.
    const vList = `<div class="ov-vlist">${vRows}</div>`;
    // SMS-/Link-Feedback-Container fuer die Uebersicht (Klasse statt ID —
    // der Personal-Strip hat seinen eigenen; siehe contractShareBox-Lookup).
    const vShare = '<div class="contractShareBox" style="margin:4px 0 0"></div>';
    // Verträge breit + rechts Stunden/Saldi-Tabelle (Walter 19.07.2026).
    // KTG/UVG-Tagessatz bleibt im Absenzen-Tab (Sidebar) — nicht mehr hier.
    // Neuer Vertrag nur ohne easy@work (Walter 27.10.2026): mit Zuordnung
    // kommen die Verträge nur aus easy. Filiale = Seitenleiste.
    const filialId = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    const hatEasy = filialId != null && typeof filialeHatEasy === 'function' && filialeHatEasy(filialId);
    const neuerVertrag = !filialId
        ? ''
        : hatEasy
            ? `<span style="font-size:12px;font-weight:600;color:#8b8b8b">aus easy@work</span>`
            : `<button type="button" class="ov-hbtn ov-hbtn-primary" style="padding:4px 12px;font-size:12px" onclick="openNewContractInModal(${emp.id})">+ Neuer Vertrag</button>`;
    const kVert = _ovCard(`Verträge <span class="ov-count">${contracts.length}</span>`, null, '', vList + vShare, neuerVertrag);
    const kSaldi = `<div class="ov-card ov-saldi-card">
        <div class="ov-saldi-head">
            <div class="ov-saldi-switch" role="group" aria-label="Saldi-Zeitraum">
                <button type="button" class="ov-saldi-sw" data-mode="aktuell" onclick="ovSaldiSetMode('aktuell')">aktuell</button>
                <button type="button" class="ov-saldi-sw" data-mode="monat" onclick="ovSaldiSetMode('monat')">Monat</button>
            </div>
        </div>
        <div id="ovSaldiContent" class="ov-saldi-slot">${_ovSaldiSkeletonHtml()}</div>
    </div>`;

    // Dokumente-Karte entfernt (Walter 17.07.2026) — Dokumente haben wie
    // gehabt ihren eigenen Bereich (Tab «Dokumente»).

    // Personalien · Anstellung|Nachtarbeit · Verträge|Saldi · Weitere Adressen
    el.innerHTML = emp._neu
        ? `<div class="ov-wrap"><div class="ov-full">${kPers}</div></div>`
        : `<div class="ov-wrap">${emp.isPayrollExcluded
        ? `<div class="ov-full">${kPers}</div>`
        : `<div id="pdKarte" class="ov-full" style="display:none"></div><div class="ov-full">${kPers}</div>${kAnst}${kNacht}<div class="ov-vertraege-ktg">${kVert}${kSaldi}</div><div class="ov-addr-full">${kAddr}</div>`}</div>`;
    if (emp._neu) {
        ovNatFuellen(emp);
        ovNrPruefen();
        ovDirty();
        return;
    }
    if (!emp.isPayrollExcluded && typeof pdKarteLaden === 'function')
        pdKarteLaden(emp.id);
    if (!emp.isPayrollExcluded && typeof loadEmployeeAddressesTab === 'function')
        loadEmployeeAddressesTab(emp.id);
    if (!emp.isPayrollExcluded) {
        loadOvSaldi(emp.id);
        // loadOvVertragSms(emp.id);  // entfernt — Status jetzt in der ONBOARDING-Auswertung (HR-Hub)
    }
    ovNatFuellen(emp);
}

function ovAhvUnbekannt() {
    const box = document.getElementById('ov-ahv-unbekannt');
    const inp = document.getElementById('ov-ahv');
    const status = document.getElementById('ov-ahv-status');
    if (!inp) return;
    inp.disabled = !!(box && box.checked);
    if (box && box.checked) {
        inp.value = '';
        if (status) status.textContent = '';
    }
    if (typeof ovDirty === 'function') ovDirty();
}

async function ovNatFuellen(emp) {
    const sel = document.getElementById('ov-nat');
    if (!sel || !emp) return;
    const list = await getNationalities();
    const cur = emp.nationalityId;
    const zeilen = (list || []).filter(n => n.isActive !== false);
    zeilen.sort((a, b) => {
        const ac = String(a.code || '');
        const bc = String(b.code || '');
        const sonder = c => c === '11' ? 0 : c === '22' ? 1 : 2;
        const d = sonder(ac) - sonder(bc);
        if (d) return d;
        return String(a.name || a.nameDe || ac).localeCompare(String(b.name || b.nameDe || bc), 'de');
    });
    sel.innerHTML = '<option value="">–</option>' + zeilen.map(n => {
        const id = n.id ?? n.Id;
        const code = n.code ?? n.Code ?? '';
        const name = n.name ?? n.nameDe ?? n.Name ?? code;
        const label = code === '11' || code === '22' ? `${code} ${name}` : `${name}${code ? ' (' + code + ')' : ''}`;
        return `<option value="${id}">${esc(label)}</option>`;
    }).join('');
    if (cur) sel.value = String(cur);
}

// SMS-/Link-Status in der Vertragszeile (Walter 05.08.2026): NUR wenn
// wirklich eine Vertrags-SMS versendet wurde (sms_log) — Link-only-Erzeugung
// oder gar kein Versand zeigt nichts. Best-effort nach dem Render.
async function loadOvVertragSms(employeeId) {
    try {
        const r = await fetch(`/api/contract-share/status-by-employee?employeeId=${employeeId}`, { headers: ah() });
        if (!r.ok) return;
        const s = await r.json();
        if (!s.lastSmsSentAt || !Array.isArray(s.tokens)) return; // nie eine SMS raus → nichts anzeigen
        const f = ts => {
            const d = new Date(ts);
            return isNaN(d.getTime()) ? '' :
                d.toLocaleDateString('de-CH', { day: '2-digit', month: '2-digit', year: '2-digit' }) +
                ' ' + d.toLocaleTimeString('de-CH', { hour: '2-digit', minute: '2-digit' });
        };
        for (const t of s.tokens) {
            const el = document.getElementById(`ovVsms_${t.employmentId}`);
            if (!el) continue;
            // Kompakt (Walter 05.08.2026): nur Icons + Datum/Zeit, kein Text.
            // 📲 = gesendet · 👁 = geöffnet (PDF abgerufen ergänzt ✓) · 👁 grau = noch nicht.
            let html = `📲 ${f(t.createdAt)}`;
            if (t.openedAt) {
                html += ` · 👁 ${f(t.openedAt)}${t.usedAt ? ' ✓' : ''}`;
            } else {
                html += ' · <span style="color:#b45309">👁 –</span>';
            }
            el.innerHTML = html;
            el.title = 'Vertrags-SMS: gesendet' + (t.openedAt ? ' · Link geöffnet' + (t.usedAt ? ' · PDF abgerufen' : '') : ' · Link noch nicht geöffnet');
        }
    } catch (_) { /* Status ist nur Komfort */ }
}

// Inline-Edit in der Uebersicht (Walter 17.07.2026): Speichern liest ov-*
// direkt (Personal-Tab entfernt — kein Spiegeln mehr auf ef-*).
// Austrittsgrund — kurze Labels (Walter 26.07.2026). Codes = Backend AustrittsgrundCodes.
const _AUSTRITTSGRUND = [
    ['AUSBILDUNG', 'Ausbildung'],
    ['ANDERER_JOB', 'Anderer Job'],
    ['UMZUG', 'Umzug'],
    ['FAMILIE', 'Familie'],
    ['GESUNDHEIT', 'Gesundheit'],
    ['ARBEITSZEITEN', 'Arbeitszeiten'],
    ['LOHN', 'Lohn/Pensum'],
    ['TEAM', 'Team/Führung'],
    ['PROBEZEIT', 'Probezeit'],
    ['LEISTUNG', 'Leistung'],
    ['VERFUEGBARKEIT', 'Verfügbarkeit'],
    ['VERHALTEN', 'Verhalten'],
    ['BEFRISTUNG', 'Befristung'],
    ['PERS_GRUENDE', 'pers. Gründe'],
    ['DIVERS', 'Divers'],
];
function _austrittsgrundOptionsHtml(selected) {
    const cur = (selected || '').toUpperCase();
    let h = '<option value="">—</option>';
    for (const [code, lbl] of _AUSTRITTSGRUND)
        h += `<option value="${code}"${cur === code ? ' selected' : ''}>${lbl}</option>`;
    return h;
}
function ovDirty() {
    document.querySelectorAll('.ov-savebtn').forEach(b => b.style.display = 'inline-flex');
}
async function ovKuendAmChanged(empId) {
    if (typeof kuendAmChanged === 'function') await kuendAmChanged(empId);
    ovDirty();
}
/** Zivilstand gewechselt: Gültig ab («Zivilstand seit») neu verlangen, Bemerkung anbieten. */
function ovZivGeaendert() {
    const sel = document.getElementById('ov-zivilstand');
    if (!sel) return;
    const gewechselt = (sel.value || '') !== (sel.dataset.alt || '');
    const bem = document.getElementById('ov-zivBemerkung');
    if (bem) bem.style.display = gewechselt ? '' : 'none';
    const seit = document.getElementById('ov-maritalStatusSince');
    if (seit && gewechselt && !seit.dataset.vorWechsel) {
        seit.dataset.vorWechsel = seit.value || '-';
        seit.value = '';
        seit.focus();
    } else if (seit && !gewechselt && seit.dataset.vorWechsel) {
        seit.value = seit.dataset.vorWechsel === '-' ? '' : seit.dataset.vorWechsel;
        delete seit.dataset.vorWechsel;
    }
    // Normale Zivilstände pflegt easy@work — nur Sonderfälle bleiben in OneCrew stehen.
    const hinweis = document.getElementById('ov-zivHinweis');
    const easyKennt = ['ledig', 'verheiratet', 'geschieden', 'verwitwet', 'getrennt', 'eingetragene_partnerschaft', 'konkubinat'];
    if (hinweis) {
        const ueberschrieben = gewechselt && sel.dataset.easy && easyKennt.includes(sel.value);
        hinweis.textContent = ueberschrieben
            ? 'Dieser Zivilstand kommt aus easy@work und wird beim nächsten Abgleich wieder überschrieben — bitte in easy@work ändern.'
            : '';
        hinweis.style.display = ueberschrieben ? '' : 'none';
    }
    ovDirty();
}

function ovSave() {
    saveEmpEdit();
}
function ovAbbrechen() {
    if (selectedEmployee && selectedEmployee._neu) {
        const zurueck = window._maVorNeu || null;
        window._maVorNeu = null;
        testNeuMaZu();
        if (zurueck && typeof selectEmployee === 'function') {
            selectEmployee(zurueck);
            return;
        }
        selectedEmployee = null;
        selectedEmployeeId = null;
        window.selectedEmployeeId = null;
        const el = document.getElementById('uebersichtContent');
        if (el) el.innerHTML = '';
        updateLsEmpSyncBtn();
        return;
    }
    if (selectedEmployee) {
        delete selectedEmployee._testBearbeiten;
        renderEmployeeDetail(selectedEmployee);
        return;
    }
    updateLsEmpSyncBtn();
}

document.addEventListener('onecrew-instanz', () => {
    if (typeof updateLsEmpSyncBtn === 'function') updateLsEmpSyncBtn();
    if (window._istTestinstanz && selectedEmployee && document.getElementById('uebersichtContent'))
        loadUebersichtTab();
});

function testPersonBearbeiten() {
    if (!window._istTestinstanz || !selectedEmployee || selectedEmployee._neu) return;
    selectedEmployee._testBearbeiten = true;
    activeEmpTab = 'uebersicht';
    renderEmployeeDetail(selectedEmployee);
    ovDirty();
}

function naechstePersonalnummerGlobal() {
    const nums = (_empAllRaw || [])
        .map(e => String(e.employeeNumber || '').trim())
        .filter(n => /^\d+$/.test(n) && !n.startsWith('99') && !/alt$/i.test(n))
        .map(Number)
        .filter(n => Number.isFinite(n) && n >= 0);
    const max = nums.reduce((m, n) => (n > m ? n : m), 0);
    return String(max + 1);
}

function testNeuMaSicher() {
    let host = document.getElementById('testNeuMaCssHost');
    const page = document.getElementById('page-mitarbeiter') || document.body;
    if (!host) {
        host = document.createElement('div');
        host.id = 'testNeuMaCssHost';
        host.className = 'emp-detail-body ov-noscroll';
        host.innerHTML = '<div id="testNeuMaModal"><div class="ov-full" id="testNeuMaBody"></div></div>';
        page.appendChild(host);
    }
    return document.getElementById('testNeuMaModal');
}
function testNeuMaPlatz() {
    const modal = document.getElementById('testNeuMaModal');
    const tabs = document.querySelector('#empDetailPanel .emp-detail-tabs');
    const panel = document.getElementById('empDetailPanel');
    const ziel = tabs || panel;
    if (!modal || !ziel) return;
    const r = ziel.getBoundingClientRect();
    const top = Math.max(8, r.top);
    modal.style.top = top + 'px';
    modal.style.left = r.left + 'px';
    modal.style.width = r.width + 'px';
    modal.style.maxHeight = Math.max(280, window.innerHeight - top - 12) + 'px';
}
function testNeuMaZu() {
    const modal = document.getElementById('testNeuMaModal');
    if (!modal) return;
    modal.classList.remove('offen');
    const body = document.getElementById('testNeuMaBody');
    if (body) body.innerHTML = '';
}
function ovNrPruefen() {
    const el = document.getElementById('ov-nr');
    const hin = document.getElementById('ov-nr-hinweis');
    if (!el || !hin) return;
    const nr = el.value.trim();
    const selbst = selectedEmployee && selectedEmployee.id > 0 ? selectedEmployee.id : -1;
    const schon = !!nr && (_empAllRaw || []).some(e => String(e.employeeNumber || '').trim() === nr && e.id !== selbst);
    hin.textContent = schon ? 'Diese Nummer gibt es im Test schon.' : (nr ? 'Vorschlag. Kann geändert werden, solange sie frei ist.' : 'Personalnummer fehlt.');
    hin.classList.toggle('belegt', schon || !nr);
    if (typeof ovDirty === 'function') ovDirty();
}
if (!window._testNeuMaLage) {
    window._testNeuMaLage = true;
    const nach = () => { if (document.getElementById('testNeuMaModal')?.classList.contains('offen')) testNeuMaPlatz(); };
    window.addEventListener('resize', nach);
    document.addEventListener('scroll', nach, true);
}
function neuerMaOeffnen() {
    if (!window._istTestinstanz) return;
    if (typeof activeEmpTab !== 'undefined' && activeEmpTab !== 'uebersicht' && selectedEmployee && !selectedEmployee._neu && typeof switchEmpTab === 'function')
        switchEmpTab('uebersicht');
    let nr = '';
    try { nr = naechstePersonalnummerGlobal(); } catch (_) { nr = ''; }
    window._maVorNeu = (selectedEmployee && !selectedEmployee._neu && selectedEmployee.id > 0)
        ? selectedEmployee.id : (selectedEmployeeId > 0 ? selectedEmployeeId : null);
    selectedEmployee = {
        _neu: true, id: -1, isActive: true, isPayrollExcluded: false,
        firstName: '', lastName: '', employeeNumber: nr,
        gender: '', languageCode: 'de', country: 'CH', employments: [],
    };
    const modal = testNeuMaSicher();
    modal.classList.add('offen');
    testNeuMaPlatz();
    loadUebersichtTab();
}

async function neuerMaSpeichern() {
    const v = id => (document.getElementById(id)?.value || '').trim();
    const vorname = v('ov-firstName');
    const nachname = v('ov-lastName');
    const nummer = v('ov-nr');
    const geschlecht = v('ov-gender');
    const geburt = v('ov-dob');
    const nat = v('ov-nat');
    if (!vorname || !nachname) { alert('Vorname und Nachname fehlen.'); return; }
    if (!nummer) { alert('Personalnummer fehlt.'); return; }
    const schon = (_empAllRaw || []).some(e => String(e.employeeNumber || '').trim() === nummer);
    if (schon) { alert('Personalnummer «' + nummer + '» ist schon vergeben.'); return; }
    if (!geschlecht) { alert('Geschlecht fehlt. M oder F.'); return; }
    if (!geburt) { alert('Geburtsdatum fehlt.'); return; }
    const heute = new Date(); heute.setHours(0, 0, 0, 0);
    if (new Date(geburt + 'T00:00:00') > heute) { alert('Geburtsdatum darf nicht nach dem heutigen Tag liegen.'); return; }
    if (!nat) { alert('Nationalität fehlt.'); return; }
    let ahv = '';
    if (document.getElementById('ov-ahv-unbekannt')?.checked) ahv = 'unbekannt';
    else if (v('ov-ahv')) {
        const chk = validateAhvNummer(v('ov-ahv'));
        if (chk.valid === false) {
            alert('AHV-Nummer ist ungültig:\n' + chk.error + '\n\nGültig zum Beispiel 756.7865.5342.46.');
            return;
        }
        ahv = chk.normalized || '';
    }
    const mobilRoh = v('ov-mobile');
    const mobil = mobilRoh && window.formatPhoneIntl ? window.formatPhoneIntl(mobilRoh) : mobilRoh;
    if (mobilRoh && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(mobil)) {
        alert('Mobilnummer ungültig (erwartet +41 79 409 43 33).');
        return;
    }
    const telRoh = v('ov-phone2');
    const tel = telRoh && window.formatPhoneIntl ? window.formatPhoneIntl(telRoh) : telRoh;
    if (telRoh && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(tel)) {
        alert('Telefonnummer ungültig (erwartet +41 79 409 43 33).');
        return;
    }
    const mail = v('ov-email');
    if (mail && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(mail)) { alert('E-Mail-Adresse ist ungültig.'); return; }
    const landRoh = (v('ov-land') || 'CH').toUpperCase();
    const land = landIstCh(landRoh) ? 'CH' : landRoh;
    const body = {
        employeeNumber: nummer,
        firstName: vorname,
        lastName: nachname,
        gender: geschlecht,
        dateOfBirth: geburt,
        languageCode: v('ov-lang') || 'de',
        nationalityId: parseInt(nat, 10) || null,
        socialSecurityNumber: ahv || null,
        phoneMobile: mobil || null,
        phone2: tel || null,
        email: mail || null,
        street: v('ov-street') || null,
        zipCode: v('ov-zip') || null,
        city: v('ov-city') || null,
        country: land,
        cantonCode: land === 'CH' ? (v('ov-canton') || null) : 'EX',
        maritalStatus: v('ov-zivilstand') || null,
        maritalStatusSince: v('ov-maritalStatusSince') || null,
        isActive: true,
    };
    const btn = document.getElementById('ovSaveBtn');
    if (btn) btn.disabled = true;
    try {
        const res = await (typeof pdFetch === 'function' ? pdFetch : fetch)('/api/employees', {
            method: 'POST',
            headers: ah(),
            body: JSON.stringify(body),
        });
        if (!res.ok) {
            const msg = typeof pdFehlertext === 'function' ? await pdFehlertext(res) : 'Speichern fehlgeschlagen.';
            alert(msg || 'Speichern fehlgeschlagen.');
            return;
        }
        const created = await res.json().catch(() => ({}));
        const id = created.id || created.Id;
        if (typeof loadMitarbeiterList === 'function') await loadMitarbeiterList();
        if (id && typeof selectEmployee === 'function') await selectEmployee(id);
        if (typeof showToast === 'function') showToast('Mitarbeiter erfasst.', 'success');
    } catch (e) {
        alert('Speichern fehlgeschlagen.');
    } finally {
        if (btn) btn.disabled = false;
    }
}


// ⋮-Menue der Kopf-Card — seit 17.07.2026 nicht mehr verdrahtet (Buttons
// leben im Restaurant-Admin-Tab), Funktion bleibt als Code erhalten.
function empHeadMenuToggle(ev) {
    ev.stopPropagation();
    const menu = document.getElementById('empHeadMenu');
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (menu && !wasOpen) menu.classList.add('show');
}

function renderEmpContractList(emp) {
    const contracts = (emp.employments || []).slice().sort((a, b) => {
        const ad = a.contractStartDate || '';
        const bd = b.contractStartDate || '';
        return bd.localeCompare(ad);
    });
    if (!contracts.length) {
        return `<div class="emp-contract-strip empty">Keine Verträge vorhanden.</div>`;
    }
    const rows = contracts.map(c => {
        const model = c.employmentModel || '–';
        const from = c.contractStartDate ? formatDate(c.contractStartDate) : '–';
        const to = c.contractEndDate ? formatDate(c.contractEndDate) : 'offen';
        // Walter 22.09.2026: IMMER den gespeicherten job_title zeigen (der ist pro
        // Vertragsabschnitt historisch korrekt). Weicht die Funktionsgruppe
        // (job_group_id, vom easy@work-Sync oft auf die HEUTIGE Funktion gesetzt)
        // davon ab, kommt sie klein daneben — damit man den Unterschied sieht,
        // statt dass die Liste stumm auf die FK-Spalte zurückfällt.
        const jt = (c.jobTitle || '').trim();
        const jg = (c.jobGroupCode || '').trim();
        const title = jt || jg || c.position || 'Vertrag';
        const titleZusatz = (jt && jg && jt.toUpperCase() !== jg.toUpperCase())
            ? `<span class="emp-contract-status" title="Funktionsgruppe am Vertrag (job_group_id) — weicht von der gespeicherten Funktion ab">${esc(jg)}</span>`
            : '';
        const pensum = empContractPensumText(c);
        const wage = empContractWageText(c);
        const active = _empContractIsEnded(c)
            ? `<span class="emp-contract-status">archiviert</span>`
            : `<span class="emp-contract-status active">aktiv</span>`;
        const actions = _empContractActionsHtml(emp, c, contracts);
        const metaExtra = [pensum, empArbeitstageText(c), wage].filter(Boolean).map(t => ' · ' + esc(t)).join('');
        const filCode = empContractBranchCode(c);
        const filHtml = filCode
            ? `<span class="ov-vfil" title="${esc(empContractBranchTitle(c))}">${esc(filCode)}</span>`
            : '';
        return `<div class="emp-contract-row">
            <div class="emp-contract-main">
                <span class="emp-contract-model ${contractModelClass(model)}">${esc(modelDisplay(model))}</span>
                <span class="emp-contract-title">${esc(title)}</span>
                ${titleZusatz}
                ${filHtml}
                ${active}
            </div>
            <div class="emp-contract-meta">${from} – ${to}${metaExtra}${c.probationEndDate ? ' · Probezeit bis ' + formatDate(c.probationEndDate) : ''}</div>
            <div class="emp-contract-actions">${actions}</div>
        </div>`;
    }).join('');
    return `<div class="emp-contract-strip" aria-label="Verträge">
        <div class="emp-contract-head">
            <span>Verträge</span>
            <span>${contracts.length}</span>
        </div>
        <div class="emp-contract-scroll">${rows}</div>
    </div>
    <div class="contractShareBox" style="margin:10px 0 0"></div>`;
}

function contractModelClass(model) {
    return ({
        MTP: 'model-badge-mtp',
        FLEX: 'model-badge-utp',
        FIX: 'model-badge-fix',
        'FIX-M': 'model-badge-fix-m'
    })[model] || '';
}

function empSiehtLgav(emp) {
    if (typeof filialeUnterstehtLgav !== 'function') return true;
    const ids = (emp?.employments || []).map(c => c.companyProfileId).filter(id => id != null);
    if (!ids.length) return filialeUnterstehtLgav(typeof fixedCompanyProfileId !== 'undefined' ? fixedCompanyProfileId : null);
    return ids.some(id => filialeUnterstehtLgav(id));
}

function empContractBranch(c) {
    const id = c?.companyProfileId;
    if (id == null || typeof allBranches === 'undefined') return null;
    return allBranches.find(b => Number(b.id) === Number(id)) || null;
}

/** Restaurant-Code der Filiale am Vertrag (Walter 15.09.2026, Übertritts-Fälle). */
function empContractBranchCode(c) {
    return (empContractBranch(c)?.restaurantCode || '').trim();
}

function empContractBranchTitle(c) {
    const b = empContractBranch(c);
    if (!b) return '';
    const code = (b.restaurantCode || '').trim();
    const name = b.city || b.branchName || b.companyName || '';
    return [code, name].filter(Boolean).join(' – ');
}

function empContractWageText(c) {
    const fmt = v => Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    if (c.hourlyRate != null) return `CHF ${fmt(c.hourlyRate)}/h`;
    if (c.monthlySalaryFte != null) return `CHF ${fmt(c.monthlySalaryFte)} / 100%`;
    if (c.monthlySalary != null) return `CHF ${fmt(c.monthlySalary)} / Mt.`;
    return '';
}

// FIX/FIX-M → Pensum «80%»; MTP → Wochenstunden «25/Wo» (Walter 02.08.2026).
function empContractPensumText(c) {
    const m = (c.employmentModel || '').toUpperCase();
    if (m === 'FIX' || m === 'FIX-M') {
        if (c.employmentPercentage == null || c.employmentPercentage === '') return '';
        const n = Number(c.employmentPercentage);
        if (!Number.isFinite(n)) return '';
        return `${Number.isInteger(n) ? n : n.toLocaleString('de-CH', { maximumFractionDigits: 2 })}%`;
    }
    if (m === 'MTP') {
        const h = c.guaranteedHoursPerWeek ?? c.weeklyHours;
        if (h == null || h === '') return '';
        const n = Number(h);
        if (!Number.isFinite(n)) return '';
        return `${Number.isInteger(n) ? n : n.toLocaleString('de-CH', { maximumFractionDigits: 2 })}/Wo`;
    }
    return '';
}

// Arbeitstage pro Woche (Walter 05.10.2026) — Zeitgutschrift Krank/Unfall.
// Gleiche Regel wie Services/KrankUnfallZeitgutschrift.cs: nur FIX/FIX-M, von Hand
// oder Vorschlag 5 × Pensum (auf 0.5 gerundet, max. 5). MTP rechnet immer Garantie ÷ 5.
function empArbeitstage(c) {
    const m = (c.employmentModel || '').toUpperCase();
    if (!['FIX', 'FIX-M'].includes(m)) return null;
    const hand = c.arbeitstageProWoche != null ? Number(c.arbeitstageProWoche) : null;
    const p = Number(c.employmentPercentage) > 0 ? Number(c.employmentPercentage) : 100;
    const gueltig = p >= 100 ? (hand === 4 || hand === 5) : (hand >= 2.5 && hand <= 5 && hand * 2 === Math.floor(hand * 2));
    if (hand != null && gueltig) return { wert: hand, vonHand: true, vorschlag: empArbeitstageVorschlag(c) };
    return { wert: empArbeitstageVorschlag(c), vonHand: false, vorschlag: empArbeitstageVorschlag(c) };
}
function empArbeitstageVorschlag(c) {
    const p = Number(c.employmentPercentage);
    const roh = 5 * (p > 0 ? p : 100) / 100;
    return Math.min(5, Math.max(2.5, Math.round(roh * 2) / 2));
}
function empArbeitstageText(c) {
    const a = empArbeitstage(c);
    if (!a) return '';
    const n = a.wert.toLocaleString('de-CH', { maximumFractionDigits: 1 });
    return `${n} Tage/Wo${a.vonHand ? '' : ' (Vorschlag)'}`;
}
async function empArbeitstageBearbeiten(employmentId, employeeId) {
    const c = (selectedEmployee?.employments || []).find(x => (x.id ?? x.employmentId) === employmentId);
    if (!c) return;
    const a = empArbeitstage(c);
    if (!a) return;
    const fmt = v => v.toLocaleString('de-CH', { minimumFractionDigits: 1, maximumFractionDigits: 1 });
    let opts = `<option value="">Vorschlag (${fmt(a.vorschlag)})</option>`;
    for (let v = 2.5; v <= 5; v += 0.5)
        opts += `<option value="${v}" ${a.vonHand && a.wert === v ? 'selected' : ''}>${fmt(v)} Tage</option>`;
    const wahl = await new Promise(resolve => {
        document.getElementById('empArbeitstageModal')?.remove();
        const wrap = document.createElement('div');
        wrap.id = 'empArbeitstageModal';
        wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
        wrap.innerHTML = `
        <div class="modal" style="max-width:480px;width:92%;padding:22px 24px;border-radius:16px">
            <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:8px">Arbeitstage pro Woche</div>
            <div style="font-size:13.5px;color:#646464;line-height:1.5">
                Damit rechnet OneCrew die Zeitgutschrift bei Krankheit und Unfall:
                ein eingeplanter Tag zählt das Tagessoll = Wochenstunden ÷ Arbeitstage
                (z.B. 100 % in 4 Tagen = 42 ÷ 4 = 10.50 h).
                Ohne Eingabe gilt der Vorschlag aus dem Pensum (volle Tage, 80 % = 4 Tage).
            </div>
            <select id="empArbeitstageSel" class="ef-input" style="margin-top:14px;width:100%">${opts}</select>
            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
                <button id="empAtNo" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
                <button id="empAtYes" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Speichern</button>
            </div>
        </div>`;
        document.body.appendChild(wrap);
        const done = v => { wrap.remove(); document.removeEventListener('keydown', onKey); resolve(v); };
        const onKey = e => { if (e.key === 'Escape') done(undefined); };
        document.addEventListener('keydown', onKey);
        wrap.addEventListener('click', e => { if (e.target === wrap) done(undefined); });
        wrap.querySelector('#empAtNo').onclick  = () => done(undefined);
        wrap.querySelector('#empAtYes').onclick = () => done(wrap.querySelector('#empArbeitstageSel').value);
    });
    if (wahl === undefined) return;
    const res = await fetch(`/api/employments/${employmentId}/arbeitstage`, {
        method: 'PATCH',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({ arbeitstage: wahl === '' ? null : Number(wahl) }),
    });
    if (!res.ok) {
        const d = await res.json().catch(() => ({}));
        await liquidConfirm(d.message || `Speichern fehlgeschlagen (HTTP ${res.status}).`, { title: 'Arbeitstage', yesLabel: 'OK', hideNo: true });
        return;
    }
    selectEmployee(employeeId);
}

function _empContractIsEnded(c) {
    const today = new Date().toISOString().slice(0, 10);
    const end = c.contractEndDate ? String(c.contractEndDate).slice(0, 10) : '';
    return !!(end && end < today);
}

// Historischer Vertrag = beendet UND es gibt bereits einen neueren Vertrag.
// Dann nur noch Anschauen — kein Bearbeiten / SMS / Link-Reset (Walter 19.07.2026).
function _empContractIsHistorisch(c, allContracts) {
    if (!_empContractIsEnded(c)) return false;
    const start = String(c.contractStartDate || '');
    return (allContracts || []).some(o => String(o.contractStartDate || '') > start);
}

function _empContractActionsHtml(emp, c, allContracts) {
    const cid = c.id ?? c.employmentId;
    if (!cid) return '';
    // ⋮-Menü wie überall (Walter 31.07.2026) — statt vier Text-Buttons.
    const historisch = _empContractIsHistorisch(c, allContracts);
    // Vertrags-SMS nur für ausgewählte Benutzer (Walter 10.08.2026):
    // admin/superuser immer; Rolle user braucht das Häkchen «Vertrags-SMS
    // senden» (Filial-Tab «Unterzeichner»). Server prüft zusätzlich pro Filiale.
    const darfSms = ['admin', 'superuser', 'buchhaltung'].includes(currentUser?.role) || !!currentUser?.canVertragSms;
    const smsItems = darfSms
        ? `<button type="button" class="dok-menu-item" onclick="contractShareSendSms(${emp.id}, ${cid}, '${esc(emp.phoneMobile || '')}')">SMS</button>
           <button type="button" class="dok-menu-item danger" onclick="contractShareRevoke(${cid})">Link löschen</button>`
        : '';
    // «Bearbeiten» — nur Administrator (Walter-Vorgabe 23.09.2026): Verträge
    // führt easy@work. Wer dort etwas ändern will, ändert es in easy und holt
    // es über den Import (laufender Vertrag ohne Lohnlauf direkt; sonst neuer
    // Vertrag in easy, alten abschliessen, neuen importieren). Von Hand editiert
    // nur der Admin — das ist auch der Weg für die alte Historie vor 2026, die
    // easy@work nicht brauchbar liefert. Der Server prüft dasselbe.
    // Auf historischen Abschnitten fallen zusätzlich SMS und Link-Reset weg:
    // einen abgelaufenen Vertrag verschickt man nicht mehr.
    const editItem = darfVertragBearbeiten()
        ? `<button type="button" class="dok-menu-item" onclick="empContractEdit(${cid}, ${emp.id})">Bearbeiten</button>`
        : `<div class="dok-menu-item" style="cursor:default;color:#8b8b8b;font-size:12px" title="Verträge werden in easy@work geändert und über den Import geholt.">Änderung über easy@work</div>`;
    // «Löschen» (Walter 23.09.2026, Llalloshi: Fehl-Vertrag in falscher Filiale).
    // Server prüft Lohnbelege der Filiale des Vertrags; Zwangs-Löschen nur Admin.
    const deleteItem = (typeof isOpsRole === 'function' && isOpsRole())
        ? `<button type="button" class="dok-menu-item danger" onclick="empContractDelete(${cid}, ${emp.id})">Löschen</button>`
        : '';
    // Unterschriebener Vertrag (Walter 23.09.2026): direkt verknüpftes Dokument.
    const vDok = c.vertragDokumentId;
    const minderjaehrig = istMinderjaehrig(emp.dateOfBirth);
    const vertragItems = (vDok
        ? `<button type="button" class="dok-menu-item" onclick="openDirectDoc(${vDok})">Unterschriebenen Vertrag öffnen</button>`
        : '')
        + `<button type="button" class="dok-menu-item" onclick="openAusweisDokuModal(${emp.id},'vertrag',{employmentId:${cid}})">${vDok ? 'Unterschriebenen Vertrag ersetzen' : 'Unterschriebenen Vertrag verknüpfen'}</button>`
        + (minderjaehrig && !historisch
            ? `<button type="button" class="dok-menu-item" onclick="vertragUnterschriftElternSetzen(${emp.id}, ${cid}, ${!c.unterschriftEltern})">${c.unterschriftEltern ? 'Unterschrift Erziehungsberechtigte entfernen' : 'Unterschrift Erziehungsberechtigte bestätigen'}</button>`
            : '');
    const elternPill = minderjaehrig && !historisch
        ? (c.unterschriftEltern
            ? `<span title="Unterschrift der Erziehungsberechtigten bestätigt" style="margin-right:8px;background:#dcfce7;border:1px solid #86efac;color:#15803d;border-radius:6px;padding:2px 7px;font-size:11px;font-weight:600">Eltern ✓</span>`
            : `<button type="button" onclick="vertragUnterschriftElternSetzen(${emp.id}, ${cid}, true)" title="Unter 18: Unterschrift der Erziehungsberechtigten auf dem Vertrag bestätigen"
                   style="margin-right:8px;background:#fef2f2;border:1px dashed #fca5a5;color:#b91c1c;border-radius:6px;padding:2px 7px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit">Eltern fehlt</button>`)
        : '';
    // Doku-Symbol wie überall (Walter 26.09.2026); gelb nur bei laufenden Verträgen.
    const vertragPill = (vDok || !historisch)
        ? docIconBtn({ docId: vDok, was: 'Unterschriebener Vertrag', style: 'margin-right:8px',
            verknuepfen: `openAusweisDokuModal(${emp.id},'vertrag',{employmentId:${cid}})`,
            loesen: `docLoesenPatch('/api/employees/${emp.id}/employments/${cid}/dokument','Unterschriebener Vertrag',()=>selectEmployee(${emp.id}))` })
        : '';
    // Arbeitstage pro Woche: nur noch in «Vertrag bearbeiten» neben dem Pensum (Walter 06.10.2026).
    const arbeitsvertrag = (typeof filialeUnterstehtLgav !== 'function' || filialeUnterstehtLgav(c.companyProfileId))
        ? `<button type="button" class="dok-menu-item" onclick="openEmpContractPdf(${cid}, false)">Drucken</button>`
        : '';
    const items = historisch
        ? `${editItem}
           ${arbeitsvertrag}${vertragItems}${deleteItem}`
        : `${editItem}
           ${arbeitsvertrag}${vertragItems}${(typeof filialeUnterstehtLgav !== 'function' || filialeUnterstehtLgav(c.companyProfileId)) ? smsItems : ''}${deleteItem}`;
    const pillen = elternPill + vertragPill;
    return `${pillen ? `<span style="margin-left:auto;flex-shrink:0;display:inline-flex;align-items:center">${pillen}</span>` : ''}<div class="dok-menu-wrap ov-vmenu" style="${pillen ? '' : 'margin-left:auto;'}flex-shrink:0">
        <button type="button" class="dok-menu-btn" onclick="ctrToggleMenu(event, ${cid})" title="Aktionen" aria-label="Aktionen">⋮</button>
        <div class="dok-menu" id="ctrMenu-${cid}">${items}</div>
    </div>`;
}
function ctrToggleMenu(event, id) { rowMenuToggle(event, 'ctr', id); }

// Unterschrift Erziehungsberechtigte (Walter 23.09.2026): nötig, solange der MA
// unter 18 ist — bestätigt am Vertrag.
function istMinderjaehrig(dateOfBirthIso) {
    if (!dateOfBirthIso) return false;
    const d = new Date(String(dateOfBirthIso).slice(0, 10) + 'T00:00:00');
    d.setFullYear(d.getFullYear() + 18);
    return d > new Date();
}
async function vertragUnterschriftElternSetzen(employeeId, employmentId, wert, neuLaden = true) {
    const r = await fetch(`/api/employees/${employeeId}/employments/${employmentId}/unterschrift-eltern`, {
        method: 'PATCH', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify({ wert }) });
    if (!r.ok) { alert('Speichern fehlgeschlagen (' + r.status + ')'); return; }
    if (neuLaden && typeof selectEmployee === 'function') await selectEmployee(employeeId);
}
async function vertragElternFrage(employeeId, employmentId, vorname) {
    const ja = await liquidConfirm(`${vorname || 'Die Person'} ist noch nicht 18. Trägt der Vertrag die Unterschrift der/des Erziehungsberechtigten?`,
        { title: 'Unterschrift Erziehungsberechtigte', yesLabel: 'Ja, unterschrieben', noLabel: 'Nein / fehlt' });
    if (ja) await vertragUnterschriftElternSetzen(employeeId, employmentId, true, false);
}

// Vertrag aus dem MA-Detail löschen (Walter 23.09.2026). Gleicher Endpoint wie
// das Verträge-Modul (deleteContract in contracts-edit.js); der Server prüft,
// ob in DERSELBEN Filiale ein abgeschlossener Lohnbeleg daran hängt (409).
// Trotzdem löschen (force) darf nur der Administrator.
async function empContractDelete(employmentId, employeeId) {
    if (!(await liquidConfirm('Diesen Vertrag endgültig löschen?',
            { title: 'Vertrag löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' }))) return;
    const call = force => fetch(`/api/employments/${employmentId}` + (force ? '?force=true' : ''),
        { method: 'DELETE', headers: ah() });
    try {
        let res = await call(false);
        if (res.status === 409) {
            const body = await res.json().catch(() => ({}));
            if (currentUser?.role !== 'admin') {
                alert((body.message || 'Vertrag in abgeschlossener Lohnperiode verwendet.')
                    + '\n\nLöschen kann in diesem Fall nur der Administrator.');
                return;
            }
            if (!(await liquidConfirm((body.message || 'Vertrag in abgeschlossener Lohnperiode verwendet.')
                    + '\n\nDer Lohnbeleg bleibt bestehen. Trotzdem löschen?',
                    { title: 'Lohnbeleg vorhanden', yesLabel: 'Trotzdem löschen', noLabel: 'Abbrechen' }))) return;
            res = await call(true);
        }
        if (!res.ok) {
            const j = await res.json().catch(() => ({}));
            alert('Löschen fehlgeschlagen: ' + (j.message || j.error || ('HTTP ' + res.status)));
            return;
        }
        if (typeof invalidateEmployeeLookupCache === 'function') invalidateEmployeeLookupCache();
        if (typeof selectEmployee === 'function') await selectEmployee(employeeId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function openEmpContractPdf(contractId, printAfterOpen) {
    const filename = `Arbeitsvertrag_${contractId}.pdf`;
    const ok = await previewUrlFetch(`/api/contracts/employment/${contractId}/pdf`, filename, ah());
    // Unterzeichner-Umschalter in der Vorschau (Walter 23.08.2026) —
    // Funktion lebt global in contracts-edit.js.
    if (ok && typeof vtInjectSignerSelector === 'function') vtInjectSignerSelector(contractId);
    if (ok && printAfterOpen) {
        setTimeout(() => {
            if (typeof filePreviewPrint === 'function') filePreviewPrint();
        }, 450);
    }
}

// Klick-Handler der langSwitcher-Pill (Walter 17.07.2026, Bug-Fix v2):
// MA-Id robust aufloesen — window-Spiegel ODER Modul-Variable — und bei
// fehlender Auswahl eine klare Meldung statt stillem Nichtstun.
function lsEmpSyncClick() {
    const id = window.selectedEmployeeId
        || (typeof selectedEmployeeId !== 'undefined' ? selectedEmployeeId : null);
    if (!id) { alert('Bitte zuerst einen Mitarbeiter auswählen.'); return; }
    easyworkSyncSelectedEmployee(id);
}

async function easyworkSyncSelectedEmployee(empId) {
    if (!empId) return;
    // Spinner auf dem Button zeigen, der tatsaechlich existiert: der alte
    // Header-Button (btnEmpEasyworkSync) ist seit 17.07.2026 entfernt —
    // Schmaler Knopf unter «Dunkel» (#lsEmpSyncBtn) übernimmt die Anzeige.
    const btn = document.getElementById('btnEmpEasyworkSync')
        || document.getElementById('lsEmpSyncBtn');
    const oldHtml = btn?.innerHTML;
    // Laufender Sync sichtbar machen (Walter-Vorgabe 13.07.2026): drehender
    // Spinner im Button, solange die API-Calls laufen (kann einige Sekunden
    // dauern — Verträge, Properties, Verfügbarkeit).
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = btn.id === 'lsEmpSyncBtn'
            ? '<span class="eaw-spin"></span><span>…</span>'
            : '<span class="eaw-spin"></span> synchronisiere…';
    }
    // Ladekarte als Overlay (Walter 08.09.2026) — gleiche Optik wie beim Neuzugang.
    const _syncName = (typeof selectedEmployee !== 'undefined' && selectedEmployee && selectedEmployee.id === empId)
        ? `${selectedEmployee.firstName || ''} ${selectedEmployee.lastName || ''}`.trim() : '';
    ocLoadingOverlay(true, _syncName ? `Synchronisiere ${_syncName} mit easy@work` : 'Synchronisiere Mitarbeiter mit easy@work',
        'Stammdaten, Verträge, Lohn, Zivilstand und Verfügbarkeit werden aus easy@work geholt und abgeglichen. Das dauert einige Sekunden.');
    try {
        const res = await fetch(`/api/easywork/employees/cowork/${empId}/sync`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ companyProfileId: fixedCompanyProfileId || null })
        });
        const data = await res.json().catch(() => ({}));
        ocLoadingOverlay(false);
        if (!res.ok && data.error === 'PERSONALDATEN_BESTAETIGEN' && (data.befunde || []).length
            && typeof pdFrage === 'function') {
            if (!await pdFrage(data.befunde, _syncName)) return;
            for (const b of data.befunde) {
                const rb = await fetch(`/api/employees/${empId}/personaldaten-bestaetigen`, {
                    method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({ code: b.code, wert: b.wert || '' }),
                });
                if (!rb.ok) { alert('Bestätigung nicht gespeichert: ' + await pdFehlertext(rb)); return; }
            }
            setTimeout(() => easyworkSyncSelectedEmployee(empId), 0);
            return;
        }
        if (!res.ok || data.success === false) {
            const errors = data.errors && data.errors.length ? data.errors : [data.message || data.error || 'easy@work-Abgleich fehlgeschlagen.'];
            const notes = (data.notes && data.notes.length) ? '\n\nHinweise:\n' + data.notes.map(n => '• ' + n).join('\n') : '';
            alert('easy@work-Abgleich nicht möglich:\n\n' + errors.map(e => '• ' + e).join('\n') + notes);
            return;
        }

        // Nach Sync immer kanonisch neu laden (Walter-Bug 26.07.2026):
        // Früher: eigener GET + renderEmployeeDetail — konnte von einem noch
        // laufenden selectEmployee-Fetch mit ALTEN Verträgen überschrieben
        // werden (nur Gen-Bump + selectEmployee ist race-sicher). Ausserdem
        // aktualisiert selectEmployee Header/Übersicht/Verträge wie beim
        // manuellen MA-Wechsel (den Workaround «MA vor und zurück»).
        window._empSelectGen = (window._empSelectGen || 0) + 1;
        if (typeof selectEmployee === 'function')
            await selectEmployee(empId);
        if (selectedEmployee && selectedEmployee.id === empId) {
            const idx = allEmployees.findIndex(e => e.id === empId);
            if (idx >= 0) {
                allEmployees[idx] = {
                    ...allEmployees[idx],
                    ...selectedEmployee,
                    employments: [...(selectedEmployee.employments || [])]
                };
                renderEmployeeList(allEmployees);
            }
        }
        // Rückmeldung (Walter-Vorgabe 09.07.2026): Erfolg nur als kurzer,
        // nicht-blockierender Toast. Nur übersprungene Verträge (geschlossene
        // Lohnperiode) bleiben als alert — die muss man gesehen haben.
        const upd = data.updatedFields || [];
        const skipped = data.skippedContracts || [];
        if (skipped.length)
            // Neutrale Überschrift (Walter 10.07.2026): die Liste enthält nicht nur
            // Perioden-Sperren, sondern auch Strict-Fehler (fehlender Lohn-Tarif,
            // Überlappung …) — der konkrete Grund steht in jeder Zeile.
            alert('⚠ Diese Verträge wurden NICHT importiert:\n' + skipped.map(s => '• ' + s).join('\n'));
        else if (typeof showToast === 'function')
            showToast(upd.length ? 'Daten aktualisiert' : 'Keine Änderungen', 'success');

        // Adresswechsel aus easy@work (Walter 08.08.2026): sofort den
        // Umzugs-Dialog öffnen — Umzugsdatum bestätigen, QST folgt automatisch.
        if (upd.includes('Wohnort-Historie (Umzugsdatum bestätigen)')
            && typeof openUmzugModal === 'function') {
            showToast('Neue Adresse aus easy@work — bitte Umzugsdatum bestätigen.', 'success');
            openUmzugModal(empId);
        }

        // Schwangerschaft in easy@work gelöscht → in OneCrew nachfragen
        // (Walter-Vorgabe 27.07.2026). Kein Auto-Delete.
        const orphans = data.orphanedPregnancies || [];
        if (orphans.length) {
            const fmt = (iso) => {
                if (!iso) return '—';
                const s = String(iso).slice(0, 10);
                return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
            };
            const etList = orphans.map(p => fmt(p.errechneterTermin)).join(', ');
            const ja = typeof liquidConfirm === 'function'
                ? await liquidConfirm(
                    `Schwangerschaft in easy gelöscht.\n\nIn OneCrew löschen?\n(ET ${etList})`,
                    { title: 'Schwangerschaft in easy gelöscht', yesLabel: 'Ja, löschen', noLabel: 'Nein, behalten' })
                : confirm(`Schwangerschaft in easy gelöscht.\n\nIn OneCrew löschen?\n(ET ${etList})`);
            if (ja) {
                let okCount = 0;
                for (const p of orphans) {
                    const del = await fetch(`/api/pregnancies/${p.id}`, { method: 'DELETE', headers: ah() });
                    if (del.ok) okCount++;
                    else alert('Löschen fehlgeschlagen (Id ' + p.id + '): ' + (await del.text().catch(() => del.status)));
                }
                window._empSelectGen = (window._empSelectGen || 0) + 1;
                if (typeof selectEmployee === 'function') await selectEmployee(empId);
                if (typeof loadFamilieTab === 'function' && selectedEmployeeId === empId)
                    loadFamilieTab(empId);
                if (typeof showToast === 'function' && okCount)
                    showToast(okCount === 1 ? 'Schwangerschaft gelöscht' : `${okCount} Schwangerschaften gelöscht`, 'success');
            }
        }
    } catch (e) {
        alert('easy@work-Abgleich fehlgeschlagen: ' + (e?.message || e));
    } finally {
        ocLoadingOverlay(false);
        const btn2 = document.getElementById('btnEmpEasyworkSync')
            || document.getElementById('lsEmpSyncBtn');
        if (btn2) { btn2.disabled = false; if (oldHtml) btn2.innerHTML = oldHtml; }
    }
}

// ── Tab wechseln ───────────────────────────────
function switchEmpTab(tab) {
    if (selectedEmployee?._neu && tab && tab !== 'uebersicht' && tab !== 'personal') {
        if (typeof showToast === 'function') showToast('Zuerst die Person speichern.', 'info');
        return;
    }
    // Bank ist seit 15.07.2026 Teil von «Bewilligung QST Bank».
    if (tab === 'bank') tab = 'quellensteuer';
    // Zeiten-Kombi zurückgenommen (Walter 17.07.2026): falls noch jemand auf
    // dem Kurzzeit-Key «zeiten» hängt → Absenzen (war der Fokus-Tab).
    // Tab «KTG/UVG» entfernt 17.07.2026 → Absenzen (Tagessatz rechts).
    if (tab === 'zeiten' || tab === 'ktg') tab = 'absenzen';
    // Tab «Persönliche Angaben» entfernt 17.07.2026 → Übersicht.
    if (tab === 'personal') tab = 'uebersicht';
    activeEmpTab = tab;
    document.querySelectorAll('.emp-tab').forEach(t =>
        // Historie hat keinen eigenen Pill (Walter 20.08.2026) — die
        // Dokumente-Pill «Dokumente / Historie» bleibt aktiv markiert.
        t.classList.toggle('active', t.dataset.tab === tab
            || (tab === 'historie' && t.dataset.tab === 'dokumente')));
    document.querySelectorAll('.emp-tab-content').forEach(c =>
        c.classList.toggle('active', c.id === 'emp-tab-' + tab));
    // Übersicht / Dokumente / Stempelzeiten / Absenzen: Detail-Body ohne Scroll
    // (Walter 17./19.07.2026) — Maske fix; nur die jeweilige Liste scrollt.
    document.querySelectorAll('#page-mitarbeiter .emp-detail-body').forEach(b => {
        b.classList.toggle('ov-noscroll', tab === 'uebersicht');
        b.classList.toggle('dok-noscroll', tab === 'dokumente');
        b.classList.toggle('stempel-noscroll', tab === 'stempelzeiten');
        b.classList.toggle('abs-noscroll', tab === 'absenzen');
    });
    updateLsEmpSyncBtn();
    // Header-Actions (Inline-Speichern) — Übersicht speichert über ov-savebtn
    // in den Karten; andere Tabs haben eigene Edit-Buttons.
    const headerActions = document.getElementById('empHeaderActions');
    if (headerActions) {
        headerActions.style.display = 'none';
    }
    // Tab-spezifischer „+ Neu"-Button im Header (Walter 01.06.2026, Standard
    // wie Lohn-Tab: Aktionen oben sticky, nicht in der Liste unten).
    const tabBar = document.getElementById('empTabActionBar');
    if (tabBar) {
        const isExcluded = !!selectedEmployee?.isPayrollExcluded;
        const plusIcon = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>';
        if (tab === 'familie') {
            // Button lebt im Familie-Tab-Body (Leerzeile / Listen-Kopf),
            // nicht oben rechts neben den Stammdaten (Walter 27.07.2026).
            tabBar.innerHTML = '';
        } else if (tab === 'quellensteuer') {
            // Walter 29.08.2026: keine schwebenden Pillen mehr — «+ Neue
            // Bankverbindung» sitzt in der Bank-Sektions-Titelzeile, «+ Neue
            // QST-Version» in der QST-Sektions-Titelzeile (QST-Eintrag-Pille
            // war redundant und ist weg).
            tabBar.innerHTML = '';
        } else if (tab === 'verwarnungen' && !isExcluded) {
            // Restaurant Admin: Aktionen als Icon-Kacheln IM Tab-Body (Walter
            // 15.07.2026) — oben rechts kollidierten die Buttons mit dem
            // langSwitcher (reservierte Zone, CLAUDE.md).
            tabBar.innerHTML = '';
        } else if (tab === 'absenzen') {
            // «+ Absenz erfassen» sitzt über der Absenzen-Liste (Walter 06.10.2026).
            tabBar.innerHTML = '';
        } else if (tab === 'verfuegbarkeit' && !isExcluded) {
            tabBar.innerHTML = `<button class="btn-emp-add" onclick="verfNewForm()">${plusIcon} Neue Verfügbarkeit</button>`
                + `<button class="btn-emp-add" onclick="trOpenErfassen()">${plusIcon} Schulung erfassen</button>`;
        } else if (tab === 'dokumente') {
            // Walter-Vorgabe 09.06.2026: „Dokument hochladen" sitzt jetzt im Doku-
            // Body (rechts in der .dok-list-header-Zeile, auf Höhe des Kategorie-
            // Pfads) — nicht mehr oben rechts im Tab-Action-Bar, wo er mit dem
            // langSwitcher kollidierte.
            tabBar.innerHTML = '';
        } else {
            tabBar.innerHTML = '';
        }
    }
    if (tab === 'uebersicht'     && selectedEmployeeId) loadUebersichtTab();
    if (tab === 'familie'        && selectedEmployeeId) loadFamilieTab(selectedEmployeeId);
    if (tab === 'quellensteuer'  && selectedEmployeeId) {
        loadQuellensteuerTab(selectedEmployeeId);
        // Bankverwaltung gehoert zu «Bewilligung QST Bank» (Walter 15.07.2026).
        if (!selectedEmployee?.isPayrollExcluded) loadBankAccountsTab(selectedEmployeeId);
        if (!selectedEmployee?.isPayrollExcluded) loadWeitereAgTab(selectedEmployeeId);
    }
    if (tab === 'verwarnungen'   && selectedEmployeeId) loadVerwarnungenTab(selectedEmployeeId);
    if (tab === 'stempelzeiten'  && selectedEmployeeId) loadStempelzeitenTab(selectedEmployeeId);
    if (tab === 'absenzen'       && selectedEmployeeId) {
        loadAbsenzenTab(selectedEmployeeId);
        // Tagessatz rechts neben den Absenzen (Walter 17.07.2026).
        if (typeof loadKtgTab === 'function') loadKtgTab(selectedEmployeeId);
    }
    if (tab === 'verfuegbarkeit' && selectedEmployeeId && typeof loadVerfuegbarkeitTab === 'function') loadVerfuegbarkeitTab(selectedEmployeeId);
    if (tab === 'verfuegbarkeit' && selectedEmployeeId && typeof trLoad === 'function') trLoad(selectedEmployeeId);
    if (tab === 'verfuegbarkeit' && selectedEmployeeId && typeof loadStatistikMa === 'function') loadStatistikMa(selectedEmployeeId);
    if (tab === 'zulagen'        && selectedEmployeeId) {
        // Walter-Vorgabe 26.05.2026: BVG-Zusatz + Recurring + Lohnabtretungen
        // teilen sich den neuen „Zulagen & Abzüge"-Tab.
        // Lohnschema-Block (Walter 17.08.2026): Standard-Lohnblatt des
        // Vertragsmodells — Modell aus dem aktiven Vertrag (gleiche Wahl wie
        // das Header-Badge: jüngster aktiver Vertrag).
        if (typeof loadLohnschemaBlockForModel === 'function') {
            const _lsC = ((selectedEmployee?.employments) || []).filter(x => x.isActive)
                .sort((a, b) => String(b.contractStartDate || '').localeCompare(String(a.contractStartDate || '')))[0];
            loadLohnschemaBlockForModel(_lsC?.employmentModel || null);
        }
        if (typeof laPersonKarte === 'function') laPersonKarte(selectedEmployeeId);
        if (typeof loadUniformDepotTab === 'function') loadUniformDepotTab(selectedEmployeeId);
        if (typeof loadBvgZusatzTab === 'function') loadBvgZusatzTab(selectedEmployeeId);
        if (typeof vcLoad === 'function') vcLoad(selectedEmployeeId);
        if (typeof bpLoad === 'function') bpLoad(selectedEmployeeId);
        if (typeof loadDarlehenTab === 'function') loadDarlehenTab(selectedEmployeeId);
        loadRecurringWagesTab(selectedEmployeeId);
        loadLohnAssignmentsTab(selectedEmployeeId);
    }
    if (tab === 'dokumente'      && selectedEmployeeId) loadEmpDokumente(selectedEmployeeId);
    if (tab === 'historie'       && selectedEmployeeId) loadHistorieTab(selectedEmployeeId);

    // MA-Liste links bleibt jetzt auf ALLEN Sub-Tabs sichtbar (auch Dokumente).
    // Walter wollte konsistentes Verhalten zu Stempelzeiten — schnelles
    // Wechseln zwischen MA ohne Tab zu verlassen. Frühere Sonderbehandlung
    // mit emp-layout-dokumente entfernt; falls die Klasse von einer alten
    // Code-Stelle noch dranhängt, hier defensiv weg.
    const empLayout = document.querySelector('.emp-layout');
    if (empLayout) empLayout.classList.remove('emp-layout-dokumente');
}

// ══════════════════════════════════════════════
// HISTORIE-TAB (Walter 20.08.2026) — read-only Zeitachse
// aus vorhandenen Daten, gruppiert nach Jahr. Kein Pflegeaufwand.
// ══════════════════════════════════════════════
async function loadHistorieTab(employeeId) {
    const el = document.getElementById('historieContent');
    if (!el) return;
    try {
        const res = await fetch(`/api/employees/${employeeId}/historie`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder">Historie konnte nicht geladen werden.</div>'; return; }
        const events = await res.json();
        if (!Array.isArray(events) || !events.length) {
            el.innerHTML = '<div class="emp-placeholder">Noch keine Ereignisse vorhanden.</div>';
            return;
        }
        const typColor = t => ({
            eintritt:     '#166534',
            vertrag:      '#1d4ed8',
            vertrag_ende: '#6b7280',
            umzug:        '#b45309',
            umzug_kanton: '#b91c1c',
            qst:          '#7c3aed',
            permit:       '#0e7490',
            nummer:       '#6b7280',
            app:          '#166534',
            austritt:     '#b91c1c'
        }[t] || '#6b7280');
        let html = `<div style="display:flex;align-items:center;justify-content:space-between;gap:10px">
            <div class="emp-section-title" style="margin-top:0;margin-bottom:0">Zeitachse</div>
            <button onclick="switchEmpTab('dokumente')"
                    style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.25);color:#3f3f3f;border-radius:12px;padding:5px 14px;font-size:12.5px;font-weight:600;cursor:pointer">
                ← Dokumente
            </button>
        </div>
        <div style="font-size:12px;color:#8b8b8b;margin-bottom:12px">
            Automatisch aus Verträgen, Wohnort-Historie, QST-Versionen, Bewilligungen und Personalnummern
            zusammengestellt — nichts zu pflegen. Neueste Ereignisse zuoberst.
        </div>`;
        let jahr = null;
        html += '<div style="border-left:2px solid #d5d0c6;margin-left:8px;padding-left:0">';
        events.forEach(ev => {
            const d  = ev.datum ? new Date(ev.datum) : null;
            const j  = d ? d.getFullYear() : 'ohne Datum';
            if (j !== jahr) {
                jahr = j;
                html += `<div style="margin:14px 0 6px -8px;display:flex;align-items:center;gap:8px">
                    <span style="background:#3f3f3f;color:#fff;font-size:11px;font-weight:700;padding:2px 10px;border-radius:10px">${j}</span>
                </div>`;
            }
            const dStr = d ? d.toLocaleDateString('de-CH') : '–';
            const kritisch = ev.typ === 'umzug_kanton';
            html += `
            <div style="display:flex;gap:10px;align-items:flex-start;padding:5px 0 5px 0;margin-left:-5px">
                <span style="width:8px;height:8px;border-radius:50%;background:${typColor(ev.typ)};margin-top:5px;flex-shrink:0"></span>
                <span style="font-size:12px;color:#8b8b8b;width:78px;flex-shrink:0;margin-top:1px">${dStr}</span>
                <span style="font-size:13px;color:${kritisch ? '#b91c1c' : '#3f3f3f'};${kritisch ? 'font-weight:600;' : ''}line-height:1.4">${ev.icon || ''} ${esc(ev.text || '')}</span>
            </div>`;
        });
        html += '</div>';
        el.innerHTML = html;
    } catch {
        el.innerHTML = '<div class="emp-placeholder">Verbindungsfehler.</div>';
    }
}

