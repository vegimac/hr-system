// ── Detail rendern ─────────────────────────────
function renderEmployeeDetail(emp) {
    const panel = document.getElementById('empDetailPanel');
    const name = emp._neu ? '' : (((emp.firstName ?? '') + ' ' + (emp.lastName ?? '')).trim() || '–');
    const entry = emp.entryDate ? formatDate(emp.entryDate) : '–';
    const birthHeader = emp.dateOfBirth
        ? `${formatDate(emp.dateOfBirth)} <span style="color:#94a3b8;font-weight:500">(${calcAge(emp.dateOfBirth)} J.)</span>`
        : '–';
    const exit  = emp.exitDate  ? formatDate(emp.exitDate)  : _t('ma.detail.statusActive', 'Aktiv');
    const nr    = emp.employeeNumber ?? '–';
    // Walter-Vorgabe 14.06.2026: Header-Status MUSS sich an emp.isActive
    // orientieren — vorher zeigte er nur „● Aktiv" wenn kein ExitDate gesetzt
    // war. Wenn ein MA via Re-Import ohne ExitDate deaktiviert wird (z.B.
    // weil er aus dem easy@work-CSV verschwunden ist), entstand sonst ein
    // UI-Widerspruch: Liste/Anstellung-Block „inaktiv", Header „Aktiv".
    // Reihenfolge: Austrittsdatum (mit Austritt:-Label) > „● Inaktiv" (grau)
    // > „● Aktiv" (grün).
    const headerStatusHtml = emp.exitDate
        ? `${_t('ma.detail.exitDate','Austritt')}: ${exit}`
        : (emp.isActive
            ? '<span style="color:#22c55e">● ' + _t('ma.detail.statusActive','Aktiv') + '</span>'
            : '<span style="color:#94a3b8">● ' + _t('ma.detail.statusInactive','Inaktiv') + '</span>');
    // Walter-Vorgabe 07.06.2026: Mitarbeiterfoto im Detail-Header.
    // Initialen als Sofort-Fallback; das echte Foto wird asynchron via
    // loadEmployeePhoto() nachgeladen, falls in der Doku-Struktur ein Typ
    // mit linked_field_code='employee_photo' existiert und ein Bild da ist.
    const initials = ((emp.firstName?.[0] ?? '') + (emp.lastName?.[0] ?? '')).toUpperCase() || '?';
    const isFemale = (emp.gender || '').toLowerCase() === 'female' || (emp.salutation || '').toLowerCase() === 'frau';

    // ── KOPF-CARD (Etappe 1, Walter 17.07.2026, nach Mockup) ──
    // Badges nur wenn zutreffend; Fakten-Zeile: Eintritt · Geburtstag ·
    // Vertrag · Telefon · E-Mail (Wohnort/Kanton bewusst NICHT — stehen in
    // den Karten; Restaurant NICHT — Filiale ist global gewählt).
    const _hcToday = new Date().toISOString().slice(0, 10);
    const _hcActive = (emp.employments || []).filter(c => c.isActive)
        .sort((a, b) => String(b.contractStartDate || '').localeCompare(String(a.contractStartDate || '')))[0] || null;
    const _hcBadges = [];
    if (emp.exitDate)
        _hcBadges.push(`<span class="emp-hbadge hb-exit">${_t('ma.detail.exitDate','Austritt')} ${exit}</span>`);
    else if (emp.isActive)
        _hcBadges.push(`<span class="emp-hbadge hb-ok">● ${_t('ma.detail.statusActive','Aktiv')}</span>`);
    else
        _hcBadges.push(`<span class="emp-hbadge hb-inak">● ${_t('ma.detail.statusInactive','Inaktiv')}</span>`);
    // App-/Postfach-Status (Walter 18.08.2026): sitzt NICHT in der oberen
    // Badge-Zeile (dort ist kein Platz), sondern klein unter der E-Mail.
    if (window._activePregnancy) {
        const _p = window._activePregnancy;
        const _mutTxt = _p.geburtsdatum
            ? `Mutterschaft — Geburt ${formatDate(_p.geburtsdatum)}`
            : (_p.errechneterTermin ? `Mutterschaft — ET ${formatDate(_p.errechneterTermin)}` : 'Mutterschaft');
        _hcBadges.push(`<span class="emp-hbadge hb-mut" style="cursor:pointer" title="Zur Schwangerschaft im Familie-Tab" onclick="switchEmpTab('familie')">🤰 ${_mutTxt}</span>`);
    }
    // Walter-Vorgabe 30.08.2026: Die Kündigungs-Pille NICHT mehr in die
    // Namenszeile — dort ist white-space:nowrap, und zusammen mit dem
    // Austritts-Badge lief sie rechts in die easy@work-Pille hinein. Sie
    // sitzt jetzt eine Zeile tiefer, neben der Vertrags-Pille.
    const _hcBadges2 = [];
    // Walter 31.08.2026: Die Pille «Änderungen» ist wieder raus — der MA-Kopf
    // soll ruhig bleiben. Panel und Endpoint bleiben bestehen:
    // maAenderungenOeffnen(employeeId) lässt sich jederzeit von einer anderen
    // Stelle aus aufrufen.
    if (emp.kuendigungPer) {
        const _kd = (emp.kuendigungDurch || '').toUpperCase();
        const _kdLbl = _kd === 'AN' ? ' · durch MA' : _kd === 'AG' ? ' · durch uns' : _kd === 'GG' ? ' · gegenseitig' : '';
        _hcBadges2.push(`<span class="emp-hbadge hb-kuend">✕ Gekündigt per ${formatDate(emp.kuendigungPer)}${_kdLbl}</span>`);
    }
    // Probezeit-Badge oben: Datum + Status + «eintragen»
    // (Walter 02.08.2026) — nicht mehr in der Anstellung-Karte.
    const _hcPzEnde = _hcActive?.probationEndDate
        ? String(_hcActive.probationEndDate).slice(0, 10) : null;
    const _hcPzAktiv = !!( _hcPzEnde && _hcPzEnde >= _hcToday);
    // Walter 05.09.2026: Status = Probezeit-ENTSCHEID (weiter / Kündigung /
    // Kündigung offen/erfasst), nicht mehr nur «Gespräch erledigt».
    const _hcPzE = emp.probezeitEntscheid || null;
    if (_hcPzAktiv) {
        const pzStatus = _hcPzE === 'weiter'
            ? `<span class="emp-hpz-status ok">✓ bestanden</span>`
            : _hcPzE === 'kuendigung'
            ? `<span class="emp-hpz-status open">✕ Kündigung${emp.kuendigungAusgesprochenAm ? ' erfasst' : ' offen'}</span>
               <button type="button" class="emp-hpz-btn" onclick="event.stopPropagation();openProbezeitModal(${emp.id})">→ öffnen</button>`
            : `<span class="emp-hpz-status open">offen</span>
               <button type="button" class="emp-hpz-btn" onclick="event.stopPropagation();openProbezeitModal(${emp.id})"
                 title="Gesprächsformular, Gespräch bestätigen und Entscheid fällen">→ eintragen</button>`;
        _hcBadges.push(`<span class="emp-hbadge hb-prob">⏳ Probezeit bis ${formatDate(_hcPzEnde)} · ${pzStatus}</span>`);
    }
    if (emp.isPayrollExcluded)
        _hcBadges.push(`<span class="emp-hbadge hb-inak">⛔ MA ohne Lohn</span>`);
    // Zusatz-Angabe je Modell (Walter 17.07.2026): FIX/FIX-M = Pensum %,
    // MTP = garantierte Wochenstunden, FLEX = Wochenstunden (informativ).
    let _hcVertragZusatz = '';
    if (_hcActive) {
        const _m = (_hcActive.employmentModel || '').toUpperCase();
        if ((_m === 'FIX' || _m === 'FIX-M') && _hcActive.employmentPercentage != null)
            _hcVertragZusatz = ' · ' + Number(_hcActive.employmentPercentage) + ' %';
        else if (_m === 'MTP' && (_hcActive.guaranteedHoursPerWeek != null || _hcActive.weeklyHours != null))
            _hcVertragZusatz = ' · ' + Number(_hcActive.guaranteedHoursPerWeek ?? _hcActive.weeklyHours) + ' h/Wo.';
        else if (_m === 'FLEX' && _hcActive.weeklyHours != null)
            _hcVertragZusatz = ' · ' + Number(_hcActive.weeklyHours) + ' h/Wo.';
    }
    // Vertrag als EIGENE fixe Zeile unter dem Namen (Walter 17.07.2026,
    // final): in der Badge-Zeile wrappte die Pille je nach Namenslaenge —
    // jetzt hat jede Info ihren festen Platz, bei jedem MA identisch.
    const _hcVertragLine = _hcActive
        ? `<span class="emp-hbadge hb-vert"><span class="emp-contract-model ${contractModelClass(_hcActive.employmentModel || '')}" style="margin-right:2px">${esc(modelDisplay(_hcActive.employmentModel || '–'))}</span>${esc(_hcActive.jobTitle || _hcActive.jobGroupCode || '')}${_hcVertragZusatz}</span>`
        : `<span class="emp-hbadge hb-inak">kein aktiver Vertrag</span>`;
    const _hcFact = (label, value) => `<div class="emp-hfact"><div class="emp-hfact-l">${label}</div><div class="emp-hfact-v">${value || '<span style="color:#8b8b8b;font-weight:500">–</span>'}</div></div>`;
    // Direkt anrufen / E-Mail schreiben (Walter 19.07.2026) — Icon VOR dem
    // Wert (besser sichtbar), öffnet tel:/mailto:.
    const _hcActIcon = (kind) => kind === 'tel'
        ? `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M22 16.92v3a2 2 0 0 1-2.18 2 19.79 19.79 0 0 1-8.63-3.07 19.5 19.5 0 0 1-6-6 19.79 19.79 0 0 1-3.07-8.67A2 2 0 0 1 4.11 2h3a2 2 0 0 1 2 1.72c.13.81.36 1.6.68 2.34a2 2 0 0 1-.45 2.11L8.09 9.91a16 16 0 0 0 6 6l1.27-1.27a2 2 0 0 1 2.11-.45c.74.32 1.53.55 2.34.68A2 2 0 0 1 22 16.92z"/></svg>`
        : `<svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z"/><polyline points="22,6 12,13 2,6"/></svg>`;
    const _hcPhoneHref = (emp.phoneMobile || '').replace(/[\s\-().]/g, '');
    const _hcPhoneVal = emp.phoneMobile
        ? `<span class="emp-hfact-with-act"><a class="emp-hfact-act" href="tel:${esc(_hcPhoneHref)}" title="Anrufen: ${esc(emp.phoneMobile)}" aria-label="Anrufen">${_hcActIcon('tel')}</a><span class="emp-hfact-txt" title="${esc(emp.phoneMobile)}">${esc(emp.phoneMobile)}</span></span>`
        : null;
    const _appChip = emp.isPayrollExcluded ? '' :
        `<span id="empAppChip" style="display:none;font-size:10.5px;font-weight:600;padding:1px 8px;border-radius:8px;cursor:pointer;margin-top:2px;width:fit-content" onclick="postfachSetupQr(${emp.id})" title="Klick: Link/QR senden"></span>`;
    if (!emp.isPayrollExcluded && !emp._neu) requestAnimationFrame(() => pfLoadAppChip(emp.id));
    const _hcEmailVal = emp.email
        ? `<span class="emp-hfact-with-act" style="display:flex;flex-direction:column;align-items:flex-start"><span style="display:flex;align-items:center;gap:6px"><a class="emp-hfact-act" href="mailto:${esc(emp.email)}" title="E-Mail an ${esc(emp.email)}" aria-label="E-Mail schreiben">${_hcActIcon('mail')}</a><span class="emp-hfact-txt" title="${esc(emp.email)}">${esc(emp.email)}</span></span>${_appChip}</span>`
        : null;

    // 🆘 Notfallkontakt als 5. Fakten-Spalte (Walter 25.08.2026): kompakt
    // Name · Nummer (anrufbar), Beziehung im Tooltip; Klick auf den Text
    // öffnet das Notfall-Modal. Ohne Erfassung: «– erfassen –» in grau.
    const _hcNf = emp.notfallKontakt;
    const _hcNfHref = (_hcNf?.telefon || '').replace(/[\s\-().]/g, '');
    const _hcNotfallVal = _hcNf
        ? `<span class="emp-hfact-with-act">${_hcNf.telefon
                ? `<a class="emp-hfact-act" href="tel:${esc(_hcNfHref)}" title="Notfallkontakt anrufen: ${esc(_hcNf.telefon)}" aria-label="Notfallkontakt anrufen">${_hcActIcon('tel')}</a>`
                : ''}<span class="emp-hfact-txt" style="cursor:pointer" onclick="openNotfallModal()"
                title="Notfallkontakt — Klick: ändern">${esc(_hcNf.name)}${_hcNf.beziehung
                ? ` <span style="color:#8b8b8b;font-weight:500">(${esc(_hcNf.beziehung)})</span>` : ''}${_hcNf.telefon
                ? ` · ${esc(_hcNf.telefon)}`
                : ' · <span style="color:#b91c1c">Tel. fehlt</span>'}</span></span>`
        : `<span style="color:#8b8b8b;font-weight:500;cursor:pointer" onclick="openNotfallModal()" title="Notfallkontakt erfassen">– erfassen –</span>`;

    panel.innerHTML = `
    <div class="emp-detail-header">
        <div style="display:flex;align-items:flex-start;justify-content:flex-start;gap:16px">
            <div id="empDetailPhoto"
                 class="emp-avatar ${isFemale ? 'female' : ''}"
                 style="border-radius:50%;flex-shrink:0;background-size:cover;background-position:center;display:flex;align-items:center;justify-content:center;overflow:hidden">${initials}</div>
            <div style="min-width:0;flex:1 1 auto">
                <!-- Name + Nummer bleiben EIN unzertrennlicher Block; die
                     Status-Pillen (Aktiv, Austritt, Probezeit …) dürfen auf
                     eine 2. Zeile darunter umbrechen statt unter der
                     schwebenden Leiste (easy@work sync / Suche) zu
                     verschwinden (Walter 06.09.2026). Der rechte Freiraum
                     wird in empHeaderReserveRight() gemessen. -->
                <div class="emp-detail-name" id="empDetailNameLine" style="display:flex;align-items:baseline;gap:8px 10px;flex-wrap:wrap;min-width:0">
                    <span style="white-space:nowrap;display:inline-flex;align-items:baseline;gap:10px">
                        <span>${name}</span>
                        <span style="font-size:16px;font-weight:650;color:#8b8b8b">${nr}</span>
                        <span id="empNumberAliases" data-emp="${emp.id}"></span>
                    </span>
                    ${_hcBadges.join('')}
                </div>
                <div class="emp-hvertrag">${_hcVertragLine}${_hcBadges2.join('')}</div>
                <!-- EINE Fakten-Zeile, fixe Spalten: Eintritt|Geburtstag|Telefon|E-Mail -->
                <div class="emp-hfacts">
                    <!-- Eintritt + 🆘 Notfall DIREKT darunter in derselben Zelle
                         (Walter 25.08.2026 v4: keine eigene Grid-Zeile mehr —
                         sitzt wie der App-Chip unter der E-Mail). Feste Breite
                         380px darf optisch unter «Geburtstag» durchlaufen, die
                         2. Zeile ist dort leer. -->
                    <div class="emp-hfact">
                        <div class="emp-hfact-l">${_t('ma.detail.entryDate','Eintritt')}</div>
                        <div class="emp-hfact-v">${(emp.entryDate ? entry : null) || '<span style="color:#8b8b8b;font-weight:500">–</span>'}</div>
                        <!-- Feste Höhe 28px (Walter 25.08.2026 v5): mit Anruf-Knopf
                             (28px) und ohne («– erfassen –», ~20px Text) gleich
                             hoch — sonst springt der Kopf beim MA-Wechsel. -->
                        <div style="display:flex;align-items:center;gap:8px;margin-top:3px;width:460px;height:28px;font-size:15px;font-weight:700;color:#0a0a0a">
                            <span style="font-size:22px;line-height:1">🆘</span><div style="min-width:0;white-space:nowrap">${_hcNotfallVal}</div>
                        </div>
                    </div>
                    ${_hcFact('Geburtstag', emp.dateOfBirth ? birthHeader /* kein Doku-Symbol am Geburtstag (Walter 26.09.2026) */ : null)}
                    ${_hcFact('Mobilnummer', _hcPhoneVal)}
                    ${_hcFact('E-Mail', _hcEmailVal)}
                </div>
            </div>
            <!-- Rechte Aktions-Spalte ABSOLUT positioniert (Walter 17.07.2026:
                 «Zeilen und Positionen heilig») — nimmt dem Namen-/Fakten-
                 Bereich keine Breite weg. top:54px = unter der reservierten
                 langSwitcher-Zone (CLAUDE.md). startEmpEdit() ersetzt weiterhin
                 den Inhalt von #empHeaderActions. -->
            <div class="emp-head-right">
            <div id="empTabActionBar" style="display:flex;gap:8px;align-items:center;justify-content:flex-end"></div>
            <div id="empHeaderActions" style="display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end;align-content:flex-start;min-width:0">
                <!-- Walter 17.07.2026: alle Aktions-Buttons in den Tab
                     «Restaurant Admin» verschoben (_raTilesHtml); easy@work-
                     Sync sitzt unter «Dunkel» (#lsEmpSyncBtn, nur Übersicht).
                     Hier bleibt nur das Inline-Speichern; startEmpEdit()
                     ersetzt den Inhalt durch Speichern/Abbrechen. -->
                <button id="empInlineSaveBtn" class="emp-inline-save" onclick="saveEmpEdit()" style="display:none">Speichern</button>
            </div>
            </div>
        </div>
        <div class="emp-detail-tabs">
            <div class="emp-tab active" data-tab="uebersicht" onclick="switchEmpTab('uebersicht')" style="line-height:1.2;text-align:center">${_t('ma.tab.overview','Übersicht')}</div>
            <div class="emp-tab"        data-tab="familie"    onclick="switchEmpTab('familie')" style="line-height:1.2;text-align:center">${_t('ma.tab.family','Familie<br>Schwanger')}</div>
            <div class="emp-tab"        data-tab="quellensteuer" onclick="switchEmpTab('quellensteuer')" style="line-height:1.2;text-align:center">${_t('ma.tab.permitQst','Bewilligung QST<br>Bank weit. AG')}</div>
            <div class="emp-tab"        data-tab="verwarnungen" onclick="switchEmpTab('verwarnungen')" style="line-height:1.2;text-align:center">${_t('ma.tab.restAdmin','MA<br>Formulare')}</div>
            <div class="emp-tab"        data-tab="stempelzeiten" onclick="switchEmpTab('stempelzeiten')">${_t('ma.tab.timeRecords','Stempelzeiten')}</div>
            <div class="emp-tab"        data-tab="absenzen"   onclick="switchEmpTab('absenzen')" style="line-height:1.2;text-align:center">${_t('ma.tab.absencesKtg','Absenzen /<br>KTG/UVG')}</div>
            <div class="emp-tab"        data-tab="verfuegbarkeit" onclick="switchEmpTab('verfuegbarkeit')" style="line-height:1.2;text-align:center">${_t('ma.tab.availabilityTraining','Training,<br>Verfügbar, Statistik')}</div>
            <div class="emp-tab"        data-tab="zulagen"    onclick="switchEmpTab('zulagen')" style="line-height:1.2;text-align:center">${_t('ma.tab.zulagenAbzuege','Zulagen Abzüge<br>Abtretung BVG')}</div>
            <div class="emp-tab"        data-tab="dokumente"  onclick="switchEmpTab('dokumente')" style="line-height:1.2;text-align:center">Dokumente /<br>Historie</div>
        </div>
    </div>
    <div class="emp-detail-body">
        <!-- TAB: Uebersicht (Etappe 2, Walter 17.07.2026) — read-only Karten,
             Bearbeiten weiterhin nur in den Fach-Tabs. -->
        <div class="emp-tab-content active" id="emp-tab-uebersicht">
            <div id="uebersichtContent"><div class="emp-placeholder"><span>Wird geladen…</span></div></div>
        </div>
        <!-- TAB «Persönliche Angaben» entfernt 17.07.2026 (Walter): Inhalt lebt
             in der Übersicht (Personalien/Anstellung/Verträge/Nachtarbeit).
             Alias personal → uebersicht in switchEmpTab. buildEmpEditPersonal
             bleibt als Code erhalten (nicht mehr verdrahtet). -->

        <!-- TAB: Familie -->
        <div class="emp-tab-content" id="emp-tab-familie">
            <div id="familieContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                    <span>${_t('ma.loading','Wird geladen...')}</span>
                </div>
            </div>
        </div>

        <!-- TAB: Bewilligung QST Bank (Walter-Vorgabe 15.07.2026, final) —
             Reihenfolge (Walter 19.07.2026): Bank zuoberst, dann Bewilligungen
             + QST — die Bewilligungs-Liste kann lang werden und soll die
             Bank nicht nach unten verdrängen. Tab-Key bleibt 'quellensteuer'. -->
        <div class="emp-tab-content" id="emp-tab-quellensteuer">
            ${!emp.isPayrollExcluded ? `
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:0">
                <span style="display:inline-flex;align-items:center;gap:8px">
                    ${_t('ma.section.bank','Bankverbindung')}
                    <!-- Doku pro Bankverbindung statt neben dem Titel (Walter 25.09.2026) -->
                </span>
                <!-- Walter 29.08.2026: Pille ganz rechts in der Sektions-Titelzeile
                     (wie «+ Neue QST-Version») — nicht mehr im empTabActionBar. -->
                <button class="btn-emp-add" onclick="openBankAccountModal(null)">
                    + ${_t('ma.btn.newBank','Neue Bankverbindung')}
                </button>
            </div>
            <div id="bankAccountsContent">
                <div class="emp-placeholder"><span>Wird geladen…</span></div>
            </div>
            <!-- Weitere Arbeitgeber (Walter 23.09.2026) — nur Info für die
                 Checkliste, NICHT mit QST/Lohnlauf/Swissdec verbunden. -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:18px">
                <span>Weitere Arbeitgeber</span>
                <button class="btn-emp-add" onclick="openWeitererAgModal(${emp.id}, null)">+ Weiterer Arbeitgeber</button>
            </div>
            <div id="weitereAgContent"></div>
            ` : `
            <div style="margin:0 0 14px;padding:12px 16px;background:#fef3c7;border:1px solid #fbbf24;border-radius:8px;color:#92400e;font-size:13px;line-height:1.55">
                <strong>⛔ ${_t('ma.phantom.title','MA ohne Lohn')}</strong> — ${_t('ma.phantom.bankDesc','Phantom-MA für easy@work-Zugang. Bankverbindung wird nicht angezeigt — dieser MA hat keinen Vertrag und keine Lohnzahlung.')}
            </div>
            `}
            <div id="quellensteuerContent" style="margin-top:28px">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/></svg>
                    <span>${_t('ma.loading','Wird geladen...')}</span>
                </div>
            </div>
        </div>

        <!-- TAB: Restaurant Admin (Walter-Vorgabe 15.07.2026) — enthaelt die
             Verwarnungs-Verwaltung; weitere Admin-Funktionen kommen hier dazu. -->
        <div class="emp-tab-content" id="emp-tab-verwarnungen">
            <div id="verwarnungenContent">
                <div class="emp-placeholder"><span>${_t('ma.loading','Wird geladen...')}</span></div>
            </div>
        </div>

        <!-- TAB: Stempelzeiten -->
        <div class="emp-tab-content" id="emp-tab-stempelzeiten">
            <div id="stempelzeitenContent">
                <div class="emp-placeholder">
                    <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg>
                    <span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span>
                </div>
            </div>
        </div>

        <!-- TAB: Absenzen — Walter-Vorgabe 26.05.2026: vom alten kombinierten
             „Absenzen Zulagen Abzüge"-Tab abgetrennt; Zulagen/Abzüge sind
             jetzt ein eigener Tab.
             Walter 17.07.2026: zweispaltig — Absenzen links scrollen,
             KTG/UVG-Tagessatz schmal rechts (Arbeitsplatz Krank/Unfall).
             Walter 25.07.2026: Karenz kompakt unter dem Tagessatz (gleiche Breite). -->
        <div class="emp-tab-content" id="emp-tab-absenzen">
            <div class="abs-ktg-layout">
                <div class="abs-ktg-main">
                    <div class="emp-section-title">${_t('abs.section.absences','Absenzen')}</div>
                    <div id="absenzenContent">
                        <div class="emp-placeholder">
                            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="3" y="4" width="18" height="18" rx="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg>
                            <span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span>
                        </div>
                    </div>
                </div>
                <aside class="abs-ktg-side" aria-label="Tagessatz und Karenz">
                    <div id="ktgTagessatzSidebar">
                        <div class="emp-placeholder" style="padding:24px 12px"><span>${_t('ma.loading','Wird geladen...')}</span></div>
                    </div>
                    <div id="karenzSidebar"></div>
                </aside>
            </div>
        </div>

        <!-- TAB: Training, Verfügbar, Statistik -->
        <div class="emp-tab-content" id="emp-tab-verfuegbarkeit">
            <div id="verfuegbarkeitContent">
                <div class="emp-placeholder" style="height:200px">${_t('ma.loading','Wird geladen...')}</div>
            </div>
            <div id="trainingContent" style="margin-top:22px"></div>
            <div id="statistikMaContent"></div>
        </div>

        <!-- TAB: Zulagen & Abzüge -->
        <div class="emp-tab-content" id="emp-tab-zulagen">
            <div id="laAngabenKarte"></div>
            <!-- Lohnschema-Block (Walter 17.08.2026): Standard-Lohnblatt des
                 Vertragsmodells als read-only-Chips, gerendert von
                 loadLohnschemaBlockForModel() in js/lohnschema.js -->
            <div id="empLohnschemaBlock"></div>
            <!-- Bereich 1: Wiederkehrende Zulagen & Abzüge
                 Walter 29.08.2026 (kompakt): +-Pille + History-Pille in der
                 Titelzeile (Muster Bewilligungen/permHistPill), Hint = Tooltip. -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span title="${_t('abs.section.recurringHint','Werden bei jedem Lohnlauf im Gültigkeitszeitraum automatisch verrechnet')}">${_t('abs.section.recurring','Wiederkehrende Zulagen &amp; Abzüge')}</span>
                <span style="display:inline-flex;align-items:center;gap:8px">
                    <span id="rwHistPillSlot"></span>
                    <button class="btn-emp-add" onclick="openRecurringWageModal(null)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                        Zulage / Abzug erfassen
                    </button>
                </span>
            </div>
            <div id="recurringWagesContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:10px 0"></div>

            <!-- Bereich 2: Lohnabtretungen (Pfändung / Sozialamt)
                 Walter 29.08.2026 (kompakt): Doku-Badge + History-Pille +
                 +-Pille in der Titelzeile; der frühere innere Sektions-Header
                 in renderLohnAssignmentsList ist entfernt. -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span style="display:inline-flex;align-items:center;gap:8px" title="${(window.i18n && i18n.getLang && i18n.getLang() === 'en') ? 'Wage garnishment or assignment to social welfare — calculated on net pay' : 'Lohnpfändung oder Abtretung an Sozialamt — nach Netto berechnet'}">
                    ${_t('abs.section.lohnabtretung','Lohnabtretungen')}
                    <span id="laHeaderBadgeSlot"></span>
                </span>
                <span style="display:inline-flex;align-items:center;gap:8px">
                    <span id="laHistPillSlot"></span>
                    <button class="btn-emp-add" onclick="openLohnAssignmentModal(null)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                        Lohnabtretung erfassen
                    </button>
                </span>
            </div>
            <div id="lohnAssignmentsContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:10px 0"></div>

            <!-- K3 (Walter 29.08.2026): Darlehen & Vorschüsse — generisches
                 zinsloses MA-Darlehen (QST-Nachzahlung oder z.B. «Vorschuss
                 Hochzeit 2'000»). Rate = automatischer Abzug nach Netto im
                 Definitivlauf; letzte Rate = Rest; Austritt → Rest fällig. -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span title="zinslos, Rate wird im Lohnlauf verrechnet">Darlehen &amp; Vorschüsse</span>
                <span style="display:inline-flex;align-items:center;gap:8px">
                    <span id="dlHistPillSlot"></span>
                    <button class="btn-emp-add" onclick="openDarlehenModal(null)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                        Darlehen / Vorschuss erfassen
                    </button>
                </span>
            </div>
            <div id="darlehenContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:10px 0"></div>

            <!-- BVG versichert ja/nein pro Person (Walter 04.10.2026): mutmasslicher
                 Jahreslohn inkl. 13. ML statt Monats-Schwelle. -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span title="Ob der MA in der Pensionskasse versichert ist — entschieden nach dem erwarteten Jahreslohn inkl. 13. ML, nicht Monat für Monat.">BVG-Versicherungspflicht</span>
                <span style="display:inline-flex;align-items:center;gap:8px">
                    <span id="bpHistPillSlot"></span>
                    <button class="btn-emp-add" onclick="bpOpenModal(null)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                        Festlegen
                    </button>
                </span>
            </div>
            <div id="bpContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:10px 0"></div>

            <!-- Versicherungs-Codes (Walter 07.09.2026, Swissdec-Lösungen): UVG/UVGZ/KTG/BVG.
                 Ohne Eintrag gilt der Standard der SV-Sätze — erfasst wird nur, wer abweicht. -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span title="Versicherungs-Lösung pro Versicherung (Swissdec-Code). Ohne Eintrag gilt der Standard der SV-Sätze.">Versicherungen (UVG · UVG-Zusatz · KTG · BVG)</span>
                <span style="display:inline-flex;align-items:center;gap:8px">
                    <span id="vcHistPillSlot"></span>
                    <button class="btn-emp-add" onclick="vcOpenModal(null)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                        Abweichung erfassen
                    </button>
                </span>
            </div>
            <div id="vcContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:10px 0"></div>

            <!-- Bereich 3: BVG-Zusatz-Mitgliedschaft (Walter-Vorgabe 26.05.2026):
                 Belohnungs-Programm — Personalentscheid pro MA, versioniert.
                 Walter-Vorgabe 26.05.2026 (nachträglich): ans Ende des Tabs
                 verschoben — selten editiert, gehört untenhin. -->
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between">
                <span title="${_t('abs.section.bvgZusatzHint','— Belohnungs-Programm, Personalentscheid pro MA')}">${_t('abs.section.bvgZusatz','BVG-Zusatz')}</span>
                <span style="display:inline-flex;align-items:center;gap:8px">
                    <span id="bvgHistPillSlot"></span>
                    <button class="btn-emp-add" onclick="openBvgZusatzModal(null)">
                        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                        ${_t('abs.btn.newBvgZusatz','Mitgliedschaft erfassen')}
                    </button>
                </span>
            </div>
            <div id="bvgZusatzContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>

            <div style="height:1px;background:#e2e8f0;margin:10px 0"></div>

            <!-- Uniformen-Depot ans Ende (Walter 31.10.2026) -->
            <div class="emp-section-title" style="display:flex;align-items:center;gap:8px" title="CHF 50 beim 1. Lohn · Rückerstattung bei ordentlichem Austritt">
                <span>Uniformen-Depot</span>
            </div>
            <div id="uniformDepotContent">
                <div class="emp-placeholder"><span>${_t('ma.selectEmployee','Bitte wähle einen Mitarbeiter')}</span></div>
            </div>
        </div>

<!-- TAB KTG/UVG entfernt 17.07.2026 (Walter): Tagessatz nur noch bei Absenzen
     (Sidebar). Übersicht zeigt seit 19.07.2026 die Saldi-Tabelle. -->

<!-- Mutterschafts-Tab entfernt am 11.06.2026 (Walter-Vorgabe): Modul lebt
     jetzt komplett im Familie-Tab. Die mts*-Funktionen + renderPregnancyCard
     bleiben — werden vom Familie-Tab aufgerufen. -->

<!-- TAB: Dokumente -->
        <div class="emp-tab-content" id="emp-tab-dokumente">
            <div id="empTabDokumente">
                <div class="emp-placeholder" style="height:200px">${_t('ma.loading','Wird geladen...')}</div>
            </div>
        </div>
        <!-- TAB: Historie (Walter 20.08.2026) — read-only Zeitachse aus
             vorhandenen Daten (Verträge, Übertritte, Umzüge, QST, Bewilligungen). -->
        <div class="emp-tab-content" id="emp-tab-historie">
            <div id="historieContent">
                <div class="emp-placeholder" style="height:200px">${_t('ma.loading','Wird geladen...')}</div>
            </div>
        </div>
    </div>`;
    empHeaderReserveRight();

    // Tab-Persistenz: vorher aktiven Tab wiederherstellen statt zurück auf "personal".
    // Wenn Walter z.B. "Familie" angezeigt hat und einen anderen MA wählt, bleibt
    // er auf "Familie". switchEmpTab triggert den passenden loadXxx()-Aufruf
    // (Bankverbindung beim personal-Tab, Familie beim familie-Tab usw.).
    // easy@work-Sync-Button oben im langSwitcher (Walter 17.07.2026):
    // sichtbar sobald ein MA gewaehlt ist und die Rolle es erlaubt.
    window._lsEmpSyncAllowed = ['admin','superuser','buchhaltung','user'].includes(currentUser?.role);
    switchEmpTab(activeEmpTab || 'uebersicht');
    updateLsEmpSyncBtn();
}

