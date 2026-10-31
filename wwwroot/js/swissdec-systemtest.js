// ═══════════════════════════════════════════════════════════════════════════
//  SWISSDEC SYSTEMTEST (Walter 17.10.2026)
//  Gleiches Muster wie Kommunikations-Test (swissdec-komm.js):
//  Prüfpunkt wählen → in OneCrew öffnen/prüfen → Expected aus QT → bewerten.
//  Katalog: /api/elm/systemtest/katalog (Runs 24364 ERP + 26667 Tech)
//  Stand:   /api/elm/systemtest/stand
// ═══════════════════════════════════════════════════════════════════════════

let _stKatalog = null;
let _stStand = {};
let _stTab = 'S01';
let _stAuswahl = null;
let _stListeTab = null;

const ST_ERP_GRUPPEN = [
    { key: 'S01', gruppe: '01', name: 'Unternehmen' },
    { key: 'S02', gruppe: '02', name: 'Empfänger' },
    { key: 'S03', gruppe: '03', name: 'Personendaten' },
    { key: 'S04', gruppe: '04', name: 'Prozesse' },
    { key: 'S05', gruppe: '05', name: 'Cockpit' },
];
const ST_TECH_GRUPPEN = [
    { key: 'T01', gruppe: '01', name: 'Lohnmeldung' },
    { key: 'T02', gruppe: '02', name: 'Synchronisierung' },
    { key: 'T03', gruppe: '03', name: 'Completion' },
    { key: 'T04', gruppe: '04', name: 'Lohnausweis' },
    { key: 'T05', gruppe: '05', name: 'Dialog' },
    { key: 'T06', gruppe: '06', name: 'Warnung' },
];

