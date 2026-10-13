// ══════════════════════════════════════════════════════════════════════
//  ORGANIGRAMM (Walter 13.10.2026) — HR-Bereich → Auswertungen / Reporting.
//  Obere Stellen von Hand (Lizenznehmer, Supervisor, HR …), darunter die
//  Filialen mit GF und Managern — gleiche Auswahl wie der Manager-Dienstplan
//  (FIX-M mit laufendem Vertrag im Monat). Farben pro Stufe: jede obere
//  Stelle hat ihre eigene, dazu je eine für «Filiale / GF» und «Manager».
// ══════════════════════════════════════════════════════════════════════
let _orgData = null;
let _orgJahr = null, _orgMonat = null;
let _orgEdit = null;   // { stellen:[{titel,name,art,farbe}], farben:{FILIALE,MANAGER} } während der Bearbeitung

const ORG_MONATE = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
const ORG_STANDARD = { LINIE: '#3f3f3f', STAB: '#ffffff', FILIALE: '#e7e4db', MANAGER: '#ffffff' };
const _orgGlas = 'background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:6px 14px;font-size:13px;font-weight:600;cursor:pointer;color:#3f3f3f';
const _orgKohle = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 14px;font-size:13px;font-weight:600;cursor:pointer';
const _orgInp = 'background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;padding:6px 10px;font-size:13px;color:#3f3f3f;box-shadow:0 2px 6px rgba(60,55,48,0.13),inset 0 1px 0 rgba(255,255,255,0.9);min-width:0';

const ORG_CSS = `
.dpo{display:flex;flex-direction:column;align-items:center;padding:10px 20px 24px;min-width:max-content;font-family:inherit;color:#3f3f3f}
.dpo-v{width:2px;height:20px;background:rgba(60,55,48,0.35)}
.dpo-box{border-radius:14px;padding:9px 22px;text-align:center;min-width:190px;border:1px solid rgba(60,55,48,0.18);box-shadow:0 4px 14px rgba(60,55,48,0.16)}
.dpo-titel{font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;font-weight:700;opacity:.75}
.dpo-name{font-size:15px;font-weight:700;margin-top:1px}
.dpo-stab-row{display:grid;grid-template-columns:1fr 2px 1fr;align-self:stretch}
.dpo-stab-row>.dpo-linie-mitte{background:rgba(60,55,48,0.35)}
.dpo-stab-links{display:flex;flex-direction:column;align-items:flex-end;gap:10px;padding:10px 0}
.dpo-stab-rechts{display:flex;flex-direction:column;align-items:flex-start;gap:10px;padding:10px 0}
.dpo-stab-item{display:flex;align-items:center}
.dpo-h{width:28px;height:2px;background:rgba(60,55,48,0.35)}
.dpo-leer{font-size:12.5px;color:#8b8b8b;border:1px dashed rgba(60,55,48,0.3);border-radius:12px;padding:10px 18px}
.dpo-fil-row{display:flex;justify-content:center}
.dpo-col{position:relative;padding:20px 9px 0;display:flex;flex-direction:column;align-items:center}
.dpo-col::before{content:'';position:absolute;top:0;left:0;right:0;border-top:2px solid rgba(60,55,48,0.35)}
.dpo-col:first-child::before{left:50%}
.dpo-col:last-child::before{right:50%}
.dpo-col:only-child::before{display:none}
.dpo-col::after{content:'';position:absolute;top:0;left:50%;height:20px;border-left:2px solid rgba(60,55,48,0.35)}
.dpo-fil{border:1px solid rgba(60,55,48,0.18);border-radius:14px;padding:8px 14px;text-align:center;min-width:140px;box-shadow:0 2px 8px rgba(60,55,48,0.12)}
.dpo-fil b{display:block;font-size:14px}
.dpo-fil span{font-size:13px}
.dpo-fil .dpo-offen{opacity:.7;font-style:italic}
.dpo-mgr-liste{align-self:stretch;margin:10px 0 0 14px;border-left:2px solid rgba(60,55,48,0.25);display:flex;flex-direction:column;gap:8px;padding:4px 0}
.dpo-mgr{display:flex;align-items:center}
.dpo-mgr::before{content:'';width:12px;height:2px;background:rgba(60,55,48,0.25);flex:none}
.dpo-mgr span{border:1px solid rgba(60,55,48,0.16);border-radius:10px;padding:6px 12px;font-size:13px;min-width:110px;text-align:center;box-shadow:0 1px 4px rgba(60,55,48,0.08)}
.org-farbe{-webkit-appearance:none;appearance:none;width:34px;height:30px;padding:2px;border:1px solid rgba(255,255,255,0.95);border-radius:9px;background:#fff;cursor:pointer;box-shadow:0 2px 6px rgba(60,55,48,0.13);flex:none}
.org-farbe::-webkit-color-swatch-wrapper{padding:0}
.org-farbe::-webkit-color-swatch{border:1px solid rgba(60,55,48,0.15);border-radius:6px}
.org-farbe::-moz-color-swatch{border:1px solid rgba(60,55,48,0.15);border-radius:6px}
`;

