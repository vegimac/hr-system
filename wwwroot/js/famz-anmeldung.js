// ══════════════════════════════════════════════════════════════════════
//  ANMELDUNG FAMILIENZULAGEN — GastroSocial FOR 0402 (HR-Hub → Behörden-
//  Korrespondenz). Walter 11.10.2026, docs/famz-anmeldung-konzept.md.
//  MA wählen → Kinder wählen → Vorbefüllung aus OneCrew (Familie, Vertrag,
//  Zivilstand) → alles editierbar → PDF im Vorschaufenster. Persistiert nichts;
//  fehlende Angaben gehören in die Familie-Maske, nicht hierher.
//  Rot = Pflichtangabe für die Kasse fehlt (famzPflicht), gelb = Hinweis.
// ══════════════════════════════════════════════════════════════════════
let _famzAlleMa = [];
let _famzEmpId = null;
let _famzDaten = null;        // Antwort von /prefill (formular wird beim PDF überschrieben)
let _famzPendingEmpId = null;
let _famzPartnerOffen = false;
let _famzStart = {};          // Feldwerte direkt nach dem Rendern (leer = in OneCrew leer)

// Felder, die «In OneCrew speichern» nachtragen kann — gleiche Liste wie
// Services/FamzRueckschreiben.cs (nur leere OneCrew-Felder, nie überschreiben).
const FAMZ_SPEICHERBAR = {
    antragsteller: ['zivilstandSeit', 'ahvNummer'],
    eltern: ['geschlecht', 'geburtsdatum', 'ahvNummer', 'zivilstand', 'angestellt', 'arbeitsortKanton', 'arbeitspensum',
        'einkommenHoeher', 'einkommenUeberMinimum', 'seNe', 'ausgleichskasse', 'leistungBezug', 'leistungVon1'],
    kind: ['geschlecht', 'geburtsdatum', 'beziehung'],
};
function famzSpeicherbar(pfad) {
    const t = pfad.split('.');
    if (t.length === 2 && t[0] === 'antragsteller') return FAMZ_SPEICHERBAR.antragsteller.includes(t[1]);
    if (t.length === 2 && (t[0] === 'andererElternteil' || t[0] === 'partner')) return FAMZ_SPEICHERBAR.eltern.includes(t[1]);
    if (t.length === 3 && t[0] === 'kinder') return FAMZ_SPEICHERBAR.kind.includes(t[2]);
    return false;
}

const FAMZ_JN = { '': '–', ja: 'Ja', nein: 'Nein' };
const FAMZ_ZIVIL = { '': '–', ledig: 'ledig', verheiratet: 'verheiratet', getrennt: 'getrennt', geschieden: 'geschieden',
    verwitwet: 'verwitwet', eingetragene_partnerschaft: 'eingetr. Partnerschaft', aufgeloeste_partnerschaft: 'aufgelöste Partnerschaft' };
const FAMZ_GESCHLECHT = { '': '–', F: 'weiblich', M: 'männlich' };
const FAMZ_SENE = { '': '–', NEIN: 'nein', SE: 'selbständig', NE: 'nichterwerbstätig' };
const FAMZ_ELTERN = { '': '–', ELTERN: 'beide Eltern', MUTTER: 'Mutter', VATER: 'Vater' };
const FAMZ_BEZ = { '': '–', L: 'leiblich', A: 'Adoptivkind', S: 'Stiefkind', P: 'Pflegekind', G: 'Geschwister', E: 'Enkel' };

