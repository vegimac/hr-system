// ══════════════════════════════════════════════
// MITARBEITER BEARBEITEN
// ══════════════════════════════════════════════

// Permit-Type Cache – ausschliesslich aus der Datenbank (permit_type Tabelle)
let _permitTypeCache = null;
async function getPermitTypes() {
    if (_permitTypeCache) return _permitTypeCache;
    try {
        const res = await fetch('/api/permittypes', { headers: ah() });
        if (res.ok) {
            _permitTypeCache = await res.json();
        } else {
            console.error('Permit-Types konnten nicht geladen werden:', res.status);
            _permitTypeCache = [];
        }
    } catch (e) {
        console.error('Fehler beim Laden der Permit-Types:', e);
        _permitTypeCache = [];
    }
    return _permitTypeCache;
}

// Nationalitäten-Cache (analog zu PermitTypes). Wird einmalig geladen und
// für den Nationalitäten-Dropdown im Edit-Formular wiederverwendet.
// ── Nationalitäts-Combobox (Walter 12.07.2026, v2) ──────────────────────
// Die native Select-Aufklappliste fängt alle Tasten selbst ab — «BGR»
// tippen landet auf Bhutan. Darum wird das Select durch ein SUCH-FELD mit
// eigener Liste ersetzt (Liquid-Glass-Konvention: native Selects ersetzen,
// Original-Select bleibt versteckt im DOM und trägt weiterhin den Wert —
// alle Speicher-Pfade lesen unverändert sel.value). Suche matcht Name,
// alpha-2 UND Ausweis-Kürzel alpha-3, als «enthält»-Suche.
function natMakeCombo(sel) {
    if (!sel || sel._natCombo || sel.disabled) return;   // easy@work-gesperrt → nativ lassen
    sel._natCombo = true;
    // Walter-Bug 20.08.2026 («zwei Felder»): der globale liquid-select-Enhancer
    // hat das Select evtl. schon in ein .lqsel-wrap (Button + Panel) umgebaut —
    // dann stünden Combo-Input UND Liquid-Button doppelt da. Liquid-Wrap
    // rückbauen; das Select wird gleich darunter zur unsichtbaren Datenquelle
    // des Combo-Controls (.no-liquid = erlaubte technische Ausnahme, verhindert
    // dass der MutationObserver es erneut umbaut).
    const lqWrap = sel.closest('.lqsel-wrap');
    if (lqWrap && lqWrap.parentNode) {
        lqWrap.parentNode.insertBefore(sel, lqWrap);
        lqWrap.remove();
        sel._lq = false;
        sel.style.display = '';
    }
    sel.classList.add('no-liquid');
    const readOpts = () => Array.from(sel.options).map(o => ({ value: o.value, label: (o.textContent || '').trim() }));
    let opts = readOpts();
    const wrap = document.createElement('div');
    wrap.style.cssText = 'position:relative';
    sel.parentNode.insertBefore(wrap, sel);
    wrap.appendChild(sel);
    sel.style.display = 'none';
    const inp = document.createElement('input');
    inp.type = 'text';
    inp.className = sel.className || 'ef-input';
    inp.placeholder = 'Name oder Kürzel, z.B. BGR…';
    inp.autocomplete = 'off';
    const list = document.createElement('div');
    list.style.cssText = 'position:absolute;top:100%;left:0;right:0;z-index:3000;background:#fff;border:1px solid #e2ddd3;border-radius:10px;box-shadow:0 12px 28px rgba(60,55,48,0.18);max-height:260px;overflow:auto;display:none;margin-top:4px';
    wrap.appendChild(inp);
    wrap.appendChild(list);
    const curLabel = () => {
        const c = opts.find(o => o.value === sel.value);
        return c && c.value !== '' ? c.label : '';
    };
    inp.value = curLabel();
    let items = [], hi = -1;
    const norm = s => (s || '').toUpperCase();
    function render(q) {
        const Q = norm(q);
        items = opts.filter(o => o.value !== '' && (!Q || norm(o.label).includes(Q)));
        // Walter 20.08.2026: Treffer, die mit der Eingabe BEGINNEN, zuerst
        // («bul» → Bulgarien vor Ländern, die «bul» nur enthalten).
        if (Q) items.sort((a, b) =>
            (norm(b.label).startsWith(Q) ? 1 : 0) - (norm(a.label).startsWith(Q) ? 1 : 0)
            || a.label.localeCompare(b.label));
        hi = items.length ? 0 : -1;
        list.innerHTML = items.slice(0, 300).map((o, i) =>
            `<div data-i="${i}" style="padding:7px 12px;font-size:13px;cursor:pointer;color:#3f3f3f;${i === hi ? 'background:#ece9e2' : ''}">${esc(o.label)}</div>`).join('')
            || '<div style="padding:8px 12px;color:#8b8b8b;font-size:12.5px">kein Treffer</div>';
        list.style.display = 'block';
    }
    function paint() {
        Array.from(list.children).forEach((el, i) => { el.style.background = i === hi ? '#ece9e2' : ''; });
        const el = list.children[hi];
        if (el && el.scrollIntoView) el.scrollIntoView({ block: 'nearest' });
    }
    function choose(i) {
        const o = items[i];
        if (!o) return;
        sel.value = o.value;
        sel.dispatchEvent(new Event('change'));
        inp.value = o.label;
        list.style.display = 'none';
    }
    inp.addEventListener('focus', () => { inp.select(); render(''); });
    inp.addEventListener('input', () => render(inp.value));
    inp.addEventListener('keydown', (e) => {
        if (list.style.display === 'none' && (e.key === 'ArrowDown' || e.key === 'Enter')) { render(inp.value); e.preventDefault(); return; }
        if (e.key === 'ArrowDown')      { hi = Math.min(hi + 1, items.length - 1); paint(); e.preventDefault(); }
        else if (e.key === 'ArrowUp')   { hi = Math.max(hi - 1, 0); paint(); e.preventDefault(); }
        else if (e.key === 'Enter')     { choose(hi); e.preventDefault(); }
        else if (e.key === 'Escape')    { list.style.display = 'none'; }
    });
    list.addEventListener('mousedown', (e) => {   // mousedown: feuert VOR blur
        const t = e.target.closest('[data-i]');
        if (t) { choose(parseInt(t.getAttribute('data-i'), 10)); e.preventDefault(); }
    });
    inp.addEventListener('blur', () => setTimeout(() => {
        list.style.display = 'none';
        inp.value = curLabel();   // Tipp-Rest ohne Auswahl → zurück auf den gewählten Stand
    }, 150));

    // KRITISCH (Walter-Bug 13.07.2026, Enisa/Shkozjan): das Familien-Modal
    // baut die Optionen bei JEDEM Öffnen neu (innerHTML) — die Combobox
    // zeigte dann noch den ALTEN Text («Schweiz»), obwohl sel.value leer
    // war → nationality_id wurde als NULL gespeichert. Options-Änderungen
    // beobachten und Optionsliste + Anzeige neu synchronisieren.
    new MutationObserver(() => {
        opts = readOpts();
        inp.value = curLabel();
        if (list.style.display !== 'none') render(inp.value);
    }).observe(sel, { childList: true, subtree: true });
    // Programmatisches Setzen von sel.value (z.B. Ausweis-OCR, 08.09.2026) →
    // Anzeige nachziehen.
    sel._natSync = () => { opts = readOpts(); inp.value = curLabel(); };
}

