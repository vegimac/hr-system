// ══════════════════════════════════════════════
// FAMILIE MODAL
// ══════════════════════════════════════════════

let editingFamilyMemberId = null;

async function openFamilyModal(member) {
    editingFamilyMemberId = member ? member.id : null;

    document.getElementById('familyModalTitle').textContent =
        member ? _t('fam.modalTitleEdit','Familienangehöriger bearbeiten')
               : _t('fam.modalTitleNew','Familienangehörigen hinzufügen');

    // Statische Strings die wir per data-i18n im Modal markiert haben jetzt
    // einmal anwenden — das Modal wurde via .style.display=flex „geöffnet",
    // i18n.applyAll() rendert alle data-i18n-Elemente in die aktuelle Sprache.
    if (window.i18n && window.i18n.applyAll) window.i18n.applyAll(document.getElementById('familyModal'));

    // Felder befüllen oder leeren
    document.getElementById('fmMemberType').value      = member?.memberType         ?? 'Kind';
    document.getElementById('fmGender').value          = member?.gender             ?? '';
    document.getElementById('fmFirstName').value       = member?.firstName          ?? '';
    // Walter-Vorgabe 28.05.2026: Bei NEU + Kind/Ehepartner den Nachnamen des MA
    // vorbefüllen — kann der User natürlich überschreiben. Bei bestehenden
    // Einträgen (Edit) den gespeicherten Wert übernehmen.
    const _maLast = (selectedEmployee?.lastName || '').trim();
    const _defaultType = member?.memberType ?? 'Kind';
    const _prefillLast = (!member && _maLast && (_defaultType === 'Kind' || _defaultType === 'Ehepartner'))
        ? _maLast : (member?.lastName ?? '');
    document.getElementById('fmLastName').value        = _prefillLast;
    document.getElementById('fmMaidenName').value      = member?.maidenName         ?? '';
    document.getElementById('fmDateOfBirth').value     = toDateInput(member?.dateOfBirth);
    updateFmAgeDisplay();
    document.getElementById('fmSocialSecurity').value  = member?.socialSecurityNumber ?? '';
    ahvMaskInput(document.getElementById('fmSocialSecurity'));
    document.getElementById('fmPhone').value           = member?.phone ?? '';

    // Walter-Vorgabe 14.06.2026: NEUE Familienmitglieder (vor allem Kinder)
    // bekommen sinnvolle Defaults vom MA:
    //   • Lebt in Schweiz   → wie das Land des MA (CH = Ja, Ausland = Nein)
    //   • Nationalität      → wie MA (Mutter/Vater)
    //   • Bewilligung       → wie MA (Mutter/Vater)
        // Bei bestehenden Einträgen wird der gespeicherte Wert übernommen.
    const _isNewMember = !member;
    document.getElementById('fmLivesInSwitzerland').checked =
        _isNewMember
            ? fmIstLandSchweiz(selectedEmployee?.country)
            : (member?.livesInSwitzerland ?? false);
    // Walter-Vorgabe 20.08.2026: QST-Relevanz-Felder — Ehepartner-Erwerb +
    // Kind-Erstausbildung (+ typ-abhängige Sichtbarkeit).
    fmSetErwerb(member?.erwerbstaetig ?? null);
    // Konkubinat (Walter 25.08.2026): Einkommensfrage — seit 11.10.2026 bei
    // allen Eltern-Typen (Erstanspruch Familienzulagen).
    fmSetMaEink(member?.maHatHoeheresEinkommen ?? null);

    // Familienzulagen-Angaben (Walter 11.10.2026, docs/famz-anmeldung-konzept.md)
    const zsSel = document.getElementById('fmFamilyStatus');
    if (zsSel) {
        const zs = member?.familyStatus ?? '';
        // Unbekannter Altwert (Import) bleibt wählbar, sonst würde er beim Speichern gelöscht.
        if (zs && ![...zsSel.options].some(o => o.value === zs))
            zsSel.insertAdjacentHTML('beforeend', `<option value="${esc(zs)}">${esc(zs)}</option>`);
        zsSel.value = zs;
    }
    document.getElementById('fmBeziehung').value = member?.beziehungZumMa ?? (_isNewMember ? 'L' : '');
    document.getElementById('fmElterlicheSorge').value = member?.elterlicheSorge ?? '';
    document.getElementById('fmErwerbsunfaehig').checked = member?.erwerbsunfaehig === true;
    {
        // Anderer Elternteil: gespeichert = Id; «nicht erfasst» = gemeinsamesKind false ohne Id;
        // neues Kind mit genau einem lebenden Partner/Elternteil → vorschlagen.
        let ae = member?.andererElternteilId != null ? String(member.andererElternteilId)
               : (member && member.gemeinsamesKindMitPartner === false) ? '0' : '';
        if (_isNewMember) {
            const kandidaten = (window._familyMembersCache || [])
                .filter(m => FM_ELTERN_TYPEN.includes(m.memberType) && !m.dateOfDeath);
            if (kandidaten.length === 1) ae = String(kandidaten[0].id);
        }
        document.getElementById('fmAndererElternteil').dataset.wert = ae;
    }
    document.getElementById('fmArbeitspensum').value = member?.arbeitspensum ?? '';
    document.getElementById('fmArbeitgeberTelefon').value = member?.arbeitgeberTelefon ?? '';
    fmPille('fmEinkMin', member?.einkommenUeberMinimum ?? null);
    document.getElementById('fmSeNe').value = member?.seNeAngemeldet ?? '';
    document.getElementById('fmSeNeKasse').value = member?.seNeAusgleichskasse ?? '';
    document.getElementById('fmBeziehtLeistung').value = fmJn(member?.beziehtLeistung ?? null);
    document.getElementById('fmLeistungVon').value = member?.leistungVon ?? '';
    fmSeNeSichtbarkeit();
    document.getElementById('fmKindStartHaushalt').value = 'ja';
    document.getElementById('fmKindStartUnterhalt').value = 'ja';
    document.getElementById('fmKindStart').style.display = _isNewMember ? '' : 'none';
    window._fmKindLebtImHaushalt = member?.lebtImHaushalt;

    document.getElementById('fmArbeitgeberName').value    = member?.arbeitgeberName    ?? '';
    document.getElementById('fmArbeitgeberStrasse').value = member?.arbeitgeberStrasse ?? '';
    document.getElementById('fmArbeitgeberPlz').value     = member?.arbeitgeberPlz     ?? '';
    document.getElementById('fmArbeitgeberOrt').value     = member?.arbeitgeberOrt     ?? '';
    const agKanton = (member?.arbeitgeberKanton || '').trim().toUpperCase();
    const agKantonSel = document.getElementById('fmArbeitgeberKanton');
    if (agKantonSel) agKantonSel.value = agKanton;
    document.getElementById('fmStellenantritt').value     = toDateInput(member?.stellenantritt);
    const stellenende = document.getElementById('fmStellenende');
    if (stellenende) stellenende.value = toDateInput(member?.stellenende);
    const agHint = document.getElementById('fmAgPlzHint');
    if (agHint) agHint.innerHTML = '';
    fmQstBlocksVisibility(member?.memberType ?? 'Kind');
    if (typeof kqvOeffnen === 'function') kqvOeffnen(selectedEmployeeId ?? selectedEmployee?.id, member);

    // ── Aufenthalt + Nationalität: Permit-Types + Nationalitäten füllen
    //    (gleiche Listen wie beim MA-Edit-Modal). Vorausgewählt wird der
    //    bestehende Wert; bei NEU der Wert vom MA als Default.
    //    AUSNAHME (Walter 29.08.2026): beim NEUEN Kind KEINE Bewilligungs-
    //    Vorbelegung — das Feld ist beim Kind ausgeblendet und würde sonst
    //    unsichtbar mitgespeichert.
    const _startType = member?.memberType ?? 'Kind';
    const _defaultPermitTypeId = _isNewMember
        ? (_startType === 'Kind' ? null
            : (selectedEmployee?.permitTypeId ?? selectedEmployee?.permitType?.id ?? null))
        : (member?.permitTypeId ?? null);
    const _defaultNationalityId = _isNewMember
        ? (selectedEmployee?.nationalityId ?? null)
        : (member?.nationalityId ?? null);
    fmFillPermitAndNationalitySelects(_defaultPermitTypeId, _defaultNationalityId);
    document.getElementById('fmPermitExpiry').value = toDateInput(member?.permitExpiryDate);
    document.getElementById('fmZemisNumber').value  = member?.zemisNumber ?? '';
    // Bewilligung/Erwerb: Status-UI (Walter 08.10.2026) — Ersterfassung oder «Status ändern».
    const _fmHeute = (() => { const t = new Date(); return `${t.getFullYear()}-${String(t.getMonth()+1).padStart(2,'0')}-${String(t.getDate()).padStart(2,'0')}`; })();
    window._fmPermitTypeId = member?.permitTypeId ?? _defaultPermitTypeId ?? null;
    window._fmErwerbStand = member?.erwerbstaetig ?? null;
    const _pSeit = document.getElementById('fmPermitSeit');
    const _pEa = document.getElementById('fmPermitErfahrenAm');
    if (_pSeit) _pSeit.value = '';
    if (_pEa) _pEa.value = _fmHeute;
    const _eSeit = document.getElementById('fmErwerbSeit');
    const _eEa = document.getElementById('fmErwerbErfahrenAm');
    if (_eSeit) _eSeit.value = '';
    if (_eEa) _eEa.value = _fmHeute;
    fmClosePermitWechsel();
    fmCloseErwerbWechsel();
    if (member?.id) {
        await fmLoadPartnerHistories(member.id);
        fmRefreshStatusUi(true);
    } else {
        const pl = document.getElementById('fmPermitHistList');
        const el = document.getElementById('fmErwerbHistList');
        if (pl) { pl.style.display = 'none'; pl.innerHTML = ''; }
        if (el) el.innerHTML = '';
        fmRefreshStatusUi(false);
    }

    // Walter-Vorgabe 07.06.2026: „Dokument hochladen"-Button NUR beim
    // Ehepartner sichtbar — klickt auf den normalen Standard-Upload-Dialog
    // (openDokUploadModal), wo Walter Kategorie + Typ frei wählt
    // (typisch: „Persönliche Angaben / Ehegatten Dokumente"). Damit ist
    // der Mechanismus unempfindlich gegen Änderungen in der Dokument-Struktur.
    const docUploadBtn = document.getElementById('fmSpouseDocBtn');
    const isSpouse = (member?.memberType ?? document.getElementById('fmType')?.value) === 'Ehepartner';
    if (docUploadBtn) {
        docUploadBtn.style.display = isSpouse ? 'inline-flex' : 'none';
    }
    const ocrBtn = document.getElementById('fmOcrBtn');
    if (ocrBtn) ocrBtn.style.display = 'inline-flex';
    const ocrInfo = document.getElementById('fmOcrInfo');
    if (ocrInfo) ocrInfo.textContent = '';

    // ── Adresse: Radio-Modus + Dropdown der MA-Zusatzadressen befüllen
    fmRefreshAddressUi(member?.alternativeAddressId ?? null, member?.lebtImHaushalt);

    // Zulagen-Block ist seit 18.05.2026 aus dem Modal entfernt (Walter:
    // Zulagen leben jetzt INLINE pro Kind in der Familie-Tab). Der
    // versteckte Mount-Point #fmAllowanceList bleibt für Backwards-Compat,
    // wird aber nicht mehr aktiv befüllt. Null-Checks aufs Add-Button-
    // Element, weil das DOM-Element nicht mehr existiert.
    const allowanceList = document.getElementById('fmAllowanceList');
    const allowanceAddBtn = document.getElementById('fmAllowanceAddBtn');
    if (member?.id && allowanceList) {
        // Nur laden falls jemand das Element via DevTools sichtbar gemacht hat
        // (kein normaler User-Pfad).
        loadFamilyAllowances(member.id);
    }
    if (allowanceAddBtn) allowanceAddBtn.style.display = member?.id ? 'inline-block' : 'none';

    document.getElementById('familyModal').style.display = 'flex';
    // Ausweis des Ehepartners daneben anzeigen (Walter-Vorgabe 12.07.2026):
    // beim Erfassen soll der geöffnete Scan sichtbar BLEIBEN — gleiche
    // Panel-Mechanik wie im Bewilligungs-Modal.
    famLoadSpouseDocs(member);
}