function famzEsc(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

async function famzInit() {
    try { _famzAlleMa = await loadEmployeeLookup(); } catch { _famzAlleMa = []; }
    _famzEmpId = null;
    _famzDaten = null;
    const block = document.getElementById('famzFormBlock');
    if (block) block.style.display = 'none';
    for (const id of ['famzKopfKinder', 'famzKopfMeldungen']) {
        const el = document.getElementById(id);
        if (el) el.innerHTML = '';
    }
    famzRenderMaListe();
    if (_famzPendingEmpId != null) {
        const id = _famzPendingEmpId;
        _famzPendingEmpId = null;
        const sel = document.getElementById('famzEmpSelect');
        if (sel) sel.value = String(id);
        await famzLaden(id);
    }
}

/** Sprung aus dem Familie-Tab: Seite öffnen und MA vorbefüllen. */
function famzOpenForEmployee(empId) {
    _famzPendingEmpId = empId;
    window.activeEmpId = empId;
    showPage('famz-anmeldung');
}

function famzRenderMaListe() {
    const sel = document.getElementById('famzEmpSelect');
    if (!sel) return;
    const suche = (document.getElementById('famzEmpSearch')?.value || '').toLowerCase().trim();
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    const heute = new Date().toISOString().slice(0, 10);
    const laeuft = v => v.isActive !== false
        && (!v.contractStartDate || String(v.contractStartDate).slice(0, 10) <= heute)
        && (!v.contractEndDate || String(v.contractEndDate).slice(0, 10) >= heute);
    const inFiliale = e => {
        if (!cid) return true;
        const emps = e.employments || [];
        if (emps.length === 0) return true;
        return emps.some(v => laeuft(v) && (v.companyProfileId === cid || v.companyProfileId == null));
    };
    let liste = _famzAlleMa.filter(inFiliale).filter(e => e.isActive);
    if (suche) liste = liste.filter(e =>
        `${e.firstName || ''} ${e.lastName || ''}`.toLowerCase().includes(suche) || (e.employeeNumber || '').toLowerCase().includes(suche));
    liste.sort((a, b) => (a.firstName || '').localeCompare(b.firstName || '') || (a.lastName || '').localeCompare(b.lastName || ''));
    sel.innerHTML = liste.map(e => `<option value="${e.id}">${famzEsc(e.firstName)} ${famzEsc(e.lastName)} (${famzEsc(e.employeeNumber || '–')})</option>`).join('');
    sel.value = _famzEmpId ? String(_famzEmpId) : '';
}

async function famzMaGewaehlt() {
    const id = parseInt(document.getElementById('famzEmpSelect')?.value || '0', 10);
    if (id) await famzLaden(id);
}

async function famzLaden(empId, kinderIds) {
    _famzEmpId = empId;
    window.activeEmpId = empId;
    const q = kinderIds ? `?kinder=${kinderIds.join(',')}` : '';
    try {
        const r = await fetch(`/api/famz-anmeldung/${empId}/prefill${q}`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) {
            let msg = 'Vorbefüllung fehlgeschlagen';
            try { const j = await r.json(); if (j.message) msg = j.message; } catch { /* kein JSON */ }
            showToast(msg, 'error');
            return;
        }
        _famzDaten = await r.json();
    } catch {
        showToast('Vorbefüllung fehlgeschlagen', 'error');
        return;
    }
    _famzPartnerOffen = !!_famzDaten.formular?.partner;
    _famzStart = {};
    famzRender();
}

// ── Felder ───────────────────────────────────────────────────────────────
// Jedes Eingabefeld trägt seinen Pfad im Formular-Objekt (data-pfad) und
// seinen Typ (text | jn | sel | bool). Gelesen wird nur über diese Pfade.
function famzWert(pfad) {
    return pfad.split('.').reduce((o, k) => (o == null ? undefined : o[k]), _famzDaten?.formular);
}
const famzSpan = n => n > 1 ? ` style="grid-column:span ${n}"` : '';
function famzText(label, pfad, opt = {}) {
    const v = famzWert(pfad);
    return `<label class="famz-f" data-feld="${pfad}"${famzSpan(opt.span)}>${label}
        <input data-pfad="${pfad}" data-typ="text" value="${famzEsc(v ?? '')}"${opt.ph ? ` placeholder="${famzEsc(opt.ph)}"` : ''}></label>`;
}
function famzOptionen(werte, v) {
    return Object.entries(werte).map(([k, t]) => `<option value="${k}"${String(v ?? '') === k ? ' selected' : ''}>${famzEsc(t)}</option>`).join('');
}
function famzSel(label, pfad, werte, opt = {}) {
    return `<label class="famz-f" data-feld="${pfad}"${famzSpan(opt.span)}>${label}
        <select data-pfad="${pfad}" data-typ="sel" onchange="famzPruefen()">${famzOptionen(werte, famzWert(pfad))}</select></label>`;
}
function famzJn(label, pfad, opt = {}) {
    const v = famzWert(pfad);
    const w = v === true ? 'ja' : v === false ? 'nein' : '';
    return `<label class="famz-f" data-feld="${pfad}"${famzSpan(opt.span)}>${label}
        <select data-pfad="${pfad}" data-typ="jn" onchange="famzPruefen()">${famzOptionen(FAMZ_JN, w)}</select></label>`;
}
function famzTitel(text, info = '', knopf = '') {
    return `<div class="famz-titel">${text}${info}${knopf}</div>`;
}
const famzRaster = inhalt => `<div class="famz-raster">${inhalt}</div>`;

function famzPerson(p) {
    return famzText('Name, Vorname', `${p}.nameVorname`, { span: 2 })
        + famzSel('Geschlecht', `${p}.geschlecht`, FAMZ_GESCHLECHT)
        + famzText('Geburtsdatum', `${p}.geburtsdatum`, { ph: 'tt.mm.jjjj' })
        + famzText('AHV-Nr.', `${p}.ahvNummer`, { ph: '756.…' })
        + famzText('Nationalität', `${p}.nationalitaet`)
        + famzText('Strasse, Nr.', `${p}.strasseNr`, { span: 2 })
        + famzText('PLZ, Ort', `${p}.plzOrt`, { span: 2 })
        + famzText('Kanton', `${p}.kanton`)
        + famzText('Ausländerausweis', `${p}.auslaenderausweis`)
        + famzSel('Zivilstand', `${p}.zivilstand`, FAMZ_ZIVIL)
        + famzText('Zivilstand seit', `${p}.zivilstandSeit`, { ph: 'tt.mm.jjjj' });
}
function famzElternteil(p) {
    return famzPerson(p)
        + famzJn('Angestellt?', `${p}.angestellt`)
        + famzText('Arbeitgeber (Name, Strasse)', `${p}.arbeitgeberZeile1`, { span: 3 })
        + famzText('Arbeitgeber (PLZ Ort, Telefon)', `${p}.arbeitgeberZeile2`, { span: 2 })
        + famzText('Arbeitsort Kanton', `${p}.arbeitsortKanton`)
        + famzText('Pensum %', `${p}.arbeitspensum`)
        + famzJn('Verdient mehr als MA?', `${p}.einkommenHoeher`)
        + famzJn('Einkommen > 7\'560/J.?', `${p}.einkommenUeberMinimum`)
        + famzSel('SE/NE angemeldet?', `${p}.seNe`, FAMZ_SENE)
        + famzText('Ausgleichskasse', `${p}.ausgleichskasse`, { span: 2 })
        + famzJn('Bezieht Leistungen?', `${p}.leistungBezug`)
        + famzText('Von wem', `${p}.leistungVon1`, { span: 2 })
        + famzText('Von wem (2)', `${p}.leistungVon2`, { span: 2 });
}

function famzRender() {
    const d = _famzDaten;
    const block = document.getElementById('famzFormBlock');
    if (!d || !block) return;
    const f = d.formular || {};

    // Kopf Mitte: Kinder-Auswahl (max. 5 pro Formular)
    const auswahl = (d.kinderAuswahl || []).map(k => `
        <label class="famz-kind-cb-zeile">
            <input type="checkbox" class="famz-kind-cb" value="${k.id}"${k.gewaehlt ? ' checked' : ''} style="width:15px;height:15px">
            ${famzEsc(k.name)} <span>${famzEsc(k.geburtsdatum || '')}${k.hatZulage ? ' · Zulage erfasst' : ''}</span>
        </label>`).join('') || '<div style="color:#8b8b8b;font-size:12.5px">Keine Kinder bis 25 Jahre erfasst.</div>';
    const kopfKinder = document.getElementById('famzKopfKinder');
    if (kopfKinder) kopfKinder.innerHTML = `
        <div class="famz-kopf-titel">Kinder auf diesem Formular <span style="font-weight:400;color:#8b8b8b">(max. 5)</span></div>
        ${auswahl}
        ${(d.kinderAuswahl || []).length ? `<button type="button" class="famz-btn-sek" style="margin-top:6px" onclick="famzKinderNeu()"
            title="Lädt die Vorbefüllung neu — Änderungen unten gehen dabei verloren">Auswahl übernehmen</button>` : ''}`;

    const kinder = (f.kinder || []).map((k, i) => `
        <tr>
            <td data-feld="kinder.${i}.name"><input data-pfad="kinder.${i}.name" data-typ="text" value="${famzEsc(k.name || '')}"></td>
            <td data-feld="kinder.${i}.vorname"><input data-pfad="kinder.${i}.vorname" data-typ="text" value="${famzEsc(k.vorname || '')}"></td>
            <td data-feld="kinder.${i}.geburtsdatum" style="width:100px"><input data-pfad="kinder.${i}.geburtsdatum" data-typ="text" value="${famzEsc(k.geburtsdatum || '')}"></td>
            <td data-feld="kinder.${i}.geschlecht" style="width:110px"><select data-pfad="kinder.${i}.geschlecht" data-typ="sel" onchange="famzPruefen()">${famzOptionen(FAMZ_GESCHLECHT, k.geschlecht)}</select></td>
            <td data-feld="kinder.${i}.beziehung" style="width:130px"><select data-pfad="kinder.${i}.beziehung" data-typ="sel" onchange="famzPruefen()">${famzOptionen(FAMZ_BEZ, k.beziehung)}</select></td>
            <td style="text-align:center;width:70px"><input type="checkbox" data-pfad="kinder.${i}.erwerbsunfaehig" data-typ="bool"${k.erwerbsunfaehig ? ' checked' : ''} style="width:15px;height:15px"></td>
            <td data-feld="kinder.${i}.wohnadresse"><input data-pfad="kinder.${i}.wohnadresse" data-typ="text" value="${famzEsc(k.wohnadresse || '')}" placeholder="nur wenn nicht beim MA"></td>
        </tr>`).join('');

    const aeInfo = f.andererElternteil ? ''
        : '<span class="famz-titel-info rot">nicht erfasst — am besten in Familie → Kind → «Anderer Elternteil» ergänzen</span>';
    const partnerInfo = f.partner ? '' : '<span class="famz-titel-info">keine/r, oder ist der andere Elternteil</span>';
    const partnerKnopf = _famzPartnerOffen ? ''
        : '<button type="button" class="famz-titel-knopf" onclick="famzPartnerOeffnen()">von Hand ausfüllen</button>';

    block.innerHTML = `
        ${famzTitel('1. Antrag')}
        ${famzRaster(
            famzSel('Ich beantrage', 'beantrage', { FAMILIENZULAGE: 'Familienzulage', GEBURTSZULAGE: 'Geburts-/Adoptionszulage', DIFFERENZZULAGE: 'Differenzzulage' })
            + famzSel('Ich bin', 'status', { ARBEITNEHMER: 'Arbeitnehmer/in', SE: 'Selbständig', NE: 'Nichterwerbstätig' })
            + famzText('Ab wann', 'abWann', { ph: 'tt.mm.jjjj' })
            + famzText('Erreichbar (Telefon / E-Mail)', 'erreichbar', { span: 3 })
            + famzJn('Beziehe ich Leistungen?', 'leistungBezug')
            + famzText('Leistungen von', 'leistungVon', { span: 2 }))}

        ${famzTitel('2. Antragsteller/in (Mitarbeiter/in)')}
        ${famzRaster(famzPerson('antragsteller'))}

        ${famzTitel('3. Arbeitgeber')}
        ${famzRaster(
            famzText('Firma', 'agName', { span: 2 })
            + famzText('Abrechnungsnummer', 'agAbrechnungsnummer')
            + famzText('Strasse', 'agStrasse', { span: 2 })
            + famzText('PLZ, Ort', 'agPlzOrt')
            + famzText('Beschäftigt von', 'beschaeftigtVon', { ph: 'tt.mm.jjjj' })
            + famzText('Beschäftigt bis', 'beschaeftigtBis', { ph: 'leer = unbefristet' })
            + famzText('Pensum %', 'arbeitspensum')
            + famzText('Arbeitsort Kanton', 'arbeitsortKanton')
            + famzText('Bruttolohn', 'bruttolohn')
            + famzJn('Einkommen > 7\'560/J.?', 'einkommenUeberMinimum')
            + famzJn('Weitere Arbeitgeber?', 'weitereArbeitgeber')
            + famzText('Weiterer Arbeitgeber 1', 'weitereAg1', { span: 2 })
            + famzText('Weiterer Arbeitgeber 2', 'weitereAg2')
            + famzText('Höheres Einkommen bei', 'hoeheresEinkommenBei', { span: 2 }))}

        ${famzTitel('4. Anderer Elternteil', aeInfo)}
        ${famzRaster(famzElternteil('andererElternteil'))}

        ${famzTitel('5. Ehepartner/in', partnerInfo, partnerKnopf)}
        ${_famzPartnerOffen ? famzRaster(famzElternteil('partner')) : ''}

        ${famzTitel('6. Kinder')}
        <div style="overflow-x:auto">
            <table class="famz-kinder">
                <thead><tr><th>Name</th><th>Vorname</th><th>Geburt</th><th>Geschlecht</th><th>Beziehung</th><th>erwerbs&shy;unfähig</th><th>Wohnadresse</th></tr></thead>
                <tbody>${kinder || '<tr><td colspan="7" style="color:#8b8b8b">keine Kinder gewählt</td></tr>'}</tbody>
            </table>
        </div>
        <div style="margin-top:6px">${famzRaster(
            famzSel('Elterliche Sorge', 'sorge', FAMZ_ELTERN)
            + famzText('Bemerkung Sorge', 'sorgeBemerkung', { span: 2 })
            + famzSel('Kinder leben bei', 'lebenBei', FAMZ_ELTERN)
            + famzText('Bemerkung Wohnen', 'lebenBeiBemerkung', { span: 2 }))}</div>

        ${famzTitel('7. Bemerkungen und Unterschrift')}
        ${famzRaster(
            famzText('Bemerkung 1', 'bemerkung1', { span: 3 })
            + famzText('Bemerkung 2', 'bemerkung2', { span: 3 })
            + famzText('Bemerkung 3', 'bemerkung3', { span: 2 })
            + famzText('Ort, Datum Antragsteller/in', 'ortDatumAntragsteller', { span: 2 })
            + famzText('Ort, Datum Arbeitgeber', 'ortDatumArbeitgeber', { span: 2 }))}

        <div class="famz-fuss">
            <span id="famzFussInfo" class="famz-fuss-info"></span>
            <button id="famzSpeichernBtn" class="famz-btn-sek" style="display:none;padding:8px 16px;font-size:13px" onclick="famzInOneCrewSpeichern()"></button>
            <button id="famzPdfBtn" class="famz-btn-primaer" onclick="famzPdf()">PDF-Vorschau</button>
        </div>`;
    block.style.display = '';
    if (!block._famzInput) {
        block.addEventListener('input', () => famzPruefen());
        block._famzInput = true;
    }
    // Ausgangswert nur beim ersten Erscheinen merken — ein Neu-Rendern (Ehepartner
    // einblenden) macht schon Eingetragenes nicht zum Ausgangswert.
    block.querySelectorAll('[data-pfad]').forEach(el => {
        const p = el.dataset.pfad;
        if (famzSpeicherbar(p) && !(p in _famzStart)) _famzStart[p] = (el.value || '').trim();
    });
    famzPruefen();
}

/** Speicherbare Felder, die beim Laden leer waren und jetzt einen Wert haben. */
function famzOffeneFelder() {
    const f = _famzDaten?.formular || {};
    const hatId = p => {
        const t = p.split('.');
        if (t[0] === 'andererElternteil' || t[0] === 'partner') return !!f[t[0]]?.familyMemberId;
        if (t[0] === 'kinder') return !!f.kinder?.[+t[1]]?.familyMemberId;
        return true;
    };
    return Object.keys(_famzStart).filter(p => _famzStart[p] === '' && (famzFeldWert(p) || '') !== '' && hatId(p));
}

async function famzInOneCrewSpeichern() {
    const felder = famzOffeneFelder();
    if (!_famzEmpId || felder.length === 0) return;
    const btn = document.getElementById('famzSpeichernBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Speichere…'; }
    const formular = famzSammeln();
    try {
        const res = await fetch(`/api/famz-anmeldung/${_famzEmpId}/in-onecrew-speichern`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ formular, felder }),
        });
        let j = {};
        try { j = await res.json(); } catch { /* kein JSON */ }
        if (!res.ok) { showToast(j.message || 'Speichern fehlgeschlagen', 'error'); return; }
        const liste = j.gespeichert || [];
        showToast(liste.length ? `In OneCrew gespeichert: ${liste.join(', ')}` : 'Nichts gespeichert — die Felder sind in OneCrew schon ausgefüllt.',
            liste.length ? 'success' : 'info');
        // Meldungen neu holen, Eingaben im Formular behalten.
        const kinder = (_famzDaten.kinderAuswahl || []).filter(k => k.gewaehlt).map(k => k.id);
        const r = await fetch(`/api/famz-anmeldung/${_famzEmpId}/prefill${kinder.length ? `?kinder=${kinder.join(',')}` : ''}`,
            { headers: ah(), cache: 'no-store' });
        if (r.ok) _famzDaten = { ...(await r.json()), formular };
        else _famzDaten.formular = formular;
        _famzStart = {};
        famzRender();
    } catch {
        showToast('Speichern fehlgeschlagen', 'error');
    } finally {
        const b = document.getElementById('famzSpeichernBtn');
        if (b) b.disabled = false;
        famzPruefen();
    }
}