let _nationalityCache = null;
async function getNationalities() {
    if (_nationalityCache) return _nationalityCache;
    try {
        const res = await fetch('/api/nationalities', { headers: ah() });
        if (res.ok) {
            _nationalityCache = await res.json();
        } else {
            console.error('Nationalitäten konnten nicht geladen werden:', res.status);
            _nationalityCache = [];
        }
    } catch (e) {
        console.error('Fehler beim Laden der Nationalitäten:', e);
        _nationalityCache = [];
    }
    return _nationalityCache;
}

// Personal-Tab entfernt (Walter 17.07.2026) — Bearbeiten läuft über die
// Soft-Inputs in der Übersicht. Stub bleibt für Alt-Aufrufer.
async function startEmpEdit() {
    if (!selectedEmployee) return;
    switchEmpTab('uebersicht');
}

function buildEmpEditPersonal(emp, permitTypes = [], nationalities = []) {
    const permitOptions = permitTypes
        .filter(p => p.isActive !== false)
        .map(p => `<option value="${p.id}" ${emp.permitTypeId == p.id ? 'selected' : ""}>${p.description ?? p.code}</option>`)
        .join("");
    // Nationalitäten-Optionen — id, code, name. Die meisten User wollen nach
    // Land suchen, daher zeigen wir den Namen primär; Code sekundär falls
    // unterschiedlich (z.B. "Schweiz" vs CH).
    const nationalityOptions = (nationalities || [])
        .filter(n => n.isActive !== false)
        .map(n => {
            const id    = n.id   ?? n.Id;
            const code  = n.code ?? n.Code ?? '';
            const name  = n.name ?? n.Name ?? code;
            const sel   = (emp.nationalityId == id) ? 'selected' : '';
            // Ausweis-Kürzel (alpha-3) mit anzeigen (Walter 12.07.2026).
            const codes = code ? (n.code3 ? `${code} / ${n.code3}` : code) : '';
            return `<option value="${id}" ${sel}>${name}${codes && code !== name ? ' (' + codes + ')' : ''}</option>`;
        }).join('');
    const isMtp = emp.employmentModel === 'MTP';
    const isFix = emp.employmentModel === 'FIX' || emp.employmentModel === 'FIX-M';
    const ewTitle = 'Kommt aus easy@work und ist hier nicht editierbar.';
    const ewInput = `readonly data-easywork-locked="1" title="${ewTitle}"`;
    const ewSelect = `disabled data-easywork-locked="1" title="${ewTitle}"`;
    return `
    <div class="emp-section-title">${_t('ma.section.personalien','Personalien')}</div>
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-personal-main-line">
        ${eField(_t('ma.field.salutation','Anrede'), `<select id="ef-salutation" class="ef-input" ${ewSelect}>
            <option value="">–</option>
            <option value="Herr"   ${emp.salutation==='Herr'  ?'selected':''}>${_t('ma.value.salutation.herr','Herr')}</option>
            <option value="Frau"   ${emp.salutation==='Frau'  ?'selected':''}>${_t('ma.value.salutation.frau','Frau')}</option>
        </select>`)}
        ${eField(_t('ma.field.letterSalutation','Briefanrede'), `<input id="ef-letterSalutation" class="ef-input" value="${esc(emp.letterSalutation)}" placeholder="${_t('ma.placeholder.letterSalutation','z.B. Sehr geehrte Frau Muster')}">`)}
        ${eField(_t('ma.field.maidenName','Ledigname'), `<input id="ef-maidenName" class="ef-input" value="${esc(emp.maidenName)}">`)}
        ${eField(_t('ma.field.shortName','Kurzname'),   `<input id="ef-shortName"  class="ef-input" value="${esc(emp.shortName)}">`)}
        ${eField(_t('ma.field.gender','Geschlecht'), `<select id="ef-gender" class="ef-input" ${ewSelect}>
            <option value="">–</option>
            <option value="female" ${emp.gender==='female'?'selected':''}>${_t('ma.value.gender.female','Weiblich')}</option>
            <option value="male"   ${emp.gender==='male'  ?'selected':''}>${_t('ma.value.gender.male','Männlich')}</option>
            <option value="divers" ${emp.gender==='divers'?'selected':''}>${_t('ma.value.gender.divers','Divers')}</option>
        </select>`)}
    </div>

    <!-- Walter 26.05.2026: Adresse + Kontakt in die Personalien-Card. -->
    <!-- Auslands-Hauptwohnsitz (Walter 28.08.2026, präzisiert): die KOMPLETTE
         Adresse inkl. LAND kommt aus easy@work (country_key) — Land ist bei
         easy-geführten MA gesperrt wie die übrigen Adressfelder. Bei Land ≠
         CH: PLZ-Prüfung gelockert (bis 10 Zeichen), Kanton ausgeblendet —
         der QST-Kanton kommt dann von der Filiale (Arbeitskanton). -->
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-address-line">
        ${eField(_t('ma.field.street','Strasse'),       `<input id="ef-street"  class="ef-input" value="${esc(emp.street)}" ${ewInput}>`)}
        ${eField(_t('ma.field.zipCode','PLZ'),          `<input id="ef-zip" class="ef-input" value="${esc(emp.zipCode)}" inputmode="numeric" maxlength="10" ${ewInput}>`)}
        ${eField(_t('ma.field.city','Ort'),             `<input id="ef-city" class="ef-input" value="${esc(stripCityCantonSuffix(emp.city))}" ${ewInput}>`)}
        <span id="ef-canton-wrap" style="${efIstCHAdresse(emp) ? 'display:contents' : 'display:none'}">
        ${eField(_t('ma.field.canton','Kanton'),        renderKantonSelect('ef-canton', emp.cantonCode, ewSelect))}
        </span>
        ${eField(_t('ma.field.country','Land'),         maLandSelectHtml('ef-country', emp.country || 'CH', easyWorkLocked
            ? 'disabled data-easywork-locked="1" title="Kommt aus easy@work. Anzeige englisch, gespeichert wird der ISO-Code."'
            : 'title="Anzeige englisch in Grossbuchstaben (SWITZERLAND). Gespeichert wird der ISO-Code daneben."'))}
    </div>
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-personal-extra-line">
        ${eField(_t('ma.field.maritalStatus','Zivilstand'), `<select id="ef-zivilstand" class="ef-input" ${ewSelect}>
            ${zivOptionenHtml(emp.zivilstand ?? emp.maritalStatus)}
        </select>`)}
        ${eField(_t('ma.field.maritalSince','Zivilstand seit'), `<input id="ef-maritalStatusSince" class="ef-input" type="date" value="${toDateInput(emp.maritalStatusSince)}">`)}
        ${eField(_t('ma.field.religion','Konfession'), `<select id="ef-religion" class="ef-input">
            <option value="">–</option>
            <option value="evangelisch_reformiert" ${emp.religion==='evangelisch_reformiert'?'selected':''}>${_t('ma.value.religion.evangelisch_reformiert','Evang.-reformiert')}</option>
            <option value="roemisch_katholisch"    ${emp.religion==='roemisch_katholisch'   ?'selected':''}>${_t('ma.value.religion.roemisch_katholisch','Röm.-katholisch')}</option>
            <option value="christ_katholisch"      ${emp.religion==='christ_katholisch'     ?'selected':''}>${_t('ma.value.religion.christ_katholisch','Christ-katholisch')}</option>
            <option value="israelitisch"           ${emp.religion==='israelitisch'          ?'selected':''}>${_t('ma.value.religion.israelitisch','Israelitische Kultusgemeinde')}</option>
            <option value="andere"                 ${emp.religion==='andere'                ?'selected':''}>${_t('ma.value.religion.andere','Andere')}</option>
            <option value="keine"                  ${emp.religion==='keine'                 ?'selected':''}>${_t('ma.value.religion.keine','Keine')}</option>
        </select>`)}
        ${eField(_t('ma.field.nationality','Nationalität'), `<select id="ef-nationalityId" class="ef-input" ${ewSelect}>
            <option value="">–</option>
            ${nationalityOptions}
        </select>`)}
        ${eField(_t('ma.field.zemis','ZEMIS-Nr.'), `<input id="ef-zemisNumber" class="ef-input" value="${esc(emp.zemisNumber)}" placeholder="${_t('ma.placeholder.zemis','z.B. 12345678.9')}">`)}
    </div>
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-contact-line">
        ${eField(_t('ma.field.phone','Telefon'), `<input id="ef-phone" class="ef-input" type="tel" value="${esc(emp.phoneMobile)}" placeholder="${_t('ma.placeholder.phone','+41 79 409 43 33')}" ${ewInput}>`)}
        ${eField('Telefon 2', `<input id="ef-phone2" class="ef-input" type="tel" value="${esc(emp.phone2)}" placeholder="${_t('ma.placeholder.phone','+41 79 409 43 33')}" oninput="validatePhone(this)" onblur="validatePhoneBlur(this)">`)}
        ${eField(_t('ma.field.email','E-Mail'),  `<input id="ef-email" class="ef-input" type="email" value="${esc(emp.email)}" ${ewInput}>`)}
    </div>
    <div id="ef-plz-hint" style="font-size:12px;margin-top:-6px;margin-bottom:6px"></div>

    <div class="emp-section-title" style="margin-top:2px">${_t('ma.section.anstellung','Anstellung')}</div>
    <!-- Walter-Vorgabe 07.06.2026: 5 Anstellungs-Felder in EINER Zeile.
         Eintritt/Austritt/Aktiv links, die zwei Booleans (L-GAV / <8 h)
         rechts schmaler. -->
    <div class="emp-field-grid easywork-info-grid emp-flow-line emp-employment-line">
        ${eField(_t('ma.field.exitDate','Austrittsdatum'),
            `<input id="ef-exit"  class="ef-input" type="date" value="${toDateInput(emp.exitDate)}" ${ewInput}>`)}
        ${eField('Gekündigt am',
            `<input id="ef-kuendAm" class="ef-input" type="date" value="${toDateInput(emp.kuendigungAusgesprochenAm)}">`,
            'Vom Kündigungsschreiben gesetzt')}
        ${eField('Kündigung per',
            `<input id="ef-kuendPer" class="ef-input" type="date" value="${toDateInput(emp.kuendigungPer)}">`,
            'Letzter Arbeitstag gemäss Kündigung')}
        ${eField('Kündigung durch',
            `<select id="ef-kuendDurch" class="ef-input">
                <option value="">—</option>
                <option value="AG"${(emp.kuendigungDurch || '').toUpperCase() === 'AG' ? ' selected' : ''}>durch uns</option>
                <option value="AN"${(emp.kuendigungDurch || '').toUpperCase() === 'AN' ? ' selected' : ''}>durch Mitarbeiter</option>
                <option value="GG"${(emp.kuendigungDurch || '').toUpperCase() === 'GG' ? ' selected' : ''}>gegenseitig</option>
             </select>`,
            'Wer die Kündigung ausgesprochen hat')}
        ${eField('Austrittsgrund',
            `<select id="ef-austrittsgrund" class="ef-input">${_austrittsgrundOptionsHtml(emp.austrittsgrund)}</select>`,
            'Für Statistik')}
        ${empSiehtLgav(emp)
            ? eField('L-GAV',
            `<label style="display:flex;align-items:center;gap:8px;height:19px;cursor:pointer">
                 <input id="ef-lgavPflichtig" type="checkbox" ${emp.lgavPflichtig ? 'checked' : ''}
                        style="width:16px;height:16px;cursor:pointer;margin:0">
                 <span style="font-size:12px;color:#475569">pflichtig</span>
             </label>`,
            _t('ma.field.lgavPflichtigHint','Jährlicher Abzug im Lohnlauf'))
            : ''}
    </div>
    <div style="margin:6px 0 0;font-size:11px;color:#94a3b8">Nachtarbeit-Untersuchung (Ausstellungsdatum + Dokument) wird in der Ansicht direkt erfasst.</div>
    <div style="margin:4px 0 16px;font-size:11.5px;color:#64748b;line-height:1.45">
        ${_t('ma.entryDate.hint','Eintrittsdatum wird benötigt für: Sperrfrist-Berechnung (Art. 336c OR), Karenzjahr-Berechnung (Krank/Unfall), Ferien-Kürzung (Art. 329b OR), Dienstjubiläen.')}
    </div>

    <!-- Versteckte Felder für Backwards-Compat: Edit-Save sendet sie immer noch,
         aber sie sind read-only und werden vom History-Sync ohnehin überschrieben.
         Bei „MA ohne Lohn" (IsPayrollExcluded) ist die Aufenthalts-Sektion
         ausgeblendet — die Hidden-Inputs bleiben aber bestehen, damit der
         Save-Handler den DB-Wert nicht versehentlich auf 0/null setzt. -->
    <input type="hidden" id="ef-permitType" value="${emp.permitTypeId ?? 0}">
    <input type="hidden" id="ef-permitExpiry" value="${toDateInput(emp.permitExpiryDate)}">

    ${emp.isPayrollExcluded ? `
    <div style="margin:14px 0;padding:12px 16px;background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;color:#92400e;font-size:13px;line-height:1.55">
        <strong>⛔ ${_t('ma.phantom.title','MA ohne Lohn')}</strong> — ${_t('ma.phantom.editDesc','Phantom-MA für easy@work-Zugang. Bewilligung und Zusatzadressen werden hier nicht angeboten, da dieser MA keinen Vertrag und keine Lohnzahlung hat. Über die Checkbox „Kein Lohn" unten kann die Markierung wieder aufgehoben werden.')}
    </div>
    ` : ''}

    <!-- Arbeitsverhältnis-Sektion (Eintritt, Modell, Pensum, Stundenlohn) wurde
         entfernt — wird im Verträge-Tab gepflegt. Eintrittsdatum + Personal-Nr.
         stehen ohnehin im MA-Detail-Header. -->

    ${currentUser?.role === 'admin' ? `
    <div class="emp-section-title">${_t('ma.section.lohn','Lohn')}</div>
    <div style="padding:10px 14px;background:${emp.isPayrollExcluded ? '#fef3c7' : '#f8fafc'};border:1px solid ${emp.isPayrollExcluded ? '#fde68a' : '#e2e8f0'};border-radius:8px">
        <label style="display:flex;align-items:flex-start;gap:10px;cursor:pointer">
            <input type="checkbox" id="ef-isPayrollExcluded"
                   ${emp.isPayrollExcluded ? 'checked' : ''}
                   style="margin-top:3px;width:16px;height:16px;flex-shrink:0">
            <div style="font-size:13px;color:#0f172a">
                <b>${_t('ma.payrollExcluded.title','Kein Lohn')}</b> <span style="color:#94a3b8;font-weight:400;font-size:11.5px">${_t('ma.payrollExcluded.role','(nur Admin)')}</span>
                <div style="font-size:11.5px;color:#64748b;margin-top:3px;line-height:1.45">
                    ${_t('ma.payrollExcluded.desc','MA wird im System geführt (Stempelsystem, Vorgesetzter-Referenz, Posteingang) — aber NICHT im Lohn-Tab abgerechnet. Beim CSV-Re-Import bleibt diese Markierung erhalten.')}
                </div>
            </div>
        </label>
    </div>` : ''}

    ${!emp.isPayrollExcluded ? `
    <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:2px">
        <span>${_t('ma.section.otherAddresses','Weitere Adressen')} <span style="font-weight:400;color:#94a3b8;font-size:12px">${_t('ma.section.otherAddrHint','(z.B. Wochenaufenthalt, Korrespondenz, Sozialamt — Hauptadresse oben)')}</span></span>
        <button type="button" class="btn-emp-add" onclick="openEmployeeAddressModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            ${_t('ma.btn.addAddress','Adresse hinzufügen')}
        </button>
    </div>
    <div id="otherAddressesContent">
        <div class="emp-placeholder"><span>${_t('ma.loading','Wird geladen…')}</span></div>
    </div>
    ` : ''}`;
    // Hinweis: Bankverbindung + Postfach-Zugang sind NICHT mehr im Edit-Formular —
    // sie leben im eigenen Tab „Bank & Postfach" (Walter-Vorgabe 14.05.2026)
    // und werden dort über eigene Modals/Buttons gepflegt, nicht über das
    // Personal-Edit-Formular.
}