// ── Ehegatten-Ausweis-Panel neben dem Familien-Modal (Walter 12.07.2026) ──
// Zeigt die Ausweis-Dokumente aus ZWEI Quellen: das DIREKT am Familienmitglied
// verknüpfte Doku (member.dokumentId — der grüne «Doku verknüpft»-Badge) und
// alle spouse-getypten Dokumente des MA; bei mehreren mit Auswahl.
// Kein Dokument → Panel bleibt unsichtbar (kein Lärm bei Kindern etc.).
async function famLoadSpouseDocs(member) {
    const modal = document.getElementById('familyModal');
    if (!modal || !selectedEmployeeId) return;
    let panel = document.getElementById('fam-docpanel');
    if (!panel) {
        modal.style.gap = '14px';
        panel = document.createElement('div');
        panel.id = 'fam-docpanel';
        panel.className = 'side-docpanel';
        panel.style.cssText = 'display:none;background:#fff;border-radius:14px;flex:1;min-width:380px;padding:14px;flex-direction:column;gap:8px;box-shadow:0 24px 48px rgba(0,0,0,0.25)';
        panel.innerHTML = `
            <div style="display:flex;align-items:center;justify-content:space-between;gap:8px">
                <div id="fam-docname" style="font-size:12.5px;font-weight:700;color:#3f3f3f;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:0"></div>
                <button id="fam-doczoom" style="display:none;background:rgba(255,255,255,0.6);border:1px solid #e2ddd3;color:#3f3f3f;border-radius:10px;padding:6px 13px;font-size:12.5px;font-weight:600;cursor:pointer;flex-shrink:0" title="Im grossen Vorschaufenster öffnen (mit Drucken/Zoom)">⤢ Vergrössern</button>
            </div>
            <div id="fam-docview" class="side-docview" style="flex:1;min-height:0;overflow:auto;display:flex;align-items:flex-start;justify-content:center;background:#f6f3ee;border-radius:10px"></div>`;
        modal.appendChild(panel);
    }
    panel.style.display = 'none';
    try {
        // ── Quellen sammeln (Walter-Vorgabe 20.08.2026, «beim Erfassen des
        // Ehemanns ein Dokument daneben anzeigen»): nicht mehr NUR die
        // spouse-getypten Dokus — zusätzlich ALLE Dokumente des MA und die
        // Postfach-Eingänge (dort landet die frisch geschickte Ausweiskopie/
        // Heiratsurkunde meistens zuerst). Reihenfolge im Dropdown:
        // verknüpftes Doku → spouse-Dokus → Postfach (neueste zuerst) →
        // übrige Doku-Tab-Dokumente.
        const entries = [];
        const seenDoc = new Set();
        const pushDoc = (d, prefix) => {
            if (!d || seenDoc.has('d' + d.id)) return;
            seenDoc.add('d' + d.id);
            entries.push({
                previewUrl: `/api/documents/preview/${d.id}`,
                filename:   d.filenameOriginal || 'dokument',
                label:      `${prefix}${(d.bemerkung || '').trim() || d.filenameOriginal || 'Dokument'}`
                          + (d.geaendertAm || d.hochgeladenAm ? ' · ' + formatDate(d.geaendertAm || d.hochgeladenAm) : '')
            });
        };

        const r = await fetch(`/api/documents/by-field?employeeId=${selectedEmployeeId}&code=spouse&all=true`, { headers: ah() });
        let spouseDocs = r.ok ? await r.json() : [];
        if (!Array.isArray(spouseDocs)) spouseDocs = [];

        let alleDocs = [];
        try {
            const rd = await fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah() });
            if (rd.ok) alleDocs = await rd.json() || [];
        } catch (_) { /* best-effort */ }

        // 1) explizit verknüpftes Doku zuoberst
        if (member?.dokumentId) {
            const dm = spouseDocs.find(d => d.id === member.dokumentId)
                    || (alleDocs || []).find(d => d.id === member.dokumentId);
            pushDoc(dm, '');
        }
        // 2) spouse-getypte Dokus
        spouseDocs.forEach(d => pushDoc(d, ''));
        // 3) BEWUSST KEINE Postfach-Eingänge (Walter-Vorgabe 20.08.2026):
        // nur beim MA ABGELEGTE Dokumente anzeigen — ein Doku im Postfach
        // muss zuerst beim MA abgelegt werden, sonst könnten falsche/fremde
        // Dokus erscheinen (Postfach kann Sammel-/Momentaufnahmen enthalten).
        // 4) übrige Dokumente aus dem Doku-Tab (neueste zuerst).
        // Walter-Bug 25.08.2026: das frühere .slice(0, 25) hat bei MA mit
        // vielen Krank-/Taggeld-Belegen ältere Dokus (z.B. «Anmeldung
        // Quellensteuer») aus dem Dropdown gedrängt — ALLE anzeigen,
        // das Dropdown scrollt.
        (alleDocs || [])
            .sort((a, b) => String(b.geaendertAm || b.hochgeladenAm || '').localeCompare(String(a.geaendertAm || a.hochgeladenAm || '')))
            .forEach(d => pushDoc(d, '📂 '));

        if (entries.length === 0) return;
        window._famDocs = entries;
        panel.style.display = 'flex';
        // Box schmaler machen, damit Panel + Erfassung nebeneinander passen
        // (CSS-Regel #familyModal.fam-docs-open, Walter 20.08.2026).
        modal.classList.add('fam-docs-open');
        const nameEl = document.getElementById('fam-docname');
        if (entries.length > 1) {
            nameEl.innerHTML = `<select onchange="famShowSpouseDoc(parseInt(this.value))"
                style="max-width:100%;background:rgba(255,255,255,0.7);border:1px solid #e2ddd3;border-radius:10px;padding:5px 9px;font-size:12.5px;font-weight:600;color:#3f3f3f;cursor:pointer">
                ${entries.map((d, i) => `<option value="${i}">${esc(d.label)}</option>`).join('')}
            </select>`;
        }
        await famShowSpouseDoc(0);
    } catch (_) { /* Panel bleibt unsichtbar */ }
}