/** Ehepartner-Block auf Wunsch einblenden — Eingaben oben bleiben erhalten. */
function famzPartnerOeffen() {
    if (!_famzDaten) return;
    _famzDaten.formular = famzSammeln();
    _famzPartnerOffen = true;
    famzRender();
}

// ── Pflichtangaben (rot) ─────────────────────────────────────────────────
// Nur was die Kasse wirklich braucht; bedingte Felder erst, wenn ihre
// Auslöser-Frage es verlangt (z.B. «Leistungen von» nur bei «Ja»).
function famzFeldWert(pfad) {
    const el = document.querySelector(`#famzFormBlock [data-pfad="${pfad}"]`);
    if (!el) return null;
    return (el.value || '').trim();
}
function famzPflicht() {
    const v = famzFeldWert;
    const istCh = n => /schweiz|^ch$/i.test(n || '');
    const liste = [];
    const add = (...pfade) => liste.push(...pfade);

    add('beantrage', 'status', 'abWann');
    if (v('leistungBezug') === 'ja') add('leistungVon');

    const a = 'antragsteller';
    add(`${a}.nameVorname`, `${a}.geschlecht`, `${a}.geburtsdatum`, `${a}.ahvNummer`, `${a}.nationalitaet`,
        `${a}.strasseNr`, `${a}.plzOrt`, `${a}.zivilstand`);
    if (v(`${a}.zivilstand`) && v(`${a}.zivilstand`) !== 'ledig') add(`${a}.zivilstandSeit`);
    if (v(`${a}.nationalitaet`) && !istCh(v(`${a}.nationalitaet`))) add(`${a}.auslaenderausweis`);

    add('agName', 'agAbrechnungsnummer', 'agStrasse', 'agPlzOrt', 'beschaeftigtVon', 'arbeitspensum',
        'arbeitsortKanton', 'bruttolohn', 'einkommenUeberMinimum', 'weitereArbeitgeber');
    if (v('weitereArbeitgeber') === 'ja') add('weitereAg1', 'hoeheresEinkommenBei');

    const elternteil = p => {
        add(`${p}.nameVorname`, `${p}.geschlecht`, `${p}.geburtsdatum`, `${p}.nationalitaet`,
            `${p}.strasseNr`, `${p}.plzOrt`, `${p}.zivilstand`, `${p}.angestellt`);
        const wohntInCh = !!v(`${p}.kanton`);
        if (wohntInCh) add(`${p}.ahvNummer`);
        if (wohntInCh && v(`${p}.nationalitaet`) && !istCh(v(`${p}.nationalitaet`))) add(`${p}.auslaenderausweis`);
        if (v(`${p}.angestellt`) === 'ja')
            add(`${p}.arbeitgeberZeile1`, `${p}.arbeitsortKanton`, `${p}.einkommenHoeher`, `${p}.einkommenUeberMinimum`);
        if (v(`${p}.angestellt`) === 'nein') add(`${p}.seNe`);
        if (v(`${p}.leistungBezug`) === 'ja') add(`${p}.leistungVon1`);
    };
    elternteil('andererElternteil');
    if (_famzPartnerOffen) elternteil('partner');

    (_famzDaten?.formular?.kinder || []).forEach((_, i) =>
        add(`kinder.${i}.name`, `kinder.${i}.vorname`, `kinder.${i}.geburtsdatum`, `kinder.${i}.geschlecht`, `kinder.${i}.beziehung`));
    add('sorge', 'lebenBei');
    return liste;
}