// Dunkler Hintergrund ⇒ weisse Schrift, heller ⇒ Kohle.
function _orgSchrift(hex) {
    const m = /^#([0-9a-f]{6})$/i.exec(hex || '');
    if (!m) return '#3f3f3f';
    const n = parseInt(m[1], 16);
    const hell = (0.299 * (n >> 16) + 0.587 * ((n >> 8) & 255) + 0.114 * (n & 255)) / 255;
    return hell > 0.6 ? '#3f3f3f' : '#ffffff';
}
function _orgStil(hex, verlauf) {
    const bg = verlauf
        ? `linear-gradient(165deg,rgba(255,255,255,0.22) 0%,rgba(255,255,255,0) 55%,rgba(0,0,0,0.05) 100%),${hex}`
        : hex;
    return `background:${bg};color:${_orgSchrift(hex)}`;
}
function _orgStelleFarbe(s) { return s.farbe || (_orgIstStab(s) ? ORG_STANDARD.STAB : ORG_STANDARD.LINIE); }
function _orgStufenFarbe(farben, stufe) { return (farben && farben[stufe]) || ORG_STANDARD[stufe]; }

function _orgNamen(filialen) {
    const anzahl = {};
    filialen.forEach(f => f.personen.forEach(p => { const k = (p.vorname || '').trim().toLowerCase(); anzahl[k] = (anzahl[k] || 0) + 1; }));
    return p => {
        const v = (p.vorname || '').trim();
        return anzahl[v.toLowerCase()] > 1 && p.nachname ? `${v} ${p.nachname.trim()[0]}.` : v;
    };
}

function _orgIstStab(s) { return s.art === 'STAB' || s.art === 'STAB_R'; }

function _orgHtml(d) {
    const box = s => `<div class="dpo-box" style="${_orgStil(_orgStelleFarbe(s), true)}">${s.titel ? `<div class="dpo-titel">${esc(s.titel)}</div>` : ''}<div class="dpo-name">${esc(s.name)}</div></div>`;
    const filStil = _orgStil(_orgStufenFarbe(d.farben, 'FILIALE'), true);
    const mgrStil = _orgStil(_orgStufenFarbe(d.farben, 'MANAGER'), false);
    const name = _orgNamen(d.filialen);
    const voll = p => `${p.vorname || ''} ${p.nachname || ''}`.trim();
    // Stab-Stellen hängen an der Linie unter der Linien-Stelle, die in der Liste davor steht.
    const gruppen = [];
    d.stellen.forEach(s => {
        if (!_orgIstStab(s) || !gruppen.length) gruppen.push({ linie: _orgIstStab(s) ? null : s, stab: [] });
        if (_orgIstStab(s)) gruppen[gruppen.length - 1].stab.push(s);
    });
    let html = '<div class="dpo">';
    if (!gruppen.length)
        html += '<div class="dpo-leer">Obere Stellen (Lizenznehmer, Supervisor, HR …) noch nicht erfasst</div><div class="dpo-v"></div>';
    gruppen.forEach(g => {
        if (g.linie) html += box(g.linie) + '<div class="dpo-v"></div>';
        if (!g.stab.length) return;
        const links = g.stab.filter(s => s.art !== 'STAB_R');
        const rechts = g.stab.filter(s => s.art === 'STAB_R');
        html += `<div class="dpo-stab-row">
            <div class="dpo-stab-links">${links.map(s => `<div class="dpo-stab-item">${box(s)}<div class="dpo-h"></div></div>`).join('')}</div>
            <div class="dpo-linie-mitte"></div>
            <div class="dpo-stab-rechts">${rechts.map(s => `<div class="dpo-stab-item"><div class="dpo-h"></div>${box(s)}</div>`).join('')}</div>
        </div><div class="dpo-v"></div>`;
    });
    if (!d.filialen.length) html += '<div class="dpo-leer">Keine FIX-M-Verträge in diesem Monat</div>';
    html += '<div class="dpo-fil-row">' + d.filialen.map(f => {
        const gf = f.personen.filter(p => p.istGf);
        const mgr = f.personen.filter(p => !p.istGf);
        return `<div class="dpo-col">
            <div class="dpo-fil" style="${filStil}"><b>${esc(f.name)}</b>${gf.length
                ? gf.map(p => `<span title="${esc(voll(p))} — Geschäftsführer/in">${esc(name(p))}</span>`).join('<br>')
                : '<span class="dpo-offen">GF offen</span>'}</div>
            ${mgr.length ? `<div class="dpo-mgr-liste">${mgr.map(p => `<div class="dpo-mgr"><span style="${mgrStil}" title="${esc(voll(p))}">${esc(name(p))}</span></div>`).join('')}</div>` : ''}
        </div>`;
    }).join('') + '</div></div>';
    return html;
}

