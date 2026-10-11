// QST-Verlauf am Kind (Walter 11.10.2026, docs/kind-qst-verlauf-konzept.md).
// Im Familien-Fenster: Ausbildung (Abschnitte), Haushalt & Unterhalt
// (Versionen ab Datum) und QST-Entscheide. Gerechnet wird nur auf dem Server
// (Services/KindQstVerlauf.cs) — hier wird nur angezeigt und erfasst.

let _kqv = { empId: null, memberId: null, daten: null, dokumente: null, verlaufOffen: {} };

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

// Seit 11.10.2026 in drei Blöcken der Kind-Maske: «Wohnen & Unterhalt»
// (#kqvMountWohnen), «Ausbildung» (#kqvMountAusbildung), «Quellensteuer»
// (#fmQstVerlauf: Ergebnis + Entscheide).
/** Heute gültige Version «Wohnen & Unterhalt» (sonst die erste). */
function kqvVerhaeltnisHeute(liste) {
    if (!liste || !liste.length) return null;
    const heute = new Date().toISOString().slice(0, 10);
    return [...liste].filter(v => (v.gueltigAb || '') <= heute)
        .sort((x, y) => String(y.gueltigAb).localeCompare(String(x.gueltigAb)))[0] || liste[0];
}

/** Heute laufender Abschnitt (von ≤ heute ≤ bis), sonst der zuletzt begonnene. */
function kqvZeitraumHeute(liste) {
    if (!liste || !liste.length) return null;
    const heute = new Date().toISOString().slice(0, 10);
    const laufend = liste.find(x => (x.von || '') <= heute && (!x.bis || x.bis >= heute));
    return laufend || [...liste].sort((x, y) => String(y.von || '').localeCompare(String(x.von || '')))[0];
}

/** Ein Eintrag: zeigen. Mehrere: nur den aktuellen + Knopf «Verlauf (n)», der alle aufklappt. */
function kqvMitVerlauf(schluessel, liste, aktuell, zeile) {
    liste = liste || [];
    if (liste.length <= 1) return { zeilen: liste.map(zeile).join(''), knopf: '' };
    const offen = !!_kqv.verlaufOffen?.[schluessel];
    return {
        zeilen: offen ? liste.map(zeile).join('') : zeile(aktuell || liste[liste.length - 1]),
        knopf: `<button type="button" class="kqv-btn-neu kqv-verlauf" onclick="kqvVerlaufUmschalten('${schluessel}')">${offen ? 'Verlauf ausblenden' : `Verlauf (${liste.length})`}</button>`,
    };
}
function kqvVerlaufUmschalten(schluessel) {
    _kqv.verlaufOffen = _kqv.verlaufOffen || {};
    _kqv.verlaufOffen[schluessel] = !_kqv.verlaufOffen[schluessel];
    kqvRender();
}

/** Ausbildung erst ab 15 zeigen (Ausbildungszulage ab 16) — oder wenn schon etwas erfasst ist. */
function kqvAusbildungSichtbarkeit() {
    const sec = document.getElementById('fmKindAusbildungSection');
    if (!sec || document.getElementById('fmMemberType')?.value !== 'Kind') return;
    const d = _kqv.memberId != null && _kqv.memberId === (typeof editingFamilyMemberId !== 'undefined' ? editingFamilyMemberId : null) ? _kqv.daten : null;
    const erfasst = !!(d && ((d.ausbildungen || []).length || (d.zulagenOhneAusbildung || []).length));
    const geb = document.getElementById('fmDateOfBirth')?.value;
    let alter = null;
    if (geb) {
        const g = new Date(geb), h = new Date();
        alter = h.getFullYear() - g.getFullYear() - ((h.getMonth() < g.getMonth() || (h.getMonth() === g.getMonth() && h.getDate() < g.getDate())) ? 1 : 0);
    }
    sec.style.display = (erfasst || alter == null || alter >= 15) ? '' : 'none';
}