// (buildEmpEditAdressen wurde entfernt — Adresse + Kontakt sind jetzt
// im Personal-Edit-Formular integriert.)

// Hilfshelfer: Edit-Feld
// Auslands-Hauptwohnsitz (Walter 28.08.2026): Helfer für die Adress-Felder.
// «CH-Adresse» = Land leer, CH oder Schweiz (Alt-Daten-tolerant).
function efIstCHAdresse(empOrLand) {
    const land = (typeof empOrLand === 'string' ? empOrLand : (empOrLand?.country ?? 'CH')) || 'CH';
    const l = land.trim().toUpperCase();
    return l === '' || l === 'CH' || l === 'CHE' || l === 'SCHWEIZ' || l === 'SWITZERLAND'
        || l === 'SUISSE' || l === 'SVIZZERA';
}

// Live-Umschaltung beim Tippen im Land-Feld: CH → PLZ 4-stellig numerisch +
// Kanton sichtbar; Ausland → PLZ bis 10 Zeichen frei + Kanton ausgeblendet
// (QST-Kanton kommt dann von der Filiale, ApplyWohnadresseAsync).
function efLandChanged() {
    const istCH = efIstCHAdresse(document.getElementById('ef-country')?.value ?? 'CH');
    const zip = document.getElementById('ef-zip');
    if (zip) {
        if (istCH) { zip.maxLength = 4;  zip.setAttribute('inputmode', 'numeric'); }
        else       { zip.maxLength = 10; zip.removeAttribute('inputmode'); }
    }
    const kt = document.getElementById('ef-canton-wrap');
    if (kt) kt.style.display = istCH ? 'contents' : 'none';
}