async function famShowSpouseDoc(idx) {
    const doc = (window._famDocs || [])[idx];
    if (!doc) return;
    if ((window._famDocs || []).length <= 1) {
        const nameEl = document.getElementById('fam-docname');
        if (nameEl) nameEl.textContent = doc.label || 'Ausweis Ehepartner';
    }
    const zoomBtn = document.getElementById('fam-doczoom');
    if (zoomBtn) {
        zoomBtn.style.display = 'inline-flex';
        zoomBtn.onclick = () => previewUrlFetch(doc.previewUrl, doc.filename || 'ausweis', ah());
    }
    try {
        const pr = await fetch(doc.previewUrl, { headers: ah() });
        if (!pr.ok) return;
        const blob = await pr.blob();
        const url = URL.createObjectURL(blob);
        const view = document.getElementById('fam-docview');
        if ((blob.type || '').startsWith('image/')) {
            view.innerHTML = `<img src="${url}" style="max-width:100%;max-height:100%;object-fit:contain">`;
        } else {
            view.innerHTML = `<iframe src="${url}" style="width:100%;height:100%;border:none;min-height:480px"></iframe>`;
        }
    } catch (_) { /* Vorschau best-effort */ }
}

// Permit-Typen + Nationalitäten ins Familienmitglied-Modal laden.
// Selbe Datenquellen wie im MA-Edit-Modal — wir cachen via getPermitTypes()
// + getNationalities() um nicht bei jedem Modal-Open neu zu fetchen.
async function fmFillPermitAndNationalitySelects(currentPermitTypeId, currentNationalityId) {
    const permitSel = document.getElementById('fmPermitTypeId');
    const natSel    = document.getElementById('fmNationalityId');
    if (permitSel) {
        try {
            const types = await getPermitTypes();
            permitSel.innerHTML = '<option value="">– keine / CH-Bürger –</option>'
                + (types || [])
                    .filter(p => p.isActive !== false)
                    .map(p => {
                        const sel = (currentPermitTypeId == p.id) ? 'selected' : '';
                        return `<option value="${p.id}" ${sel}>${p.description ?? p.code}</option>`;
                    }).join('');
        } catch { /* ignore */ }
    }
    if (natSel) {
        try {
            const nats = await getNationalities();
            natSel.innerHTML = '<option value="">–</option>'
                + (nats || [])
                    .filter(n => n.isActive !== false)
                    .map(n => {
                        const id    = n.id   ?? n.Id;
                        const code  = n.code ?? n.Code ?? '';
                        const name  = n.name ?? n.Name ?? code;
                        const sel   = (currentNationalityId == id) ? 'selected' : '';
                        // Ausweis-Kürzel (alpha-3) mit anzeigen (Walter 12.07.2026):
                        // die Ausweise drucken BGR/MKD/… — so kann man direkt abtippen.
                        const codes = code ? (n.code3 ? `${code} / ${n.code3}` : code) : '';
                        return `<option value="${id}" ${sel}>${name}${codes && code !== name ? ' (' + codes + ')' : ''}</option>`;
                    }).join('');
            natMakeCombo(natSel);   // Such-Combobox statt nativem Dropdown (Walter 12.07.2026)
        } catch { /* ignore */ }
    }
}