/** Leere Pflichtfelder rot markieren, Meldungen oben + Zähler unten auffrischen. */
function famzPruefen() {
    const block = document.getElementById('famzFormBlock');
    if (!block || !_famzDaten) return;
    block.querySelectorAll('[data-feld].fehlt').forEach(el => el.classList.remove('fehlt'));
    let anzahl = 0;
    for (const pfad of famzPflicht()) {
        const w = famzFeldWert(pfad);
        if (w == null || w !== '') continue;
        block.querySelector(`[data-feld="${pfad}"]`)?.classList.add('fehlt');
        anzahl++;
    }
    // «(Adresse nicht erfasst)» kommt so aus OneCrew — Adresse fehlt wirklich.
    block.querySelectorAll('input[data-pfad$=".wohnadresse"]').forEach(el => {
        if (/nicht erfasst/i.test(el.value)) { el.closest('[data-feld]')?.classList.add('fehlt'); anzahl++; }
    });

    const fuss = document.getElementById('famzFussInfo');
    if (fuss) fuss.textContent = anzahl ? `${anzahl} ${anzahl === 1 ? 'Angabe fehlt' : 'Angaben fehlen'} (rot)` : '';
    const sp = document.getElementById('famzSpeichernBtn');
    if (sp && !sp.disabled) {
        const n = famzOffeneFelder().length;
        sp.style.display = n ? '' : 'none';
        sp.textContent = `In OneCrew speichern (${n})`;
        sp.title = 'Trägt die hier ergänzten Angaben in OneCrew nach (nur Felder, die dort leer waren). '
            + 'Name, Adresse, Nationalität, Bewilligung und Arbeitgeber bitte in der Familie-Maske ändern.';
    }

    const d = _famzDaten;
    const fehlt = (d.fehlt || []).map(t => `<div>• ${famzEsc(t)}</div>`).join('');
    const hinweise = (d.hinweise || []).map(h => `<div>${famzEsc(h)}</div>`).join('');
    const beilagen = (d.beilagen || []).map(b => `<li>${famzEsc(b)}</li>`).join('');
    const rot = fehlt || anzahl
        ? `<div class="famz-box famz-box-rot"><b>Fehlt</b>${fehlt}${anzahl ? `<div>• ${anzahl} rot markierte${anzahl === 1 ? 's Feld' : ' Felder'} im Formular</div>` : ''}</div>`
        : '';
    const meld = document.getElementById('famzKopfMeldungen');
    if (meld) meld.innerHTML = `
        ${rot}
        ${hinweise ? `<div class="famz-box famz-box-gelb">${hinweise}</div>` : ''}
        ${beilagen ? `<details class="famz-beilagen"><summary>Beilagen (${(d.beilagen || []).length})</summary><ul>${beilagen}</ul></details>` : ''}`;
}