function eField(label, inputHtml, hint) {
    return `<div class="emp-field liquid-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value">${inputHtml}</div>
        ${hint ? `<div class="emp-field-hint">${hint}</div>` : ''}
    </div>`;
}

// (schwache esc()-Dublette entfernt 22.07.2026 — die vollständige Definition weiter unten gilt)

// Schweizer Kantone — 2-Zeichen-Codes mit deutschem Namen, alphabetisch.
const SWISS_KANTONE = [
    ['AG', 'Aargau'],           ['AI', 'Appenzell Innerrhoden'],
    ['AR', 'Appenzell Ausserrhoden'], ['BE', 'Bern'],
    ['BL', 'Basel-Landschaft'], ['BS', 'Basel-Stadt'],
    ['FR', 'Freiburg'],         ['GE', 'Genf'],
    ['GL', 'Glarus'],           ['GR', 'Graubünden'],
    ['JU', 'Jura'],             ['LU', 'Luzern'],
    ['NE', 'Neuenburg'],        ['NW', 'Nidwalden'],
    ['OW', 'Obwalden'],         ['SG', 'St. Gallen'],
    ['SH', 'Schaffhausen'],     ['SO', 'Solothurn'],
    ['SZ', 'Schwyz'],           ['TG', 'Thurgau'],
    ['TI', 'Tessin'],           ['UR', 'Uri'],
    ['VD', 'Waadt'],            ['VS', 'Wallis'],
    ['ZG', 'Zug'],              ['ZH', 'Zürich']
];

function renderKantonSelect(id, current, extraAttrs = '') {
    const cur = (current ?? '').toString().toUpperCase();
    const opts = SWISS_KANTONE
        .map(([code, name]) => `<option value="${code}" ${code === cur ? 'selected' : ''}>${code} — ${name}</option>`)
        .join('');
    return `<select id="${id}" class="ef-input" ${extraAttrs}>
        <option value="" ${!cur ? 'selected' : ''}>— nicht gepflegt —</option>
        <option value="EX" ${cur === 'EX' ? 'selected' : ''}>EX — Ausland</option>
        ${opts}
    </select>`;
}

function kantonNameFor(code) {
    if (!code) return null;
    const found = SWISS_KANTONE.find(([c]) => c === code.toUpperCase());
    return found ? found[1] : null;
}

// Lookup für PLZ → Gemeinde(n) + Kanton.
// Wird aus dem Edit-Form heraus aufgerufen, wenn der User die PLZ
// tippt oder das Feld verlässt. Befüllt die Ort- und Kanton-Felder
// automatisch. Bei mehreren Gemeinden pro PLZ erscheint eine Auswahl
// im Ort-Dropdown, der Kanton wird an die erste Gemeinde angepasst
// und aktualisiert sich live, wenn der User eine andere Gemeinde wählt.
let _plzLookupAbort = null;
let _plzLookupCache = new Map();

function plzSuchbar(plz) { return /^\d{3,10}$/.test(plz || ''); }
function plzLandVon(l) { return ((l && (l.land || (l.quelle === 'grenz' ? '' : 'CH'))) || 'CH').toUpperCase(); }
function plzIstGrenz(l) { return plzLandVon(l) !== 'CH' || (l && l.quelle === 'grenz'); }

function plzTrefferAnwenden(l, cityEl, cantonEl, bfsEl, landEl) {
    if (!l) return '';
    const land = plzLandVon(l);
    const ortName = (typeof stripCityCantonSuffix === 'function'
        ? stripCityCantonSuffix(l.ortschaftsname || l.gemeindename)
        : (l.ortschaftsname || l.gemeindename)) || '';
    if (cityEl) cityEl.value = ortName;
    if (landEl) {
        landEl.value = land;
        try { landEl.dispatchEvent(new Event('change', { bubbles: true })); } catch (_) {}
    }
    if (plzIstGrenz(l)) {
        if (cantonEl) cantonEl.value = 'EX';
        if (bfsEl) bfsEl.value = '';
        if (landEl && landEl.id === 'ov-land' && typeof ovLandGeaendert === 'function') ovLandGeaendert();
    } else {
        if (cantonEl && l.kantonskuerzel) cantonEl.value = l.kantonskuerzel;
        if (bfsEl && l.bfsNr) bfsEl.value = l.bfsNr;
        if (landEl && landEl.id === 'ov-land' && typeof ovLandGeaendert === 'function') ovLandGeaendert();
    }
    return ortName;
}

function plzTrefferHinweis(l) {
    const ort = (typeof stripCityCantonSuffix === 'function'
        ? stripCityCantonSuffix(l.ortschaftsname || l.gemeindename)
        : (l.ortschaftsname || l.gemeindename)) || '';
    if (!plzIstGrenz(l)) return ort;
    const region = l.region || '';
    return ort + (region ? ' · ' + region : '') + (l.land ? ' · ' + l.land : '');
}