function _orgDarfBearbeiten() {
    return ['admin', 'superuser', 'buchhaltung'].includes(currentUser?.role);
}

async function orgOpen() {
    if (_orgJahr == null) { const h = new Date(); _orgJahr = h.getFullYear(); _orgMonat = h.getMonth() + 1; }
    _orgEdit = null;
    document.getElementById('orgModal')?.remove();
    const ov = document.createElement('div');
    ov.id = 'orgModal';
    ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:8900;display:flex;align-items:center;justify-content:center;padding:20px';
    ov.innerHTML = `<style>${ORG_CSS}</style>
        <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 50px rgba(60,55,48,0.22);width:min(1500px,96vw);height:92vh;display:flex;flex-direction:column">
            <div style="display:flex;align-items:center;gap:10px;padding:16px 20px 10px;flex-wrap:wrap">
                <div style="font-size:15px;font-weight:700;color:#3f3f3f">🏢 Organigramm</div>
                <button onclick="orgMonatWechseln(-1)" title="Vormonat" style="${_orgGlas};padding:4px 10px">‹</button>
                <span id="orgMonatTitel" style="font-size:14px;font-weight:600;color:#3f3f3f;min-width:120px;text-align:center"></span>
                <button onclick="orgMonatWechseln(1)" title="Nächster Monat" style="${_orgGlas};padding:4px 10px">›</button>
                <span style="font-size:12px;color:#8b8b8b">Filialen und Manager aus den FIX-M-Verträgen dieses Monats</span>
                <span style="flex:1"></span>
                ${_orgDarfBearbeiten() ? `<button id="orgEditBtn" onclick="orgBearbeiten()" style="${_orgGlas}">✎ Stellen &amp; Farben</button>` : ''}
                <button onclick="orgDrucken()" style="${_orgKohle}">🖨 Drucken</button>
                <button onclick="orgSchliessen()" style="${_orgGlas};padding:4px 10px">✕</button>
            </div>
            <div style="flex:1;min-height:0;display:flex;gap:12px;padding:0 10px 10px">
                <div id="orgBody" style="flex:1;min-width:0;overflow:auto"><div style="color:#8b8b8b;padding:20px;font-size:12.5px">Wird geladen…</div></div>
                <div id="orgEditPanel" style="display:none;width:420px;flex:none;overflow-y:auto;background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:14px;padding:14px 16px"></div>
            </div>
        </div>`;
    ov.onclick = (e) => { if (e.target === ov) orgSchliessen(); };
    document.body.appendChild(ov);
    await orgLaden();
}

function orgSchliessen() {
    _orgEdit = null;
    document.getElementById('orgModal')?.remove();
}

function orgMonatWechseln(d) {
    _orgMonat += d;
    if (_orgMonat < 1) { _orgMonat = 12; _orgJahr--; }
    if (_orgMonat > 12) { _orgMonat = 1; _orgJahr++; }
    orgLaden();
}

function _orgAnzeigen() {
    const el = document.getElementById('orgBody');
    if (!el || !_orgData) return;
    el.innerHTML = _orgHtml(_orgEdit ? { ..._orgData, stellen: _orgEdit.stellen, farben: _orgEdit.farben } : _orgData);
}