async function famzKinderNeu() {
    const ids = [...document.querySelectorAll('.famz-kind-cb:checked')].map(cb => parseInt(cb.value, 10));
    if (ids.length === 0) { showToast('Mindestens ein Kind wählen.', 'error'); return; }
    if (ids.length > 5) { showToast('Höchstens 5 Kinder pro Formular — für weitere ein zweites Formular erstellen.', 'error'); return; }
    if (!_famzEmpId) return;
    await famzLaden(_famzEmpId, ids);
}

/** Formular-Objekt aus den Feldern zusammensetzen (Basis = Vorbefüllung). */
function famzSammeln() {
    const f = JSON.parse(JSON.stringify(_famzDaten.formular || {}));
    for (const k of ['antragsteller', 'andererElternteil', 'partner']) if (!f[k]) f[k] = {};
    document.querySelectorAll('#famzFormBlock [data-pfad]').forEach(el => {
        const teile = el.dataset.pfad.split('.');
        let ziel = f;
        for (let i = 0; i < teile.length - 1; i++) {
            if (ziel[teile[i]] == null) ziel[teile[i]] = /^\d+$/.test(teile[i + 1]) ? [] : {};
            ziel = ziel[teile[i]];
        }
        const typ = el.dataset.typ;
        let v;
        if (typ === 'bool') v = el.checked;
        else if (typ === 'jn') v = el.value === 'ja' ? true : el.value === 'nein' ? false : null;
        else v = (el.value || '').trim() || null;
        ziel[teile[teile.length - 1]] = v;
    });
    // Leere Elternteil-Blöcke nicht mitschicken (sonst kreuzt das PDF Leeres an).
    for (const k of ['andererElternteil', 'partner']) {
        const p = f[k];
        const leer = Object.entries(p).every(([key, v]) => key === 'familyMemberId' || v == null || v === '');
        if (leer) f[k] = null;
    }
    return f;
}

async function famzPdf() {
    if (!_famzEmpId || !_famzDaten) return;
    const btn = document.getElementById('famzPdfBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Erzeuge PDF…'; }
    try {
        const res = await fetch(`/api/famz-anmeldung/${_famzEmpId}/pdf`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(famzSammeln()),
        });
        if (!res.ok) {
            let msg = 'PDF-Erzeugung fehlgeschlagen';
            try { const j = await res.json(); if (j.message) msg = j.message; } catch { /* kein JSON */ }
            showToast(msg, 'error');
            return;
        }
        const blob = await res.blob();
        const cd = res.headers.get('Content-Disposition') || '';
        await previewFileModal(blob, cdFilename(cd, 'Anmeldung-Familienzulagen.pdf'), { employeeId: _famzEmpId });
    } catch {
        showToast('PDF-Erzeugung fehlgeschlagen', 'error');
    } finally {
        if (btn) { btn.disabled = false; btn.textContent = 'PDF-Vorschau'; }
    }
}