/** easy@work-Sync unter «Dunkel». Test: in der Liste Neuer MA + Bearbeiten statt easy-Neu. */
function updateLsEmpSyncBtn() {
    const btn = document.getElementById('lsEmpSyncBtn');
    const easyNeu = document.getElementById('empEasyNeuBtn');
    const testZeile = document.getElementById('empTestAktionen');
    const edit = document.getElementById('empBearbeitenBtn');
    const aufUebersicht = (typeof activeEmpTab === 'undefined' || activeEmpTab === 'uebersicht');
    const aufMa = (typeof currentPageName === 'undefined' || currentPageName === 'mitarbeiter') && aufUebersicht;
    const test = !!window._istTestinstanz;
    if (btn) {
        const ok = aufMa
            && window.selectedEmployeeId
            && window._lsEmpSyncAllowed
            && !selectedEmployee?._neu
            && !test;
        btn.style.display = ok ? 'inline-flex' : 'none';
    }
    if (easyNeu) easyNeu.style.display = test ? 'none' : '';
    if (testZeile) testZeile.style.display = test ? 'flex' : 'none';
    if (edit) {
        const zeigen = test && selectedEmployee && selectedEmployee.id > 0
            && !selectedEmployee._neu && !selectedEmployee._testBearbeiten;
        edit.style.display = zeigen ? 'inline-flex' : 'none';
    }
}

