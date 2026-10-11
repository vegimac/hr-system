// QST-Verlauf am Kind (Walter 11.10.2026, docs/kind-qst-verlauf-konzept.md).
// Im Familien-Fenster: Ausbildung (Abschnitte), Haushalt & Unterhalt
// (Versionen ab Datum) und QST-Entscheide. Gerechnet wird nur auf dem Server
// (Services/KindQstVerlauf.cs) — hier wird nur angezeigt und erfasst.

let _kqv = { empId: null, memberId: null, daten: null, form: null, dokumente: null };

const KQV_ARTEN = { LEHRE: 'Lehre', SCHULE: 'Schule', STUDIUM: 'Studium', BRUECKE: 'Brückenangebot', ANDERE: 'andere' };
const KQV_ENDE = { ABSCHLUSS: 'abgeschlossen', ABBRUCH: 'abgebrochen', UNTERBRUCH: 'unterbrochen' };
const KQV_STATUS = { ANERKANNT: 'anerkannt', NICHT_ANERKANNT: 'nicht anerkannt', IN_PRUEFUNG: 'in Prüfung' };
const KQV_QUELLE = { HAND: '', ALTBESTAND: 'übernommen', ZULAGE: 'aus Zulage', SWISSDEC: 'Testdaten', ERFASSUNG: '' };

