// ── Familie Tab laden ──────────────────────────
async function loadFamilieTab(employeeId) {
    const el = document.getElementById('familieContent');
    if (!el) return;
    el.innerHTML = '<div class="emp-placeholder"><span>Wird geladen...</span></div>';
    try {
        const res = await fetch(`/api/employees/${employeeId}/family`, { headers: ah() });
        if (!res.ok) { el.innerHTML = '<div class="emp-placeholder"><span>Fehler beim Laden</span></div>'; return; }
        const members = await res.json();
        // Walter 18.05.2026: Zulagen pro Kind vorab parallel laden für die
        // Inline-Darstellung (analog Bank-Liste).
        const kinder = members.filter(m => m.memberType === 'Kind');
        const allowanceMap = {};
        if (kinder.length) {
            await Promise.all(kinder.map(async k => {
                try {
                    const r = await fetch(`/api/family-members/${k.id}/allowances`, { headers: ah() });
                    allowanceMap[k.id] = r.ok ? await r.json() : [];
                } catch {
                    allowanceMap[k.id] = [];
                }
            }));
        }
        // Walter 11.06.2026: bei Frauen ALLE Schwangerschaften mitladen
        // (inkl. Fristen pro Eintrag) — das Mutterschafts-Modul lebt jetzt
        // komplett im Familie-Tab. Aktive (laufende) Schwangerschaft als
        // erste Card, ältere darunter, Erfass-Button unten.
        let pregnancyDetails = [];
        const emp = selectedEmployee;
        if (emp && IstWeiblich(emp.gender)) {
            try {
                const r = await fetch(`/api/pregnancies?employeeId=${employeeId}`, { headers: ah() });
                if (r.ok) {
                    const list = await r.json();
                    pregnancyDetails = await Promise.all(list.map(p =>
                        fetch(`/api/pregnancies/${p.id}`, { headers: ah() }).then(rr => rr.ok ? rr.json() : null)));
                    pregnancyDetails = pregnancyDetails.filter(Boolean);
                }
            } catch {}
        }
        // Walter-Vorgabe 13.06.2026: QST-Pflicht-Status mitladen, damit der
        // Familie-Tab beim Ehepartner einen roten Warnbanner zeigen kann,
        // wenn die QST-Befreiung über den Spouse läuft und das Beleg-Doku
        // fehlt (analog zum Banner im Bewilligung/QST-Tab).
        let pflicht = null;
        try {
            const pr = await fetch(`/api/employees/${employeeId}/qst-pflicht`, { headers: ah() });
            if (pr.ok) pflicht = await pr.json();
        } catch {}
        window._famPflichtCache = pflicht;
        renderFamilieTab(el, members, employeeId, allowanceMap, pregnancyDetails, pflicht);
    } catch {
        el.innerHTML = '<div class="emp-placeholder"><span>Verbindungsfehler</span></div>';
    }
}

// Mutterschafts-Block des Familie-Tabs (Walter 11.06.2026; als Helper
// extrahiert 16.07.2026, weil er bei MA OHNE Familienmitglieder durch den
// early-return verloren ging — Walter-Bug: «hier konnte ich frueher die
// mutterschaft eintragen»). Nur bei Frauen.
function _familieMutterschaftHtml(employeeId, pregnancyDetails) {
    const empF = selectedEmployee;
    if (!empF || !IstWeiblich(empF.gender)) return '';
    const list = pregnancyDetails || [];
    // Abgeschlossene Schwangerschaften ausblenden (Walter 12.08.2026):
    // sichtbar ist nur die laufende (bis 16 Wochen nach Geburt/ET, gleiches
    // Fenster wie Badge/Kündigungsschutz). Ältere sind Historie und stehen
    // eingeklappt hinter «frühere anzeigen».
    const _mtsHeute = new Date().toISOString().slice(0, 10);
    const _mtsIsCurrent = d => {
        const p = d?.pregnancy || {};
        const basis = p.geburtsdatum || p.errechneterTermin;
        if (!basis) return true; // ohne Datum sicherheitshalber zeigen
        const ende = new Date(basis); ende.setDate(ende.getDate() + 16 * 7);
        return ende.toISOString().slice(0, 10) >= _mtsHeute;
    };
    const current = list.filter(_mtsIsCurrent);
    const older   = list.filter(d => !_mtsIsCurrent(d))
        .sort((a, b) => String(b?.pregnancy?.errechneterTermin || '').localeCompare(String(a?.pregnancy?.errechneterTermin || '')));
    // Button nur wenn keine LAUFENDE offene Schwangerschaft (ohne Geburt) —
    // ältere/abgeschlossene blockieren «+ Schwangerschaft erfassen» nicht
    // mehr (Walter 27.07.2026, präzisiert 12.08.2026).
    const hasOpen = current.some(d => d?.pregnancy && !d.pregnancy.geburtsdatum);
    const addBtn = hasOpen ? '' : `
            <button type="button" class="btn-emp-add" style="padding:6px 14px;font-size:12px;margin-left:auto" onclick="mtsOpenNew(${employeeId})">+ Schwangerschaft erfassen</button>`;
    const olderHtml = older.length ? `
            <div style="margin-top:10px">
                <a href="#" id="mtsOldToggle" style="font-size:12px;color:#8b8b8b;text-decoration:underline"
                   onclick="document.getElementById('mtsOldWrap').style.display='block';this.style.display='none';return false">
                   ${older.length} frühere Schwangerschaft${older.length > 1 ? 'en' : ''} anzeigen</a>
                <div id="mtsOldWrap" style="display:none;opacity:0.75">${older.map(d => renderPregnancyCard(d)).join('')}</div>
            </div>` : '';
    return `
        <div class="emp-section-title" style="margin-top:24px;display:flex;align-items:center;justify-content:space-between">
            <span>Mutterschaft</span>
            ${addBtn}
        </div>
        <div id="mutterschaftContent">
            ${current.length
                ? current.map(d => renderPregnancyCard(d)).join('')
                : `<div class="emp-placeholder" style="padding:24px"><span>${list.length ? 'Keine laufende Schwangerschaft.' : 'Keine Schwangerschaft erfasst.'}</span></div>`
            }
            ${olderHtml}
        </div>`;
}