// ── Orts-Vorwärtssuche (Walter 20.08.2026) ─────────────────────────────
// Wer die PLZ nicht kennt, tippt den Ortsnamen an («Reid» → Reiden LU …);
// die Auswahl füllt PLZ + Ort + Kanton. Wiederverwendbar: im HTML
// oninput="ortNameSuggest(this, '<plzId>', '<ortId>', '<kantonId>')".
let _ortSugTimer = null, _ortSugBox = null;
function ortSugClose() { if (_ortSugBox) { _ortSugBox.remove(); _ortSugBox = null; } }
function ortNameSuggest(inp, plzId, ortId, kantonId, landId) {
    clearTimeout(_ortSugTimer);
    const q = (inp.value || '').trim();
    if (q.length < 2 || /^\d/.test(q)) { ortSugClose(); return; }
    _ortSugTimer = setTimeout(async () => {
        try {
            const res = await fetch(`/api/swiss-locations/by-name?q=${encodeURIComponent(q)}`, { headers: ah() });
            if (!res.ok) return;
            const locs = await res.json();
            ortSugClose();
            if (!Array.isArray(locs) || !locs.length) return;
            const r = inp.getBoundingClientRect();
            const box = document.createElement('div');
            box.style.cssText = `position:fixed;left:${r.left}px;top:${r.bottom + 4}px;width:${Math.max(r.width, 260)}px;z-index:9000;background:#fff;border:1px solid #e2ddd3;border-radius:10px;box-shadow:0 12px 28px rgba(60,55,48,0.18);max-height:240px;overflow:auto`;
            box.innerHTML = locs.map((l, i) =>
                `<div data-i="${i}" style="padding:7px 12px;font-size:13px;cursor:pointer;color:#3f3f3f">${esc(l.plz4)} ${esc(l.ortschaftsname)} <span style="color:#8b8b8b">${esc(plzLandVon(l) === 'CH' ? (l.kantonskuerzel || '') : ((l.region || '') + (l.land ? ' · ' + l.land : '')))}</span></div>`).join('');
            box.addEventListener('mousedown', e => {   // mousedown feuert VOR blur
                const t = e.target.closest('[data-i]');
                if (!t) return;
                const l = locs[parseInt(t.getAttribute('data-i'), 10)];
                const set = (id, v) => { const el = document.getElementById(id); if (el) el.value = v || ''; };
                set(plzId, l.plz4); set(ortId, l.ortschaftsname);
                plzTrefferAnwenden(l, null, document.getElementById(kantonId), null, document.getElementById(landId || ''));
                if (!landId && (plzLandVon(l) === 'CH')) set(kantonId, l.kantonskuerzel);
                ortSugClose();
                e.preventDefault();
            });
            document.body.appendChild(box);
            _ortSugBox = box;
            const closeOnBlur = () => { setTimeout(ortSugClose, 150); inp.removeEventListener('blur', closeOnBlur); };
            inp.addEventListener('blur', closeOnBlur);
        } catch (_) { /* best-effort */ }
    }, 250);
}

// ── Adresse-Auswahl im Familienmitglied-Modal ──────────────────────────
// Drei Zustände (Walter 25.08.2026): "same" = Ja, gleicher Haushalt;
// "extern" = Nein, nicht (mehr) im Haushalt (ohne erfasste Adresse);
// "alt"/"spouse" = andere bekannte Adresse (Zusatzadressen des MA bzw.
// die des Ehepartners). Dropdown wird live aus dem Backend befüllt —
// beim Speichern legen wir alternativeAddressId + lebtImHaushalt mit.
async function fmRefreshAddressUi(currentAlternativeAddressId, lebtImHaushalt) {
    const sameRadio   = document.getElementById('fmAddrSameAsEmp');
    const externRadio = document.getElementById('fmAddrExtern');
    const altRadio  = document.getElementById('fmAddrAlt');
    const summaryEl = document.getElementById('fmAddrEmpSummary');
    const altBox    = document.getElementById('fmAddrAltBox');
    const select    = document.getElementById('fmAlternativeAddressId');
    const hintEl    = document.getElementById('fmAddrAltHint');

    // Hauptadresse-Zusammenfassung anzeigen (vom geladenen MA-Datensatz)
    const emp = selectedEmployee;
    if (summaryEl && emp) {
        const parts = [
            emp.street,
            [emp.zipCode, stripCityCantonSuffix(emp.city)].filter(Boolean).join(' '),
            emp.country && emp.country.toLowerCase() !== 'schweiz' ? emp.country : null,
        ].filter(Boolean);
        summaryEl.textContent = parts.length ? parts.join(', ') : '— keine Hauptadresse erfasst —';
    }

    // Zusatzadressen des MA laden und Dropdown füllen
    if (select && selectedEmployeeId) {
        select.innerHTML = '<option value="">— Adresse wählen —</option>';
        try {
            const res = await fetch(`/api/employees/${selectedEmployeeId}/addresses`, { headers: ah() });
            if (res.ok) {
                const list = await res.json();
                list.forEach(a => {
                    const opt = document.createElement('option');
                    opt.value = a.id;
                    const summary = [
                        a.description || null,
                        [a.street, a.houseNumber].filter(Boolean).join(' '),
                        [a.zipCode, stripCityCantonSuffix(a.city)].filter(Boolean).join(' '),
                        a.country && a.country.toLowerCase() !== 'schweiz' ? a.country : null,
                    ].filter(Boolean).join(' · ');
                    opt.textContent = summary || `Adresse ${a.id}`;
                    opt.dataset.country = a.country || '';
                    select.appendChild(opt);
                });
                if (hintEl) {
                    hintEl.textContent = list.length === 0
                        ? 'Noch keine Zusatzadressen beim Mitarbeiter — über «+ Neue Zusatzadresse» eine anlegen.'
                        : '';
                }
            } else if (hintEl) {
                hintEl.textContent = '';
            }
        } catch { /* still */ }
    }
    // Kind: gleiche Zusatzadressen im Block «Wohnen & Unterhalt» (Walter 11.10.2026).
    const kindSel = document.getElementById('fmKindAdresseId');
    if (kindSel && select) {
        kindSel.innerHTML = select.innerHTML.replace('— Adresse wählen —', '— keine Adresse erfasst —');
        kindSel.value = currentAlternativeAddressId ? String(currentAlternativeAddressId) : '';
        fmKindAdresseSichtbarkeit();
    }

    // «Lebt beim Ehepartner» (Walter 21.08.2026): nur anbieten, wenn ein
    // Ehepartner mit ANDERER Adresse existiert und nicht gerade der
    // Ehepartner selbst bearbeitet wird — übernimmt dessen Zusatzadresse
    // (wichtig für QST/Halbfamilie: Kind nicht im Haushalt des MA).
    const spouseRow  = document.getElementById('fmAddrSpouseRow');
    const spouseRad  = document.getElementById('fmAddrSpouse');
    const spouseSum  = document.getElementById('fmAddrSpouseSummary');
    const editiertTyp = document.getElementById('fmMemberType')?.value;
    const spouse = (window._familyMembersCache || []).find(m =>
        m.memberType === 'Ehepartner' && m.alternativeAddressId
        && m.id !== editingFamilyMemberId);
    window._fmSpouseAltAddrId = (editiertTyp !== 'Ehepartner' && spouse)
        ? spouse.alternativeAddressId : null;
    if (spouseRow) {
        spouseRow.style.display = window._fmSpouseAltAddrId ? '' : 'none';
        if (spouseSum && spouse) {
            const a = spouse.alternativeAddress;
            spouseSum.textContent = a
                ? [ [a.street].filter(Boolean).join(' '),
                    [a.zipCode, stripCityCantonSuffix(a.city)].filter(Boolean).join(' ') ]
                    .filter(Boolean).join(', ')
                : `${spouse.firstName ?? ''} ${spouse.lastName ?? ''}`.trim();
        }
    }

    // Initial-Modus setzen (Walter 25.08.2026: lebtImHaushalt === false ohne
    // Adresse → «Nein, nicht im Haushalt»; undefined = Neuerfassung → «Ja»)
    const useSpouse = !!(window._fmSpouseAltAddrId && currentAlternativeAddressId
        && Number(currentAlternativeAddressId) === Number(window._fmSpouseAltAddrId));
    const useAlt = !!currentAlternativeAddressId && !useSpouse;
    const useExtern = !useAlt && !useSpouse && lebtImHaushalt === false;
    if (sameRadio && altRadio) {
        sameRadio.checked = !useAlt && !useSpouse && !useExtern;
        altRadio.checked  = useAlt;
    }
    if (externRadio) externRadio.checked = useExtern;
    if (spouseRad) spouseRad.checked = useSpouse;
    if (altBox) altBox.style.display = useAlt ? 'block' : 'none';
    if (select && useAlt) select.value = String(currentAlternativeAddressId);
    fmUpdateAuslandHint();
}