function _stEsc(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function _stAlle() {
    if (!_stKatalog) return [];
    const erp = (_stKatalog.erp || []).map(c => ({ ...c, _bereich: 'erp' }));
    const tech = (_stKatalog.tech || []).map(c => ({ ...c, _bereich: 'tech' }));
    return erp.concat(tech);
}

function _stListeFuerTab(tab) {
    const alle = _stAlle();
    const erp = ST_ERP_GRUPPEN.find(g => g.key === tab);
    if (erp) return alle.filter(c => c._bereich === 'erp' && c.gruppe === erp.gruppe);
    const tech = ST_TECH_GRUPPEN.find(g => g.key === tab);
    if (tech) return alle.filter(c => c._bereich === 'tech' && c.gruppe === tech.gruppe);
    return [];
}

function _stEintrag(c) {
    const s = _stStand[c.id];
    const katalogNotiz = (c.notiz || '').trim();
    if (s) {
        if (!(s.notiz || '').trim() && katalogNotiz)
            return { ...s, notiz: katalogNotiz, vorbelegt: true };
        return s;
    }
    if (katalogNotiz)
        return { status: 'offen', notiz: katalogNotiz, vorbelegt: true };
    if (c.stand === 'gebaut' && c.kommentarQt)
        return { status: 'ok', notiz: c.kommentarQt, vorbelegt: true };
    return { status: 'offen' };
}

function _stPunkt(c) {
    if (c.gebaut === false) return '<span class="komm-dot komm-dot-leer" title="noch nicht gebaut"></span>';
    const st = _stEintrag(c).status;
    return `<span class="komm-dot komm-dot-${st}" title="${st === 'ok' ? 'erledigt' : st === 'fehler' ? 'nicht bestanden' : 'offen'}"></span>`;
}

async function stInit() {
    const kopf = document.getElementById('stKopf');
    if (kopf) kopf.innerHTML = '<div class="komm-kachel"><div class="komm-kachel-titel">Laden</div><div>Katalog wird geladen…</div></div>';
    try {
        _stTab = localStorage.getItem('stTab') || 'S01';
        _stAuswahl = localStorage.getItem('stAuswahl') || null;
    } catch (_) { /* egal */ }
    try {
        await Promise.all([stKatalogLaden(), stStandLaden()]);
    } catch (e) {
        if (kopf) kopf.innerHTML = `<div class="komm-kachel komm-kachel-warn"><div class="komm-kachel-titel">Fehler</div><div class="komm-rot">${_stEsc(e.message || e)}</div></div>`;
        return;
    }
    if (!_stKatalog) {
        if (kopf) kopf.innerHTML = '<div class="komm-kachel komm-kachel-warn"><div class="komm-kachel-titel">Katalog fehlt</div><div>Weder API noch <code>/data/swissdec-systemtest-katalog.json</code> lieferbar. Server neu starten / deployen?</div></div>';
        const tabs = document.getElementById('stTabs');
        if (tabs) tabs.innerHTML = '';
        return;
    }
    stKopfZeichnen();
    stTab(_stTab);
}

async function stKatalogLaden() {
    _stKatalog = null;
    // 1) API (Super-Admin) · 2) statische Datei aus wwwroot (Fallback ohne Restart)
    try {
        const r = await fetch('/api/elm/systemtest/katalog', { headers: typeof ah === 'function' ? ah() : {} });
        if (r.ok) { _stKatalog = await r.json(); return; }
    } catch (_) { /* Fallback */ }
    try {
        const r2 = await fetch('/data/swissdec-systemtest-katalog.json?v=20261031h');
        if (r2.ok) _stKatalog = await r2.json();
    } catch (_) { _stKatalog = null; }
}

async function stStandLaden() {
    try {
        const r = await fetch('/api/elm/systemtest/stand', { headers: ah() });
        _stStand = r.ok ? await r.json() : {};
    } catch (_) { _stStand = {}; }
}

function stKopfZeichnen() {
    const el = document.getElementById('stKopf');
    if (!el) return;
    const alle = _stAlle();
    const ok = alle.filter(c => _stEintrag(c).status === 'ok').length;
    const fehler = alle.filter(c => _stEintrag(c).status === 'fehler').length;
    const gebaut = alle.filter(c => c.gebaut !== false).length;
    const erpN = (_stKatalog?.erp || []).length;
    const techN = (_stKatalog?.tech || []).length;
    const quelle = _stKatalog?.quelle || {};
    const kachel = (titel, inhalt, klasse = '') =>
        `<div class="komm-kachel ${klasse ? 'komm-kachel-' + klasse : ''}"><div class="komm-kachel-titel">${titel}</div><div>${inhalt}</div></div>`;
    el.innerHTML =
        kachel('Bereich', `<b>Systemtest ERP + Tech</b><div class="komm-klein">Quality Tool · ELMv6.0.1</div>`, 'ok')
        + kachel('ERP-Katalog', `<b>${erpN}</b> Prüfpunkte<div class="komm-klein">${_stEsc(quelle.erpPdf || 'Run 24364')}</div>`)
        + kachel('Tech-Katalog', `<b>${techN}</b> Fälle / Gates<div class="komm-klein">${_stEsc(quelle.techPdf || 'Run 26667')}</div>`)
        + kachel('In OneCrew', `<b>${gebaut}</b> vorführbar<div class="komm-klein">${alle.length - gebaut} noch nicht gebaut</div>`, gebaut ? 'ok' : 'warn')
        + kachel('Fortschritt', `<b>${ok}</b> von ${alle.length} erledigt${fehler ? ` · <span class="komm-rot">${fehler} nicht bestanden</span>` : ''}
            <div class="komm-klein">Bewertung speichern wie beim Foundation-Test</div>`, ok ? 'ok' : '');
}

function stTab(key) {
    _stTab = key;
    try { localStorage.setItem('stTab', key); } catch (_) { /* egal */ }
    const tabs = document.getElementById('stTabs');
    if (tabs) {
        const btn = (g, prefix) => {
            const liste = _stListeFuerTab(g.key);
            const ok = liste.filter(c => _stEintrag(c).status === 'ok').length;
            return `<button type="button" class="komm-tab ${key === g.key ? 'aktiv' : ''}" onclick="stTab('${g.key}')">
                <b>${g.key}</b> ${_stEsc(g.name)} <span class="komm-tab-zahl">${ok}/${liste.length}</span></button>`;
        };
        tabs.innerHTML =
            `<button type="button" class="komm-tab ${key === 'uebersicht' ? 'aktiv' : ''}" onclick="stTab('uebersicht')">📋 Übersicht</button>
             <span class="komm-tab-trenner"></span>`
            + ST_ERP_GRUPPEN.map(g => btn(g)).join('')
            + `<span class="komm-tab-trenner"></span>`
            + ST_TECH_GRUPPEN.map(g => btn(g)).join('')
            + `<span class="komm-tab-trenner"></span>
               <button type="button" class="komm-tab" onclick="showPage('swissdec-kommunikation')">📡 Foundation</button>
               <button type="button" class="komm-tab" onclick="showPage('elm-meldungen')">🧾 ELM-Meldungen</button>`;
    }
    const zeige = (id, an) => { const e = document.getElementById(id); if (e) e.style.display = an ? '' : 'none'; };
    zeige('stPaneTest', key !== 'uebersicht');
    zeige('stPaneUebersicht', key === 'uebersicht');
    if (key === 'uebersicht') {
        stUebersichtZeichnen();
        return;
    }
    const liste = _stListeFuerTab(key);
    if (!liste.some(c => c.id === _stAuswahl)) _stAuswahl = liste[0]?.id || null;
    stListeZeichnen();
    stDetailZeichnen();
}

function stUebersichtZeichnen() {
    const el = document.getElementById('stUebersicht');
    if (!el) return;
    if (!_stKatalog) {
        el.innerHTML = '<div class="komm-rot">Katalog konnte nicht geladen werden (Super-Admin?).</div>';
        return;
    }
    const zeile = (g, bereich) => {
        const liste = _stListeFuerTab(g.key);
        const ok = liste.filter(c => _stEintrag(c).status === 'ok').length;
        const gebaut = liste.filter(c => c.gebaut !== false).length;
        return `<tr style="cursor:pointer" onclick="stTab('${g.key}')">
            <td><b>${g.key}</b></td>
            <td>${_stEsc(g.name)}</td>
            <td>${bereich}</td>
            <td>${ok}/${liste.length}</td>
            <td>${gebaut}/${liste.length} vorführbar</td></tr>`;
    };
    el.innerHTML = `
        <div class="komm-detail" style="max-width:980px">
            <div class="komm-detail-titel" style="margin-bottom:6px">Wegleitung Systemtest</div>
            <div class="komm-klein" style="margin-bottom:14px">
                Gleicher Ablauf wie der Kommunikations-Test: Reiter wählen → Prüfpunkt links anklicken →
                in OneCrew die genannte Maske öffnen → Expected aus dem Quality Tool prüfen →
                unten «Bestanden / Nicht bestanden / Offen» setzen. Kommentartext fürs QT steht im Detail.
                Quellen: Run 24364 (ERP) und Run 26667 (Tech).
            </div>
            <table class="komm-archiv"><thead><tr>
                <th>Reiter</th><th>Thema</th><th>Bereich</th><th>Bewertet</th><th>Gebaut</th>
            </tr></thead><tbody>
            ${ST_ERP_GRUPPEN.map(g => zeile(g, 'ERP')).join('')}
            ${ST_TECH_GRUPPEN.map(g => zeile(g, 'Tech')).join('')}
            </tbody></table>
        </div>`;
}

function stListeZeichnen() {
    const el = document.getElementById('stListe');
    if (!el) return;
    const oben = _stListeTab === _stTab ? el.scrollTop : 0;
    _stListeTab = _stTab;
    el.innerHTML = _stListeFuerTab(_stTab).map(c => `
        <button type="button" class="komm-zeile ${c.id === _stAuswahl ? 'aktiv' : ''} ${c.gebaut === false ? 'leer' : ''}"
                onclick="stWaehle('${c.id}')">
            ${_stPunkt(c)}
            <span class="komm-id">${_stEsc(c.id.replace(/^S_ELM_V6_/, 'S').replace(/^ST_ELM_V6_/, 'T'))}</span>
            <span class="komm-titel">${_stEsc(c.titel)}</span>
        </button>`).join('');
    el.scrollTop = oben;
}

function stWaehle(id) {
    _stAuswahl = id;
    try { localStorage.setItem('stAuswahl', id); } catch (_) { /* egal */ }
    stListeZeichnen();
    stDetailZeichnen();
}

function stDetailZeichnen() {
    const el = document.getElementById('stDetail');
    const c = _stAlle().find(x => x.id === _stAuswahl);
    if (!el || !c) {
        if (el) el.innerHTML = '<div class="komm-klein">Prüfpunkt wählen.</div>';
        return;
    }
    const e = _stEintrag(c);
    const statusText = { ok: '✓ erledigt', fehler: '✗ nicht bestanden', offen: 'offen' }[e.status] || 'offen';
    const typ = c._bereich === 'tech' ? 'SYSTEMTEST TECH' : 'SYSTEMTEST ERP';
    const dir = c.direktive ? ` · Directive ${ _stEsc(c.direktive)}` : '';
    const knoepfe = (c.aktionen || []).map((a, i) =>
        `<button type="button" class="${i === 0 ? 'komm-btn-primaer' : 'komm-btn-sekundaer'}" onclick="stAktion(${i})">${_stEsc(a.label)}</button>`).join('');
    const besch = c.beschreibung || '';
    const erw = c.erwartet || '';
    const hinweis = c.kommentarQt || c.notiz || '';
    el.innerHTML = `
        <div class="komm-detail-kopf">
            <div>
                <div class="komm-detail-id">${_stEsc(c.id)} · <span>${typ}${dir}</span></div>
                <div class="komm-detail-titel">${_stEsc(c.titel)}</div>
            </div>
            <span class="komm-status komm-status-${c.gebaut === false ? 'leer' : e.status}">${c.gebaut === false ? 'noch nicht gebaut' : statusText}</span>
        </div>
        <div class="komm-schritt">
            <div class="komm-schritt-nr">1</div>
            <div><div class="komm-schritt-titel">Was das Quality Tool verlangt</div>
                 <div>${besch ? _stEsc(besch) : '<span class="komm-klein">Siehe Expected.</span>'}</div></div>
        </div>
        <div class="komm-schritt">
            <div class="komm-schritt-nr">2</div>
            <div style="flex:1;min-width:0"><div class="komm-schritt-titel">In OneCrew prüfen</div>
                 ${c.gebaut === false
                    ? '<span class="komm-klein">Noch nicht vorführbar — Lücke schliessen, dann hier bewerten.</span>'
                    : `<div class="komm-knoepfe">${knoepfe || '<span class="komm-klein">Manuell in der genannten Maske prüfen.</span>'}</div>
                       ${c.seiteLabel ? `<div class="komm-klein" style="margin-top:6px">Ziel: ${_stEsc(c.seiteLabel)}</div>` : ''}`}</div>
        </div>
        <div class="komm-schritt">
            <div class="komm-schritt-nr">3</div>
            <div><div class="komm-schritt-titel">Erwartet</div><div>${erw ? _stEsc(erw) : '<span class="komm-klein">—</span>'}</div>
                 ${hinweis ? `<div class="komm-hinweis">💬 Kommentar fürs QT:<br>${_stEsc(hinweis)}</div>` : ''}</div>
        </div>
        ${c.gebaut === false ? '' : `
        <div class="komm-bewertung">
            <div class="komm-schritt-titel">Bewertung</div>
            <div class="komm-knoepfe">
                <button type="button" class="komm-btn-ok ${e.status === 'ok' ? 'gewaehlt' : ''}" onclick="stBewerten('ok')">✓ Bestanden</button>
                <button type="button" class="komm-btn-fehler ${e.status === 'fehler' ? 'gewaehlt' : ''}" onclick="stBewerten('fehler')">✗ Nicht bestanden</button>
                <button type="button" class="komm-btn-sekundaer ${e.status === 'offen' ? 'gewaehlt' : ''}" onclick="stBewerten('offen')">Offen</button>
            </div>
            <textarea id="stNotiz" rows="3" placeholder="Notiz / Kommentar fürs Quality Tool" onchange="stBewerten(null)">${_stEsc(e.notiz || '')}</textarea>
            <div class="komm-klein">${e.letzterVersuch
                ? `Letzter Versuch ${e.letzterVersuchAm ? new Date(e.letzterVersuchAm).toLocaleString('de-CH') : ''}: ${_stEsc(e.letzterVersuch)}`
                : (e.vorbelegt ? 'Stand aus dem Systemtest-Protokoll (vorbelegt).' : 'Noch kein Versuch aufgezeichnet.')}</div>
        </div>`}
        ${(c.gates && c.gates.length) ? `
        <div class="komm-schritt" style="margin-top:12px">
            <div class="komm-schritt-nr">G</div>
            <div style="flex:1"><div class="komm-schritt-titel">Gates (Tech)</div>
            <table class="komm-archiv"><thead><tr><th>Gate</th><th>Name</th><th>Erwartet</th></tr></thead><tbody>
            ${c.gates.map(g => `<tr><td><code>${_stEsc(g.id)}</code></td><td>${_stEsc(g.name)}</td><td>${_stEsc(g.erwartet || '')}</td></tr>`).join('')}
            </tbody></table></div>
        </div>` : ''}`;
}

async function stAktion(i) {
    const c = _stAlle().find(x => x.id === _stAuswahl);
    const a = c?.aktionen?.[i];
    if (!a) return;
    if (a.art === 'seite' && a.seite && typeof showPage === 'function') {
        // «← Zurück» oben rechts führt wieder hierher (wie AKIS → MA, Walter 14.10.2026).
        window._ruecksprung = {
            seite: a.seite,
            ziel: 'swissdec-systemtest',
            vorher: window._prevPageName || 'swissdec-systemtest'
        };
        showPage(a.seite);
        await stSpeichern(c.id, { letzterVersuch: `Seite geöffnet: ${a.seiteLabel || a.seite}` });
        return;
    }
}

async function stBewerten(status) {
    const c = _stAlle().find(x => x.id === _stAuswahl);
    if (!c) return;
    const notiz = document.getElementById('stNotiz')?.value ?? '';
    const dto = { notiz };
    dto.status = status || _stEintrag(c).status;
    await stSpeichern(c.id, dto);
    if (status) {
        stTab(_stTab);
        stKopfZeichnen();
    }
}

async function stSpeichern(id, dto) {
    try {
        const alt = _stEintrag(_stAlle().find(x => x.id === id) || { id });
        const body = {
            status: dto.status || alt.status,
            notiz: dto.notiz ?? (alt.vorbelegt ? alt.notiz : null),
            letzterVersuch: dto.letzterVersuch || null
        };
        const r = await fetch(`/api/elm/systemtest/stand/${encodeURIComponent(id)}`, {
            method: 'PUT', headers: { ...(typeof ah === 'function' ? ah() : {}), 'Content-Type': 'application/json' },
            body: JSON.stringify(body)
        });
        if (r.ok) _stStand[id] = await r.json();
    } catch (_) { /* Anzeige bleibt */ }
    if (dto.letzterVersuch) {
        const e = _stStand[id];
        const fuss = document.querySelector('#stDetail .komm-bewertung .komm-klein');
        if (e && fuss) fuss.textContent = `Letzter Versuch ${new Date(e.letzterVersuchAm).toLocaleString('de-CH')}: ${e.letzterVersuch}`;
        stListeZeichnen();
        stKopfZeichnen();
    }
}

window.stInit = stInit;
window.stTab = stTab;
window.stWaehle = stWaehle;
window.stBewerten = stBewerten;
window.stAktion = stAktion;