async function plzLookup(rawPlz) {
    const plz = (rawPlz ?? '').toString().trim();
    const cityInput   = document.getElementById('ef-city');
    const kantonSelect = document.getElementById('ef-canton');
    const hint = document.getElementById('ef-plz-hint');
    if (!cityInput || !kantonSelect || !hint) return;

    if (!plzSuchbar(plz)) {
        hint.innerHTML = '';
        return;
    }

    // Vorherigen Request abbrechen (z.B. wenn User schnell tippt)
    if (_plzLookupAbort) _plzLookupAbort.abort();
    _plzLookupAbort = new AbortController();

    let locs = _plzLookupCache.get(plz);
    if (!locs) {
        try {
            const res = await fetch(`/api/swiss-locations/by-plz?plz=${encodeURIComponent(plz)}`, {
                headers: ah(),
                signal: _plzLookupAbort.signal
            });
            if (!res.ok) return;
            locs = await res.json();
            _plzLookupCache.set(plz, locs);
        } catch (e) {
            if (e.name === 'AbortError') return;
            return;
        }
    }

    if (!locs.length) {
        hint.innerHTML = `<span style="color:#b45309">⚠ PLZ ${plz} weder in den Schweizer Ortschaften noch in der Grenzregion — bitte von Hand eintragen.</span>`;
        return;
    }

    if (locs.length === 1) {
        const l = locs[0];
        const ortName = plzTrefferAnwenden(l, cityInput, kantonSelect, null, document.getElementById('ef-country'));
        hint.innerHTML = `<span style="color:#16a34a">✓ ${esc(plzTrefferHinweis(l) || ortName)}</span>`;
        return;
    }

    // Mehrere Gemeinden → Combobox im Ort-Feld via HTML5-<datalist>.
    // Walter klickt im Ort-Feld auf das Pfeil-Symbol und sieht die Liste,
    // kann aber jederzeit auch frei tippen (und "off-list"-Werte eingeben).
    // Wenn der getippte/gewählte Wert zu einer Gemeinde matcht, wird der
    // Kanton automatisch synchronisiert.
    bindDatalistToCityInput(cityInput, kantonSelect, null, locs, plz, hint, document.getElementById('ef-country'));
}

// Hilfsfunktion: hängt eine <datalist> ans Ort-Input und installiert einen
// input-Handler, der Kanton (und ggf. BFS-Nr.) bei einem Treffer aktualisiert.
function bindDatalistToCityInput(cityEl, cantonEl, bfsEl, locs, plz, hint, landEl) {
    if (!cityEl) return;
    const dlId = (cityEl.id || 'plz') + '-datalist';

    // Bestehende datalist (von früherer PLZ-Eingabe) entfernen
    const old = document.getElementById(dlId);
    if (old) old.remove();

    const dl = document.createElement('datalist');
    dl.id = dlId;
    const locName = l => stripCityCantonSuffix(l.ortschaftsname || l.gemeindename);
    dl.innerHTML = locs.map(l => {
        const bfs = l.bfsNr ?? l.bfsNumber ?? l.bfs_number ?? '';
        return `<option value="${esc(locName(l))}" data-kanton="${l.kantonskuerzel}" data-bfs="${bfs}"></option>`;
    }).join('');
    cityEl.setAttribute('list', dlId);
    cityEl.parentElement?.appendChild(dl);

    // Wenn aktueller Wert schon einer der Treffer ist → Kanton/BFS sofort syncen
    const pre = locs.find(l => locName(l) === cityEl.value || l.gemeindename === cityEl.value);
    if (pre) plzTrefferAnwenden(pre, cityEl, cantonEl, bfsEl, landEl);

    cityEl.oninput = () => {
        const match = locs.find(l => locName(l) === cityEl.value || l.gemeindename === cityEl.value);
        if (match) plzTrefferAnwenden(match, cityEl, cantonEl, bfsEl, landEl);
    };

    if (hint) {
        hint.innerHTML = pre
            ? `<span style="color:#16a34a">✓ ${esc(locName(pre))}</span>
               <span style="color:#94a3b8;font-size:11.5px;margin-left:6px">— ${locs.length} Gemeinden für PLZ ${plz}; Ort-Feld öffnen für andere Auswahl.</span>`
            : `<span style="color:#475569">PLZ ${plz} → ${locs.length} Gemeinden — im Ort-Feld auswählen oder frei tippen.</span>`;
    }
}

function cancelEmpEdit() {
    if (selectedEmployee) renderEmployeeDetail(selectedEmployee);
}

// Walter-Vorgabe 18.05.2026: Aktiv-Checkbox prüft beim Entfernen des Hakens
// SOFORT (nicht erst beim Speichern) ob es offene Lohnperioden mit Daten gibt.
// Bei Sperre: Checkbox zurücksetzen + Klartext-Hinweis mit Grund (welche
// Periode, Stempelzeiten und/oder Absenz-Typen mit Datumsbereich).
async function onIsActiveChange(cb, empId) {
    // Label live nachziehen — gleicher visueller Effekt wie vorher
    const lbl = document.getElementById('ef-isactive-label');
    if (lbl) lbl.textContent = cb.checked ? 'aktiv' : 'inaktiv';

    // Aktivieren ist immer erlaubt — Lock greift nur beim Inaktivsetzen.
    if (cb.checked) return;

    try {
        const res = await fetch(`/api/employees/${empId}/deactivate-check`,
                                { headers: ah(), cache: 'no-store' });
        if (!res.ok) {
            console.warn('deactivate-check HTTP', res.status);
            return;
        }
        const data = await res.json();
        if (data.canDeactivate) return;

        // Detail-Meldung aus blockers zusammenbauen — eine Karte je Periode,
        // mit Stempelzeit-Count und Absenz-Zeilen (Typ + Datumsbereich).
        const fmt = d => d ? d.substring(8,10) + '.' + d.substring(5,7) + '.' + d.substring(0,4) : '';
        const lines = (data.blockers || []).map(b => {
            const parts = [];
            if (b.timeEntriesCount > 0)
                parts.push(`• ${b.timeEntriesCount} Stempel-Eintrag(e)`);
            (b.absences || []).forEach(a => {
                const typLabel = ({KRANK:'Krankheit',UNFALL:'Unfall',FERIEN:'Ferien',
                                   SCHULUNG:'Schulung',MUTT_VATER:'Mutter-/Vaterschaft'})[a.type] || a.type;
                parts.push(`• ${typLabel} ${fmt(a.dateFrom)} – ${fmt(a.dateTo)}`);
            });
            return `Lohnperiode '${b.periodLabel}' (${fmt(b.periodFrom)} – ${fmt(b.periodTo)}):\n${parts.join('\n')}`;
        }).join('\n\n');

        alert(`MA kann nicht inaktiv gesetzt werden.\n\n${lines}\n\nBitte zuerst den Lohnlauf abschliessen oder die blockierenden Daten löschen.`);
        // Checkbox zurück auf "aktiv" — der MA bleibt im Form-State aktiv
        cb.checked = true;
        if (lbl) lbl.textContent = 'aktiv';
    } catch (e) {
        console.error('deactivate-check', e);
        // Bei Netzwerkfehler nicht blockieren — der Save-Endpoint wirft sonst
        // sowieso noch die 409 wenn Daten im Weg sind.
    }
}

