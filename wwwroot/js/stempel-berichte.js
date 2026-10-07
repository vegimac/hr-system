// ═══════════════════════════════════════════════════════════════════════════
//  STEMPEL-BERICHTE im McAdmin (Walter 07.10.2026)
//  «Arbeitszeit-Verstösse» (szv…) und «Korrekturen Stempelzeiten» (szk…).
//  Filiale = Sidebar-Selektor. Endpoints: GET /api/reports/stempel-verstoesse
//  und /api/reports/stempel-korrekturen (je + /pdf). Regeln: ArbeitszeitVerstoesse.cs
// ═══════════════════════════════════════════════════════════════════════════

const _SZ_FARBE = {
    PAUSE: '#d97706', BLOCK: '#ea580c', NACHT_9H: '#4f46e5', PRAESENZ: '#0e7490',
    WOCHE_50H: '#be123c', RUHETAGE: '#7c3aed', NAECHTE: '#1e3a8a', JUGEND: '#db2777', SONNTAG_JUGEND: '#9d174d',
    ZEIT: '#b45309', MANUELL: '#4f46e5', BEARBEITET: '#6b7280', KOMMENTAR: '#0e7490'
};
const _SZK_ART = { ZEIT: 'Zeit korrigiert', MANUELL: 'Von Hand erfasst', BEARBEITET: 'Bearbeitet', KOMMENTAR: 'Nur Kommentar' };
const _SZ_WT = ['So', 'Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa'];

let _szvData = null, _szvFilter = null;
let _szkData = null, _szkFilter = null;