function renderFamilieTab(el, members, employeeId, allowanceMap = {}, pregnancyDetails = [], pflicht = null) {
    // Cache für Detail-Popup-Lookup
    window._familyMembersCache = members;

    // Walter-Vorgabe 27.07.2026: „+ Familienmitglied" in der Leerzeile /
    // im Listen-Kopf — NICHT oben rechts im Header (empTabActionBar).
    const isExcluded = !!selectedEmployee?.isPayrollExcluded;
    const addBtn = isExcluded ? '' : `
        <button type="button" class="btn-emp-add" onclick="openFamilyModal(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            ${_t('famTab.add','Familienmitglied')}
        </button>`;

    if (!members.length) {
        el.innerHTML = `
        <div class="emp-familie-empty-row">
            <div class="emp-familie-empty-msg">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/></svg>
                <span>${_t('famTab.empty','Keine Familienmitglieder erfasst')}</span>
            </div>
            ${addBtn}
        </div>` + _familieMutterschaftHtml(employeeId, pregnancyDetails);
        return;
    }

    // Anmeldung Familienzulagen (GastroSocial 0402): Zuhause ist der HR-Hub; der Knopf
    // neben «Kinder» ist eine Abkürzung zum Testen (Walter 11.10.2026).
    const famzBtn = isExcluded ? '' : `<button type="button" onclick="famzOpenForEmployee(${employeeId})"
        title="Formular «Anmeldung Familienzulagen» vorausgefüllt erstellen (wie im HR-Hub)"
        style="margin-left:12px;background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:5px 12px;font-size:12px;font-weight:600;cursor:pointer;vertical-align:middle">📄 Anmeldung Familienzulagen</button>`;
    const listHead = `
    <div class="emp-section-title" style="margin-top:0;display:flex;align-items:center;justify-content:space-between;gap:12px">
        <span>${_t('famTab.listTitle','Familienmitglieder')}</span>
        ${addBtn}
    </div>`;

    // Gruppieren nach Typ
    const groups = {};
    members.forEach(m => {
        if (!groups[m.memberType]) groups[m.memberType] = [];
        groups[m.memberType].push(m);
    });

    // Walter-Bug 25.08.2026: Konkubinatspartner fehlte in der Reihenfolge →
    // Karte verschwand nach dem Typ-Wechsel. Zusätzlich Robustheits-Netz:
    // UNBEKANNTE Typen werden hinten angehängt statt verschluckt.
    const typeOrder = ['Ehepartner', 'Konkubinatspartner', 'AndererElternteil', 'Kind', 'Mutter', 'Vater', 'Sonstige'];
    Object.keys(groups).forEach(t => { if (!typeOrder.includes(t)) typeOrder.push(t); });
    // Walter-Vorgabe 13.06.2026: roter Warnbanner ganz oben, wenn die QST-
    // Befreiung über den Ehepartner läuft und das Beleg-Doku noch nicht am
    // Family-Member verknüpft ist. Klick öffnet das gleiche Auswahl+Upload-
    // Modal wie für ID/Pass / C-Ausweis beim MA selbst.
    let spouseTopBanner = '';
    if (pflicht && pflicht.spouseDokumentFehlt && pflicht.spouseFamilyMemberId
        && (pflicht.befreiungsGrund === 'Ehepartner-CH' || pflicht.befreiungsGrund === 'Ehepartner-C')) {
        const grundText = pflicht.befreiungsGrund === 'Ehepartner-CH'
            ? 'Der Ehepartner ist Schweizer Staatsbürger — bitte Pass oder Identitätskarte des Ehepartners hier verknüpfen.'
            : 'Der Ehepartner hat einen C-Ausweis — bitte das Bewilligungs-Dokument des Ehepartners hier verknüpfen.';
        spouseTopBanner = `
        <div style="background:#fef2f2;border:1px solid #fca5a5;border-left:4px solid #dc2626;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
            <span style="font-size:18px">⚠️</span>
            <div style="flex:1;min-width:200px">
                <div style="font-weight:700;color:#991b1b;font-size:13px">Ausweis Ehepartner fehlt für die QST-Befreiung</div>
                <div style="color:#b91c1c;font-size:12px;margin-top:2px">${grundText}</div>
            </div>
            <button onclick="openAusweisDokuModal(${employeeId},'spouse',{spouseFamilyMemberId:${pflicht.spouseFamilyMemberId}})" style="background:#dc2626;color:#fff;border:none;padding:7px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;margin-left:auto;white-space:nowrap">
                📎 Dokument verknüpfen
            </button>
        </div>`;
    }

    let html = spouseTopBanner + listHead;

    // Display-Label für Mitglieds-Typen (mit Plural für „Kinder").
    const typeLabel = (type, count) => {
        const isEn = window.i18n && window.i18n.getLang && i18n.getLang() === 'en';
        if (type === 'Kind' && count > 1) return isEn ? 'Children' : 'Kinder';
        if (type === 'Konkubinatspartner') return 'Konkubinatspartner/in';
        if (type === 'AndererElternteil') return count > 1 ? 'Andere Elternteile' : 'Anderer Elternteil';
        return _t('fam.value.type.' + type, type);
    };
    const yearsLabel = window.i18n && window.i18n.getLang && i18n.getLang() === 'en' ? 'years' : 'Jahre';

    // Gruppen klar abgrenzen (Walter 08.09.2026): jede Gruppe (Ehepartner,
    // Kinder …) in einem eigenen Rahmen mit Anzahl im Titel.
    // Emoji-Symbole raus (Walter 16.09.2026): kindisch, nicht Kohle-Design.
    const famGroupCls = {
        Ehepartner: 'fam-group-partner',
        Konkubinatspartner: 'fam-group-partner',
        AndererElternteil: 'fam-group-partner',
        Kind: 'fam-group-kinder',
        Mutter: 'fam-group-eltern',
        Vater: 'fam-group-eltern',
        Sonstige: 'fam-group-sonstige',
    };
    typeOrder.forEach(type => {
        if (!groups[type]) return;
        const sectionTitle = typeLabel(type, groups[type].length);
        const cls = famGroupCls[type] || famGroupCls.Sonstige;
        const n = groups[type].length;
        html += `<div class="fam-group ${cls}">`;
        html += `<div class="emp-section-title fam-group-title">${sectionTitle}${n > 1 ? ` <span class="fam-group-count">${n}</span>` : ''}${type === 'Kind' ? famzBtn : ''}</div>`;
        // Walter 18.07.2026: kompakte Kachel-Raster statt vollbreiter
        // Listen + leerer Zulagen-Boxen (war unübersichtlich bei mehreren Kindern).
        html += `<div class="fam-tile-grid">`;
        groups[type].forEach(m => {
            const name = ((m.firstName ?? '') + ' ' + (m.lastName ?? '')).trim() || '–';
            const dob  = m.dateOfBirth ? formatDate(m.dateOfBirth) : '';
            const age  = m.dateOfBirth ? calcAge(m.dateOfBirth) : null;
            const phoneMeta = m.phone ? esc(m.phone) : '';
            const metaParts = [
                dob ? `${dob}${age !== null ? ' · ' + age + ' ' + yearsLabel : ''}` : '',
                phoneMeta
            ].filter(Boolean);
            const meta = metaParts.join(' · ');

            // Walter-Vorgabe 07.06.2026: Beim EHEPARTNER (nicht bei Kindern!)
            // die Bewilligung + Ablaufdatum als Badge anzeigen, plus einen
            // Doku-Button für die Ausweis-Kopie.
            let spousePermitBadge = '';
            let spouseDocBtn = '';
            if (type === 'Ehepartner') {
                const pCode = m.permitType?.code || null;
                const pDesc = m.permitType?.description || null;
                const pExp  = m.permitExpiryDate ? formatDate(m.permitExpiryDate) : null;
                const natCode = (m.nationalityCode || '').toUpperCase();
                const isCh = natCode === 'CH';
                if (pCode || pDesc || pExp) {
                    let nameLabel;
                    if (pCode && pDesc)      nameLabel = `${esc(pCode)} — ${esc(pDesc)}`;
                    else if (pCode)          nameLabel = esc(pCode);
                    else if (pDesc)          nameLabel = esc(pDesc);
                    else                     nameLabel = '';
                    const label = nameLabel
                        ? (pExp ? `${nameLabel} bis ${pExp}` : nameLabel)
                        : `bis ${pExp}`;
                    spousePermitBadge = `<span class="fam-tile-badge fam-tile-badge-permit" title="Bewilligung Ehepartner">📋 ${label}</span>`;
                } else if (isCh) {
                    spousePermitBadge = `<span class="fam-tile-badge fam-tile-badge-ch" title="CH-Bürger — keine Bewilligung nötig">🇨🇭 CH-Bürger</span>`;
                } else if (!fmPartnerGiltAlsInDerSchweiz(m)) {
                    spousePermitBadge = `<span class="fam-tile-badge" title="Lebt nicht in der Schweiz — keine CH-Bewilligung nötig">🌍 Ausland</span>`;
                } else {
                    spousePermitBadge = `<span class="fam-tile-badge fam-tile-badge-warn" title="Keine Bewilligung erfasst">⚠ Keine Bewilligung</span>`;
                }
                // Walter-Vorgabe 20.08.2026: Erwerbstätig-Badge am Ehepartner —
                // rot wenn die Frage offen ist (blockt bei QST-pflichtigen
                // verheirateten MA den Lohnlauf).
                // Pflicht-Badges nur, wenn der MA verheiratet UND nicht getrennt
                // ist (Walter 26.08.2026, Fall Helmt Zug: bei Zivilstand
                // «getrennt» gilt Tarif A/H — Arbeitgeber/Erwerb des Ehepartners
                // sind dann irrelevant, der Server blockt auch nicht mehr).
                const _msFam = ((selectedEmployee?.zivilstand ?? selectedEmployee?.maritalStatus) || '').toLowerCase();
                // Walter-Vorgabe 29.08.2026: ist der Ehepartner CH-Bürger ODER
                // C-Ausweis-Inhaber UND lebt in der Schweiz, ist der MA von der
                // QST BEFREIT (Befreiungsgründe 4/5) — die Erwerbstätigkeit
                // interessiert dann nicht, keine ⚠-Badges (der Server blockt
                // solche MA im Lohnlauf ohnehin nicht).
                const _partnerBefreit = (isCh || ['C', 'C_EU_EFTA'].includes((pCode || '').toUpperCase()))
                    && m.livesInSwitzerland === true;
                const _partnerPflicht = (_msFam.includes('verheiratet') || (_msFam.includes('partnerschaft') && !_msFam.includes('aufgel')))
                    && !_msFam.includes('getrennt') && !selectedEmployee?.separatedSince
                    && !_partnerBefreit;
                if (m.erwerbstaetig === true) {
                    const agTxt = [m.arbeitgeberName, m.arbeitgeberOrt].filter(Boolean).join(', ');
                    spousePermitBadge += `<span class="fam-tile-badge" style="background:#dcfce7;color:#166534" title="Erwerbstätig${agTxt ? ' bei ' + esc(agTxt) : ''}">💼 erwerbstätig${agTxt ? ' · ' + esc(agTxt) : ''}</span>`;
                    if (!m.arbeitgeberName && !m.arbeitgeberKanton && !m.arbeitgeberOrt && _partnerPflicht && fmPartnerGiltAlsInDerSchweiz(m))
                        spousePermitBadge += `<span class="fam-tile-badge fam-tile-badge-warn" title="Arbeitgeber fehlt — blockt den Lohnlauf">⚠ Arbeitgeber fehlt</span>`;
                } else if (m.erwerbstaetig === false) {
                    spousePermitBadge += `<span class="fam-tile-badge" title="Nicht erwerbstätig">nicht erwerbstätig</span>`;
                } else if (_partnerPflicht) {
                    spousePermitBadge += `<span class="fam-tile-badge fam-tile-badge-warn" title="Erwerbstätig-Frage offen — blockt bei QST-pflichtigen verheirateten MA den Lohnlauf">⚠ Erwerbstätig?</span>`;
                }
                // Doku-Symbol wie überall (Walter 26.09.2026)
                spouseDocBtn = docIconBtn({ docId: m.dokumentId, was: 'Ausweis Partner/in', stop: true,
                    verknuepfen: `openAusweisDokuModal(${employeeId},'spouse',{spouseFamilyMemberId:${m.id}})`,
                    loesen: `spouseDokuUnlink(${employeeId}, ${m.id})` });
            }

            // Walter-Vorgabe 20.08.2026: Kind in Erstausbildung — Kinderziffer
            // läuft über den 18. Geburtstag hinaus (KS 45; Beleg hinterlegen).
            if (type === 'Kind' && m.keineUnterhaltspflicht) {
                spousePermitBadge += `<span class="fam-tile-badge" style="background:#fee2e2;color:#991b1b" title="Heute kein Unterhalt zur Hauptsache durch den MA (Verlauf «Wohnen & Unterhalt») — zählt nicht für die QST-Kinderziffer">⊘ kein Unterhalt</span>`;
            }
            if (type === 'Kind' && m.inErstausbildung && !m.keineUnterhaltspflicht) {
                spousePermitBadge += `<span class="fam-tile-badge" style="background:#dbeafe;color:#1d4ed8" title="In Erstausbildung — QST-Kinderziffer läuft über 18 hinaus (Lehrvertrag/Immatrikulation als Beleg)">🎓 Erstausbildung</span>`;
            }
            // Konkubinat (Walter 25.08.2026): gemeinsames Kind mit dem
            // K-Partner — entscheidet die H1/A0-Einkommensregel.
            if (type === 'Kind' && m.gemeinsamesKindMitPartner === true) {
                spousePermitBadge += `<span class="fam-tile-badge" style="background:#fce7f3;color:#9d174d" title="Gemeinsames Kind mit dem Konkubinatspartner — H1/A0 nach höherem Bruttoeinkommen (nie beide H1)">👪 gemeinsames Kind</span>`;
            }
            // Belege am Kind (Walter 23.09.2026): Ausweis (nur Anzeige, wenn
            // verknüpft) + Geburtsurkunde (verknüpfen/öffnen).
            if (type === 'Kind') {
                // Doku-Symbole wie überall (Walter 26.09.2026): Ausweis nur, wenn
                // verknüpft (Ausweis ist beim Kind keine Pflicht), Geburtsurkunde immer.
                const famUrl = `/api/employees/${employeeId}/family/${m.id}/dokument`;
                if (m.dokumentId)
                    spouseDocBtn += docIconBtn({ docId: m.dokumentId, was: 'Ausweis Kind', stop: true, style: 'margin-right:4px',
                        verknuepfen: `openAusweisDokuModal(${employeeId},'spouse',{spouseFamilyMemberId:${m.id}})`,
                        loesen: `docLoesenPatch('${famUrl}','Ausweis Kind',()=>loadFamilieTab(${employeeId}))` });
                spouseDocBtn += docIconBtn({ docId: m.geburtsurkundeDokumentId, was: 'Geburtsurkunde', stop: true,
                    verknuepfen: `openAusweisDokuModal(${employeeId},'fam_geburtsurkunde',{familyMemberId:${m.id}})`,
                    loesen: `docLoesenPatch('${famUrl}','Geburtsurkunde',()=>loadFamilieTab(${employeeId}),{art:'geburtsurkunde'})` });
            }
            if (type === 'Konkubinatspartner') {
                spousePermitBadge += `<span class="fam-tile-badge" style="background:#fce7f3;color:#9d174d" title="Konkubinat — befreit NICHT von der QST (auch mit CH/C); H1/A0 läuft über das gemeinsame Kind">💞 Konkubinat</span>`;
            }
            if (type === 'AndererElternteil' || type === 'Konkubinatspartner') {
                if (m.erwerbstaetig === true) {
                    const agTxt = [m.arbeitgeberName, m.arbeitgeberOrt].filter(Boolean).join(', ');
                    spousePermitBadge += `<span class="fam-tile-badge" style="background:#dcfce7;color:#166534">💼 erwerbstätig${agTxt ? ' · ' + esc(agTxt) : ''}</span>`;
                } else if (m.erwerbstaetig === false) {
                    spousePermitBadge += `<span class="fam-tile-badge">nicht erwerbstätig</span>`;
                }
            }
            // Erstanspruch Familienzulagen (Walter 11.10.2026): verdient der
            // andere Elternteil mehr, bezieht in der Regel er/sie die Kinderzulage.
            if (FM_ELTERN_TYPEN.includes(type) && m.erwerbstaetig === true && m.maHatHoeheresEinkommen === false) {
                spousePermitBadge += `<span class="fam-tile-badge fam-tile-badge-warn" title="Verdient mehr als der MA — in der Regel bezieht diese Person die Kinderzulage (Erstanspruch). Zulage bei uns nur mit Verfügung der Familienausgleichskasse.">⚠ verdient mehr · Erstanspruch Kinderzulage</span>`;
            }

            let addrBadge = '';
            if (m.alternativeAddress) {
                const a = m.alternativeAddress;
                const ort = [a.zipCode, stripCityCantonSuffix(a.city)].filter(Boolean).join(' ');
                const land = a.country && a.country.toLowerCase() !== 'schweiz' ? a.country : '';
                const tip  = [a.description, [a.street, a.street2].filter(Boolean).join(' / '), ort, land].filter(Boolean).join(' · ');
                const short = ort || a.description || a.country || 'andere Adresse';
                addrBadge = `<span class="fam-tile-badge fam-tile-badge-addr" title="${esc(tip)}">📍 ${esc(short)}</span>`;
            } else if (m.lebtImHaushalt === false) {
                // Walter 25.08.2026: nicht (mehr) im Haushalt, ohne erfasste
                // Adresse (z.B. erwachsenes, ausgezogenes Kind) — QST-relevant.
                addrBadge = `<span class="fam-tile-badge fam-tile-badge-addr" title="Lebt nicht (mehr) im gleichen Haushalt — keine Adresse erfasst">📍 nicht im Haushalt</span>`;
            }

            const memberJson = JSON.stringify(m).replace(/"/g, '&quot;');

            // Kinder: Zulagen kompakt — KEINE leere gestrichelte Box mehr.
            // Ohne Zulagen nur „+ Zulage"; mit Zulagen eine Chip-Zeile + Detail.
            let kindAllowancesBlock = '';
            // Seit 11.10.2026 keine Sperre mehr ohne Unterhalt: die Familien-
            // ausgleichskasse entscheidet (Verfügung) — Hinweis steht in der QST-Zeile.
            if (type === 'Kind') {
                const allowances = allowanceMap[m.id] || [];
                if (allowances.length === 0) {
                    kindAllowancesBlock = `
                    <div class="fam-tile-foot" onclick="event.stopPropagation()">
                        <button type="button" class="fam-tile-zulage-btn" onclick="openAllowanceFromCard(${m.id}, null)">+ Zulage</button>
                    </div>`;
                } else {
                    // Walter 04.09.2026: Zulagen ändern sich — darum als Liste
                    // untereinander, neueste zuoberst, neben der Pille die
                    // Gültigkeit «1.4.26 – 31.3.34» (Kurzjahr).
                    const kurz = d => { if (!d) return ''; const x = new Date(d); return isNaN(x) ? d : `${x.getDate()}.${x.getMonth() + 1}.${String(x.getFullYear()).slice(-2)}`; };
                    const heute = new Date().toISOString().slice(0, 10);
                    const sortedAllow = [...allowances].sort((a, b) =>
                        String(b.validFrom || '').localeCompare(String(a.validFrom || '')) || ((b.id || 0) - (a.id || 0)));
                    const rows = sortedAllow.map(a => {
                        const artShort = a.allowanceType || 'Zulage';
                        const bisShort = a.validTo ? formatDate(a.validTo) : 'offen';
                        const locked = a.inLohnVerwendet === true;
                        const warnOffen = (a.posten || []).filter(p => p.status === 'OFFEN' || p.status === 'VORJAHR').length;
                        const hasDok = !!a.dokumentId;
                        const von = String(a.validFrom || '').slice(0, 10);
                        const bis = String(a.validTo || '').slice(0, 10);
                        const abgelaufen = bis && bis < heute;
                        const zukunft = von && von > heute;
                        const aJson = JSON.stringify(a).replace(/"/g, '&quot;');
                        const zeitraum = `${kurz(a.validFrom)} – ${a.validTo ? kurz(a.validTo) : 'offen'}`;
                        // Walter 21.10.2026: Betrag wieder auf der Pille — Lohn holt
                        // weiterhin den FZ-Tarif; Anzeige = erfasster Monatsbetrag.
                        const hatSatz = a.tarifSatzNr != null && a.tarifSatzNr !== '';
                        const betragTxt = Number(a.monthlyAmount || 0).toFixed(2);
                        const pillText = hatSatz
                            ? `${esc(artShort)} Satz ${a.tarifSatzNr} · ${betragTxt}`
                            : `${esc(artShort)} · ${betragTxt}`;
                        const betragHint = hatSatz
                            ? 'Betrag gemäss FZ-Tarif des Kantons (erfasst mit CHF ' + betragTxt + ')'
                            : 'fixer Betrag CHF ' + betragTxt + ' — kein Tarif-Satz hinterlegt';
                        return `<div class="fam-tile-zulage-row${abgelaufen ? ' is-past' : ''}${zukunft ? ' is-future' : ''}">
                            <button type="button" class="fam-tile-chip${locked ? ' is-locked' : ''}${hatSatz ? '' : ' is-fixed'}"
                                title="${esc(artShort)} · ${betragHint} · bis ${bisShort}${hasDok ? ' · mit Entscheid-Doku' : ''}${locked ? ' · in Lohn verwendet' : ''}"
                                onclick="event.stopPropagation();openAllowanceFromCard(${m.id}, ${aJson})">
                                ${locked ? '🔒 ' : ''}${hasDok ? '📄 ' : ''}${pillText}
                            </button>
                            ${warnOffen ? `<span title="${warnOffen} frühere${warnOffen === 1 ? 'r' : ''} Monat${warnOffen === 1 ? '' : 'e'} offen — Warnung im Lohnlauf" style="font-size:11px;font-weight:700;color:#92400e">⚠ ${warnOffen}</span>` : ''}
                            <span class="fam-tile-zulage-zeit" title="${abgelaufen ? 'abgelaufen' : zukunft ? 'künftig' : 'gültig'}">${zeitraum}</span>
                        </div>`;
                    }).join('');
                    kindAllowancesBlock = `
                    <div class="fam-tile-foot fam-tile-foot-list" onclick="event.stopPropagation()">
                        <div class="fam-tile-zulage-list">${rows}</div>
                        <button type="button" class="fam-tile-zulage-btn" onclick="openAllowanceFromCard(${m.id}, null)" title="Neue Zulage">+</button>
                    </div>`;
                }
            }

            // QST-Verlauf am Kind (Walter 11.10.2026, docs/kind-qst-verlauf-konzept.md):
            // Monate, in denen das Kind zählt (Regel serverseitig, KindQstVerlauf),
            // offene Punkte und Hinweise gelb.
            let qstZeitraumLine = '';
            const qv = m.qstVerlauf;
            if (type === 'Kind' && qv) {
                const heuteIso = new Date().toISOString().slice(0, 10);
                const mon = iso => iso ? `${iso.slice(5, 7)}.${iso.slice(0, 4)}` : '';
                const zeilen = (qv.wirkung || []).map(w => {
                    const aktiv = w.von <= heuteIso && (!w.bis || w.bis >= heuteIso);
                    return `<span style="color:${aktiv ? '#166534' : '#8b8b8b'}">${mon(w.von)} – ${w.bis ? mon(w.bis) : 'offen'} <span style="opacity:.75">· ${esc(w.grund)}</span></span>`;
                });
                if (!qv.zaehltHeute)
                    zeilen.push(`<span style="color:#8b8b8b">zählt zurzeit nicht · ${esc(qv.grundHeute || '')}</span>`);
                const gelb = [...(qv.offen || []), ...(qv.hinweise || [])]
                    .map(t => `<span style="color:#92400e">⚠ ${esc(t)}</span>`);
                qstZeitraumLine = `<div class="fam-tile-qst" style="font-size:11px;line-height:1.45;margin-top:2px"><span style="color:#646464;font-weight:600">QST</span> ${[...zeilen, ...gelb].join('<br>')}</div>`;
            }

            const tileWide = type === 'Ehepartner' ? ' fam-tile-wide' : '';
            html += `
            <div class="fam-tile${tileWide}" onclick="openFamilyModal(${memberJson})">
                <div class="fam-tile-top">
                    <div class="fam-tile-name">${esc(name)}</div>
                    <div class="dok-menu-wrap" onclick="event.stopPropagation()">
                        <button class="dok-menu-btn" onclick="famToggleMenu(event, ${m.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="famMenu-${m.id}">
                            <button class="dok-menu-item" onclick="openFamilyModal(${memberJson})">Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="deleteFamilyMember(${m.id})">Löschen</button>
                        </div>
                    </div>
                </div>
                <div class="fam-tile-meta">${meta || '–'}</div>
                ${qstZeitraumLine}
                ${(spousePermitBadge || spouseDocBtn || addrBadge)
                    ? `<div class="fam-tile-badges" onclick="event.stopPropagation()">${spousePermitBadge}${spouseDocBtn}${addrBadge}</div>`
                    : ''}
                ${kindAllowancesBlock}
            </div>`;
        });
        html += `</div></div>`;
    });

    // Walter 11.06.2026: Mutterschafts-Modul komplett im Familie-Tab — nur
    // bei Frauen (Helper _familieMutterschaftHtml, siehe oben).
    html += _familieMutterschaftHtml(employeeId, pregnancyDetails);

    el.innerHTML = html;
}

function showFamilyDetailPopup(memberId) {
    const m = (window._familyMembersCache ?? []).find(x => x.id === memberId);
    if (!m) return;
    const name = ((m.firstName ?? '') + ' ' + (m.lastName ?? '')).trim() || '–';
    const dob  = m.dateOfBirth ? formatDate(m.dateOfBirth) : '–';
    const age  = m.dateOfBirth ? calcAge(m.dateOfBirth) : null;
    const yearsLbl = window.i18n && i18n.getLang && i18n.getLang() === 'en' ? 'years' : 'Jahre';
    const memberTypeLbl = _t('fam.value.type.' + m.memberType, m.memberType);
    const subtitle = `${memberTypeLbl}${age !== null ? ' · ' + dob + ' (' + age + ' ' + yearsLbl + ')' : ''}`;

    const row = (label, value) => `
        <div style="display:flex;justify-content:space-between;padding:6px 0;border-bottom:1px solid #f1f5f9">
            <span style="color:#64748b;font-size:12px">${label}</span>
            <span style="color:#1e293b;font-weight:500">${value ?? '–'}</span>
        </div>`;

    const html = `
        <div style="position:fixed;inset:0;background:rgba(0,0,0,0.4);z-index:2000;display:flex;align-items:center;justify-content:center" onclick="if(event.target===this)closeFamilyDetailPopup()">
            <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:14px;width:480px;max-width:92vw;box-shadow:0 12px 48px rgba(0,0,0,0.2);overflow:hidden">
                <div style="padding:18px 22px;border-bottom:1px solid #e2e8f0;display:flex;align-items:flex-start;justify-content:space-between;gap:8px">
                    <div>
                        <div style="font-size:16px;font-weight:700;color:#0f172a">${esc(name)}</div>
                        <div style="font-size:12px;color:#64748b;margin-top:2px">${esc(subtitle)}</div>
                    </div>
                    <button onclick="closeFamilyDetailPopup()" style="background:none;border:none;cursor:pointer;font-size:18px;color:#94a3b8;padding:4px 8px">✕</button>
                </div>
                <div style="padding:14px 22px">
                    ${row(_t('fam.field.ahv','AHV-Nummer'),     m.socialSecurityNumber || '–')}
                    ${row(_t('fam.field.phone','Telefon'),      m.phone || '–')}
                    ${row(_t('fam.field.livesInCh','In der Schweiz lebend'), m.livesInSwitzerland ? (i18n.getLang && i18n.getLang() === 'en' ? 'Yes' : 'Ja') : (i18n.getLang && i18n.getLang() === 'en' ? 'No' : 'Nein'))}
                    ${m.keineUnterhaltspflicht
                        ? row('Unterhalt', 'heute kein Unterhalt zur Hauptsache — zählt nicht für die QST-Kinderziffer')
                        : ''}
                    ${!m.qstVerlauf ? '' :
                      row('QST zählt', (m.qstVerlauf.wirkung || []).map(w =>
                          `${w.von.slice(5, 7)}.${w.von.slice(0, 4)} – ${w.bis ? w.bis.slice(5, 7) + '.' + w.bis.slice(0, 4) : 'offen'} · ${esc(w.grund)}`).join('<br>') || 'zurzeit nicht')}
                </div>
                <div style="padding:14px 22px;border-top:1px solid #e2e8f0;display:flex;justify-content:flex-end;gap:8px">
                    <button onclick="closeFamilyDetailPopup();openFamilyModal(${JSON.stringify(m).replace(/"/g, '&quot;')})" style="background:#1a1a1a;color:white;border:none;border-radius:8px;padding:8px 16px;font-size:13px;cursor:pointer;font-weight:600">✎ ${_t('docs.btn.edit','Bearbeiten')}</button>
                    <button onclick="closeFamilyDetailPopup()" style="background:white;border:1px solid #e2e8f0;border-radius:8px;padding:8px 16px;font-size:13px;cursor:pointer">${_t('common.close','Schliessen')}</button>
                </div>
            </div>
        </div>`;

    let pop = document.getElementById('familyDetailPopup');
    if (!pop) {
        pop = document.createElement('div');
        pop.id = 'familyDetailPopup';
        document.body.appendChild(pop);
    }
    pop.innerHTML = html;
}

function closeFamilyDetailPopup() {
    const pop = document.getElementById('familyDetailPopup');
    if (pop) pop.innerHTML = '';
}

function calcAge(dateStr) {
    const dob = new Date(dateStr);
    if (isNaN(dob)) return null;
    const today = new Date();
    let age = today.getFullYear() - dob.getFullYear();
    if (today < new Date(today.getFullYear(), dob.getMonth(), dob.getDate())) age--;
    return age;
}

// Live-Anzeige neben „Geburtsdatum" im Familienangehöriger-Modal — Alter
// wird mit aktuellem Datum berechnet (Walter-Vorgabe).
function updateFmAgeDisplay() {
    const dob = document.getElementById('fmDateOfBirth')?.value;
    const dispEl = document.getElementById('fmAgeDisplay');
    if (!dispEl) return;
    const age = dob ? calcAge(dob) : null;
    dispEl.textContent = age != null ? `(${age} J.)` : '';
}

function fmAutoQstFromDob() {
    // Seit 11.10.2026 gibt es kein «QST ab/bis» mehr am Kind — die Minderjährigkeit
    // rechnet der Server aus dem Geburtsdatum (KindQstVerlauf). Aufruf bleibt im
    // oninput des Geburtsdatums, darum leer statt entfernt.
}

// Walter-Vorgabe 28.05.2026: Wenn der User im Familien-Modal den Typ auf
// Kind oder Ehepartner ändert UND das Nachname-Feld noch leer ist, mit dem
// Nachnamen des MA vorbefüllen. Bei Mutter/Vater/Sonstige NICHT vorbefüllen
// (die haben oft eigene Nachnamen). Wir überschreiben NIE einen schon vom
// User eingetippten Wert — Vorbefüllen passiert nur wenn das Feld leer ist.
function fmTypeChanged() {
    const typeEl = document.getElementById('fmMemberType');
    const lastEl = document.getElementById('fmLastName');
    if (!typeEl || !lastEl) return;
    const type = typeEl.value;
    const maLast = (selectedEmployee?.lastName || '').trim();
    if ((type === 'Kind' || type === 'Ehepartner') && maLast && !lastEl.value.trim()) {
        lastEl.value = maLast;
    }
    fmQstBlocksVisibility(type);
    fmRefreshStatusUi(!!editingFamilyMemberId);
}

// Walter-Vorgabe 20.08.2026: typ-abhängige QST-Blöcke im Familien-Modal —
// Erwerbstätigkeit nur beim Ehepartner, Erstausbildung nur beim Kind.
function fmQstBlocksVisibility(type) {
    // Ohne Argument aufgerufen (Checkbox «keine Unterhaltspflicht») → Typ aus
    // dem Formular lesen, sonst wuerde die Funktion alles als Nicht-Kind
    // behandeln und die Felder faelschlich ausblenden.
    if (type == null) type = document.getElementById('fmMemberType')?.value || 'Kind';
    // Konkubinatspartner (Walter 25.08.2026, docs/konkubinat-qst-konzept.md):
    // Erwerbstätigkeits-Block auch hier sichtbar (freiwillig, angemahnt erst
    // bei gemeinsamem Kind), plus die Einkommensfrage H1/A0.
    // Anderer Elternteil (Walter 11.10.2026): gleiche Erwerbsfragen wie die
    // Partner — fürs Formular «Anmeldung Familienzulagen».
    const elternTyp = FM_ELTERN_TYPEN.includes(type);
    const erwerbSec = document.getElementById('fmErwerbSection');
    if (erwerbSec) erwerbSec.style.display = elternTyp ? '' : 'none';
    const erwerbTitle = document.getElementById('fmErwerbTitle');
    if (erwerbTitle) erwerbTitle.textContent = type === 'Konkubinatspartner'
        ? 'Erwerbstätigkeit Konkubinatspartner/in'
        : type === 'AndererElternteil' ? 'Erwerbstätigkeit anderer Elternteil'
        : 'Erwerbstätigkeit Ehepartner';
    const zsFeld = document.getElementById('fmFamilyStatusField');
    if (zsFeld) zsFeld.style.display = elternTyp ? '' : 'none';
    fmEinkommenZeile();
    // Walter 11.10.2026: Kind-Maske in vier Blöcken — Kind, Wohnen & Unterhalt,
    // Ausbildung, Quellensteuer. Nationalität, «In der Schweiz lebend» und die
    // Adress-Radios gibt es beim Kind nicht mehr (Wohnadresse im Wohnen-Block).
    const istKind = type === 'Kind';
    ['fmKindSection', 'fmKindWohnenSection', 'fmKindAusbildungSection', 'fmKindZulageSection', 'fmQstSection'].forEach(id => {
        const el = document.getElementById(id);
        const ohneQst = id === 'fmQstSection' && typeof kqvOhneQst === 'function' && kqvOhneQst();
        if (el) el.style.display = istKind && !ohneQst ? '' : 'none';
    });
    ['fmAufenthaltTitel', 'fmOcrInfo', 'fmAufenthaltGrid', 'fmAdresseSection'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.style.display = istKind ? 'none' : '';
    });
    if (istKind) {
        fmAeOptionen(); fmKindAdresseSichtbarkeit();
        if (typeof kqvAusbildungSichtbarkeit === 'function') kqvAusbildungSichtbarkeit();
    }
    // Walter-Vorgabe 29.08.2026: beim KIND unnötige Felder ausblenden —
    // Ledigname, Telefon, Bewilligung, Gültig bis, ZEMIS-Nr.
    // Die AHV-Nummer bleibt sichtbar (Walter 12.10.2026: Kinder haben eine,
    // GastroSocial führt die Familienzulage darüber).
    // Verborgene Inputs behalten ihre Werte (kein Datenverlust
    // beim Bearbeiten von Alt-Erfassungen).
    ['fmMaidenNameField', 'fmPhoneField', 'fmZemisField', 'fmLivesInChField', 'fmPermitInline'].forEach(id => {
        const el = document.getElementById(id);
        if (el) el.style.display = istKind ? 'none' : '';
    });
    const pBlock = document.getElementById('fmPermitStatusBlock');
    if (pBlock) pBlock.style.display = istKind ? 'none' : '';
    // Auslands-Partner-Hinweis hängt am Typ (nur Ehepartner) — mitschalten.
    if (typeof fmUpdateAuslandHint === 'function') fmUpdateAuslandHint();
}

// Segment-Pille Erwerbstätig lesen/schreiben ('' = Frage offen).
function fmSetErwerb(val) {
    const want = val === true ? 'ja' : val === false ? 'nein' : '';
    document.querySelectorAll('input[name="fmErwerb"]').forEach(r => { r.checked = (r.value === want); });
    fmErwerbChanged();
}
function fmGetErwerb() {
    const r = document.querySelector('input[name="fmErwerb"]:checked');
    return r?.value === 'ja' ? true : r?.value === 'nein' ? false : null;
}
// Konkubinat (Walter 25.08.2026): Einkommensfrage (K-Partner) + Gemeinsames-
// Kind-Frage (Kind) — gleiche 3-Zustands-Pillen wie Erwerbstätig.
// ACHTUNG SPIEGELUNG (Walter 25.08.2026 v3): Das UI fragt aus PARTNER-
// Perspektive («Ist das Einkommen des K-Partners höher als das des MA?»),
// gespeichert wird aber ma_hat_hoeheres_einkommen aus MA-Perspektive.
// DB true (MA verdient mehr)  → Radio «Nein» (Partner verdient NICHT mehr)
// DB false (Partner mehr)     → Radio «Ja»
function fmSetMaEink(dbVal) {
    const want = dbVal === true ? 'nein' : dbVal === false ? 'ja' : '';
    document.querySelectorAll('input[name="fmPartnerMehr"]').forEach(r => { r.checked = (r.value === want); });
}
function fmGetMaEink() {
    const r = document.querySelector('input[name="fmPartnerMehr"]:checked');
    return r?.value === 'ja' ? false : r?.value === 'nein' ? true : null;
}
// ── Familienzulagen-Angaben (Walter 11.10.2026, docs/famz-anmeldung-konzept.md) ──
const FM_ELTERN_TYPEN = ['Ehepartner', 'Konkubinatspartner', 'AndererElternteil'];

function fmPille(name, val) {
    const want = val === true ? 'ja' : val === false ? 'nein' : '';
    document.querySelectorAll(`input[name="${name}"]`).forEach(r => { r.checked = (r.value === want); });
}
function fmPilleLesen(name) {
    const r = document.querySelector(`input[name="${name}"]:checked`);
    return r?.value === 'ja' ? true : r?.value === 'nein' ? false : null;
}
const fmJn = v => v === true ? 'ja' : v === false ? 'nein' : '';
const fmJnLesen = id => { const v = document.getElementById(id)?.value; return v === 'ja' ? true : v === 'nein' ? false : null; };

/** Aktueller Erwerbsstand im Formular (Ersterfassung oder gespeicherter Status). */
function fmErwerbAktuell() {
    if (document.getElementById('fmErwerbErst')?.style.display !== 'none') return fmGetErwerb();
    return (window._fmErwerbStand === true || window._fmErwerbStand === false) ? window._fmErwerbStand : fmGetErwerb();
}

/** «Verdient … mehr als der MA?» — bei allen Eltern-Typen, sobald erwerbstätig. */
function fmEinkommenZeile() {
    const typ = document.getElementById('fmMemberType')?.value || '';
    const row = document.getElementById('fmEinkommenRow');
    if (!row) return;
    row.style.display = (FM_ELTERN_TYPEN.includes(typ) && fmErwerbAktuell() === true) ? '' : 'none';
    const frage = document.getElementById('fmEinkommenFrage');
    if (frage) frage.textContent = typ === 'Konkubinatspartner'
        ? 'Ist das Einkommen des Konkubinatspartners höher als das des MA?'
        : typ === 'AndererElternteil' ? 'Ist das Einkommen des anderen Elternteils höher als das des MA?'
        : 'Ist das Einkommen des Ehepartners höher als das des MA?';
    const qst = document.getElementById('fmEinkommenQstHint');
    if (qst) qst.style.display = typ === 'Konkubinatspartner' ? '' : 'none';
    fmEinkommenHinweis();
}
function fmEinkommenHinweis() {
    const h = document.getElementById('fmEinkommenFamzHint');
    if (h) h.style.display = fmGetMaEink() === false ? '' : 'none';
}

function fmSeNeSichtbarkeit() {
    const seNe = document.getElementById('fmSeNe')?.value || '';
    const kasse = document.getElementById('fmSeNeKasseField');
    if (kasse) kasse.style.display = (seNe === 'SE' || seNe === 'NE') ? '' : 'none';
    const von = document.getElementById('fmLeistungVonField');
    if (von) von.style.display = document.getElementById('fmBeziehtLeistung')?.value === 'ja' ? '' : 'none';
}

/** Auswahl «Anderer Elternteil» am Kind: Partner + andere Elternteile des MA. */
function fmAeOptionen(aktuell) {
    const sel = document.getElementById('fmAndererElternteil');
    if (!sel) return;
    if (aktuell === undefined) aktuell = sel.dataset.wert ?? '';
    const eltern = (window._familyMembersCache || [])
        .filter(m => FM_ELTERN_TYPEN.includes(m.memberType) && m.id !== editingFamilyMemberId)
        .sort((a, b) => (a.firstName || '').localeCompare(b.firstName || '') || (a.lastName || '').localeCompare(b.lastName || ''));
    const typText = { Ehepartner: 'Ehepartner/in', Konkubinatspartner: 'Konkubinatspartner/in', AndererElternteil: 'anderer Elternteil' };
    sel.innerHTML = `<option value="">– offen –</option>`
        + eltern.map(m => `<option value="${m.id}">${esc(`${m.firstName || ''} ${m.lastName || ''}`.trim() || '–')} · ${typText[m.memberType]}${m.dateOfDeath ? ' · verstorben' : ''}</option>`).join('')
        + `<option value="0">nicht erfasst / unbekannt</option>`;
    sel.value = String(aktuell ?? '');
    if (sel.value !== String(aktuell ?? '')) sel.value = '';
    sel.dataset.wert = sel.value;
    fmAeHinweisAktualisieren();
}
function fmAeHinweisAktualisieren() {
    const sel = document.getElementById('fmAndererElternteil');
    const box = document.getElementById('fmAeHinweis');
    if (!sel || !box) return;
    sel.dataset.wert = sel.value;
    const id = parseInt(sel.value, 10);
    const ae = id > 0 ? (window._familyMembersCache || []).find(m => m.id === id) : null;
    const name = ae ? `${ae.firstName || ''} ${ae.lastName || ''}`.trim() : '';
    const teile = [];
    if (sel.value === '') teile.push('Fehlt die Person? Zuerst als «Anderer Elternteil» erfassen — wichtig für die Familienzulagen.');
    if (ae && ae.erwerbstaetig === true && ae.maHatHoeheresEinkommen === false)
        teile.push(`<span style="color:#92400e">⚠ ${esc(name)} verdient mehr — in der Regel bezieht ${esc(name)} die Kinderzulage (Erstanspruch).</span>`);
    box.innerHTML = teile.join('<br>');
}

/** Lebt das Kind heute im Haushalt? Neu: Startwert; sonst gültige Version aus dem Verlauf. */
function fmKindImHaushalt() {
    if (!editingFamilyMemberId) return document.getElementById('fmKindStartHaushalt')?.value !== 'nein';
    const verh = (typeof _kqv !== 'undefined' && _kqv?.memberId === editingFamilyMemberId && _kqv?.daten?.verhaeltnisse) || null;
    if (verh && verh.length) {
        const heute = _fmHeuteIso();
        const gueltig = [...verh].filter(v => (v.gueltigAb || '') <= heute)
            .sort((a, b) => String(b.gueltigAb).localeCompare(String(a.gueltigAb)))[0] || verh[0];
        return gueltig.imHaushalt !== false;
    }
    return window._fmKindLebtImHaushalt !== false;
}
function fmKindAdresseSichtbarkeit() {
    // Nur wenn das Kind heute nicht im Haushalt lebt — sonst gilt die Adresse des MA.
    const box = document.getElementById('fmKindAdresseBox');
    if (box) box.style.display = fmKindImHaushalt() ? 'none' : '';
}
function fmErwerbChanged() {
    // CH-Arbeitgeber-Felder nur bei «Ja» UND Partner in der Schweiz.
    // Im Ausland (inkl. gleicher Haushalt eines Auslands-MA) komplett weg.
    const aktiv = fmGetErwerb() === true;
    ['fmArbeitgeberName', 'fmArbeitgeberStrasse', 'fmArbeitgeberPlz',
     'fmArbeitgeberOrt', 'fmArbeitgeberKanton', 'fmStellenantritt', 'fmStellenende', 'fmArbeitgeberTelefon'].forEach(id => {
        const el = document.getElementById(id);
        if (!el) return;
        el.disabled = !aktiv;
        el.style.opacity = aktiv ? '' : '0.5';
    });
    fmSyncArbeitgeberSichtbarkeit();
    // Einkommensfrage erst bei «Erwerbstätig = Ja» (Walter 25.08.2026 v2).
    fmEinkommenZeile();
}

// Analog für das MA-Edit-Modal — Alter neben dem Geburtsdatum-Input.
function updateEfDobAgeDisplay() {
    const dob = document.getElementById('ef-dob')?.value;
    const dispEl = document.getElementById('ef-dob-age');
    if (!dispEl) return;
    const age = dob ? calcAge(dob) : null;
    dispEl.textContent = age != null ? `(${age} J.)` : '';
}

function formatMonthYear(dateStr) {
    if (!dateStr) return '–';
    const d = new Date(dateStr);
    if (isNaN(d)) return dateStr;
    return d.toLocaleDateString('de-CH', { month: '2-digit', year: 'numeric' });
}

// ── AHV-Nummer-Validierung ──────────────────────────────────────────────
// Schweizer Sozialversicherungsnummer (NNSS) im EAN-13-Format:
//   - 13 Ziffern, beginnt mit 756 (CH)
//   - Letzte Ziffer = Prüfziffer aus den ersten 12
//   - EAN-13: Position 1,3,5,…×1, Position 2,4,6,…×3, Summe mod 10
function validateAhvNummer(input) {
    if (!input || !input.trim()) return { valid: null };  // leer = kein Fehler
    const digits = input.replace(/\D/g, '');
    if (digits.length !== 13) {
        return { valid: false, error: `Muss 13 Ziffern haben (aktuell ${digits.length})` };
    }
    if (!digits.startsWith('756')) {
        return { valid: false, error: 'Muss mit 756 beginnen (CH-Code)' };
    }
    // EAN-13 Prüfziffer auf den ersten 12 Ziffern
    let sum = 0;
    for (let i = 0; i < 12; i++) {
        const d = parseInt(digits[i], 10);
        sum += (i % 2 === 0) ? d : d * 3;
    }
    const expected = (10 - sum % 10) % 10;
    const actual   = parseInt(digits[12], 10);
    if (expected !== actual) {
        return {
            valid: false,
            error: `Prüfziffer falsch (erwartet ${expected}, eingegeben ${actual})`
        };
    }
    // Normalisiert: "756.XXXX.XXXX.XX"
    return {
        valid: true,
        normalized: `${digits.slice(0,3)}.${digits.slice(3,7)}.${digits.slice(7,11)}.${digits.slice(11)}`
    };
}

// Live-Maske beim Tippen (Walter 04.09.2026, «wie in easy@work»): nur
// Ziffern, Punkte automatisch nach 3/7/11 Stellen → 756.1234.5678.97.
// Der Prüfziffer-Check läuft erst beim Verlassen des Felds (validateAhvField).
function ahvMaskInput(inputEl) {
    const digits = (inputEl.value || '').replace(/\D/g, '').slice(0, 13);
    let out = digits.slice(0, 3);
    if (digits.length > 3) out += '.' + digits.slice(3, 7);
    if (digits.length > 7) out += '.' + digits.slice(7, 11);
    if (digits.length > 11) out += '.' + digits.slice(11, 13);
    if (inputEl.value !== out) inputEl.value = out;
    const statusEl = document.getElementById(inputEl.id + '-status');
    if (statusEl && digits.length < 13) { statusEl.textContent = ''; inputEl.style.borderColor = ''; }
    else if (statusEl) validateAhvField(inputEl, false);
}

// Hängt Validierungs-Status an ein Input-Feld + sein Status-Element.
// onBlur=true: bei gültiger AHV-Nr automatisch ins Standard-Format normalisieren
// (756.XXXX.XXXX.XX), damit die Anzeige konsistent ist.
function validateAhvField(inputEl, onBlur) {
    const statusEl = document.getElementById(inputEl.id + '-status');
    if (!statusEl) return;
    const result = validateAhvNummer(inputEl.value);
    if (result.valid === null) {
        statusEl.textContent = '';
        inputEl.style.borderColor = '';
        return;
    }
    if (result.valid) {
        statusEl.textContent = '✓ AHV-Nr. gültig';
        statusEl.style.color = '#15803d';
        inputEl.style.borderColor = '#86efac';
        if (onBlur) inputEl.value = result.normalized;
    } else {
        statusEl.textContent = '✗ ' + result.error;
        statusEl.style.color = '#dc2626';
        inputEl.style.borderColor = '#fca5a5';
    }
}

// ── Hilfsfunktionen ────────────────────────────
// Optional 3. Argument: Field-Code (permit, passport, ahv_card, ...).
// Wenn ein verknüpftes Dokument für diesen MA existiert, erscheint ein
// 📎-Button rechts vom Wert. Klick öffnet das neueste Dokument im Preview.
function linkedDocButton(linkedCode, directKind) {
    if (!linkedCode) return '';
    const hasDoc = window._linkedDocCodes && window._linkedDocCodes.has(linkedCode);
    // Walter-Vorgabe 10.07.2026: «vorhanden» muss klar erkennbar sein —
    // GRÜN mit Häkchen; «fehlt» bleibt blass mit gestricheltem Rand.
    const styleActive   = "background:#dcfce7;border:1px solid #86efac;color:#15803d";
    const styleInactive = "background:#f8f7f4;border:1px dashed #d5d0c6;color:#b3ada1";
    // Feld mit direkter Verknüpfung (Walter 25.09.2026): liegt nur ein Dokument
    // in der Kategorie, ist noch NICHTS verknüpft → gelb «Doku verknüpfen» statt
    // grünem Häkchen (sonst sieht es aus wie verknüpft, Fall Pass Tomova).
    if (directKind) {
        return `<button class="emp-field-docbtn" data-linked-code="${linkedCode}" data-direct-kind="${directKind}"
                   title="Noch kein Dokument verknüpft — klicken zum Verknüpfen oder Hochladen"
                   onclick="openLinkedDocOrLink('${linkedCode}','${directKind}')"
                   style="margin-left:8px;background:#fef3c7;border:1px dashed #fcd34d;color:#92400e;border-radius:6px;padding:3px 5px;cursor:pointer;vertical-align:middle;display:inline-flex;align-items:center;gap:3px;font-size:11px;font-weight:600;line-height:1">
                   <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                   
               </button>`;
    }
    const tooltip = hasDoc ? 'Dokument vorhanden — klicken zum Öffnen' : 'Noch kein Dokument vorhanden — klicken um hochzuladen';
    // directKind (Walter 23.09.2026): Feld hat eine direkte Verknüpfung, die
    // noch leer ist — fehlt auch das Typ-Dokument, öffnet der Klick den
    // Verknüpfen-Dialog statt nur den Dokumente-Tab.
    const onclickJs = directKind ? `openLinkedDocOrLink('${linkedCode}','${directKind}')` : `openLinkedDoc('${linkedCode}')`;
    return `<button class="emp-field-docbtn" data-linked-code="${linkedCode}"${directKind ? ` data-direct-kind="${directKind}"` : ''} title="${tooltip}"
               onclick="${onclickJs}"
               style="margin-left:8px;${hasDoc ? styleActive : styleInactive};border-radius:6px;padding:3px 5px;cursor:pointer;vertical-align:middle;display:inline-flex;align-items:center;gap:3px;font-size:11px;font-weight:600;line-height:1;transition:all .15s">
               <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
                 <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                 <polyline points="14 2 14 8 20 8"/>
                 <line x1="16" y1="13" x2="8" y2="13"/>
                 <line x1="16" y1="17" x2="8" y2="17"/>
                 <line x1="10" y1="9" x2="8" y2="9"/>
               </svg>
               
           </button>`;
}

// Direkt verknüpftes Dokument (Walter 23.09.2026): Feld am MA zeigt genau EIN
// Dokument (z.B. employee.ahv_karte_dokument_id). Gesetzt → grüner Knopf, Klick
// öffnet die Vorschau. Leer → Rückfall auf die alte Typ-Verknüpfung
// (fallbackCode), sonst grauer Knopf, der den Verknüpfen-Dialog öffnet.
function directDocButton(docId, kind, fallbackCode) {
    if (docId) {
        return `<button class="emp-field-docbtn" title="Dokument verknüpft — klicken zum Anschauen (ersetzen/lösen unten im Fenster)"
                   onclick="openDirectDocVerknuepft(${docId}, '${kind}')"
                   style="margin-left:8px;background:#dcfce7;border:1px solid #86efac;color:#15803d;border-radius:6px;padding:3px 5px;cursor:pointer;vertical-align:middle;display:inline-flex;align-items:center;gap:3px;font-size:11px;font-weight:600;line-height:1">
                   <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                   
               </button>`;
    }
    if (fallbackCode) return linkedDocButton(fallbackCode, kind);
    return `<button class="emp-field-docbtn" title="Noch kein Dokument verknüpft — klicken zum Verknüpfen oder Hochladen"
               onclick="openAusweisDokuModal(selectedEmployeeId,'${kind}')"
               style="margin-left:8px;background:#fef3c7;border:1px dashed #fcd34d;color:#92400e;border-radius:6px;padding:3px 5px;cursor:pointer;vertical-align:middle;display:inline-flex;align-items:center;gap:3px;font-size:11px;font-weight:600;line-height:1">
               <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
               
           </button>`;
}

// Word/Excel wird über /preview-pdf als PDF angezeigt statt heruntergeladen
// (Walter 01.10.2026). Rückgabe 'umgewandelt' = gespeichert ist noch Word/Excel.
async function openDirectDoc(docId) {
    if (!docId) return false;
    try {
        let r = await fetch(`/api/documents/preview-pdf/${docId}`, { headers: ah() });
        if (r.status === 415) r = await fetch(`/api/documents/preview/${docId}`, { headers: ah() });
        if (!r.ok) {
            const j = await r.json().catch(() => null);
            alert('Konnte Dokument nicht laden: ' + (j?.message || j?.error || 'HTTP ' + r.status));
            return false;
        }
        const umgewandelt = r.headers.get('X-Umgewandelt') === '1';
        const blob = await r.blob();
        const name = cdFilename(r.headers.get('Content-Disposition') || '', 'Dokument');
        await previewFileModal(blob, name, { ohneDownload: umgewandelt });
        return umgewandelt ? 'umgewandelt' : true;
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        return false;
    }
}

// Knopf im Vorschaufenster für Word/Excel: dauerhaft in PDF umwandeln.
// nachher = JS, das die Vorschau danach neu öffnet (mit denselben Knöpfen).
function _docPdfKnopf(docId, nachher) {
    const knopf = 'padding:7px 14px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer';
    return `<button type="button" style="${knopf};color:#0f172a" title="Word/Excel durch ein PDF ersetzen — die Verknüpfung bleibt"
            onclick="docDauerhaftInPdf(${docId}, () => { ${nachher} })">📄 In PDF umwandeln</button>`;
}
async function docDauerhaftInPdf(docId, nachher) {
    try {
        const r = await fetch(`/api/documents/${docId}/in-pdf-umwandeln`, { method: 'POST', headers: ah() });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { alert('Umwandlung fehlgeschlagen: ' + (j.message || j.error || 'HTTP ' + r.status)); return; }
        filePreviewClose();
        if (typeof showToast === 'function') showToast(`In PDF umgewandelt: «${j.filenameOriginal}»`, 'success');
        if (typeof nachher === 'function') nachher();
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

// Walter 25.09.2026: Feld mit direkter Verknüpfung, die noch leer ist → IMMER
// verknüpfen (Auswahl, passende Dokumente oben), nie in die Kategorie springen.
function openLinkedDocOrLink(code, kind) {
    openAusweisDokuModal(selectedEmployeeId, kind);
}

// Grünes «Doku ✓» eines Feldes OHNE Historie (Pass/Ausweis, AHV-Karte,
// Geburtsurkunde, Zivilstand — Walter 26.09.2026): Klick zeigt das Dokument
// direkt; unten links im Vorschaufenster «Anderes Dokument verknüpfen» (ersetzt,
// keine Historie — das alte bleibt nur in den Dokumenten) und «Verknüpfung lösen».
// Felder MIT Historie (Bewilligung, Vertrag, Bank, Absenz) haben das nicht.
async function openDirectDocVerknuepft(docId, kind, ctx) {
    const empId = selectedEmployeeId;
    const labels = { id_pass: 'Ausweis', ahv_karte: 'AHV-Karte', geburtsurkunde: 'Geburtsurkunde', zivilstand: 'Zivilstandsdokument', bank_beleg: 'Bankbeleg' };
    const was = labels[kind] || 'Dokument';
    const geoeffnet = await openDirectDoc(docId);
    if (!geoeffnet || typeof filePreviewSetExtra !== 'function') return;
    const knopf = 'padding:7px 14px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer';
    // Bank (Walter 26.09.2026): Verknüpfung hängt am Konto → Konto-ID mitgeben.
    const ctxJs = ctx ? JSON.stringify(ctx).replace(/"/g, '&quot;') : 'undefined';
    const loesen = kind === 'bank_beleg' && ctx?.bankAccountId
        ? `bankBelegLoesen(${empId}, ${ctx.bankAccountId})`
        : `nwUnlinkDoku(${empId},'${kind}','${was}')`;
    filePreviewSetExtra(
        (geoeffnet === 'umgewandelt' ? _docPdfKnopf(docId, `openDirectDocVerknuepft(${docId},'${kind}',${ctxJs})`) : '') + `
        <button type="button" style="${knopf};color:#0f172a" title="${was}: anderes Dokument wählen oder hochladen — ersetzt das bisherige"
                onclick="filePreviewClose(); openAusweisDokuModal(${empId},'${kind}',${ctxJs})">↻ Anderes Dokument verknüpfen</button>
        <button type="button" style="${knopf};color:#b91c1c" title="Nur die Verknüpfung lösen — das Dokument bleibt in den Dokumenten"
                onclick="filePreviewClose(); ${loesen}">Verknüpfung lösen</button>`);
}

// ══ Doku-Symbol, überall gleich (Walter 26.09.2026) ══════════════════════
// Grün = verknüpft: Klick zeigt das Dokument, unten im Vorschaufenster
// «↻ Anderes Dokument verknüpfen» und (wo möglich) «Verknüpfung lösen».
// Gelb = nicht verknüpft: Klick öffnet das Verknüpfen. Nur das Symbol, der
// Text steht im Tooltip. opts: { docId, was, verknuepfen (JS), loesen (JS,
// optional), stop (Klick nicht an die Kachel weitergeben), style (extra) }.
const _DOK_SVG = '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>';
window._docIconReg = window._docIconReg || {};
let _docIconN = 0;
function docIconBtn(opts) {
    const stop = opts.stop ? 'event.stopPropagation();' : '';
    const base = 'border-radius:6px;padding:3px 5px;cursor:pointer;vertical-align:middle;display:inline-flex;align-items:center;line-height:1;font-family:inherit;flex-shrink:0;' + (opts.style || '');
    const was = opts.was || 'Dokument';
    if (opts.docId) {
        const key = 'd' + (++_docIconN);
        window._docIconReg[key] = { was, verknuepfen: opts.verknuepfen || '', loesen: opts.loesen || '' };
        return `<button type="button" class="emp-field-docbtn" title="${was} verknüpft — klicken zum Anschauen"
                   onclick="${stop}docIconOeffnen(${opts.docId}, '${key}')"
                   style="${base};background:#dcfce7;border:1px solid #86efac;color:#15803d">${_DOK_SVG}</button>`;
    }
    return `<button type="button" class="emp-field-docbtn" title="${was}: noch nicht verknüpft — klicken zum Verknüpfen oder Hochladen"
               onclick="${stop}${opts.verknuepfen}"
               style="${base};background:#fef3c7;border:1px dashed #fcd34d;color:#92400e">${_DOK_SVG}</button>`;
}
async function docIconOeffnen(docId, key) {
    const reg = window._docIconReg[key] || {};
    const geoeffnet = await openDirectDoc(docId);
    if (!geoeffnet || typeof filePreviewSetExtra !== 'function') return;
    const knopf = 'padding:7px 14px;border:1px solid #cbd5e1;background:white;border-radius:7px;font-size:13px;cursor:pointer';
    filePreviewSetExtra(
        (geoeffnet === 'umgewandelt' ? _docPdfKnopf(docId, `docIconOeffnen(${docId}, '${key}')`) : '')
        + (reg.verknuepfen ? `<button type="button" style="${knopf};color:#0f172a" title="${reg.was}: anderes Dokument wählen oder hochladen — ersetzt das bisherige"
                onclick="filePreviewClose(); ${reg.verknuepfen}">↻ Anderes Dokument verknüpfen</button>` : '')
        + (reg.loesen ? `<button type="button" style="${knopf};color:#b91c1c" title="Nur die Verknüpfung lösen — das Dokument bleibt in den Dokumenten"
                onclick="filePreviewClose(); ${reg.loesen}">Verknüpfung lösen</button>` : ''));
}
// Verknüpfung per PATCH {dokumentId:null} lösen, danach neu laden.
// extra = weitere Body-Felder. Die Geburtsurkunde am Kind liegt in einem eigenen
// Feld und braucht art='geburtsurkunde' — ohne das löste der Aufruf den Ausweis
// statt der Geburtsurkunde (Walter 28.09.2026).
async function docLoesenPatch(url, was, nachher, extra) {
    if (!(await liquidConfirm(`${was} lösen?\n\nNur die Verknüpfung wird entfernt — das Dokument selbst bleibt in den Dokumenten.`,
            { title: 'Verknüpfung lösen', yesLabel: 'Lösen', noLabel: 'Abbrechen' }))) return;
    try {
        const r = await fetch(url, { method: 'PATCH', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify({ dokumentId: null, ...(extra || {}) }) });
        if (!r.ok) { const j = await r.json().catch(() => null); alert(j?.message || `Lösen fehlgeschlagen (${r.status})`); return; }
        if (typeof nachher === 'function') nachher();
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

// Bankbeleg vom Konto lösen (Walter 26.09.2026) — das Dokument bleibt in den Dokumenten.
async function bankBelegLoesen(empId, bankId) {
    if (!(await liquidConfirm('Bankbeleg von dieser Bankverbindung lösen?\n\nNur die Verknüpfung wird entfernt — das Dokument selbst bleibt in den Dokumenten.',
            { title: 'Verknüpfung lösen', yesLabel: 'Lösen', noLabel: 'Abbrechen' }))) return;
    try {
        const r = await fetch(`/api/employees/${empId}/bank-accounts/${bankId}/dokument`, {
            method: 'PATCH', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentId: null }) });
        if (!r.ok) { const j = await r.json().catch(() => null); alert(j?.message || `Lösen fehlgeschlagen (${r.status})`); return; }
        if (typeof loadBankAccountsTab === 'function') loadBankAccountsTab(empId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

// Nachgeladenes linked-codes-Set → bestehende Doku-Buttons tauschen,
// OHNE die ganze Übersicht (Verträge/KTG) neu zu bauen.
function _ovPatchLinkedDocButtons() {
    document.querySelectorAll('.emp-field-docbtn[data-linked-code]').forEach(btn => {
        const code = btn.getAttribute('data-linked-code');
        if (!code) return;
        const html = linkedDocButton(code, btn.getAttribute('data-direct-kind') || undefined);
        if (!html) return;
        const tmp = document.createElement('span');
        tmp.innerHTML = html;
        const neu = tmp.firstElementChild;
        if (neu) btn.replaceWith(neu);
    });
}

function field(label, value, linkedCode, easyworkInfo = false) {
    const empty = !value || value === 'null' || value === 'undefined';
    const docBtn = linkedDocButton(linkedCode);
    return `<div class="emp-field liquid-field${easyworkInfo ? ' easywork-source-field' : ''}">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value${empty ? ' empty' : ''}${easyworkInfo ? ' easywork-info' : ''}">${empty ? '–' : value}${docBtn}</div>
    </div>`;
}

function inlineEditField(label, inputHtml) {
    return `<div class="emp-field liquid-field inline-edit-field">
        <div class="emp-field-label">${label}</div>
        <div class="emp-field-value">${inputHtml}</div>
    </div>`;
}

function yesNoToggle(id, value) {
    return `<div class="emp-yesno-toggle" data-target="${id}">
        <input type="hidden" id="${id}" value="${value ? 'true' : 'false'}">
        <button type="button" class="${value ? 'active' : ''}" onclick="empSetYesNo('${id}', true)">ja</button>
        <button type="button" class="${!value ? 'active' : ''}" onclick="empSetYesNo('${id}', false)">nein</button>
    </div>`;
}

function empSetYesNo(id, value) {
    const input = document.getElementById(id);
    if (input) input.value = value ? 'true' : 'false';
    const wrap = input?.closest('.emp-yesno-toggle');
    if (wrap) {
        wrap.querySelectorAll('button').forEach(btn => {
            const isYes = btn.textContent.trim() === 'ja';
            btn.classList.toggle('active', isYes === value);
        });
    }
    // Vertrags-Modal (ce-*) speichert über «Speichern» — kein Inline-Dirty.
    if (!String(id || '').startsWith('ce-')) empInlineDirty();
    if (id.startsWith('ov-') && typeof ovDirty === 'function') ovDirty();
}

function empInlineDirty() {
    // Walter-Bug 16.07.2026: der Button steht mit style="display:none" im DOM —
    // visibility allein machte ihn nie sichtbar. display umschalten. Es gibt
    // mehrere Inline-Speichern-Buttons (oben + direkt neben den Kuendigungs-
    // Feldern) — alle einblenden.
    document.querySelectorAll('.emp-inline-save').forEach(btn => {
        btn.style.display = 'inline-flex';
        btn.style.visibility = 'visible';
    });
}

// «Gekündigt am» erfasst → «Kündigung per» automatisch aus der Kündigungs-
// frist des MA berechnen (Walter 16.07.2026). Gleiche Quelle wie die
// Kündigungs-Seite: GET /api/kuendigung/{id}/info?datum=… liefert den
// letzten Arbeitstag (Probezeit/Dienstjahre/Filial-Einstellung inklusive).
async function kuendAmChanged(empId) {
    // Übersicht (ov-*) ist die aktive Quelle; ef-* nur noch Fallback (Edit-Legacy).
    const am = document.getElementById('ov-kuendAm')?.value
            || document.getElementById('ef-kuendAm')?.value;
    const perEl = document.getElementById('ov-kuendPer')
               || document.getElementById('ef-kuendPer');
    if (!am || !perEl) return;
    try {
        const r = await fetch(`/api/kuendigung/${empId}/info?datum=${am}`, { headers: ah() });
        if (!r.ok) return;
        const info = await r.json();
        const per = info?.notice?.letzterArbeitstag || info?.letzterArbeitstag;
        if (per) {
            perEl.value = String(per).slice(0, 10);
            if (typeof ovDirty === 'function') ovDirty();
            else empInlineDirty();
        }
    } catch (_) {}
}

// Klick auf 📎: springt in den Dokumente-Tab des MA und filtert auf den
// passenden Dokument-Typ. So sieht der User ALLE Dokumente dieses Typs
// (auch wenn mehrere vorhanden oder unsauber abgelegt) und kann das
// richtige selbst auswählen.
async function openLinkedDoc(code) {
    if (!selectedEmployeeId) return;
    // Taxonomie direkt über die API holen (_dokState ist mit `let` in
    // index.html deklariert → nicht auf window verfügbar).
    let taxonomy = null;
    try {
        const r = await fetch('/api/documents/taxonomie', { headers: ah() });
        if (r.ok) taxonomy = await r.json();
    } catch {}
    if (!taxonomy || taxonomy.length === 0) {
        alert('Dokumenten-Struktur konnte nicht geladen werden.');
        return;
    }

    // Ersten Typ mit passendem linked_field_code suchen.
    let matchedTyp = null, matchedKat = null;
    for (const k of taxonomy) {
        const t = (k.typen || []).find(x => x.linkedFieldCode === code);
        if (t) { matchedTyp = t; matchedKat = k; break; }
    }
    if (!matchedTyp) {
        alert('Kein Dokument-Typ mit dieser Verknüpfung gefunden.\n'
            + 'Bitte unter Systemeinstellungen → Dokument-Struktur einen Typ '
            + 'mit dem passenden Code anlegen.');
        return;
    }

    // Walter-Vorgabe 17.07.2026 (Ruecknahme des Umwegs): existiert genau EIN
    // Dokument dieses Typs, wird es DIREKT im Vorschaufenster geoeffnet —
    // OHNE Wechsel in die Doku-Verwaltung (gruene Pille = ansehen). Erst bei
    // keinem (Upload noetig) oder mehreren Dokumenten (Auswahl noetig) geht
    // es wie bisher in den Dokumente-Tab mit gesetztem Filter.
    try {
        const rd = await fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah() });
        if (rd.ok) {
            const alle = await rd.json();
            const typDocs = (alle || []).filter(d => d.dokumentTypId === matchedTyp.id);
            if (typDocs.length === 1 && typeof qstOpenBefreiungsDok === 'function') {
                qstOpenBefreiungsDok(selectedEmployeeId, typDocs[0].id);
                return;
            }
        }
    } catch {}

    // Auf Dokumente-Tab umschalten — triggert loadEmpDokumente().
    if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');

    // Warten bis loadEmpDokumente die Liste fertig geladen hat — sonst
    // überschreibt sein renderDokumenteUi unseren Filter wieder.
    let attempts = 0;
    while (!document.getElementById('empTabDokumente')
            ?.querySelector('.dok-tree-node')
           && attempts < 30) {
        await new Promise(r => setTimeout(r, 100));
        attempts++;
    }

    // Filter setzen — gleiche Funktion wie im Tree-Klick.
    if (typeof dokSelectType === 'function') {
        dokSelectType(matchedTyp.id, matchedKat.id);
    }

    // Walter-Vorgabe 07.06.2026: bei genau EINEM Dokument im gefilterten
    // Typ direkt das Vorschau-Panel öffnen — spart einen Klick. Bei mehreren
    // bleibt die Liste sichtbar (Walter wählt selbst).
    try {
        const docs = (typeof _dokState !== 'undefined' && Array.isArray(_dokState.docs))
            ? _dokState.docs.filter(d => d.dokumentTypId === matchedTyp.id)
            : [];
        if (docs.length === 1 && typeof dokOpenPreviewPanel === 'function') {
            dokOpenPreviewPanel(docs[0].id);
        }
    } catch {}
}

// Sprung aus einer Krank-/Unfall-Absenz direkt in den Dokumente-Tab, gefiltert
// auf den Dokument-Typ „Arztzeugnis". Anders als openLinkedDoc() matcht es über
// den Typ-NAMEN (nicht über linked_field_code) — der Typ „Arztzeugnis" ist im
// Standard-Dokumentenraster (Kategorie Absenzen) fix vorhanden.
async function openAbsenceArztzeugnis() {
    if (!selectedEmployeeId) return;
    // Auf Dokumente-Tab umschalten — triggert loadEmpDokumente().
    if (typeof switchEmpTab === 'function') switchEmpTab('dokumente');

    let taxonomy = null;
    try {
        const r = await fetch('/api/documents/taxonomie', { headers: ah() });
        if (r.ok) taxonomy = await r.json();
    } catch {}
    if (!taxonomy || taxonomy.length === 0) {
        alert('Dokumenten-Struktur konnte nicht geladen werden.');
        return;
    }

    // Ersten Typ suchen, dessen Name „Arztzeugnis" enthält (case-insensitive).
    let matchedTyp = null, matchedKat = null;
    for (const k of taxonomy) {
        const t = (k.typen || []).find(x => (x.name || '').toLowerCase().includes('arztzeugnis'));
        if (t) { matchedTyp = t; matchedKat = k; break; }
    }
    if (!matchedTyp) {
        alert('Kein Dokument-Typ „Arztzeugnis" gefunden.\n'
            + 'Bitte unter Systemeinstellungen → Dokument-Struktur einen Typ '
            + 'namens „Arztzeugnis" anlegen.');
        return;
    }

    // Warten bis loadEmpDokumente die Tree-Liste fertig gerendert hat — sonst
    // überschreibt sein renderDokumenteUi unseren Filter wieder.
    let attempts = 0;
    while (!document.getElementById('empTabDokumente')
            ?.querySelector('.dok-tree-node')
           && attempts < 30) {
        await new Promise(r => setTimeout(r, 100));
        attempts++;
    }

    // Filter setzen — gleiche Funktion wie im Tree-Klick.
    if (typeof dokSelectType === 'function') {
        dokSelectType(matchedTyp.id, matchedKat.id);
    }

    // Walter-Vorgabe 07.06.2026: bei genau EINEM Dokument im gefilterten
    // Typ direkt das Vorschau-Panel öffnen — spart einen Klick. Bei mehreren
    // bleibt die Liste sichtbar (Walter wählt selbst).
    try {
        const docs = (typeof _dokState !== 'undefined' && Array.isArray(_dokState.docs))
            ? _dokState.docs.filter(d => d.dokumentTypId === matchedTyp.id)
            : [];
        if (docs.length === 1 && typeof dokOpenPreviewPanel === 'function') {
            dokOpenPreviewPanel(docs[0].id);
        }
    } catch {}
}

function getInitials(first, last) {
    const f = (first ?? '').trim()[0] ?? '';
    const l = (last  ?? '').trim()[0] ?? '';
    return (f + l).toUpperCase() || '?';
}

function formatDate(dateStr) {
    if (!dateStr) return '–';
    const d = new Date(dateStr);
    if (isNaN(d)) return dateStr;
    return d.toLocaleDateString('de-CH');
}

// Geschlecht-Code (female/male/m/w) → menschen-lesbares Label.
// Übersetzung folgt der UI-Sprache (i18n); Default ist Deutsch falls i18n
// noch nicht geladen ist. Edit-Modal-Dropdown bleibt mit englischen
// value-Codes (female/male) — wir mappen nur die Anzeige.
function formatGender(g) {
    if (!g) return null;
    const v = String(g).toLowerCase();
    if (v === 'female' || v === 'f' || v.startsWith('w')) return _t('ma.value.gender.female','Weiblich');
    if (v === 'male'   || v === 'm' || v.startsWith('m')) return _t('ma.value.gender.male',  'Männlich');
    if (v === 'divers' || v === 'diverse' || v === 'andere' || v === 'other' || v === 'x' || v === 'd') return _t('ma.value.gender.divers','Divers');
    return g;
}

// Zivilstand-Code (ledig/verheiratet/...) → Anzeige-Label.
// Backend speichert lowercase ohne Umlaute; UI zeigt mit korrekter Schreibweise
// und in EN/DE.
function formatMaritalStatus(s) {
    if (!s) return null;
    if (typeof ZIV_OPTIONEN !== 'undefined') {
        const hit = ZIV_OPTIONEN.find(([v]) => v === String(s).toLowerCase().trim());
        if (hit) return hit[1];
    }
    const key = 'ma.value.maritalStatus.' + String(s).toLowerCase().trim();
    if (window.i18n && window.i18n.t) {
        const v = window.i18n.t(key);
        if (v && v !== key) return v;   // gefunden
    }
    // Fallback: ersten Buchstaben gross
    return String(s).charAt(0).toUpperCase() + String(s).slice(1);
}

// Sprach-Code (de/fr/it/en) → Anzeige-Label in der UI-Sprache.
function formatLanguage(code) {
    if (!code) return null;
    const key = 'ma.value.language.' + String(code).toLowerCase().trim();
    if (window.i18n && window.i18n.t) {
        const v = window.i18n.t(key);
        if (v && v !== key) return v;
    }
    return code;
}

// Anrede (Herr/Frau, ggf. lowercase aus DB) → Display.
function formatSalutation(s) {
    if (!s) return null;
    const v = String(s).toLowerCase().trim();
    if (v === 'herr' || v === 'mr' || v === 'mr.') return _t('ma.value.salutation.herr','Herr');
    if (v === 'frau' || v === 'ms' || v === 'mrs.' || v === 'ms.') return _t('ma.value.salutation.frau','Frau');
    if (v === 'divers' || v === 'diverse') return null;
    return s;
}