// Auslands-Partner-Hinweis (Walter 25.08.2026, Fall Flüchtlingsfamilien;
// erweitert 15.09.2026: gleicher Haushalt eines im Ausland wohnhaften MA
// zählt ebenfalls als Ausland — dann kein CH-Arbeitgeber).
function fmIstLandSchweiz(land) {
    if (typeof efIstCHAdresse === 'function') return efIstCHAdresse(land ?? 'CH');
    const l = String(land || 'CH').trim().toUpperCase();
    return l === '' || l === 'CH' || l === 'SCHWEIZ';
}

/** Partner gilt für QST-Mängel (Bewilligung / CH-Arbeitgeber) als in der CH. */
function fmPartnerGiltAlsInDerSchweiz(m) {
    if (!m) return fmPartnerGiltAlsInDerSchweizAusFormular();
    if (m.livesInSwitzerland === true) return true;
    if (m.lebtImHaushalt) return fmIstLandSchweiz(selectedEmployee?.country);
    return true;
}

function fmPartnerGiltAlsInDerSchweizAusFormular() {
    if (document.getElementById('fmLivesInSwitzerland')?.checked) return true;
    if (document.getElementById('fmAddrSameAsEmp')?.checked)
        return fmIstLandSchweiz(selectedEmployee?.country);
    if (document.getElementById('fmAddrAlt')?.checked
        || document.getElementById('fmAddrSpouse')?.checked) {
        const sel = document.getElementById('fmAlternativeAddressId');
        const land = sel?.selectedOptions?.[0]?.dataset?.country;
        if (land == null || land === '') return true;
        return fmIstLandSchweiz(land);
    }
    return true;
}

function fmSyncArbeitgeberSichtbarkeit() {
    const block = document.getElementById('fmArbeitgeberFelder');
    const aktiv = fmGetErwerb() === true;
    const inCh = fmPartnerGiltAlsInDerSchweizAusFormular();
    if (block) block.style.display = (aktiv && inCh) ? '' : 'none';
    const qst = document.getElementById('fmQstErwerbZeile');
    if (qst) qst.style.display = aktiv ? '' : 'none';
    if (aktiv && !inCh) {
        const k = document.getElementById('fmArbeitgeberKanton');
        if (k && !k.value) k.value = 'EX';
    }
}

function fmUpdateAuslandHint() {
    const hint = document.getElementById('fmAuslandHint');
    if (!hint) return;
    const typ = document.getElementById('fmMemberType')?.value || '';
    const istPartner = FM_ELTERN_TYPEN.includes(typ);
    const inCh = fmPartnerGiltAlsInDerSchweizAusFormular();
    hint.style.display = (istPartner && !inCh) ? '' : 'none';
    fmSyncArbeitgeberSichtbarkeit();
}

function fmAddrModeChanged() {
    fmUpdateAuslandHint();
    const useAlt = document.getElementById('fmAddrAlt')?.checked;
    const altBox = document.getElementById('fmAddrAltBox');
    if (altBox) altBox.style.display = useAlt ? 'block' : 'none';
    if (!useAlt) {
        // Auswahl beim Wechsel zurück auf "Hauptadresse" zurücksetzen,
        // damit kein verwaister AlternativeAddressId mitgespeichert wird.
        const sel = document.getElementById('fmAlternativeAddressId');
        if (sel) sel.value = '';
    }
}

// "+ Neue Zusatzadresse" im Familie-Modal: öffnet das bestehende
// Mitarbeiter-Adress-Modal. Nach Speichern dort wird die Liste hier
// neu geladen und die neue Adresse als Default ausgewählt.
function openEmployeeAddressModalFromFamily() {
    if (!selectedEmployeeId) {
        alert('Mitarbeiter zuerst laden.');
        return;
    }
    // Hook: nach erfolgreichem Speichern im Adress-Modal → unsere Liste auffrischen
    window._fmReloadAddressesAfterSave = true;
    openEmployeeAddressModal(null);
}

function closeFamilyModal() {
    window._famAfterSave = null;   // Abbruch → kein Dokument verknüpfen
    const modal = document.getElementById('familyModal');
    modal.style.display = 'none';
    modal.classList.remove('fam-docs-open');
    editingFamilyMemberId = null;
}