function _szEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
function _szDatum(iso) { return iso ? iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4) : ''; }
function _szWt(iso) { return _SZ_WT[new Date(iso + 'T12:00:00').getDay()]; }
function _szIso(d) { return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0'); }
function _szDauer(min) { return Math.floor(min / 60) + ' h ' + String(min % 60).padStart(2, '0'); }
function _szFiliale() {
    return (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) ? fixedCompanyProfileId : null;
}
function _szInitialen(v, n) { return ((v || '').charAt(0) + (n || '').charAt(0)).toUpperCase() || '?'; }

function _szZeitraumSetzen(prefix, art) {
    const heute = new Date();
    let von, bis;
    if (art === 'vormonat') { von = new Date(heute.getFullYear(), heute.getMonth() - 1, 1); bis = new Date(heute.getFullYear(), heute.getMonth(), 0); }
    else if (art === 'monat') { von = new Date(heute.getFullYear(), heute.getMonth(), 1); bis = heute; }
    else if (art === 'drei') { von = new Date(heute.getFullYear(), heute.getMonth() - 3, 1); bis = new Date(heute.getFullYear(), heute.getMonth(), 0); }
    else { von = new Date(heute.getFullYear(), 0, 1); bis = heute; }
    document.getElementById(prefix + 'From').value = _szIso(von);
    document.getElementById(prefix + 'To').value = _szIso(bis);
    document.getElementById(prefix + 'From').dispatchEvent(new Event('change'));
}

function _szInitZeitraum(prefix) {
    const f = document.getElementById(prefix + 'From');
    if (f && !f.value) {
        const heute = new Date();
        f.value = _szIso(new Date(heute.getFullYear(), heute.getMonth() - 1, 1));
        document.getElementById(prefix + 'To').value = _szIso(new Date(heute.getFullYear(), heute.getMonth(), 0));
    }
}

async function _szLaden(prefix, pfad) {
    const box = document.getElementById(prefix + 'Result');
    if (!box) return null;
    const cp = _szFiliale();
    if (!cp) { box.innerHTML = '<div class="szb-leer">Bitte zuerst oben links in der Sidebar eine Filiale wählen.</div>'; return null; }
    const qs = new URLSearchParams({ companyProfileId: cp, from: document.getElementById(prefix + 'From').value, to: document.getElementById(prefix + 'To').value });
    box.innerHTML = '<div class="szb-leer">Lädt…</div>';
    try {
        const res = await fetch('/api/reports/' + pfad + '?' + qs, { headers: ah() });
        const j = await res.json().catch(() => ({}));
        if (!res.ok) { box.innerHTML = `<div class="szb-leer szb-fehler">${_szEsc(j.message || ('Fehler (HTTP ' + res.status + ')'))}</div>`; return null; }
        return j;
    } catch (e) {
        box.innerHTML = `<div class="szb-leer szb-fehler">Verbindungsfehler: ${_szEsc(e.message)}</div>`;
        return null;
    }
}

function _szPdf(prefix, pfad, name) {
    const cp = _szFiliale();
    if (!cp) return alert('Bitte zuerst oben links in der Sidebar eine Filiale wählen.');
    const von = document.getElementById(prefix + 'From').value, bis = document.getElementById(prefix + 'To').value;
    const qs = new URLSearchParams({ companyProfileId: cp, from: von, to: bis });
    previewUrlFetch('/api/reports/' + pfad + '/pdf?' + qs, `${name}_${von}_${bis}.pdf`, ah());
}

function _szMaKopf(m, rechts) {
    return `<div class="szb-ma-kopf">
        <div class="szb-avatar">${_szEsc(_szInitialen(m.vorname, m.nachname))}</div>
        <div class="szb-ma-name">${_szEsc((m.vorname || '') + ' ' + (m.nachname || ''))}${m.nummer ? `<span>${_szEsc(m.nummer)}</span>` : ''}</div>
        <div class="szb-ma-rechts">${rechts}</div>
    </div>`;
}

function _szSuche(prefix, m) {
    const q = (document.getElementById(prefix + 'Suche')?.value || '').trim().toLowerCase();
    return !q || ((m.vorname || '') + ' ' + (m.nachname || '') + ' ' + (m.nummer || '')).toLowerCase().includes(q);
}

// ── Verstösse ─────────────────────────────────────────────────────────────

function szvInit() { _szInitZeitraum('szv'); szvLoad(); }
function szvZeitraum(art) { _szZeitraumSetzen('szv', art); }
function szvPdf() { _szPdf('szv', 'stempel-verstoesse', 'Arbeitszeit-Verstoesse'); }

async function szvLoad() {
    const j = await _szLaden('szv', 'stempel-verstoesse');
    if (!j) return;
    _szvData = j;
    szvRender();
}

function szvFilter(art) { _szvFilter = _szvFilter === art ? null : art; szvRender(); }

// Tagesband 05:00 → 05:00: Stempel als Balken, Nacht 23–6 schraffiert.
function _szvBand(stempel) {
    const start = 5 * 60, breite = 24 * 60;
    const pos = m => Math.max(0, Math.min(100, (m - start) / breite * 100));
    const balken = stempel.map(s =>
        `<div class="szb-bar" style="left:${pos(s.einMin)}%;width:${Math.max(0.4, pos(s.ausMin) - pos(s.einMin))}%" title="${s.ein}–${s.aus}"></div>`).join('');
    const nacht = `<div class="szb-nacht" style="left:${pos(23 * 60)}%;width:${pos(30 * 60) - pos(23 * 60)}%"></div>`;
    const ticks = [6, 9, 12, 15, 18, 21, 24, 27].map(h =>
        `<span style="left:${pos(h * 60)}%">${String(h % 24).padStart(2, '0')}</span>`).join('');
    return `<div class="szb-band">${nacht}${balken}</div><div class="szb-ticks">${ticks}</div>`;
}

function _szvWoche(v) {
    const tage = {};
    v.stempel.forEach(s => { tage[s.tag] = (tage[s.tag] || 0) + s.minuten; });
    const max = Math.max(600, ...Object.values(tage));
    const von = new Date(v.von + 'T12:00:00');
    let html = '<div class="szb-woche">';
    for (let i = 0; i < 7; i++) {
        const d = new Date(von); d.setDate(von.getDate() + i);
        const iso = _szIso(d), min = tage[iso] || 0;
        html += `<div class="szb-wtag${min ? '' : ' frei'}">
            <div class="szb-wsaeule"><div style="height:${min / max * 100}%"></div></div>
            <b>${_SZ_WT[d.getDay()]}</b><small>${min ? _szDauer(min) : 'frei'}</small></div>`;
    }
    return html + '</div>';
}

function szvRender() {
    const box = document.getElementById('szvResult');
    const d = _szvData;
    if (!box || !d) return;
    const total = d.proArt.reduce((s, a) => s + a.anzahl, 0);

    let html = `<div class="szb-summe"><div class="szb-zahl">${total}</div><div>Verstösse bei <b>${d.mitarbeiter.length}</b> von ${d.anzahlMa} Mitarbeitenden<br><span>${_szEsc(d.filiale)} · ${_szDatum(d.von)} – ${_szDatum(d.bis)}</span></div></div>`;
    html += '<div class="szb-kacheln">' + d.proArt.map(a => `
        <button type="button" class="szb-kachel${_szvFilter === a.art ? ' aktiv' : ''}${a.anzahl ? '' : ' null'}" style="--f:${_SZ_FARBE[a.art]}" onclick="szvFilter('${a.art}')" title="${_szEsc(a.regel)}">
            <b>${a.anzahl}</b><span>${_szEsc(a.titel)}</span></button>`).join('') + '</div>';

    const liste = d.mitarbeiter.filter(m => _szSuche('szv', m))
        .map(m => ({ ...m, verstoesse: m.verstoesse.filter(v => !_szvFilter || v.art === _szvFilter) }))
        .filter(m => m.verstoesse.length);

    if (!liste.length) html += `<div class="szb-leer">${total ? 'Keine Treffer für diese Auswahl.' : 'Keine Verstösse im Zeitraum. 👍'}</div>`;
    liste.forEach(m => {
        const proArt = {};
        m.verstoesse.forEach(v => { proArt[v.art] = (proArt[v.art] || 0) + 1; });
        const chips = Object.entries(proArt).map(([a, n]) =>
            `<span class="szb-chip" style="--f:${_SZ_FARBE[a]}">${n}× ${_szEsc(d.proArt.find(x => x.art === a)?.titel || a)}</span>`).join('');
        html += `<div class="szb-karte">${_szMaKopf(m, chips)}`;
        m.verstoesse.forEach(v => {
            const tag = v.von === v.bis
                ? `<b>${_szWt(v.von)}</b><span>${_szDatum(v.von)}</span>`
                : `<b>${v.art === 'NAECHTE' ? 'Zeitraum' : 'Woche'}</b><span>${_szDatum(v.von).slice(0, 6)} – ${_szDatum(v.bis)}</span>`;
            const bild = v.art === 'NAECHTE' ? '' : (v.von === v.bis ? _szvBand(v.stempel) : _szvWoche(v));
            const zeiten = v.von === v.bis && v.stempel.length
                ? `<div class="szb-zeiten">${v.stempel.map(s => `${s.ein}–${s.aus}`).join(' · ')}</div>` : '';
            html += `<div class="szb-zeile" style="--f:${_SZ_FARBE[v.art]}">
                <div class="szb-tag">${tag}</div>
                <div class="szb-inhalt">
                    <div><span class="szb-pill">${_szEsc(v.titel)}</span><span class="szb-recht">${_szEsc(v.recht)}</span></div>
                    <div class="szb-text">${_szEsc(v.text)}</div>
                    ${bild}${zeiten}
                </div>
            </div>`;
        });
        html += '</div>';
    });

    html += `<details class="szb-regeln"><summary>Wie wird geprüft?</summary>
        ${d.proArt.map(a => `<div style="--f:${_SZ_FARBE[a.art]}"><b>${_szEsc(a.titel)}</b>${_szEsc(a.regel)}</div>`).join('')}
        ${(d.ausgeschaltet || []).length ? `<p>Für diese Filiale ausgeschaltet: ${d.ausgeschaltet.map(_szEsc).join(', ')}.</p>` : ''}
        <p>Grundlage sind die Stempelzeiten aus easy@work und die Regeln und Feiertage der Filiale (Filial-Detail → «Arbeitszeit &amp; Feiertage»). Wochen zählen zum Zeitraum, in dem ihr Sonntag liegt. Stempel ohne Dauer zählen nicht.</p>
    </details>`;
    box.innerHTML = html;
}

// ── Korrekturen ───────────────────────────────────────────────────────────

function szkInit() { _szInitZeitraum('szk'); szkLoad(); }
function szkZeitraum(art) { _szZeitraumSetzen('szk', art); }
function szkPdf() { _szPdf('szk', 'stempel-korrekturen', 'Stempel-Korrekturen'); }

async function szkLoad() {
    const j = await _szLaden('szk', 'stempel-korrekturen');
    if (!j) return;
    _szkData = j;
    szkRender();
}

function szkFilter(art) { _szkFilter = _szkFilter === art ? null : art; szkRender(); }

function szkRender() {
    const box = document.getElementById('szkResult');
    const d = _szkData;
    if (!box || !d) return;
    const alle = d.mitarbeiter.flatMap(m => m.zeilen);
    const anteil = d.stempelTotal ? (100 * d.korrigiert / d.stempelTotal).toFixed(1) : '0.0';

    let html = `<div class="szb-summe"><div class="szb-zahl">${d.korrigiert}</div><div>von <b>${d.stempelTotal}</b> Stempeln korrigiert oder kommentiert (${anteil} %) · ${d.mitarbeiter.length} Mitarbeitende<br><span>${_szEsc(d.filiale)} · ${_szDatum(d.von)} – ${_szDatum(d.bis)}</span></div></div>`;
    html += '<div class="szb-kacheln">' + Object.entries(_SZK_ART).map(([a, t]) => {
        const n = alle.filter(z => z.art === a).length;
        return `<button type="button" class="szb-kachel${_szkFilter === a ? ' aktiv' : ''}${n ? '' : ' null'}" style="--f:${_SZ_FARBE[a]}" onclick="szkFilter('${a}')"><b>${n}</b><span>${t}</span></button>`;
    }).join('') + '</div>';
    if (d.proBearbeiter.length)
        html += `<div class="szb-wer"><span>Korrigiert durch</span>${d.proBearbeiter.map(b => `<span class="szb-chip" style="--f:#6b7280">${_szEsc(b.name)} <b>${b.anzahl}</b></span>`).join('')}</div>`;

    const liste = d.mitarbeiter.filter(m => _szSuche('szk', m))
        .map(m => ({ ...m, zeilen: m.zeilen.filter(z => !_szkFilter || z.art === _szkFilter) }))
        .filter(m => m.zeilen.length);
    if (!liste.length) html += `<div class="szb-leer">${alle.length ? 'Keine Treffer für diese Auswahl.' : 'Keine Korrekturen im Zeitraum.'}</div>`;

    liste.forEach(m => {
        html += `<div class="szb-karte">${_szMaKopf(m, `<span class="szb-chip" style="--f:#6b7280">${m.zeilen.length} Stempel</span>`)}`;
        m.zeilen.forEach(z => {
            const vorher = (z.vorherEin || z.vorherAus)
                ? `<div class="szb-vorher">vorher <s>${_szEsc(z.vorherEin || z.ein)} – ${_szEsc(z.vorherAus || z.aus || 'offen')}</s></div>` : '';
            const am = z.am ? new Date(z.am).toLocaleString('de-CH', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' }) : '';
            html += `<div class="szb-zeile" style="--f:${_SZ_FARBE[z.art]}">
                <div class="szb-tag"><b>${_szWt(z.tag)}</b><span>${_szDatum(z.tag)}</span></div>
                <div class="szb-zeit"><div class="szb-jetzt">${_szEsc(z.ein)} – ${_szEsc(z.aus || 'offen')}</div>${vorher}</div>
                <div class="szb-inhalt">
                    <div><span class="szb-pill">${_SZK_ART[z.art]}</span></div>
                    ${z.kommentar ? `<div class="szb-kommentar">«${_szEsc(z.kommentar)}»</div>` : ''}
                    ${z.protokoll ? `<div class="szb-protokoll">${_szEsc(z.protokoll)}</div>` : ''}
                </div>
                <div class="szb-von">${z.von ? `<b>${_szEsc(z.von)}</b>` : ''}${am ? `<span>${am}</span>` : ''}</div>
            </div>`;
        });
        html += '</div>';
    });
    box.innerHTML = html;
}