function kqvBoxen() {
    return {
        wohnen: document.getElementById('kqvMountWohnen'),
        ausbildung: document.getElementById('kqvMountAusbildung'),
        qst: document.getElementById('fmQstVerlauf'),
    };
}

/** Beim Öffnen des Familien-Fensters: Kind laden oder Hinweis für neue Kinder. */
async function kqvOeffnen(empId, member) {
    _kqv = { empId, memberId: member?.id ?? null, daten: null, dokumente: null, verlaufOffen: {} };
    const b = kqvBoxen();
    if (!b.qst) return;
    if (!member?.id || member.memberType !== 'Kind') {
        if (b.wohnen) b.wohnen.innerHTML = '';
        if (b.ausbildung) b.ausbildung.innerHTML = `<div class="kqv-hinweis">Nach dem Speichern erfassen.</div>`;
        b.qst.innerHTML = `<div class="kqv-hinweis">Wird nach dem Speichern gerechnet.</div>`;
        kqvAusbildungSichtbarkeit();
        return;
    }
    [b.wohnen, b.ausbildung, b.qst].forEach(x => { if (x) x.innerHTML = `<div class="kqv-hinweis">Lade …</div>`; });
    await kqvLaden();
}

async function kqvLaden() {
    try {
        const r = await fetch(`/api/employees/${_kqv.empId}/family/${_kqv.memberId}/qst-verlauf`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error(`HTTP ${r.status}`);
        _kqv.daten = await r.json();
    } catch (e) {
        const b = kqvBoxen();
        [b.wohnen, b.ausbildung].forEach(x => { if (x) x.innerHTML = ''; });
        b.qst.innerHTML = `<div class="kqv-hinweis" style="color:#991b1b">Verlauf konnte nicht geladen werden (${kqvEsc(e.message)}).</div>`;
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
    const b = kqvBoxen();
    if (!b.qst || !d) return;
    const a = d.anzeige || {};

    // Quellensteuer am Kind: nur das Ergebnis in einer Zeile (Walter 11.10.2026 —
    // Details gehören in die QST-Erfassung, nicht in die Kind-Maske).
    const wirkung = (a.wirkung || []).map(w =>
        `<span style="color:#166534">${kqvMonat(w.von)} – ${w.bis ? kqvMonat(w.bis) : 'offen'}</span> <span class="kqv-grau">(${kqvEsc(w.grund)})</span>`).join(' · ');
    const gelb = [...(a.offen || []), ...(a.hinweise || [])].map(t => `<div style="color:#92400e">⚠ ${kqvEsc(t)}</div>`).join('');
    const kopf = `<div title="Eine Änderung wirkt ab dem Folgemonat (KS 45). Lohnrelevant erst mit einer QST-Erfassung durch HR.">
        Zählt für die Kinderziffer: ${wirkung || '<span class="kqv-grau">in keinem Monat</span>'}
        ${a.zaehltHeute === false && wirkung ? `<span class="kqv-grau"> · zurzeit nicht (${kqvEsc(a.grundHeute || '')})</span>` : ''}
        ${gelb}
    </div>`;

    // ── Ausbildung ──
    const ausbZeile = x => {
        const quelle = KQV_QUELLE[x.quelle] ? ` · ${KQV_QUELLE[x.quelle]}` : '';
        return `<div class="kqv-zeile">
            <div class="kqv-text">
                <b>${x.von ? kqvDatum(x.von) : 'Beginn unbekannt'} – ${x.bis ? kqvDatum(x.bis) : 'läuft'}</b>
                · ${KQV_ARTEN[x.art] || 'andere'}${x.endeGrund ? ' · ' + (KQV_ENDE[x.endeGrund] || '') : ''}
                · Erstausbildung ${kqvJaNein(x.erstausbildung)}
                ${x.geprueft ? '' : ' · <span style="color:#92400e">nicht geprüft</span>'}${quelle}${kqvDokLink(x.dokumentId)}
                ${x.bemerkung ? `<div class="kqv-klein">${kqvEsc(x.bemerkung)}</div>` : ''}
            </div>
            ${kqvMenu('kqvA', x.id, `kqvAusbildungDialog(${x.id})`, `kqvLoeschen('ausbildung',${x.id})`)}
        </div>`;
    };
    const zulagen = (d.zulagenOhneAusbildung || []).map(z => `<div class="kqv-zeile kqv-gelb">
            <div class="kqv-text">Ausbildungszulage ${kqvDatum(z.von)} – ${z.bis ? kqvDatum(z.bis) : 'offen'}${kqvDokLink(z.dokumentId)}
                <div class="kqv-klein">Für die Familienzulage anerkannt — für die QST noch nicht erfasst.</div></div>
            <button type="button" class="kqv-btn" onclick="kqvAusZulage(${z.id})">Als QST-Ausbildung übernehmen</button>
        </div>`).join('');

    // ── Haushalt & Unterhalt ──
    const verhZeile = x => {
        const quelle = KQV_QUELLE[x.quelle] ? ` <span class="kqv-grau">· ${KQV_QUELLE[x.quelle]}</span>` : '';
        const bem = x.bemerkung && x.quelle !== 'ALTBESTAND' ? `<div class="kqv-klein">${kqvEsc(x.bemerkung)}</div>` : '';
        return `<div class="kqv-zeile">
            <div class="kqv-text"><b>ab ${kqvDatum(x.gueltigAb)}</b> · im Haushalt: ${kqvJaNein(x.imHaushalt)}
                · Unterhalt: ${kqvJaNein(x.unterhaltHauptsache)}${quelle}${bem}</div>
            ${kqvMenu('kqvV', x.id, `kqvVerhaeltnisDialog(_kqv.empId,_kqv.memberId,${x.id})`, (d.verhaeltnisse.length > 1) ? `kqvLoeschen('verhaeltnis',${x.id})` : '')}
        </div>`;
    };

    // ── QST-Entscheide ──
    const entZeile = x => {
        const farbe = x.status === 'ANERKANNT' ? '#166534' : x.status === 'NICHT_ANERKANNT' ? '#991b1b' : '#92400e';
        const quelle = KQV_QUELLE[x.quelle] ? ` · ${KQV_QUELLE[x.quelle]}` : '';
        return `<div class="kqv-zeile">
            <div class="kqv-text"><b style="color:${farbe}">${KQV_STATUS[x.status] || x.status}</b>
                · ${kqvDatum(x.von)} – ${x.bis ? kqvDatum(x.bis) : 'offen'}${quelle}${kqvDokLink(x.dokumentId)}
                ${x.grund ? `<div class="kqv-klein">${kqvEsc(x.grund)}</div>` : ''}</div>
            ${kqvMenu('kqvE', x.id, `kqvEntscheidDialog(${x.id})`, `kqvLoeschen('qst-entscheid',${x.id})`)}
        </div>`;
    };

    // Pro Bereich nur der heute gültige Eintrag; ältere/spätere hinter «Verlauf» (Walter 11.10.2026).
    const verh = kqvMitVerlauf('V', d.verhaeltnisse, kqvVerhaeltnisHeute(d.verhaeltnisse), verhZeile);
    const ausb = kqvMitVerlauf('A', d.ausbildungen, kqvZeitraumHeute(d.ausbildungen), ausbZeile);
    const ent = kqvMitVerlauf('E', d.entscheide, kqvZeitraumHeute(d.entscheide), entZeile);

    const neuKnopf = (onclick, text, titel = '') =>
        `<button type="button" class="kqv-btn-neu" onclick="${onclick}"${titel ? ` title="${titel}"` : ''}>${text}</button>`;
    const fuss = (...teile) => `<div class="kqv-fuss">${teile.join('')}</div>`;

    const ausbLeer = '<div class="kqv-leer">keine erfasst — ab 16 gibt es die Ausbildungszulage statt der Kinderzulage: Lehre oder Schule hier erfassen.</div>';
    const ausbildung = `${ausb.zeilen || ausbLeer}${zulagen}${fuss(neuKnopf('kqvAusbildungDialog()', '+ Ausbildung'), ausb.knopf)}`;
    const heute = kqvVerhaeltnisHeute(d.verhaeltnisse);
    const ohneUnterhalt = heute && heute.unterhaltHauptsache === false
        ? '<div class="kqv-klein" style="color:#92400e">Ohne Unterhalt zählt das Kind nicht für die QST. Läuft trotzdem eine Familienzulage: Verfügung der Familienausgleichskasse prüfen.</div>' : '';
    const wohnen = `${verh.zeilen || '<div class="kqv-leer">keine erfasst</div>'}${ohneUnterhalt}${fuss(
        neuKnopf('kqvVerhaeltnisDialog(_kqv.empId,_kqv.memberId)', '+ Änderung ab Datum'), verh.knopf)}`;
    const entscheide = ent.zeilen ? `<div><div class="kqv-sub">Entscheid der Steuerverwaltung</div>${ent.zeilen}</div>` : '';
    const entscheidNeu = fuss(neuKnopf('kqvEntscheidDialog()', '+ Entscheid der Steuerverwaltung',
        'Nur wenn die Steuerverwaltung über das Kind entschieden hat'), ent.knopf);

    if (b.wohnen && b.ausbildung) {
        b.wohnen.innerHTML = wohnen;
        b.ausbildung.innerHTML = ausbildung;
        b.qst.innerHTML = kopf + entscheide + entscheidNeu;
        kqvAusbildungSichtbarkeit();
    } else {
        b.qst.innerHTML = `${kopf}
            <div><div class="kqv-sub">Ausbildung</div>${ausbildung}</div>
            <div><div class="kqv-sub">Haushalt &amp; Unterhalt</div>${wohnen}</div>${entscheide}${entscheidNeu}`;
    }

    if (typeof fmKindAdresseSichtbarkeit === 'function') fmKindAdresseSichtbarkeit();
}

const KQV_JNU = { '': 'ungeklärt', ja: 'ja', nein: 'nein' };
const kqvJnuWert = v => v === true ? 'ja' : v === false ? 'nein' : '';
const kqvJnuLesen = id => { const v = document.getElementById(id)?.value; return v === 'ja' ? true : v === 'nein' ? false : null; };

// ── Kleine Fenster statt Formular mitten in der Kind-Maske (Walter 11.10.2026) ──
// Wohnen & Unterhalt, Ausbildung und Entscheid der Steuerverwaltung: je ein
// eigenes Fenster mit nur den nötigen Feldern. «Erfahren am» wird nicht mehr
// abgefragt — beim Bearbeiten bleibt ein gespeicherter Wert erhalten.
const KQV_PILL = 'width:100%;box-sizing:border-box;background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;box-shadow:0 2px 6px rgba(60,55,48,0.13);padding:8px 10px;font-size:13px;color:#3f3f3f';
const KQV_LAB = 'display:block;font-size:11px;font-weight:700;color:#646464;text-transform:uppercase;letter-spacing:.03em;margin-bottom:4px';

function kqvFeld(label, inhalt, extra = '') {
    return `<div ${extra}><label style="${KQV_LAB}">${label}</label>${inhalt}</div>`;
}
function kqvAuswahl(id, werte, aktuell, onchange = '') {
    return `<select id="${id}" style="${KQV_PILL}"${onchange ? ` onchange="${onchange}"` : ''}>${Object.entries(werte)
        .map(([k, t]) => `<option value="${k}"${String(aktuell ?? '') === k ? ' selected' : ''}>${t}</option>`).join('')}</select>`;
}
function kqvDatumFeld(id, wert) { return `<input id="${id}" type="date" style="${KQV_PILL}" value="${wert || ''}">`; }
function kqvTextFeld(id, wert) { return `<input id="${id}" type="text" style="${KQV_PILL}" value="${kqvEsc(wert || '')}">`; }
const kqvOptional = '<span style="font-weight:400;text-transform:none;letter-spacing:0;color:#8b8b8b">(optional)</span>';

/** Öffnet ein kleines Fenster. speichern() liefert true, wenn es zugehen darf. */
function kqvFenster({ titel, unter, inhalt, hinweis, speichern, fokus }) {
    document.getElementById('kqvFenster')?.remove();
    const wrap = document.createElement('div');
    wrap.id = 'kqvFenster';
    wrap.style.cssText = 'position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9800;display:flex;align-items:center;justify-content:center';
    wrap.innerHTML = `
    <div style="background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);width:min(540px,92vw);padding:20px 22px">
        <div style="font-size:15px;font-weight:800;color:#3f3f3f">${titel}</div>
        <div style="font-size:12px;color:#8b8b8b;margin:4px 0 14px">${unter || ''}</div>
        ${inhalt}
        ${hinweis ? `<div style="font-size:11px;color:#8b8b8b;margin-top:10px;line-height:1.4">${hinweis}</div>` : ''}
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:18px">
            <button type="button" data-kqv="nein" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13px;font-weight:700">Abbrechen</button>
            <button type="button" data-kqv="ja" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13px;font-weight:700">Speichern</button>
        </div>
    </div>`;
    document.body.appendChild(wrap);
    const zu = () => { wrap.remove(); document.removeEventListener('keydown', onKey, true); };
    const onKey = e => { if (e.key === 'Escape') { e.stopPropagation(); zu(); } };
    document.addEventListener('keydown', onKey, true);
    wrap.addEventListener('mousedown', e => { if (e.target === wrap) zu(); });
    wrap.querySelector('[data-kqv="nein"]').onclick = zu;
    const ja = wrap.querySelector('[data-kqv="ja"]');
    ja.onclick = async () => {
        ja.disabled = true;
        try { if (await speichern()) zu(); } finally { ja.disabled = false; }
    };
    if (fokus) setTimeout(() => document.getElementById(fokus)?.focus(), 50);
}

/** Speichert und zeichnet Kind-Maske + Familie-Tab neu. */
async function kqvSchreiben(empId, memberId, methode, pfad, body, meldung) {
    const r = await fetch(`/api/employees/${empId}/family/${memberId}/${pfad}`,
        { method: methode, headers: ah(), body: body ? JSON.stringify(body) : undefined });
    if (!r.ok) {
        let msg = `Fehler (HTTP ${r.status})`;
        try { const j = await r.json(); msg = j.message || msg; } catch { /* kein JSON */ }
        kqvFehler(msg);
        return false;
    }
    const neu = await r.json();
    if (_kqv.memberId === memberId) { _kqv.daten = neu; kqvRender(); }
    if (typeof loadFamilieTab === 'function') loadFamilieTab(empId);
    if (meldung && typeof showToast === 'function') showToast(meldung, 'success');
    return true;
}

async function kqvBelegAuswahl(aktuell) {
    const dok = await kqvDokumente();
    return `<select id="kqvFBeleg" style="${KQV_PILL}"><option value="">– kein Beleg –</option>${dok
        .map(d => `<option value="${d.id}"${d.id === aktuell ? ' selected' : ''}>${kqvEsc(d.text)}</option>`).join('')}</select>`;
}
const kqvWert = id => document.getElementById(id)?.value || null;
const kqvBelegWert = () => { const v = kqvWert('kqvFBeleg'); return v ? parseInt(v, 10) : null; };

/** Wohnen & Unterhalt — auch direkt aus dem ⋮ der Kind-Karte (ohne Kind-Maske). */
async function kqvVerhaeltnisDialog(empId, memberId, eintragId = null) {
    let daten = (_kqv.memberId === memberId && _kqv.daten) ? _kqv.daten : null;
    if (!daten) {
        try {
            const r = await fetch(`/api/employees/${empId}/family/${memberId}/qst-verlauf`, { headers: ah(), cache: 'no-store' });
            if (!r.ok) throw new Error(`HTTP ${r.status}`);
            daten = await r.json();
        } catch (e) { kqvFehler(`Verlauf konnte nicht geladen werden (${e.message}).`); return; }
    }
    const x = eintragId == null ? null : (daten.verhaeltnisse || []).find(v => v.id === eintragId);
    const bisher = x ? null : kqvVerhaeltnisHeute(daten.verhaeltnisse);
    kqvFenster({
        titel: x ? 'Wohnen &amp; Unterhalt bearbeiten' : 'Wohnen &amp; Unterhalt ändern',
        unter: bisher
            ? `Bisher: ab ${kqvDatum(bisher.gueltigAb)} · im Haushalt ${kqvJaNein(bisher.imHaushalt)} · Unterhalt ${kqvJaNein(bisher.unterhaltHauptsache)}`
            : 'Gilt ab dem Datum, bis zur nächsten Änderung.',
        inhalt: `<div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px">
                ${kqvFeld('Gültig ab', kqvDatumFeld('kqvFAb', x?.gueltigAb))}
                ${kqvFeld('Im Haushalt', kqvAuswahl('kqvFHaushalt', KQV_JNU, x ? kqvJnuWert(x.imHaushalt) : kqvJnuWert(bisher?.imHaushalt ?? true)))}
                ${kqvFeld('Unterhalt', kqvAuswahl('kqvFUnterhalt', KQV_JNU, x ? kqvJnuWert(x.unterhaltHauptsache) : kqvJnuWert(bisher?.unterhaltHauptsache ?? true)))}
            </div>
            ${kqvFeld(`Bemerkung ${kqvOptional}`, kqvTextFeld('kqvFBemerkung', x?.bemerkung), 'style="margin-top:12px"')}`,
        hinweis: '«Unterhalt» = der MA kommt zur Hauptsache für das Kind auf. Auszug wegen Studium oder Wochenaufenthalt ist kein «nein» — im Zweifel «ungeklärt».',
        fokus: 'kqvFAb',
        speichern: async () => {
            const ab = kqvWert('kqvFAb');
            if (!ab) { kqvFehler('Bitte «Gültig ab» erfassen.'); return false; }
            return kqvSchreiben(empId, memberId, x ? 'PUT' : 'POST', x ? `verhaeltnis/${x.id}` : 'verhaeltnis', {
                gueltigAb: ab, imHaushalt: kqvJnuLesen('kqvFHaushalt'), unterhaltHauptsache: kqvJnuLesen('kqvFUnterhalt'),
                erfahrenAm: x?.erfahrenAm || null, bemerkung: kqvWert('kqvFBemerkung'),
            }, 'Wohnen & Unterhalt gespeichert');
        },
    });
}

/** «Ende» nur zeigen, wenn ein Bis-Datum erfasst ist. */
function kqvEndeSichtbarkeit() {
    const feld = document.getElementById('kqvFEndeFeld');
    if (feld) feld.style.visibility = kqvWert('kqvFBis') ? '' : 'hidden';
}

async function kqvAusbildungDialog(eintragId = null) {
    const x = eintragId == null ? null : (_kqv.daten?.ausbildungen || []).find(a => a.id === eintragId);
    const beleg = await kqvBelegAuswahl(x?.dokumentId ?? null);
    kqvFenster({
        titel: x ? 'Ausbildung bearbeiten' : 'Ausbildung erfassen',
        unter: 'Lehre, Schule oder Studium — ab 16 gibt es dafür die Ausbildungszulage.',
        inhalt: `<div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px">
                ${kqvFeld('Von', kqvDatumFeld('kqvFVon', x?.von))}
                ${kqvFeld('Bis <span style="font-weight:400;text-transform:none;letter-spacing:0;color:#8b8b8b">(leer = läuft)</span>',
                    `<input id="kqvFBis" type="date" style="${KQV_PILL}" value="${x?.bis || ''}" onchange="kqvEndeSichtbarkeit()" oninput="kqvEndeSichtbarkeit()">`)}
                ${kqvFeld('Art', kqvAuswahl('kqvFArt', KQV_ARTEN, x?.art || 'LEHRE'))}
                ${kqvFeld('Erstausbildung', kqvAuswahl('kqvFErst', KQV_JNU, x ? kqvJnuWert(x.erstausbildung) : 'ja'))}
                ${kqvFeld('Beleg', beleg, 'style="grid-column:span 2"')}
                ${kqvFeld('Ende', kqvAuswahl('kqvFEnde', { '': '–', ...KQV_ENDE }, x?.endeGrund || ''), `id="kqvFEndeFeld" style="visibility:${x?.bis ? 'visible' : 'hidden'}"`)}
                ${kqvFeld(`Bemerkung ${kqvOptional}`, kqvTextFeld('kqvFBemerkung', x?.bemerkung), 'style="grid-column:span 2"')}
            </div>
            <label style="display:inline-flex;align-items:center;gap:6px;margin-top:10px;font-size:12.5px;color:#3f3f3f;cursor:pointer">
                <input type="checkbox" id="kqvFGeprueft"${!x || x.geprueft ? ' checked' : ''}> Beleg gesehen (Lehrvertrag, Schulbestätigung …)</label>`,
        hinweis: 'Für die Quellensteuer zählt nur die Erstausbildung.',
        fokus: 'kqvFVon',
        speichern: async () => {
            const beleg = kqvBelegWert();
            return kqvSchreiben(_kqv.empId, _kqv.memberId, x ? 'PUT' : 'POST', x ? `ausbildung/${x.id}` : 'ausbildung', {
                von: kqvWert('kqvFVon'), bis: kqvWert('kqvFBis'), art: kqvWert('kqvFArt'),
                endeGrund: kqvWert('kqvFBis') ? kqvWert('kqvFEnde') : null,
                erstausbildung: kqvJnuLesen('kqvFErst'),
                geprueft: beleg != null || !!document.getElementById('kqvFGeprueft')?.checked,
                dokumentId: beleg, erfahrenAm: x?.erfahrenAm || null, bemerkung: kqvWert('kqvFBemerkung'),
            }, 'Ausbildung gespeichert');
        },
    });
}

async function kqvEntscheidDialog(eintragId = null) {
    const x = eintragId == null ? null : (_kqv.daten?.entscheide || []).find(e => e.id === eintragId);
    const beleg = await kqvBelegAuswahl(x?.dokumentId ?? null);
    kqvFenster({
        titel: x ? 'Entscheid der Steuerverwaltung bearbeiten' : 'Entscheid der Steuerverwaltung',
        unter: 'Nur erfassen, wenn die Steuerverwaltung über dieses Kind entschieden hat.',
        inhalt: `<div style="display:grid;grid-template-columns:1fr 1fr 1fr;gap:12px">
                ${kqvFeld('Entscheid', kqvAuswahl('kqvFStatus', KQV_STATUS, x?.status || 'ANERKANNT'))}
                ${kqvFeld('Von', kqvDatumFeld('kqvFVon', x?.von))}
                ${kqvFeld('Bis <span style="font-weight:400;text-transform:none;letter-spacing:0;color:#8b8b8b">(leer = offen)</span>', kqvDatumFeld('kqvFBis', x?.bis))}
                ${kqvFeld('Beleg', beleg, 'style="grid-column:span 3"')}
                ${kqvFeld(`Grund ${kqvOptional}`, kqvTextFeld('kqvFGrund', x?.grund), 'style="grid-column:span 3"')}
            </div>`,
        hinweis: 'Gilt genau so, wie die Steuerverwaltung datiert hat.',
        speichern: async () => kqvSchreiben(_kqv.empId, _kqv.memberId, x ? 'PUT' : 'POST', x ? `qst-entscheid/${x.id}` : 'qst-entscheid', {
            status: kqvWert('kqvFStatus'), von: kqvWert('kqvFVon'), bis: kqvWert('kqvFBis'),
            dokumentId: kqvBelegWert(), erfahrenAm: x?.erfahrenAm || null, grund: kqvWert('kqvFGrund'),
        }, 'Entscheid gespeichert'),
    });
}

async function kqvSenden(methode, pfad, body) {
    return kqvSchreiben(_kqv.empId, _kqv.memberId, methode, pfad, body);
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