async function saveEmpEdit() {
    if (!selectedEmployeeId || !selectedEmployee) return;
    const emp = selectedEmployee;
    if (emp._neu) { await neuerMaSpeichern(); return; }
    const easyWorkLocked = true;

    // AHV-Nr-Prüfziffer-Check: blockt Save wenn AHV-Nr eingegeben aber ungültig.
    const ahvInput = document.getElementById('ef-ahvNummer');
    if (!easyWorkLocked && ahvInput && ahvInput.value.trim()) {
        const ahvCheck = validateAhvNummer(ahvInput.value);
        if (ahvCheck.valid === false) {
            alert('AHV-Nr. ist nicht gültig:\n' + ahvCheck.error
                + '\n\nBitte korrigieren oder Feld leer lassen.');
            ahvInput.focus();
            return;
        }
        if (ahvCheck.valid === true) {
            // In normalisiertem Format speichern (756.XXXX.XXXX.XX)
            ahvInput.value = ahvCheck.normalized;
        }
    }

    // ── Employee Stammdaten ──────────────────────────────────────────────
    // Walter-Vorgabe 01.06.2026: harte Validierung Telefon/E-Mail/PLZ vor dem Save.
    const _phoneRaw = easyWorkLocked ? (emp.phoneMobile || '') : (document.getElementById('ef-phone')?.value || '');
    const _phoneFmt = _phoneRaw ? (easyWorkLocked ? _phoneRaw : window.formatPhoneIntl(_phoneRaw)) : '';
    if (!easyWorkLocked && _phoneRaw && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(_phoneFmt)) {
        alert('Telefon-Format ungültig (erwartet +99 99 999 99 99, z.B. +41 79 409 43 33).');
        return;
    }
    if (!easyWorkLocked && _phoneFmt) document.getElementById('ef-phone').value = _phoneFmt;
    const _emailRaw = easyWorkLocked ? (emp.email || '') : (document.getElementById('ef-email')?.value || '').trim();
    if (!easyWorkLocked && _emailRaw && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(_emailRaw)) {
        alert('E-Mail-Adresse ist ungültig.');
        return;
    }
    // Land wie die ganze Adresse easy@work-geführt (Walter 28.08.2026):
    // bei Lock kommt der Wert aus emp, sonst aus dem Feld. PLZ-Prüfung
    // hängt am Land (Ausland = gelockert).
    const _landEl  = document.getElementById('ef-country');
    const _landVal = (easyWorkLocked
        ? (emp.country || 'CH')
        : ((_landEl ? _landEl.value : (emp.country || 'CH')) || 'CH')).trim();
    const _istCHAdresse = efIstCHAdresse(_landVal);
    const _zipRaw = easyWorkLocked ? (emp.zipCode || '') : (document.getElementById('ef-zip')?.value || '').trim();
    if (!easyWorkLocked && _zipRaw && _istCHAdresse && !/^\d{4}$/.test(_zipRaw)) {
        alert('PLZ muss 4-stellig numerisch sein.');
        return;
    }
    if (!easyWorkLocked && _zipRaw && !_istCHAdresse && !/^[A-Za-z0-9 \-]{2,10}$/.test(_zipRaw)) {
        alert('PLZ (Ausland) ungültig — 2 bis 10 Zeichen (Buchstaben/Ziffern).');
        return;
    }
    // Übersicht (ov-*) ist die aktive Edit-Quelle; ef-* nur noch Legacy
    // (buildEmpEditPersonal, nicht mehr verdrahtet).
    const formVal = (efId, ovId) => {
        const ef = document.getElementById(efId);
        if (ef) return ef.value;
        const ov = ovId ? document.getElementById(ovId) : null;
        return ov ? ov.value : '';
    };
    const formEl = (efId, ovId) => document.getElementById(efId) || (ovId ? document.getElementById(ovId) : null);

    // Zivilstand-Wechsel aus der Übersicht: neuer Wert braucht ein Gültig ab («Zivilstand seit»).
    const zivSel = document.getElementById('ov-zivilstand');
    const zivAlt = (emp.zivilstand ?? emp.maritalStatus) || null;
    const zivNeu = zivSel ? (zivSel.value || null) : zivAlt;
    const zivGewechselt = (zivNeu || '') !== (zivAlt || '');
    // Leer gibt es nicht mehr (Walter 02.11.2026): ohne Zivilstand kein Speichern.
    if (zivSel && !zivSel.value) {
        alert(typeof ZIV_FEHLT_TEXT !== 'undefined' ? ZIV_FEHLT_TEXT : 'Zivilstand fehlt. Ist er nicht bekannt, bitte «unbekannt» wählen.');
        zivSel.focus();
        return;
    }
    if (zivGewechselt && zivNeu && !formVal('ef-maritalStatusSince', 'ov-maritalStatusSince')) {
        alert('Neuer Zivilstand: bitte «Zivilstand seit» (gültig ab) eintragen.');
        document.getElementById('ov-maritalStatusSince')?.focus();
        return;
    }

    const _phone2Raw = formVal('ef-phone2', 'ov-phone2');
    const _phone2Fmt = _phone2Raw ? window.formatPhoneIntl(_phone2Raw) : '';
    if (_phone2Raw && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(_phone2Fmt)) {
        alert('Telefon 2-Format ungültig (erwartet +99 99 999 99 99, z.B. +41 79 409 43 33).');
        return;
    }
    const phone2El = formEl('ef-phone2', 'ov-phone2');
    if (_phone2Fmt && phone2El) phone2El.value = _phone2Fmt;

    const exitVal = easyWorkLocked ? toDateInput(emp.exitDate) : document.getElementById('ef-exit')?.value;
    const isActiveInput = document.getElementById('ef-isactive');
    const boolVal = (efId, ovId, fallback) => {
        const el = formEl(efId, ovId);
        if (!el) return !!fallback;
        if (el.type === 'checkbox') return el.checked === true;
        return el.value === 'true';
    };
    const ahvFeld = document.getElementById('ov-ahv');
    const ahvUnb = document.getElementById('ov-ahv-unbekannt');
    let ahvWert = emp.socialSecurityNumber || null;
    if (ahvFeld) {
        if (ahvUnb && ahvUnb.checked) ahvWert = 'unbekannt';
        else {
            const chk = validateAhvNummer(ahvFeld.value);
            if (chk.valid === false) {
                alert('AHV-Nummer ist ungültig:\n' + chk.error + '\n\nGültig zum Beispiel 756.7865.5342.46. Ungültig zum Beispiel 756.7865.4342.46.');
                ahvFeld.focus();
                return;
            }
            ahvWert = chk.valid === true ? chk.normalized : '';
        }
    }
    const dobFeld = document.getElementById('ov-dob');
    if (dobFeld && dobFeld.value) {
        const heute = new Date(); heute.setHours(0, 0, 0, 0);
        if (new Date(dobFeld.value + 'T00:00:00') > heute) {
            alert('Geburtsdatum darf nicht nach dem heutigen Tag liegen.');
            dobFeld.focus();
            return;
        }
    }
    const mobilFeld = document.getElementById('ov-mobile');
    if (mobilFeld && mobilFeld.value.trim()) {
        const fmt = window.formatPhoneIntl ? window.formatPhoneIntl(mobilFeld.value.trim()) : mobilFeld.value.trim();
        if (!/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(fmt)) {
            alert('Mobilnummer ungültig (erwartet +41 79 409 43 33).');
            mobilFeld.focus();
            return;
        }
        mobilFeld.value = fmt;
    }
    const ovZipEl = document.getElementById('ov-zip');
    const ovLandEl = document.getElementById('ov-land');
    if (ovZipEl) {
        const land = ((ovLandEl?.value || 'CH').trim().toUpperCase());
        const zip = ovZipEl.value.trim();
        const ch = !land || land === 'CH' || land === 'SCHWEIZ' || land === 'SWITZERLAND';
        if (ch && zip && !/^\d{4}$/.test(zip)) {
            alert('PLZ muss 4-stellig numerisch sein.');
            ovZipEl.focus();
            return;
        }
    }

    const permitTypeEl = document.getElementById('ef-permitType');
    const permitExpiryEl = document.getElementById('ef-permitExpiry');
    const zemisEl = formEl('ef-zemisNumber', 'ov-zemisNumber');
    const placeOfOriginEl = document.getElementById('ef-placeOfOrigin');
    const empPayload = {
        firstName:    document.getElementById('ov-firstName') ? (document.getElementById('ov-firstName').value.trim() || null) : (easyWorkLocked ? (emp.firstName || null) : (document.getElementById('ef-firstName')?.value || null)),
        lastName:     document.getElementById('ov-lastName') ? (document.getElementById('ov-lastName').value.trim() || null) : (easyWorkLocked ? (emp.lastName || null) : (document.getElementById('ef-lastName')?.value || null)),
        salutation:   easyWorkLocked ? (emp.salutation || null) : (document.getElementById('ef-salutation')?.value || null),
        gender:       document.getElementById('ov-gender') ? (document.getElementById('ov-gender').value || null) : (easyWorkLocked ? (emp.gender || null) : (document.getElementById('ef-gender')?.value || null)),
        dateOfBirth:  document.getElementById('ov-dob') ? (document.getElementById('ov-dob').value || null) : (easyWorkLocked ? (toDateInput(emp.dateOfBirth) || null) : (document.getElementById('ef-dob')?.value || null)),
        languageCode: document.getElementById('ov-lang') ? (document.getElementById('ov-lang').value || null) : (easyWorkLocked ? (emp.languageCode || null) : (document.getElementById('ef-lang')?.value || null)),
        phoneMobile:  document.getElementById('ov-mobile') ? ((document.getElementById('ov-mobile').value || '').trim()) : (easyWorkLocked ? (emp.phoneMobile || null) : (_phoneFmt || null)),
        phone2:       _phone2Fmt || null,
        email:        document.getElementById('ov-email') ? ((document.getElementById('ov-email').value || '').trim()) : (easyWorkLocked ? (emp.email || null) : (_emailRaw || null)),
        street:       document.getElementById('ov-street') ? (document.getElementById('ov-street').value.trim() || null) : (easyWorkLocked ? (emp.street || null) : (document.getElementById('ef-street')?.value || null)),
        zipCode:      document.getElementById('ov-zip') ? (document.getElementById('ov-zip').value.trim() || null) : (_zipRaw || null),
        city:         document.getElementById('ov-city') ? (stripCityCantonSuffix(document.getElementById('ov-city').value) || null) : (easyWorkLocked ? (emp.city || null) : (stripCityCantonSuffix(document.getElementById('ef-city')?.value) || null)),
        // Land IMMER aus dem Feld (auch bei easy@work-Lock, Walter 28.08.2026);
        // Kanton bei Auslands-Adresse bewusst leeren (Filial-Kanton greift).
        country:      document.getElementById('ov-land') ? ((document.getElementById('ov-land').value || 'CH').trim().toUpperCase() || 'CH') : (_landVal || null),
        cantonCode:   document.getElementById('ov-canton')
                      ? (landIstCh(document.getElementById('ov-land')?.value) ? (document.getElementById('ov-canton').value || null) : 'EX')
                      : (!_istCHAdresse ? null
                      : (easyWorkLocked ? (emp.cantonCode || null) : (document.getElementById('ef-canton')?.value || null))),
        permitTypeId: permitTypeEl ? (parseInt(permitTypeEl.value) || 0) : (emp.permitTypeId || 0),
        permitExpiryDate: permitExpiryEl ? (permitExpiryEl.value || null) : (toDateInput(emp.permitExpiryDate) || null),
        nationalityId: (document.getElementById('ov-nat') && document.getElementById('ov-nat').options.length > 1)
            ? (parseInt(document.getElementById('ov-nat').value, 10) || null)
            : (easyWorkLocked ? (emp.nationalityId || null) : (parseInt(document.getElementById('ef-nationalityId')?.value) || null)),
        // ZEMIS: Backend-Konvention — null = «nicht ändern», '' = «löschen».
        // Feld gerendert → Wert (oder '' zum Löschen) senden; sonst null.
        zemisNumber:  zemisEl ? (zemisEl.value || '').trim() : null,
        entryDate:    easyWorkLocked ? (toDateInput(emp.entryDate) || null) : (document.getElementById('ef-entry')?.value || null),
        exitDateSet:  true,
        exitDate:     exitVal || null,
        // Kündigungs-Daten (Walter 16.07.2026) — kuendigungSet:true, damit das
        // Backend sie schreibt (leer = löschen, z.B. nach manuellem Rückzug).
        kuendigungSet: true,
        kuendigungAusgesprochenAm: formVal('ef-kuendAm', 'ov-kuendAm') || null,
        kuendigungPer:             formVal('ef-kuendPer', 'ov-kuendPer') || null,
        kuendigungDurch:           formVal('ef-kuendDurch', 'ov-kuendDurch') || null,
        austrittsgrund:            formVal('ef-austrittsgrund', 'ov-austrittsgrund') || null,
        // Walter-Vorgabe 18.05.2026: Aktiv-Flag bewusst gesetzt vom UI,
        // KEIN Auto-Sync mehr aus ExitDate (Backend nimmt diesen Wert 1:1).
        isActive:     isActiveInput ? isActiveInput.checked === true : !!emp.isActive,
        // Walter-Vorgabe 07.06.2026: Anstellungs-Booleans (Übersicht ov-*).
        lgavPflichtig:        boolVal('ef-lgavPflichtig', 'ov-lgavPflichtig', emp.lgavPflichtig),
        // < 8 h / Wo. gehört zum FLEX-Vertrag (Walter 31.07.2026) — hier nicht mehr schreiben.
        socialSecurityNumber: ahvFeld ? ahvWert : (easyWorkLocked ? (emp.socialSecurityNumber || null) : (document.getElementById('ef-ahvNummer')?.value || null)),
        ahvNummer:    ahvFeld ? ahvWert : (easyWorkLocked ? (emp.socialSecurityNumber || null) : (document.getElementById('ef-ahvNummer')?.value || null)),
        maidenName:   formVal('ef-maidenName', 'ov-maidenName') || null,
        // Kurzname = easy@work Nickname — nur anzeigen, beim Save nicht
        // ueberschreiben wenn kein Formularfeld (Uebersicht ist read-only).
        shortName:    formEl('ef-shortName', 'ov-shortName')
                        ? (formVal('ef-shortName', 'ov-shortName') || null)
                        : (emp.shortName || null),
        // Zivilstand aus der Übersicht (Walter 02.11.2026) — sonst unverändert.
        zivilstand:   zivNeu,
        maritalStatus:zivNeu,
        zivilstandBemerkung: zivGewechselt ? ((document.getElementById('ov-zivBemerkung')?.value || '').trim() || null) : null,

        // Erweiterte Zivilstand-Angaben (allgemein, nicht QST-spezifisch).
        // QST-spezifische Felder (Konkubinat, gemeinsame elterliche Sorge,
        // Unterhalt, höheres Einkommen, Grenzgänger, Wochenaufenthalter)
        // werden im Modul Quellensteuer zeitlich versioniert gepflegt.
        maritalStatusSinceSet: true,
        maritalStatusSince:    formVal('ef-maritalStatusSince', 'ov-maritalStatusSince') || null,
        // separatedSince-Feld wurde aus dem UI entfernt (Walter: „Getrennt"
        // ist bereits ein Zivilstand, separates Datum überflüssig). Wir
        // senden separatedSinceSet=false, damit der Backend-Handler das
        // Feld unverändert lässt.
        separatedSinceSet:     false,
        separatedSince:        null,
        religion:              formVal('ef-religion', 'ov-religion') || null,
        letterSalutation:      (formVal('ef-letterSalutation', 'ov-letterSalutation') || '').trim() || null,
        // Schuhgrösse nur aus der Übersicht: null = nicht ändern, '' = löschen.
        schuhgroesse:          document.getElementById('ov-schuhgroesse') ? document.getElementById('ov-schuhgroesse').value.trim() : null,
        placeOfOrigin:         placeOfOriginEl ? (placeOfOriginEl.value?.trim() || null) : (emp.placeOfOrigin || null),
        employeeNumber:        document.getElementById('ov-nr') ? document.getElementById('ov-nr').value.trim() : undefined,
    };
    const religionBefore = emp.religion || '';
    const religionAfter  = empPayload.religion || '';
    const religionChanged = religionBefore !== religionAfter;

    // "Kein Lohn"-Flag — nur senden wenn der Toggle im Formular existiert
    // (= aktueller User ist admin; sonst rendert das Feld nicht).
    // Walter-Vorgabe 13.06.2026: serverseitig auf admin-only beschränkt,
    // andere Rollen würden 403 PHANTOM_TOGGLE_ADMIN_ONLY bekommen.
    const payrollExclChk = document.getElementById('ef-isPayrollExcluded');
    if (payrollExclChk) {
        empPayload.isPayrollExcluded = payrollExclChk.checked;
    }

    try {
        // Walter-Vorgabe 07.06.2026: KEIN impliziter Permit-PUT mehr im
        // MA-Save. Bewilligungen werden ausschliesslich über das Permit-
        // Modal pro Listenzeile gepflegt — das verhindert strukturell die
        // früheren Overlap-Bugs, bei denen der MA-Save unbemerkt eine
        // Bewilligung mit-veränderte.

        // Nur Stammdaten speichern – Vertragsdaten werden im Modul Vertrag bearbeitet
        const requests = [
            (typeof pdFetch === 'function' ? pdFetch : fetch)(`/api/employees/${selectedEmployeeId}`, {
                method: 'PUT',
                headers: { ...ah(), 'Content-Type': 'application/json' },
                body: JSON.stringify(empPayload)
            })
        ];

        const results = await Promise.all(requests);
        const failed = results.find(r => !r.ok);
        if (failed) {
            // Walter-Bug 18.05.2026: bei 409 (z.B. MA_HAS_OPEN_PERIOD_DATA beim
            // Inaktiv-Setzen) bekommt der User jetzt die Klartext-Meldung statt
            // einem generischen „Fehler". Beim Re-Aktivieren des Häkchens muss
            // der User selber zuerst den Lohnlauf abschliessen.
            let msg = 'Fehler beim Speichern. Bitte erneut versuchen.';
            try {
                const json = await failed.clone().json();
                msg = json.message || json.error || msg;
            } catch {
                const txt = await failed.text().catch(() => '');
                if (txt) msg = txt;
            }
            alert(msg);
            return;
        }

        // Neu laden und in Anzeigemodus zurück
        const res = await fetch(`/api/employees/${selectedEmployeeId}`, { headers: ah() });
        if (res.ok) {
            selectedEmployee = await res.json();
            // Walter 14.06.2026: MA-Picker-Cache invalidieren — beim nächsten
            // Lookup-Open holen Posteingang/HR/Importer/etc. die frische Liste.
            if (typeof invalidateEmployeeLookupCache === 'function') invalidateEmployeeLookupCache();
            // Liste aktualisieren (Name könnte sich geändert haben)
            const idx = allEmployees.findIndex(e => e.id === selectedEmployeeId);
            if (idx >= 0) {
                allEmployees[idx] = { ...allEmployees[idx], ...selectedEmployee };
                allEmployees.sort((a, b) => {
                    const na = ((a.firstName ?? '') + ' ' + (a.lastName ?? '')).trim().toLowerCase();
                    const nb = ((b.firstName ?? '') + ' ' + (b.lastName ?? '')).trim().toLowerCase();
                    return na.localeCompare(nb, 'de');
                });
                const q = document.getElementById('empSearch')?.value ?? '';
                const list = q ? allEmployees.filter(e => {
                    const n = ((e.firstName??'')+(e.lastName??'')).toLowerCase();
                    return n.includes(q.toLowerCase()) || (e.employeeNumber??'').includes(q);
                }) : allEmployees;
                renderEmployeeList(list);
            }
            renderEmployeeDetail(selectedEmployee);

            // Konfession geändert → QST Kirchensteuer wurde serverseitig
            // nachgezogen (Walter 01.08.2026). Kurz zurückmelden + Lohn neu laden.
            if (religionChanged) {
                try {
                    const qr = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/current`, { headers: ah() });
                    if (qr.ok) {
                        const qst = await qr.json();
                        if (qst && qst.qstCode) {
                            const kirche = qst.kirchensteuer ? 'mit Kirchensteuer' : 'ohne Kirchensteuer';
                            _showQstSyncToast(`QST nachgezogen: ${qst.qstCode} · ${kirche}`);
                        }
                    }
                } catch { /* toast best-effort */ }
                if (typeof reloadLohnAfterQstChange === 'function') {
                    reloadLohnAfterQstChange(selectedEmployeeId);
                }
            }
            // QST-Prüfroutine (Walter 23.08.2026): Zivilstand/Konfession
            // können den Tarif kippen — Auffälligkeiten sofort melden.
            qstRecheckNachAenderung(selectedEmployeeId);
        }
    } catch {
        alert('Verbindungsfehler beim Speichern.');
    }
}

function parseFloatOrNull(val) {
    if (val === '' || val === null || val === undefined) return null;
    const n = parseFloat(val);
    return isNaN(n) ? null : n;
}

// ── QST-Prüfroutine nach Stammdaten-Änderungen (Walter-Vorgabe 23.08.2026) ──
// Läuft nach JEDEM Speichern der MA-Stammdaten (Zivilstand/Konfession) und
// nach Anlegen/Ändern/Löschen von Familienmitgliedern (Ehepartner/Kind).
// Holt die zentrale Prüfung (/qst-pflicht) und zeigt bei Auffälligkeiten
// einen deutlichen Dialog mit Sprung in den QST-Tab — z.B. «Tarif A, aber
// Kind im Haushalt → H1 prüfen» nach dem Löschen des Ehepartners.
async function qstRecheckNachAenderung(empId) {
    if (!empId) return;
    try {
        const r = await fetch(`/api/employees/${empId}/qst-pflicht`, { headers: ah() });
        if (!r.ok) return;
        const j = await r.json();
        const hinweise = [];
        if (j.isPflichtOffen) hinweise.push(j.message || 'QST-Pflicht offen — Erfassung fehlt.');
        if (Array.isArray(j.partnerDatenMaengel)) hinweise.push(...j.partnerDatenMaengel);
        if (Array.isArray(j.tarifWarnungen))      hinweise.push(...j.tarifWarnungen);
        if (!hinweise.length) return;
        const msg = 'Die Änderung betrifft die Quellensteuer:\n\n• '
                  + hinweise.slice(0, 3).join('\n\n• ')
                  + (hinweise.length > 3 ? `\n\n… und ${hinweise.length - 3} weitere Hinweise.` : '');
        const go = await liquidConfirm(msg, {
            title: 'QST prüfen', yesLabel: 'QST-Tab öffnen', noLabel: 'Später' });
        if (go && typeof switchEmpTab === 'function') switchEmpTab('quellensteuer');
    } catch (_) { /* Prüfung ist Komfort — nie den Speicher-Fluss stören */ }
}