async function saveFamilyMember() {
    if (!selectedEmployeeId) return;

    const istKindTyp = (document.getElementById('fmMemberType').value || 'Kind') === 'Kind';

    // Adress-Modus: "alt" = abweichende Adresse aus den Zusatzadressen des MA;
    // "spouse" (Walter 21.08.2026) = Zusatzadresse des Ehepartners übernehmen;
    // "extern" (Walter 25.08.2026) = nicht (mehr) im Haushalt, keine Adresse.
    // Wenn "same" oder leeres Dropdown → AlternativeAddressId = null.
    // lebtImHaushalt = true NUR bei "same" — massgebend für die QST-Haushalt-
    // Logik (H-Tarif); der Server erzwingt zusätzlich Adresse ⇒ false.
    const addrUseSpouse = document.getElementById('fmAddrSpouse')?.checked && window._fmSpouseAltAddrId;
    const addrUseAlt = document.getElementById('fmAddrAlt')?.checked;
    const addrUseExtern = document.getElementById('fmAddrExtern')?.checked;
    const addrSelVal = document.getElementById('fmAlternativeAddressId')?.value || '';
    let alternativeAddressId = addrUseSpouse
        ? Number(window._fmSpouseAltAddrId)
        : (addrUseAlt && addrSelVal) ? parseInt(addrSelVal, 10) : null;
    let lebtImHaushalt = !(addrUseSpouse || addrUseAlt || addrUseExtern);
    // Kind (Walter 11.10.2026): Haushalt kommt aus dem Verlauf «Wohnen & Unterhalt»
    // (neu: Startwert), die Wohnadresse aus dem eigenen Feld im selben Block.
    if (istKindTyp) {
        lebtImHaushalt = fmKindImHaushalt();
        const kAdr = document.getElementById('fmKindAdresseId')?.value || '';
        alternativeAddressId = (!lebtImHaushalt && kAdr) ? parseInt(kAdr, 10) : null;
    }
    const aeWahl = document.getElementById('fmAndererElternteil')?.value ?? '';

    // Aufenthalt + Nationalität: leer = null. Permit-Type-Id und
    // Nationality-Id sind FK in der DB.
    const permitTypeId   = parseInt(document.getElementById('fmPermitTypeId').value, 10);
    const nationalityId  = parseInt(document.getElementById('fmNationalityId').value, 10);

    // Telefon wie beim MA normalisieren (+41 79 333 44 55).
    const phoneRaw = (document.getElementById('fmPhone')?.value || '').trim();
    const phoneFmt = phoneRaw ? window.formatPhoneIntl(phoneRaw) : '';
    if (phoneRaw && !/^\+\d{2}\s\d{2}\s\d{3}\s\d{2}\s\d{2}$/.test(phoneFmt)) {
        alert('Telefon-Format ungültig (erwartet +99 99 999 99 99, z.B. +41 79 409 43 33).');
        document.getElementById('fmPhone')?.focus();
        return;
    }
    if (phoneFmt) document.getElementById('fmPhone').value = phoneFmt;

    const payload = {
        memberType:             document.getElementById('fmMemberType').value         || 'Kind',
        gender:                 document.getElementById('fmGender').value             || null,
        firstName:              document.getElementById('fmFirstName').value          || null,
        lastName:               document.getElementById('fmLastName').value           || null,
        maidenName:             document.getElementById('fmMaidenName').value         || null,
        dateOfBirth:            document.getElementById('fmDateOfBirth').value        || null,
        socialSecurityNumber:   document.getElementById('fmSocialSecurity').value     || null,
        phone:                  phoneFmt || null,
        livesInSwitzerland:     document.getElementById('fmLivesInSwitzerland').checked,
        alternativeAddressId,
        lebtImHaushalt,
        permitTypeId:           (() => {
            const fromSel = Number.isFinite(permitTypeId) && permitTypeId > 0 ? permitTypeId : null;
            if (document.getElementById('fmPermitErst')?.style.display !== 'none') return fromSel;
            return window._fmPermitTypeId != null ? Number(window._fmPermitTypeId) || null : fromSel;
        })(),
        permitExpiryDate:       document.getElementById('fmPermitExpiry').value       || null,
        zemisNumber:            (document.getElementById('fmZemisNumber').value || '').trim() || null,
        nationalityId:          Number.isFinite(nationalityId) && nationalityId > 0 ? nationalityId : null,
        // Walter-Vorgabe 20.08.2026: QST-Relevanz-Felder.
        erwerbstaetig:          (document.getElementById('fmErwerbErst')?.style.display !== 'none'
                                    ? fmGetErwerb()
                                    : (window._fmErwerbStand === true || window._fmErwerbStand === false
                                        ? window._fmErwerbStand : fmGetErwerb())),
        // Konkubinat (Walter 25.08.2026, docs/konkubinat-qst-konzept.md)
        maHatHoeheresEinkommen:     fmGetMaEink(),
        // «nicht erfasst / unbekannt» = kein gemeinsames Kind; «offen» = NULL.
        gemeinsamesKindMitPartner:  aeWahl === '0' ? false : null,
        // Familienzulagen-Anmeldung (Walter 11.10.2026)
        familyStatus:           document.getElementById('fmFamilyStatus')?.value || null,
        beziehungZumMa:         document.getElementById('fmBeziehung')?.value || null,
        elterlicheSorge:        document.getElementById('fmElterlicheSorge')?.value || null,
        erwerbsunfaehig:        document.getElementById('fmErwerbsunfaehig')?.checked === true,
        andererElternteilId:    parseInt(aeWahl, 10) > 0 ? parseInt(aeWahl, 10) : null,
        startUnterhalt:         editingFamilyMemberId ? null : fmJnLesen('fmKindStartUnterhalt'),
        arbeitspensum:          (() => { const v = parseFloat(document.getElementById('fmArbeitspensum')?.value); return Number.isFinite(v) ? v : null; })(),
        arbeitgeberTelefon:     (document.getElementById('fmArbeitgeberTelefon')?.value || '').trim() || null,
        einkommenUeberMinimum:  fmPilleLesen('fmEinkMin'),
        seNeAngemeldet:         document.getElementById('fmSeNe')?.value || null,
        seNeAusgleichskasse:    (document.getElementById('fmSeNeKasse')?.value || '').trim() || null,
        beziehtLeistung:        fmJnLesen('fmBeziehtLeistung'),
        leistungVon:            (document.getElementById('fmLeistungVon')?.value || '').trim() || null,
        arbeitgeberName:        (document.getElementById('fmArbeitgeberName')?.value    || '').trim() || null,
        arbeitgeberStrasse:     (document.getElementById('fmArbeitgeberStrasse')?.value || '').trim() || null,
        arbeitgeberPlz:         (document.getElementById('fmArbeitgeberPlz')?.value     || '').trim() || null,
        arbeitgeberOrt:         (document.getElementById('fmArbeitgeberOrt')?.value     || '').trim() || null,
        arbeitgeberKanton:      (document.getElementById('fmArbeitgeberKanton')?.value  || '').trim().toUpperCase() || null,
        stellenantritt:         document.getElementById('fmStellenantritt')?.value      || null,
        stellenende:            document.getElementById('fmStellenende')?.value         || null,
        // Historie Seit/Erfahren (Walter 08.10.2026) — leer Seit = kein neuer Hist-Eintrag
        // ausser Ersteintrag (Server). Erfahren leer = gleich Seit.
        // Seit/Erfahren nur bei Ersterfassung (Status-Wechsel hat eigenen Speichern-Knopf).
        permitSeit:             (document.getElementById('fmPermitErst')?.style.display !== 'none'
                                    ? (document.getElementById('fmPermitSeit')?.value || null) : null),
        permitErfahrenAm:       (document.getElementById('fmPermitErst')?.style.display !== 'none'
                                    ? (document.getElementById('fmPermitErfahrenAm')?.value || null) : null),
        erwerbSeit:             (document.getElementById('fmErwerbErst')?.style.display !== 'none'
                                    ? (document.getElementById('fmErwerbSeit')?.value || null) : null),
        erwerbErfahrenAm:       (document.getElementById('fmErwerbErst')?.style.display !== 'none'
                                    ? (document.getElementById('fmErwerbErfahrenAm')?.value || null) : null),
        // Zulagen werden separat über /api/family-members/{id}/allowances verwaltet.
    };
    const _ps = payload.permitSeit, _pe = payload.permitErfahrenAm;
    if (_ps && _pe && _pe < _ps) {
        alert('Bewilligung: «Erfahren am» darf nicht vor «Seit» liegen.');
        return;
    }
    const _es = payload.erwerbSeit, _ee = payload.erwerbErfahrenAm;
    if (_es && _ee && _ee < _es) {
        alert('Erwerbstätigkeit: «Erfahren am» darf nicht vor «Seit» liegen.');
        return;
    }

    const isEdit = editingFamilyMemberId !== null;
    // Neu angelegt aus der Dokument-Ablage (Walter 25.09.2026): danach das Dokument anhängen.
    const famAfterSave = !isEdit ? window._famAfterSave : null;
    const url    = isEdit
        ? `/api/employees/${selectedEmployeeId}/family/${editingFamilyMemberId}`
        : `/api/employees/${selectedEmployeeId}/family`;
    const method = isEdit ? 'PUT' : 'POST';

    try {
        const res = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        if (!res.ok) {
            // Klartext statt generisch (Walter-Bug 25.08.2026: DB-Constraint-
            // Fehler beim neuen Typ Konkubinatspartner war unsichtbar).
            let msg = 'Fehler beim Speichern.';
            try {
                const j = await res.clone().json();
                msg = j.message || j.detail || j.error || j.title || msg;
            } catch {
                const txt = await res.text().catch(() => '');
                if (txt) msg = txt.slice(0, 300);
            }
            alert(msg);
            return;
        }
        let neuId = null;
        if (!isEdit) { try { neuId = (await res.clone().json())?.id ?? null; } catch {} }
        window._famAfterSave = null;
        closeFamilyModal();
        if (famAfterSave && neuId) { try { await famAfterSave(neuId); } catch (e) { console.error('famAfterSave', e); } }
        loadFamilieTab(selectedEmployeeId);
        // Neues Kind: Fenster gleich wieder öffnen, damit Ausbildung/Haushalt/
        // QST-Entscheid erfasst werden können (QST-Verlauf, Walter 11.10.2026).
        if (!isEdit && neuId && payload.memberType === 'Kind' && !famAfterSave) {
            try {
                const rk = await fetch(`/api/employees/${selectedEmployeeId}/family/${neuId}`, { headers: ah() });
                if (rk.ok) openFamilyModal(await rk.json());
            } catch { /* Liste ist schon neu geladen */ }
        }
        // QST-Prüfroutine (Walter 23.08.2026): Ehepartner/Kind-Änderungen
        // können Tarif + Kinderziffer kippen — sofort melden.
        qstRecheckNachAenderung(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

// ── Bewilligungs-/Erwerbs-Status (Walter 08.10.2026) ─────────────────────
// Aktueller Stand anzeigen; Wechsel nur über «Status ändern» + Seit/Erfahren.
function _fmHeuteIso() {
    const t = new Date();
    return `${t.getFullYear()}-${String(t.getMonth() + 1).padStart(2, '0')}-${String(t.getDate()).padStart(2, '0')}`;
}

function fmRefreshStatusUi(hasMemberId) {
    const typ = document.getElementById('fmMemberType')?.value || '';
    const partner = FM_ELTERN_TYPEN.includes(typ);
    const pErst = document.getElementById('fmPermitErst');
    const pBtn = document.getElementById('fmPermitWechselBtn');
    const eErst = document.getElementById('fmErwerbErst');
    const eBtn = document.getElementById('fmErwerbWechselBtn');
    const pHistLen = window._fmPermitHistCount || 0;
    const eHistLen = window._fmErwerbHistCount || 0;
    const needPermitErst = partner && (!hasMemberId || pHistLen === 0);
    const needErwerbErst = partner && (!hasMemberId || eHistLen === 0 || window._fmErwerbStand == null);
    if (pErst) pErst.style.display = needPermitErst ? '' : 'none';
    if (pBtn) pBtn.style.display = (partner && hasMemberId && !needPermitErst) ? '' : 'none';
    if (eErst) eErst.style.display = needErwerbErst ? '' : 'none';
    if (eBtn) eBtn.style.display = (partner && hasMemberId && !needErwerbErst) ? '' : 'none';
    fmUpdatePermitStandText();
    fmUpdateErwerbStandText();
    fmEinkommenZeile();
}

function fmUpdatePermitStandText() {
    const el = document.getElementById('fmPermitStandText');
    if (!el) return;
    const hist = window._fmPermitHistLatest;
    if (hist) {
        const ga = (hist.validFrom || '').slice(0, 10);
        const ea = (hist.erfahrenAm || ga || '').slice(0, 10);
        const eaTxt = ea && ea !== ga ? ` · erfahren ${formatDate(ea)}` : '';
        el.textContent = `${hist.permitLabel || hist.permitCode || 'CH / keine'} · seit ${ga ? formatDate(ga) : '–'}${eaTxt}`;
        return;
    }
    const sel = document.getElementById('fmPermitTypeId');
    const opt = sel?.selectedOptions?.[0];
    const label = (opt && opt.value) ? opt.textContent : null;
    el.textContent = label || '– noch nicht erfasst –';
}

function fmUpdateErwerbStandText() {
    const el = document.getElementById('fmErwerbStandText');
    if (!el) return;
    const hist = window._fmErwerbHistLatest;
    if (hist) {
        const erw = hist.erwerbstaetig === true ? 'Ja' : (hist.erwerbstaetig === false ? 'Nein' : '–');
        const ga = (hist.validFrom || '').slice(0, 10);
        const ea = (hist.erfahrenAm || ga || '').slice(0, 10);
        const eaTxt = ea && ea !== ga ? ` · erfahren ${formatDate(ea)}` : '';
        el.textContent = `${erw} · seit ${ga ? formatDate(ga) : '–'}${eaTxt}`;
        return;
    }
    const v = window._fmErwerbStand;
    el.textContent = v === true ? 'Ja' : (v === false ? 'Nein' : '– noch nicht erfasst –');
}

function fmFillPermitWechselSelect() {
    const src = document.getElementById('fmPermitTypeId');
    const dst = document.getElementById('fmPermitWechselTyp');
    if (!src || !dst) return;
    dst.innerHTML = src.innerHTML;
    const cur = window._fmPermitTypeId;
    dst.value = cur != null && cur !== '' ? String(cur) : '';
    dst.dispatchEvent(new Event('change', { bubbles: true }));
}

function fmOpenPermitWechsel() {
    if (!editingFamilyMemberId) { alert('Bitte zuerst speichern, dann den Status ändern.'); return; }
    fmFillPermitWechselSelect();
    document.getElementById('fmPermitWechselSeit').value = '';
    document.getElementById('fmPermitWechselEa').value = _fmHeuteIso();
    document.getElementById('fmPermitWechselPanel').style.display = '';
}

function fmClosePermitWechsel() {
    const p = document.getElementById('fmPermitWechselPanel');
    if (p) p.style.display = 'none';
}

async function fmSavePermitWechsel() {
    if (!selectedEmployeeId || !editingFamilyMemberId) return;
    const seit = document.getElementById('fmPermitWechselSeit')?.value;
    const ea = document.getElementById('fmPermitWechselEa')?.value || null;
    if (!seit) { alert('«Seit» (Wirkung) ist Pflicht.'); return; }
    if (ea && ea < seit) { alert('«Erfahren am» darf nicht vor «Seit» liegen.'); return; }
    const typRaw = document.getElementById('fmPermitWechselTyp')?.value;
    const permitTypeId = typRaw ? parseInt(typRaw, 10) : null;
    const typSel = document.getElementById('fmPermitWechselTyp');
    const neuCode = _permitOptToCode(typSel?.options[typSel.selectedIndex]);
    const altCode = window._fmPermitHistLatest?.permitCode
        ?? (window._fmPermitTypeId == null ? null
            : (document.getElementById('fmPermitTypeId')?.selectedOptions?.[0]
                ? _permitOptToCode(document.getElementById('fmPermitTypeId').selectedOptions[0])
                : null));
    const qstHinweis = qstHinweisBewilligungsWechsel(altCode, neuCode, { partner: true });
    try {
        const r = await fetch(`/api/employees/${selectedEmployeeId}/family/${editingFamilyMemberId}/permit-status`, {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ permitTypeId: Number.isFinite(permitTypeId) ? permitTypeId : null, seit, erfahrenAm: ea }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { alert(j.message || j.error || ('Fehler HTTP ' + r.status)); return; }
        window._fmPermitTypeId = j.permitTypeId ?? permitTypeId;
        const main = document.getElementById('fmPermitTypeId');
        if (main) main.value = window._fmPermitTypeId != null ? String(window._fmPermitTypeId) : '';
        fmClosePermitWechsel();
        await fmLoadPartnerHistories(editingFamilyMemberId);
        fmRefreshStatusUi(true);
        await qstZeigeFolgeHinweis(qstHinweis);
        if (typeof qstRecheckNachAenderung === 'function') qstRecheckNachAenderung(selectedEmployeeId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

function fmOpenErwerbWechsel() {
    if (!editingFamilyMemberId) { alert('Bitte zuerst speichern, dann den Status ändern.'); return; }
    const cur = window._fmErwerbStand;
    const want = cur === false ? 'ja' : 'nein'; // Vorschlag = Gegenteil
    document.querySelectorAll('input[name="fmErwerbWechsel"]').forEach(r => { r.checked = r.value === want; });
    document.getElementById('fmErwerbWechselSeit').value = '';
    document.getElementById('fmErwerbWechselEa').value = _fmHeuteIso();
    document.getElementById('fmErwerbWechselPanel').style.display = '';
}

function fmCloseErwerbWechsel() {
    const p = document.getElementById('fmErwerbWechselPanel');
    if (p) p.style.display = 'none';
}

async function fmSaveErwerbWechsel() {
    if (!selectedEmployeeId || !editingFamilyMemberId) return;
    const rSel = document.querySelector('input[name="fmErwerbWechsel"]:checked');
    if (!rSel || (rSel.value !== 'ja' && rSel.value !== 'nein')) { alert('Neuer Status (Ja/Nein) wählen.'); return; }
    const seit = document.getElementById('fmErwerbWechselSeit')?.value;
    const ea = document.getElementById('fmErwerbWechselEa')?.value || null;
    if (!seit) { alert('«Seit» (Wirkung) ist Pflicht.'); return; }
    if (ea && ea < seit) { alert('«Erfahren am» darf nicht vor «Seit» liegen.'); return; }
    const erwerbstaetig = rSel.value === 'ja';
    const alt = window._fmErwerbStand;
    const qstHinweis = (alt === true || alt === false)
        ? qstHinweisErwerbWechsel(alt, erwerbstaetig)
        : null;
    try {
        const r = await fetch(`/api/employees/${selectedEmployeeId}/family/${editingFamilyMemberId}/erwerb-status`, {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ erwerbstaetig, seit, erfahrenAm: ea }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { alert(j.message || j.error || ('Fehler HTTP ' + r.status)); return; }
        window._fmErwerbStand = erwerbstaetig;
        fmSetErwerb(erwerbstaetig);
        fmCloseErwerbWechsel();
        await fmLoadPartnerHistories(editingFamilyMemberId);
        fmRefreshStatusUi(true);
        await qstZeigeFolgeHinweis(qstHinweis);
        if (typeof qstRecheckNachAenderung === 'function') qstRecheckNachAenderung(selectedEmployeeId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

// Bewilligungs-/Erwerbs-Historie im Familien-Modal (Walter 08.10.2026).
async function fmLoadPartnerHistories(memberId) {
    if (!selectedEmployeeId || !memberId) return;
    const pEl = document.getElementById('fmPermitHistList');
    const eEl = document.getElementById('fmErwerbHistList');
    try {
        const [rp, re] = await Promise.all([
            fetch(`/api/employees/${selectedEmployeeId}/family/${memberId}/permit-history`, { headers: ah(), cache: 'no-store' }),
            fetch(`/api/employees/${selectedEmployeeId}/family/${memberId}/erwerb-history`, { headers: ah(), cache: 'no-store' }),
        ]);
        const pList = rp.ok ? await rp.json() : [];
        const eList = re.ok ? await re.json() : [];
        window._fmPermitHistCount = pList.length;
        window._fmErwerbHistCount = eList.length;
        window._fmPermitHistLatest = pList[0] || null;
        window._fmErwerbHistLatest = eList[0] || null;
        if (window._fmPermitHistLatest?.permitTypeId != null)
            window._fmPermitTypeId = window._fmPermitHistLatest.permitTypeId;
        if (window._fmErwerbHistLatest && (window._fmErwerbHistLatest.erwerbstaetig === true || window._fmErwerbHistLatest.erwerbstaetig === false))
            window._fmErwerbStand = window._fmErwerbHistLatest.erwerbstaetig;
        if (pEl) {
            if (!pList.length) { pEl.style.display = 'none'; pEl.innerHTML = ''; }
            else {
                pEl.style.display = 'block';
                pEl.innerHTML = `<div style="font-size:11px;font-weight:700;color:#8b8b8b;margin-bottom:4px">Bisherige Statusänderungen</div>`
                    + pList.map((h, i) => {
                        const ga = (h.validFrom || '').slice(0, 10);
                        const ea = (h.erfahrenAm || ga || '').slice(0, 10);
                        const eaTxt = ea && ea !== ga
                            ? ` · erfahren ${formatDate(ea)} <span style="color:#c2410c">(versetzt)</span>`
                            : (ea ? ` · erfahren ${formatDate(ea)}` : '');
                        return `<div style="display:flex;align-items:center;gap:8px;font-size:12.5px;padding:6px 10px;margin-bottom:4px;border-radius:8px;background:${i===0?'#fff':'rgba(255,255,255,0.45)'};box-shadow:0 1px 3px rgba(60,55,48,0.08)">
                            <span style="flex-shrink:0;font-size:10px;font-weight:700;border-radius:999px;padding:1px 8px;${i===0?'color:#166534;background:#dcfce7':'color:#6b6152;background:#ece9e2'}">${i===0?'aktuell':'früher'}</span>
                            <div style="flex:1;min-width:0"><b>${esc(h.permitLabel || h.permitCode || 'CH / keine')}</b>
                            <span style="color:#8b8b8b"> · seit ${ga ? formatDate(ga) : '–'}${eaTxt}</span></div>
                            <button type="button" onclick="fmDeletePermitHist(${memberId},${h.id})" title="Eintrag löschen" style="background:#fff;border:1px dashed #fca5a5;color:#991b1b;border-radius:6px;padding:2px 7px;font-size:11px;cursor:pointer">🗑</button>
                        </div>`;
                    }).join('');
            }
        }
        if (eEl) {
            if (!eList.length) { eEl.innerHTML = ''; }
            else {
                eEl.innerHTML = `<div style="font-size:11px;font-weight:700;color:#8b8b8b;margin:2px 0 4px">Bisherige Statusänderungen</div>`
                    + eList.map((h, i) => {
                        const ga = (h.validFrom || '').slice(0, 10);
                        const ea = (h.erfahrenAm || ga || '').slice(0, 10);
                        const erw = h.erwerbstaetig === true ? 'Ja' : (h.erwerbstaetig === false ? 'Nein' : '–');
                        const eaTxt = ea && ea !== ga
                            ? ` · erfahren ${formatDate(ea)} <span style="color:#c2410c">(versetzt)</span>`
                            : (ea ? ` · erfahren ${formatDate(ea)}` : '');
                        return `<div style="display:flex;align-items:center;gap:8px;font-size:12.5px;padding:6px 10px;margin-bottom:4px;border-radius:8px;background:${i===0?'#fff':'rgba(255,255,255,0.45)'};box-shadow:0 1px 3px rgba(60,55,48,0.08)">
                            <span style="flex-shrink:0;font-size:10px;font-weight:700;border-radius:999px;padding:1px 8px;${i===0?'color:#166534;background:#dcfce7':'color:#6b6152;background:#ece9e2'}">${i===0?'aktuell':'früher'}</span>
                            <div style="flex:1;min-width:0"><b>${erw}</b>
                            <span style="color:#8b8b8b"> · seit ${ga ? formatDate(ga) : '–'}${eaTxt}</span></div>
                            <button type="button" onclick="fmDeleteErwerbHist(${memberId},${h.id})" title="Eintrag löschen" style="background:#fff;border:1px dashed #fca5a5;color:#991b1b;border-radius:6px;padding:2px 7px;font-size:11px;cursor:pointer">🗑</button>
                        </div>`;
                    }).join('');
            }
        }
        fmRefreshStatusUi(true);
    } catch (e) {
        if (pEl) pEl.innerHTML = `<div style="color:#b91c1c;font-size:12px">Historie: ${esc(e.message)}</div>`;
    }
}

async function fmDeletePermitHist(memberId, histId) {
    if (!selectedEmployeeId || !(await liquidConfirm('Diesen Bewilligungs-Eintrag löschen?', { yesLabel: 'Löschen' }))) return;
    const r = await fetch(`/api/employees/${selectedEmployeeId}/family/${memberId}/permit-history/${histId}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { alert('Löschen fehlgeschlagen.'); return; }
    await fmLoadPartnerHistories(memberId);
}

async function fmDeleteErwerbHist(memberId, histId) {
    if (!selectedEmployeeId || !(await liquidConfirm('Diesen Erwerbs-Eintrag löschen?', { yesLabel: 'Löschen' }))) return;
    const r = await fetch(`/api/employees/${selectedEmployeeId}/family/${memberId}/erwerb-history/${histId}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) { alert('Löschen fehlgeschlagen.'); return; }
    await fmLoadPartnerHistories(memberId);
}

async function deleteFamilyMember(id) {
    if (!(await liquidConfirm('Diesen Eintrag wirklich löschen?'))) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/family/${id}`, {
            method: 'DELETE',
            headers: ah()
        });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        loadFamilieTab(selectedEmployeeId);
        // QST-Prüfroutine (Walter 23.08.2026): z.B. Ehemann gelöscht
        // (Scheidung) → Tarif B/C stimmt nicht mehr, oder H-Ziffer ändert.
        qstRecheckNachAenderung(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