async function orgLaden() {
    const el = document.getElementById('orgBody');
    const titel = document.getElementById('orgMonatTitel');
    if (titel) titel.textContent = `${ORG_MONATE[_orgMonat - 1]} ${_orgJahr}`;
    if (!el) return;
    try {
        const r = await fetch(`/api/manager-dienstplan/organigramm?year=${_orgJahr}&month=${_orgMonat}`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) { el.innerHTML = '<div style="color:#991b1b;padding:20px;font-size:12.5px">Laden fehlgeschlagen.</div>'; return; }
        _orgData = await r.json();
        _orgData.farben = _orgData.farben || {};
        _orgAnzeigen();
    } catch (_) {
        el.innerHTML = '<div style="color:#991b1b;padding:20px;font-size:12.5px">Verbindungsfehler.</div>';
    }
}

function orgDrucken() {
    if (!_orgData) return;
    const daten = _orgEdit ? { ..._orgData, stellen: _orgEdit.stellen, farben: _orgEdit.farben } : _orgData;
    const w = window.open('', '_blank');
    if (!w) { alert('Bitte Pop-ups für OneCrew erlauben, damit gedruckt werden kann.'); return; }
    // @page margin 0 ⇒ der Browser druckt keine Kopf-/Fusszeile (Datum, about:blank); Rand kommt vom body.
    w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Organigramm ${ORG_MONATE[daten.month - 1]} ${daten.year}</title>
        <style>@page{size:A4 landscape;margin:0}html,body{margin:0}body{padding:10mm;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        h1{font-size:13px;font-weight:600;color:#8b8b8b;margin:0 0 6px;text-align:right}#o{width:277mm;height:180mm;overflow:hidden}${ORG_CSS}</style></head>
        <body><h1>Organigramm · Stand ${ORG_MONATE[daten.month - 1]} ${daten.year}</h1><div id="o">${_orgHtml(daten)}</div>
        <script>window.onload=function(){var o=document.getElementById('o'),b=o.firstElementChild,s=Math.min(1,o.clientWidth/b.scrollWidth,o.clientHeight/b.scrollHeight);if(s<1){b.style.zoom=s;}setTimeout(function(){window.print();},150);};<\/script></body></html>`);
    w.document.close();
}

// ── Stellen & Farben bearbeiten (Seitenleiste mit Live-Vorschau) ─────────
function orgBearbeiten() {
    if (!_orgData) return;
    const stellen = (_orgData.stellen || []).map(s => ({ titel: s.titel, name: s.name, art: s.art, farbe: s.farbe || null }));
    _orgEdit = {
        stellen: stellen.length ? stellen : [
            { titel: 'Lizenznehmer', name: '', art: 'LINIE', farbe: null },
            { titel: 'Supervisor', name: '', art: 'LINIE', farbe: null },
            { titel: 'HR', name: '', art: 'STAB', farbe: null },
        ],
        farben: { ...(_orgData.farben || {}) },
    };
    const btn = document.getElementById('orgEditBtn');
    if (btn) btn.style.display = 'none';
    const p = document.getElementById('orgEditPanel');
    p.style.display = '';
    p.innerHTML = `
        <div style="font-size:14px;font-weight:700;color:#3f3f3f;margin-bottom:4px">Obere Stellen</div>
        <div style="font-size:12px;color:#646464;margin-bottom:10px">«Linie» steht untereinander über den Filialen. «Stab links/rechts» hängt seitlich an der Linie, direkt unter der Linien-Stelle, die in der Liste darüber steht — mehrere auf derselben Seite kommen untereinander. Leere Zeilen werden nicht gespeichert.</div>
        <div id="orgEditListe"></div>
        <button onclick="orgStelleDazu()" style="${_orgGlas};margin-top:4px">+ Stelle</button>
        <div style="font-size:14px;font-weight:700;color:#3f3f3f;margin:18px 0 8px">Farben der Stufen darunter</div>
        <div id="orgEditStufen"></div>
        <div style="display:flex;gap:8px;margin-top:18px;flex-wrap:wrap">
            <button onclick="orgFarbenStandard()" style="${_orgGlas}">Farben auf Standard</button>
            <span style="flex:1"></span>
            <button onclick="orgBearbeitenAbbrechen()" style="${_orgGlas}">Abbrechen</button>
            <button id="orgSpeichernBtn" onclick="orgSpeichern()" style="${_orgKohle}">Speichern</button>
        </div>`;
    _orgEditRender();
}

function _orgEditRender() {
    const liste = document.getElementById('orgEditListe');
    const stufen = document.getElementById('orgEditStufen');
    if (!liste || !_orgEdit) return;
    const knopf = 'background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:8px;padding:4px 7px;font-size:12px;cursor:pointer;color:#3f3f3f';
    const n = _orgEdit.stellen.length;
    liste.innerHTML = _orgEdit.stellen.map((s, i) => `
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:6px;margin-bottom:12px;padding-bottom:12px;border-bottom:1px solid rgba(60,55,48,0.12)">
            <input value="${esc(s.titel)}" placeholder="Titel, z.B. Lizenznehmer" maxlength="80" oninput="_orgEdit.stellen[${i}].titel=this.value;_orgAnzeigen()" style="${_orgInp}">
            <input value="${esc(s.name)}" placeholder="Name" maxlength="120" oninput="_orgEdit.stellen[${i}].name=this.value;_orgAnzeigen()" style="${_orgInp}">
            <div style="grid-column:1/-1;display:flex;gap:6px;align-items:center">
                <select onchange="_orgEdit.stellen[${i}].art=this.value;_orgEditRender();_orgAnzeigen()" style="${_orgInp};flex:1">
                    <option value="LINIE"${!_orgIstStab(s) ? ' selected' : ''}>Linie</option>
                    <option value="STAB"${s.art === 'STAB' ? ' selected' : ''}>Stab links</option>
                    <option value="STAB_R"${s.art === 'STAB_R' ? ' selected' : ''}>Stab rechts</option>
                </select>
                <input type="color" class="org-farbe" title="Farbe dieser Stelle" value="${_orgStelleFarbe(s)}" oninput="_orgEdit.stellen[${i}].farbe=this.value;_orgAnzeigen()">
                <button onclick="orgStelleSchieben(${i},-1)" title="Nach oben" style="${knopf}"${i === 0 ? ' disabled' : ''}>↑</button>
                <button onclick="orgStelleSchieben(${i},1)" title="Nach unten" style="${knopf}"${i === n - 1 ? ' disabled' : ''}>↓</button>
                <button onclick="orgStelleWeg(${i})" title="Entfernen" style="${knopf};color:#991b1b">✕</button>
            </div>
        </div>`).join('') || '<div style="font-size:12.5px;color:#8b8b8b;margin-bottom:8px">Keine Stellen.</div>';
    const zeile = (stufe, text) => `
        <div style="display:flex;align-items:center;gap:10px;margin-bottom:8px">
            <input type="color" class="org-farbe" value="${_orgStufenFarbe(_orgEdit.farben, stufe)}" oninput="_orgEdit.farben['${stufe}']=this.value;_orgAnzeigen()">
            <span style="font-size:13px;color:#3f3f3f">${text}</span>
        </div>`;
    stufen.innerHTML = zeile('FILIALE', 'Filiale mit Geschäftsführer/in') + zeile('MANAGER', 'Manager');
}

function orgStelleDazu() { _orgEdit.stellen.push({ titel: '', name: '', art: 'LINIE', farbe: null }); _orgEditRender(); _orgAnzeigen(); }
function orgStelleWeg(i) { _orgEdit.stellen.splice(i, 1); _orgEditRender(); _orgAnzeigen(); }
function orgStelleSchieben(i, d) {
    const j = i + d;
    if (j < 0 || j >= _orgEdit.stellen.length) return;
    [_orgEdit.stellen[i], _orgEdit.stellen[j]] = [_orgEdit.stellen[j], _orgEdit.stellen[i]];
    _orgEditRender(); _orgAnzeigen();
}
function orgFarbenStandard() {
    _orgEdit.stellen.forEach(s => { s.farbe = null; });
    _orgEdit.farben = {};
    _orgEditRender(); _orgAnzeigen();
}
function orgBearbeitenAbbrechen() {
    _orgEdit = null;
    const p = document.getElementById('orgEditPanel');
    if (p) { p.style.display = 'none'; p.innerHTML = ''; }
    const btn = document.getElementById('orgEditBtn');
    if (btn) btn.style.display = '';
    _orgAnzeigen();
}

async function orgSpeichern() {
    const btn = document.getElementById('orgSpeichernBtn');
    if (btn) { btn.disabled = true; btn.textContent = 'Speichere…'; }
    try {
        const r = await fetch('/api/manager-dienstplan/organigramm/stellen', {
            method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ stellen: _orgEdit.stellen, farben: _orgEdit.farben }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { alert(j.message || j.error || ('Fehler ' + r.status)); return; }
        orgBearbeitenAbbrechen();
        await orgLaden();
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
    finally { if (btn) { btn.disabled = false; btn.textContent = 'Speichern'; } }
}