function kqvDatum(iso) { return iso ? `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }
function kqvMonat(iso) { return iso ? `${iso.slice(5, 7)}.${iso.slice(0, 4)}` : ''; }
function kqvJaNein(v) { return v === true ? 'ja' : v === false ? 'nein' : '<span style="color:#92400e">ungeklärt</span>'; }
function kqvEsc(s) { return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }
function kqvFehler(msg) {
    if (typeof showToast === 'function') showToast(msg, 'error'); else alert(msg);
}

/** Beim Öffnen des Familien-Fensters: Kind laden oder Hinweis für neue Kinder. */
async function kqvOeffnen(empId, member) {
    _kqv = { empId, memberId: member?.id ?? null, daten: null, form: null, dokumente: null };
    const box = document.getElementById('fmQstVerlauf');
    if (!box) return;
    if (!member?.id || member.memberType !== 'Kind') {
        box.innerHTML = `<div class="kqv-hinweis">Nach dem Speichern hier Ausbildung, Haushalt/Unterhalt und QST-Entscheide erfassen.</div>`;
        return;
    }
    box.innerHTML = `<div class="kqv-hinweis">Lade …</div>`;
    await kqvLaden();
}

async function kqvLaden() {
    try {
        const r = await fetch(`/api/employees/${_kqv.empId}/family/${_kqv.memberId}/qst-verlauf`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        _kqv.daten = await r.json();
    } catch (e) {
        document.getElementById('fmQstVerlauf').innerHTML = `<div class="kqv-hinweis" style="color:#991b1b">QST-Verlauf konnte nicht geladen werden (${kqvEsc(e.message)}).</div>`;
        return;
    }
    kqvRender();
}

async function kqvDokumente() {
    if (_kqv.dokumente) return _kqv.dokumente;
    try {
        const r = await fetch(`/api/documents/by-employee/${_kqv.empId}`, { headers: ah(), cache: 'no-store' });
        const liste = r.ok ? await r.json() : [];
        _kqv.dokumente = (Array.isArray(liste) ? liste : (liste.dokumente || liste.items || []))
            .map(d => ({ id: d.id, text: `${d.dokumentTypName || 'Dokument'} · ${d.filenameOriginal || ''}` }));
    } catch { _kqv.dokumente = []; }
    return _kqv.dokumente;
}

function kqvMenu(prefix, id, bearbeiten, loeschen) {
    return `<div class="dok-menu-wrap" onclick="event.stopPropagation()">
        <button type="button" class="dok-menu-btn" onclick="rowMenuToggle(event,'${prefix}',${id})" title="Aktionen">⋮</button>
        <div class="dok-menu" id="${prefix}Menu-${id}">
            <button type="button" class="dok-menu-item" onclick="rowMenuCloseAll();${bearbeiten}">Bearbeiten</button>
            ${loeschen ? `<button type="button" class="dok-menu-item danger" onclick="rowMenuCloseAll();${loeschen}">Löschen</button>` : ''}
        </div>
    </div>`;
}

function kqvDokLink(dokId) {
    if (!dokId) return '';
    return ` <a href="#" onclick="event.preventDefault();openDirectDoc(${dokId})" title="Beleg ansehen">📄</a>`;
}

function kqvRender() {
    // Verwaiste ⋮-Menüs (an body gehängt) vor dem Neuzeichnen entfernen — sonst doppelte IDs.
    document.querySelectorAll('[id^="kqvA"][id*="Menu-"],[id^="kqvV"][id*="Menu-"],[id^="kqvE"][id*="Menu-"]').forEach(e => e.remove());
    const d = _kqv.daten;
    const box = document.getElementById('fmQstVerlauf');
    if (!box || !d) return;
    const a = d.anzeige || {};
    const f = _kqv.form;

    const wirkung = (a.wirkung || []).map(w =>
        `<div style="color:#166534">${kqvMonat(w.von)} – ${w.bis ? kqvMonat(w.bis) : 'offen'} · ${kqvEsc(w.grund)}</div>`).join('');
    const gelb = [...(a.offen || []), ...(a.hinweise || [])].map(t => `<div style="color:#92400e">⚠ ${kqvEsc(t)}</div>`).join('');
    const kopf = `<div class="kqv-wirkung">
        <div class="kqv-sub">Zählt für die QST-Kinderziffer</div>
        ${wirkung || '<div style="color:#8b8b8b">in keinem Monat</div>'}
        ${a.zaehltHeute === false ? `<div style="color:#8b8b8b">zurzeit nicht · ${kqvEsc(a.grundHeute || '')}</div>` : ''}
        ${gelb}
        <div class="kqv-klein">Eine Änderung wirkt ab dem Folgemonat (KS 45). Lohnrelevant wird es erst mit einer QST-Erfassung durch HR.</div>
    </div>`;

    // ── Ausbildung ──
    const ausbZeilen = (d.ausbildungen || []).map(x => {
        if (f?.typ === 'A' && f.id === x.id) return kqvFormAusbildung(x);
        const quelle = KQV_QUELLE[x.quelle] ? ` · ${KQV_QUELLE[x.quelle]}` : '';
        return `<div class="kqv-zeile">
            <div class="kqv-text">
                <b>${x.von ? kqvDatum(x.von) : 'Beginn unbekannt'} – ${x.bis ? kqvDatum(x.bis) : 'läuft'}</b>
                · ${KQV_ARTEN[x.art] || 'andere'}${x.endeGrund ? ' · ' + (KQV_ENDE[x.endeGrund] || '') : ''}
                · Erstausbildung ${kqvJaNein(x.erstausbildung)}
                ${x.geprueft ? '' : ' · <span style="color:#92400e">nicht geprüft</span>'}${quelle}${kqvDokLink(x.dokumentId)}
                ${x.bemerkung ? `<div class="kqv-klein">${kqvEsc(x.bemerkung)}</div>` : ''}
            </div>
            ${kqvMenu('kqvA', x.id, `kqvBearbeiten('A',${x.id})`, `kqvLoeschen('ausbildung',${x.id})`)}
        </div>`;
    }).join('');
    const zulagen = (d.zulagenOhneAusbildung || []).map(z => `<div class="kqv-zeile kqv-gelb">
            <div class="kqv-text">Ausbildungszulage ${kqvDatum(z.von)} – ${z.bis ? kqvDatum(z.bis) : 'offen'}${kqvDokLink(z.dokumentId)}
                <div class="kqv-klein">Für die Familienzulage anerkannt — für die QST noch nicht erfasst.</div></div>
            <button type="button" class="kqv-btn" onclick="kqvAusZulage(${z.id})">Als QST-Ausbildung übernehmen</button>
        </div>`).join('');

    // ── Haushalt & Unterhalt ──
    const verhZeilen = (d.verhaeltnisse || []).map(x => {
        if (f?.typ === 'V' && f.id === x.id) return kqvFormVerhaeltnis(x);
        const quelle = KQV_QUELLE[x.quelle] ? ` · ${KQV_QUELLE[x.quelle]}` : '';
        return `<div class="kqv-zeile">
            <div class="kqv-text"><b>ab ${kqvDatum(x.gueltigAb)}</b> · im Haushalt ${kqvJaNein(x.imHaushalt)}
                · Unterhalt zur Hauptsache ${kqvJaNein(x.unterhaltHauptsache)}${quelle}
                ${x.bemerkung ? `<div class="kqv-klein">${kqvEsc(x.bemerkung)}</div>` : ''}</div>
            ${kqvMenu('kqvV', x.id, `kqvBearbeiten('V',${x.id})`, (d.verhaeltnisse.length > 1) ? `kqvLoeschen('verhaeltnis',${x.id})` : '')}
        </div>`;
    }).join('');

    // ── QST-Entscheide ──
    const entZeilen = (d.entscheide || []).map(x => {
        if (f?.typ === 'E' && f.id === x.id) return kqvFormEntscheid(x);
        const farbe = x.status === 'ANERKANNT' ? '#166534' : x.status === 'NICHT_ANERKANNT' ? '#991b1b' : '#92400e';
        const quelle = KQV_QUELLE[x.quelle] ? ` · ${KQV_QUELLE[x.quelle]}` : '';
        return `<div class="kqv-zeile">
            <div class="kqv-text"><b style="color:${farbe}">${KQV_STATUS[x.status] || x.status}</b>
                · ${kqvDatum(x.von)} – ${x.bis ? kqvDatum(x.bis) : 'offen'}${quelle}${kqvDokLink(x.dokumentId)}
                ${x.grund ? `<div class="kqv-klein">${kqvEsc(x.grund)}</div>` : ''}</div>
            ${kqvMenu('kqvE', x.id, `kqvBearbeiten('E',${x.id})`, `kqvLoeschen('qst-entscheid',${x.id})`)}
        </div>`;
    }).join('');

    const neu = (typ, label) => (f?.typ === typ && f.id == null)
        ? (typ === 'A' ? kqvFormAusbildung(null) : typ === 'V' ? kqvFormVerhaeltnis(null) : kqvFormEntscheid(null))
        : `<button type="button" class="kqv-btn-neu" onclick="kqvBearbeiten('${typ}',null)">+ ${label}</button>`;

    box.innerHTML = `${kopf}
        <div class="kqv-block"><div class="kqv-sub">Ausbildung</div>${ausbZeilen || '<div class="kqv-leer">keine erfasst</div>'}${zulagen}${neu('A', 'Ausbildung')}</div>
        <div class="kqv-block"><div class="kqv-sub">Haushalt &amp; Unterhalt</div>${verhZeilen || '<div class="kqv-leer">keine erfasst</div>'}${neu('V', 'Änderung ab Datum')}</div>
        <div class="kqv-block"><div class="kqv-sub">QST-Entscheid der Steuerverwaltung</div>${entZeilen || '<div class="kqv-leer">keiner — es gilt die Ausbildung</div>'}${neu('E', 'Entscheid')}</div>`;

    if (f && _kqv.dokumente && document.getElementById('kqvDok')) kqvDokOptionen(f.dokumentId);
}

function kqvSelect(id, werte, aktuell) {
    return `<select class="fmf-input" id="${id}">${Object.entries(werte)
        .map(([k, t]) => `<option value="${k}"${String(aktuell ?? '') === k ? ' selected' : ''}>${t}</option>`).join('')}</select>`;
}
const KQV_JNU = { '': 'ungeklärt', ja: 'ja', nein: 'nein' };
const kqvJnuWert = v => v === true ? 'ja' : v === false ? 'nein' : '';
const kqvJnuLesen = id => { const v = document.getElementById(id)?.value; return v === 'ja' ? true : v === 'nein' ? false : null; };

function kqvFormKnoepfe() {
    return `<div class="kqv-form-knoepfe">
        <button type="button" class="kqv-btn" onclick="kqvAbbrechen()">Abbrechen</button>
        <button type="button" class="kqv-btn kqv-btn-primaer" onclick="kqvSpeichern()">Übernehmen</button>
    </div>`;
}
function kqvDokFeld() {
    return `<div class="fmf-field"><label class="fmf-label">Beleg</label><select class="fmf-input" id="kqvDok"><option value="">– kein Beleg –</option></select></div>`;
}

function kqvFormAusbildung(x) {
    return `<div class="kqv-form">
        <div class="emp-field-grid-3">
            <div class="fmf-field"><label class="fmf-label">Von</label><input class="fmf-input" id="kqvVon" type="date" value="${x?.von || ''}"></div>
            <div class="fmf-field"><label class="fmf-label">Bis (leer = läuft)</label><input class="fmf-input" id="kqvBis" type="date" value="${x?.bis || ''}"></div>
            <div class="fmf-field"><label class="fmf-label">Art</label>${kqvSelect('kqvArt', KQV_ARTEN, x?.art || 'LEHRE')}</div>
            <div class="fmf-field"><label class="fmf-label">Erstausbildung</label>${kqvSelect('kqvErst', KQV_JNU, x ? kqvJnuWert(x.erstausbildung) : 'ja')}</div>
            <div class="fmf-field"><label class="fmf-label">Ende</label>${kqvSelect('kqvEnde', { '': '–', ...KQV_ENDE }, x?.endeGrund || '')}</div>
            <div class="fmf-field"><label class="fmf-label">Geprüft</label>${kqvSelect('kqvGeprueft', { ja: 'ja, Beleg gesehen', nein: 'nein' }, x && !x.geprueft ? 'nein' : 'ja')}</div>
            ${kqvDokFeld()}
            <div class="fmf-field"><label class="fmf-label">Erfahren am</label><input class="fmf-input" id="kqvErfahren" type="date" value="${x?.erfahrenAm || ''}"></div>
            <div class="fmf-field"><label class="fmf-label">Bemerkung</label><input class="fmf-input" id="kqvBemerkung" value="${kqvEsc(x?.bemerkung || '')}"></div>
        </div>${kqvFormKnoepfe()}</div>`;
}

function kqvFormVerhaeltnis(x) {
    return `<div class="kqv-form">
        <div class="emp-field-grid-3">
            <div class="fmf-field"><label class="fmf-label">Gültig ab</label><input class="fmf-input" id="kqvAb" type="date" value="${x?.gueltigAb || ''}"></div>
            <div class="fmf-field"><label class="fmf-label">Im Haushalt des MA</label>${kqvSelect('kqvHaushalt', KQV_JNU, x ? kqvJnuWert(x.imHaushalt) : 'ja')}</div>
            <div class="fmf-field"><label class="fmf-label">Unterhalt zur Hauptsache</label>${kqvSelect('kqvUnterhalt', KQV_JNU, x ? kqvJnuWert(x.unterhaltHauptsache) : 'ja')}</div>
            <div class="fmf-field"><label class="fmf-label">Erfahren am</label><input class="fmf-input" id="kqvErfahren" type="date" value="${x?.erfahrenAm || ''}"></div>
            <div class="fmf-field" style="grid-column:span 2"><label class="fmf-label">Bemerkung</label><input class="fmf-input" id="kqvBemerkung" value="${kqvEsc(x?.bemerkung || '')}"></div>
        </div>
        <div class="kqv-klein">Auszug wegen Studium oder Wochenaufenthalt ist kein «nein» — im Zweifel «ungeklärt».</div>
        ${kqvFormKnoepfe()}</div>`;
}

function kqvFormEntscheid(x) {
    return `<div class="kqv-form">
        <div class="emp-field-grid-3">
            <div class="fmf-field"><label class="fmf-label">Status</label>${kqvSelect('kqvStatus', KQV_STATUS, x?.status || 'ANERKANNT')}</div>
            <div class="fmf-field"><label class="fmf-label">Von</label><input class="fmf-input" id="kqvVon" type="date" value="${x?.von || ''}"></div>
            <div class="fmf-field"><label class="fmf-label">Bis (leer = offen)</label><input class="fmf-input" id="kqvBis" type="date" value="${x?.bis || ''}"></div>
            ${kqvDokFeld()}
            <div class="fmf-field"><label class="fmf-label">Erfahren am</label><input class="fmf-input" id="kqvErfahren" type="date" value="${x?.erfahrenAm || ''}"></div>
            <div class="fmf-field"><label class="fmf-label">Grund</label><input class="fmf-input" id="kqvGrund" value="${kqvEsc(x?.grund || '')}"></div>
        </div>
        <div class="kqv-klein">Gilt so, wie die Steuerverwaltung datiert hat — ohne Verschiebung auf den Folgemonat.</div>
        ${kqvFormKnoepfe()}</div>`;
}

function kqvDokOptionen(aktuell) {
    const sel = document.getElementById('kqvDok');
    if (!sel) return;
    sel.innerHTML = `<option value="">– kein Beleg –</option>` + (_kqv.dokumente || [])
        .map(d => `<option value="${d.id}"${d.id === aktuell ? ' selected' : ''}>${kqvEsc(d.text)}</option>`).join('');
}

async function kqvBearbeiten(typ, id) {
    const liste = typ === 'A' ? _kqv.daten.ausbildungen : typ === 'V' ? _kqv.daten.verhaeltnisse : _kqv.daten.entscheide;
    const x = id == null ? null : liste.find(e => e.id === id);
    _kqv.form = { typ, id, dokumentId: x?.dokumentId ?? null };
    kqvRender();
    if (typ !== 'V') { await kqvDokumente(); kqvDokOptionen(_kqv.form?.dokumentId); }
}

function kqvAbbrechen() { _kqv.form = null; kqvRender(); }

async function kqvSenden(methode, pfad, body) {
    const r = await fetch(`/api/employees/${_kqv.empId}/family/${_kqv.memberId}/${pfad}`,
        { method: methode, headers: ah(), body: body ? JSON.stringify(body) : undefined });
    if (!r.ok) {
        let msg = `Fehler (HTTP ${r.status})`;
        try { const j = await r.json(); msg = j.message || msg; } catch { /* kein JSON */ }
        kqvFehler(msg);
        return false;
    }
    _kqv.daten = await r.json();
    _kqv.form = null;
    kqvRender();
    if (typeof loadFamilieTab === 'function') loadFamilieTab(_kqv.empId);
    return true;
}

async function kqvSpeichern() {
    const f = _kqv.form;
    if (!f) return;
    const wert = id => document.getElementById(id)?.value || null;
    const dok = () => { const v = wert('kqvDok'); return v ? parseInt(v, 10) : null; };
    let pfad, body;
    if (f.typ === 'A') {
        pfad = 'ausbildung';
        body = { von: wert('kqvVon'), bis: wert('kqvBis'), art: wert('kqvArt'), endeGrund: wert('kqvEnde'),
            erstausbildung: kqvJnuLesen('kqvErst'), geprueft: wert('kqvGeprueft') !== 'nein',
            dokumentId: dok(), erfahrenAm: wert('kqvErfahren'), bemerkung: wert('kqvBemerkung') };
    } else if (f.typ === 'V') {
        pfad = 'verhaeltnis';
        body = { gueltigAb: wert('kqvAb'), imHaushalt: kqvJnuLesen('kqvHaushalt'),
            unterhaltHauptsache: kqvJnuLesen('kqvUnterhalt'), erfahrenAm: wert('kqvErfahren'), bemerkung: wert('kqvBemerkung') };
    } else {
        pfad = 'qst-entscheid';
        body = { status: wert('kqvStatus'), von: wert('kqvVon'), bis: wert('kqvBis'), dokumentId: dok(),
            erfahrenAm: wert('kqvErfahren'), grund: wert('kqvGrund') };
    }
    await kqvSenden(f.id == null ? 'POST' : 'PUT', f.id == null ? pfad : `${pfad}/${f.id}`, body);
}

async function kqvLoeschen(pfad, id) {
    const was = pfad === 'ausbildung' ? 'diesen Ausbildungs-Abschnitt' : pfad === 'verhaeltnis' ? 'diese Änderung' : 'diesen QST-Entscheid';
    if (!await liquidConfirm(`Wirklich ${was} löschen?`, { title: 'Löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' })) return;
    await kqvSenden('DELETE', `${pfad}/${id}`);
}

async function kqvAusZulage(allowanceId) {
    const z = (_kqv.daten.zulagenOhneAusbildung || []).find(x => x.id === allowanceId);
    const zeit = z ? `${kqvDatum(z.von)} – ${z.bis ? kqvDatum(z.bis) : 'offen'}` : '';
    const ok = await liquidConfirm(
        `Ausbildung ${zeit} als Erstausbildung für die QST übernehmen? Beleg ist der Zulagenentscheid. ` +
        `Danach Art und Erstausbildung prüfen.`,
        { title: 'Als QST-Ausbildung übernehmen', yesLabel: 'Übernehmen', noLabel: 'Abbrechen' });
    if (!ok) return;
    await kqvSenden('POST', `ausbildung/aus-zulage/${allowanceId}`);
}
