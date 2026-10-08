// ═══════════════════════════════════════════════════════════════════════════
//  STEMPEL-BERICHTE im McAdmin (Walter 07.10.2026)
//  «Arbeitszeit-Verstösse» (szv…) und «Korrekturen Stempelzeiten» (szk…).
//  Filiale = Sidebar-Selektor. Endpoints: GET /api/reports/stempel-verstoesse
//  und /api/reports/stempel-korrekturen (je + /pdf). Regeln: ArbeitszeitVerstoesse.cs
//  Dazu «Stempelzeiten alle Filialen» (szf…, HR-Hub → Auswertungen, 08.10.2026):
//  GET /api/reports/stempel-filialvergleich — pro Filiale dieselbe Rechnung.
// ═══════════════════════════════════════════════════════════════════════════

const _SZ_FARBE = {
    PAUSE: '#d97706', BLOCK: '#ea580c', NACHT_9H: '#4f46e5', PRAESENZ: '#0e7490',
    WOCHE_50H: '#be123c', RUHETAGE: '#7c3aed', NAECHTE: '#1e3a8a', JUGEND: '#db2777', SONNTAG_JUGEND: '#9d174d', RUHEZEIT: '#0f766e', GANZER_RUHETAG: '#6d28d9', SIEBEN_TAGE: '#b91c1c',
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

// Säulen pro Tag (nach Schichtbeginn); «bis 00:45» = Schicht endet nach Mitternacht.
function _szvWoche(v) {
    const tage = {}, spaet = {};
    v.stempel.forEach(s => {
        tage[s.tag] = (tage[s.tag] || 0) + s.minuten;
        if (s.ausMin > 1440 && (!spaet[s.tag] || s.ausMin > spaet[s.tag].ausMin)) spaet[s.tag] = s;
    });
    const max = Math.max(600, ...Object.values(tage));
    const von = new Date(v.von + 'T12:00:00');
    const anzahl = Math.min(14, Math.round((new Date(v.bis + 'T12:00:00') - von) / 86400000) + 1);
    let html = '<div class="szb-woche">';
    for (let i = 0; i < anzahl; i++) {
        const d = new Date(von); d.setDate(von.getDate() + i);
        const iso = _szIso(d), min = tage[iso] || 0;
        html += `<div class="szb-wtag${min ? '' : ' frei'}">
            <div class="szb-wsaeule"><div style="height:${min / max * 100}%"></div></div>
            <b>${_SZ_WT[d.getDay()]}</b><small>${min ? _szDauer(min) : 'frei'}</small>${spaet[iso] ? `<small class="szb-spaet">bis ${spaet[iso].aus}</small>` : ''}</div>`;
    }
    return html + '</div>';
}

const _SZV_SPANNE = { NAECHTE: 'Zeitraum', RUHEZEIT: 'Ruhezeit', SIEBEN_TAGE: 'In Folge' };

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
                : `<b>${_SZV_SPANNE[v.art] || 'Woche'}</b><span>${_szDatum(v.von).slice(0, 6)} – ${_szDatum(v.bis)}</span>`;
            const bild = v.art === 'NAECHTE' ? '' : (v.von === v.bis ? _szvBand(v.stempel) : _szvWoche(v));
            const zeiten = (v.von === v.bis || v.art === 'RUHEZEIT') && v.stempel.length
                ? `<div class="szb-zeiten">${v.stempel.map(s => `${v.von === v.bis ? '' : _szWt(s.tag) + ' '}${s.ein}–${s.aus}`).join(' · ')}</div>` : '';
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
        <p>Grundlage sind die Stempelzeiten aus easy@work und die Regeln der Filiale (Filial-Detail → «Arbeitszeit») sowie die Feiertage (Systemeinstellungen → Lohn-Stammdaten → Feiertage). Wochen zählen zum Zeitraum, in dem ihr Sonntag liegt. Stempel ohne Dauer zählen nicht.</p>
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

// ── Filialvergleich ───────────────────────────────────────────────────────

let _szfData = null;
const _SZF_LINIE = ['#0ea5e9', '#f59e0b', '#10b981', '#8b5cf6', '#ef4444', '#06b6d4', '#f97316', '#84cc16', '#ec4899', '#6366f1'];
const _SZF_MONAT = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'];

function szfInit() { _szInitZeitraum('szf'); szfLoad(); }
function szfZeitraum(art) { _szZeitraumSetzen('szf', art); }

async function szfLoad() {
    const box = document.getElementById('szfResult');
    if (!box) return;
    const qs = new URLSearchParams({ from: document.getElementById('szfFrom').value, to: document.getElementById('szfTo').value });
    box.innerHTML = '<div class="szb-leer">Lädt… alle Filialen werden geprüft.</div>';
    try {
        const res = await fetch('/api/reports/stempel-filialvergleich?' + qs, { headers: ah() });
        const j = await res.json().catch(() => ({}));
        if (!res.ok) { box.innerHTML = `<div class="szb-leer szb-fehler">${_szEsc(j.message || ('Fehler (HTTP ' + res.status + ')'))}</div>`; return; }
        _szfData = j;
        szfRender();
    } catch (e) {
        box.innerHTML = `<div class="szb-leer szb-fehler">Verbindungsfehler: ${_szEsc(e.message)}</div>`;
    }
}

function _szfPro100(n, stempel) { return stempel ? n * 100 / stempel : 0; }
function _szfZahl(x) { return x.toFixed(1); }

// Einzelbericht der Filiale öffnen: Sidebar umstellen, Zeitraum mitnehmen.
function szfOeffnen(id, bericht) {
    const sel = document.getElementById('branchSelect');
    if (!sel || ![...sel.options].some(o => String(o.value) === String(id))) return;
    sel.value = String(id);
    onBranchChange();
    const p = bericht === 'korrekturen' ? 'szk' : 'szv';
    document.getElementById(p + 'From').value = document.getElementById('szfFrom').value;
    document.getElementById(p + 'To').value = document.getElementById('szfTo').value;
    showPage(bericht === 'korrekturen' ? 'stempel-korrekturen' : 'stempel-verstoesse');
}

// Liegende Balken pro Filiale, gestapelt nach Art; senkrechte Linie = Schnitt aller Filialen.
function _szfBalken(titel, filialen, teileVon, farbe, artTitel, schnitt, bericht, rohText) {
    const zeilen = filialen.map(f => {
        const teile = teileVon(f).filter(t => t.n > 0);
        return { f, teile, wert: teile.reduce((s, t) => s + _szfPro100(t.n, f.stempel), 0) };
    }).sort((a, b) => b.wert - a.wert);
    const max = Math.max(1, schnitt, ...zeilen.map(z => z.wert)) * 1.08;
    const arten = [...new Set(zeilen.flatMap(z => z.teile.map(t => t.art)))];
    const legende = arten.map(a => `<span class="szb-chip" style="--f:${farbe(a)}">${_szEsc(artTitel(a))}</span>`).join('');
    const reihen = zeilen.map(z => {
        const teile = z.teile.map(t => {
            const w = _szfPro100(t.n, z.f.stempel);
            return `<div class="szf-teil" style="width:${w / max * 100}%;background:${farbe(t.art)}" title="${_szEsc(artTitel(t.art))}: ${t.n} (${_szfZahl(w)} pro 100)"></div>`;
        }).join('');
        return `<div class="szf-zeile" onclick="szfOeffnen(${z.f.id},'${bericht}')" title="Einzelbericht ${_szEsc(z.f.filiale)} öffnen">
            <div class="szf-name">${_szEsc(z.f.filiale)}</div>
            <div class="szf-bahn">${teile}<div class="szf-schnitt" style="left:${schnitt / max * 100}%"></div></div>
            <div class="szf-wert">${_szfZahl(z.wert)}</div>
            <div class="szf-roh">${rohText(z.f)}</div>
        </div>`;
    }).join('');
    return `<div class="szf-karte">
        <div class="szf-titel">${titel}<span>Ø alle Filialen ${_szfZahl(schnitt)} · gestrichelte Linie</span></div>
        ${legende ? `<div class="szf-legende">${legende}</div>` : ''}
        ${reihen}
    </div>`;
}

// Verlauf pro Monat: eine Linie pro Filiale, Korrekturen pro 100 Stempel.
function _szfVerlauf(d, breite) {
    const monate = d.filialen[0]?.proMonat.map(m => m.monat) || [];
    if (monate.length < 2) return '';
    const W = Math.max(520, Math.round(breite - 34)), H = 240, L = 40, R = 16, T = 14, B = 30;
    const punkte = d.filialen.map(f => f.proMonat.map(m => m.stempel ? _szfPro100(m.korrigiert, m.stempel) : null));
    const max = Math.max(1, ...punkte.flat().filter(v => v != null)) * 1.1;
    const x = i => L + (W - L - R) * (monate.length === 1 ? 0.5 : i / (monate.length - 1));
    const y = v => T + (H - T - B) * (1 - v / max);
    let svg = '';
    for (let s = 0; s <= 4; s++) {
        const v = max * s / 4;
        svg += `<line x1="${L}" x2="${W - R}" y1="${y(v)}" y2="${y(v)}" stroke="#e7e1d8"/><text x="${L - 6}" y="${y(v) + 4}" text-anchor="end" font-size="10" fill="#8b8b8b">${_szfZahl(v)}</text>`;
    }
    monate.forEach((m, i) => {
        svg += `<text x="${x(i)}" y="${H - 10}" text-anchor="middle" font-size="10" fill="#8b8b8b">${_SZF_MONAT[+m.slice(5, 7) - 1]} ${m.slice(2, 4)}</text>`;
    });
    punkte.forEach((reihe, fi) => {
        const farbe = _SZF_LINIE[fi % _SZF_LINIE.length];
        let pfad = '', offen = false;
        reihe.forEach((v, i) => {
            if (v == null) { offen = false; return; }
            pfad += `${offen ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)} `;
            offen = true;
            svg += `<circle cx="${x(i)}" cy="${y(v)}" r="3" fill="${farbe}"><title>${_szEsc(d.filialen[fi].filiale)} · ${_SZF_MONAT[+monate[i].slice(5, 7) - 1]}: ${_szfZahl(v)} pro 100</title></circle>`;
        });
        if (pfad) svg += `<path d="${pfad}" fill="none" stroke="${farbe}" stroke-width="2"/>`;
    });
    const legende = d.filialen.map((f, fi) =>
        `<span class="szb-chip" style="--f:${_SZF_LINIE[fi % _SZF_LINIE.length]}">${_szEsc(f.filiale)}</span>`).join('');
    return `<div class="szf-karte">
        <div class="szf-titel">Verlauf: Korrekturen pro 100 Stempel<span>pro Monat · Monat ohne Stempel = Lücke</span></div>
        <div class="szf-legende">${legende}</div>
        <svg viewBox="0 0 ${W} ${H}" width="${W}" height="${H}" style="display:block;max-width:100%">${svg}</svg>
    </div>`;
}

function _szfTabelle(d) {
    const arten = d.verstossArten.filter(a => d.filialen.some(f => !f.verstossAus.includes(a.art)));
    const maxArt = {};
    arten.forEach(a => { maxArt[a.art] = Math.max(1, ...d.filialen.map(f => f.verstossProArt[a.art] || 0)); });
    const summe = k => d.filialen.reduce((s, f) => s + f[k], 0);
    const kopf = `<tr><th>Filiale</th><th class="r">Stempel</th><th class="r">Korrek&shy;turen</th><th class="r">pro 100</th>
        <th class="r">MA mit Korr.</th><th class="r">Verstösse</th><th class="r">pro 100</th><th class="r">MA mit Verst.</th>
        ${arten.map(a => `<th class="szf-dreh" title="${_szEsc(a.regel)}"><span>${_szEsc(a.titel)}</span></th>`).join('')}</tr>`;
    const zeile = f => `<tr>
        <td class="szf-fil">${_szEsc(f.filiale)}</td>
        <td class="r">${f.stempel}</td>
        <td class="r"><a href="#" onclick="szfOeffnen(${f.id},'korrekturen');return false">${f.korrigiert}</a></td>
        <td class="r"><b>${_szfZahl(_szfPro100(f.korrigiert, f.stempel))}</b></td>
        <td class="r">${f.maMitKorrektur}</td>
        <td class="r"><a href="#" onclick="szfOeffnen(${f.id},'verstoesse');return false">${f.verstoesse}</a></td>
        <td class="r"><b>${_szfZahl(_szfPro100(f.verstoesse, f.stempel))}</b></td>
        <td class="r">${f.maMitVerstoss} / ${f.anzahlMa}</td>
        ${arten.map(a => {
            if (f.verstossAus.includes(a.art)) return '<td class="szf-aus" title="Für diese Filiale ausgeschaltet">aus</td>';
            const n = f.verstossProArt[a.art] || 0;
            return `<td class="szf-heat" style="--f:${_SZ_FARBE[a.art] || '#6b7280'};--a:${n ? 0.15 + 0.6 * n / maxArt[a.art] : 0}">${n || '·'}</td>`;
        }).join('')}
    </tr>`;
    const stempel = summe('stempel'), korr = summe('korrigiert'), verst = summe('verstoesse');
    const fuss = `<tr class="szf-total"><td>Total</td><td class="r">${stempel}</td><td class="r">${korr}</td>
        <td class="r"><b>${_szfZahl(_szfPro100(korr, stempel))}</b></td><td class="r">${summe('maMitKorrektur')}</td>
        <td class="r">${verst}</td><td class="r"><b>${_szfZahl(_szfPro100(verst, stempel))}</b></td>
        <td class="r">${summe('maMitVerstoss')} / ${summe('anzahlMa')}</td>
        ${arten.map(a => `<td class="r">${d.filialen.reduce((s, f) => s + (f.verstossProArt[a.art] || 0), 0)}</td>`).join('')}</tr>`;
    return `<div class="szf-karte">
        <div class="szf-titel">Übersicht aller Filialen<span>Zahl anklicken = Einzelbericht · Farbe = Häufung innerhalb der Spalte</span></div>
        <div style="overflow-x:auto"><table class="szf-tab"><thead>${kopf}</thead><tbody>${d.filialen.map(zeile).join('')}${fuss}</tbody></table></div>
    </div>`;
}

function szfRender() {
    const box = document.getElementById('szfResult');
    const d = _szfData;
    if (!box || !d) return;
    if (!d.filialen.length) { box.innerHTML = '<div class="szb-leer">Keine Stempel im Zeitraum.</div>'; return; }

    const stempel = d.filialen.reduce((s, f) => s + f.stempel, 0);
    const korr = d.filialen.reduce((s, f) => s + f.korrigiert, 0);
    const verst = d.filialen.reduce((s, f) => s + f.verstoesse, 0);
    const schnittK = _szfPro100(korr, stempel), schnittV = _szfPro100(verst, stempel);
    const artTitel = Object.fromEntries(d.verstossArten.map(a => [a.art, a.titel]));

    let html = `<div class="szb-summe"><div class="szb-zahl">${_szfZahl(schnittK)}</div><div>Korrekturen pro 100 Stempel über alle Filialen · <b>${korr}</b> von <b>${stempel}</b> Stempeln<br>
        <span>${verst} Verstösse (${_szfZahl(schnittV)} pro 100 Stempel) · ${d.filialen.length} Filialen · ${_szDatum(d.von)} – ${_szDatum(d.bis)}</span></div></div>`;
    html += '<div class="szf-raster">';
    html += _szfBalken('Korrekturen pro 100 Stempel', d.filialen,
        f => Object.keys(_SZK_ART).map(a => ({ art: a, n: f.korrekturProArt[a] || 0 })),
        a => _SZ_FARBE[a], a => _SZK_ART[a], schnittK, 'korrekturen',
        f => `${f.korrigiert} / ${f.stempel}`);
    html += _szfBalken('Verstösse pro 100 Stempel', d.filialen,
        f => d.verstossArten.map(a => ({ art: a.art, n: f.verstossProArt[a.art] || 0 })),
        a => _SZ_FARBE[a] || '#6b7280', a => artTitel[a] || a, schnittV, 'verstoesse',
        f => `${f.verstoesse} bei ${f.maMitVerstoss} MA`);
    html += '</div>';
    html += _szfVerlauf(d, box.clientWidth);
    html += _szfTabelle(d);
    html += `<details class="szb-regeln"><summary>Wie wird gerechnet?</summary>
        <p>Pro Filiale exakt dieselbe Rechnung wie in den Einzelberichten «Korrekturen Stempelzeiten» und «Arbeitszeit-Verstösse» (McAdmin). «Pro 100 Stempel» macht grosse und kleine Filialen vergleichbar: Korrekturen bzw. Verstösse ÷ Stempel × 100. Ein Verstoss zählt im Monat, in dem er endet (Wochenregeln: Sonntag der Woche).</p>
        ${d.ohneStempel.length ? `<p>Ohne Stempel im Zeitraum: ${d.ohneStempel.map(_szEsc).join(', ')}.</p>` : ''}
    </details>`;
    box.innerHTML = html;
}
