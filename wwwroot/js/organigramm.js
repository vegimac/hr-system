// ══════════════════════════════════════════════════════════════════════
//  ORGANIGRAMM (Walter 13.10.2026) — HR-Bereich → Auswertungen / Reporting.
//  Obere Stellen von Hand (Lizenznehmer, Supervisor, HR …), darunter die
//  Filialen mit GF und Managern — gleiche Auswahl wie der Manager-Dienstplan
//  (FIX-M mit laufendem Vertrag im Monat). Farben pro Stufe: jede obere
//  Stelle hat ihre eigene, dazu je eine für «Filiale / GF» und «Manager».
// ══════════════════════════════════════════════════════════════════════
let _orgData = null;
let _orgJahr = null, _orgMonat = null;
let _orgEdit = null;   // { stellen:[{titel,name,beschreibung,art,farbe}], farben:{FILIALE,MANAGER} } während der Bearbeitung

const ORG_MONATE = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
const ORG_STANDARD = { LINIE: '#3f3f3f', STAB: '#ffffff', FILIALE: '#e7e4db', MANAGER: '#ffffff' };
const _orgGlas = 'background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:6px 14px;font-size:13px;font-weight:600;cursor:pointer;color:#3f3f3f';
const _orgKohle = 'background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:7px 14px;font-size:13px;font-weight:600;cursor:pointer';
const _orgInp = 'background:#fff;border:1px solid rgba(255,255,255,0.95);border-radius:10px;padding:6px 10px;font-size:13px;color:#3f3f3f;box-shadow:0 2px 6px rgba(60,55,48,0.13),inset 0 1px 0 rgba(255,255,255,0.9);min-width:0';

// Darstellung: «neutral» = freigegebene Vorlage (Standard), «farbig» = Farben pro Stufe.
let _orgModus = (() => { try { return localStorage.getItem('orgModus') === 'farbig' ? 'farbig' : 'neutral'; } catch (_) { return 'neutral'; } })();

const ORG_LINIE = 'rgba(60,55,48,0.28)';
const ORG_CSS = `
.dpo{position:relative;display:flex;flex-direction:column;align-items:center;min-width:max-content;font-family:inherit}
.dpo-v{width:var(--lw);background:var(--lc)}
.dpo-box{position:relative;text-align:left}
.dpo-stab-row{display:grid;grid-template-columns:1fr var(--lw) 1fr;align-self:stretch}
.dpo-stab-row>.dpo-linie-mitte{background:var(--lc)}
.dpo-stab-links,.dpo-stab-rechts{display:flex;flex-direction:column}
.dpo-stab-links{align-items:flex-end;padding-right:var(--sh)}
.dpo-stab-rechts{align-items:flex-start;padding-left:var(--sh)}
.dpo-kette{display:flex;flex-direction:column;align-items:flex-start}
.dpo-stab-links .dpo-kette{align-items:flex-end}
.dpo-an-l::before,.dpo-an-r::before{content:'';position:absolute;top:50%;width:var(--sh);border-top:var(--lw) dashed var(--lc)}
.dpo-an-l::before{right:100%}
.dpo-an-r::before{left:100%}
.dpo-unter{margin-top:var(--ug)}
.dpo-stab-rechts .dpo-unter{margin-left:var(--ue);padding-left:var(--uw)}
.dpo-stab-links .dpo-unter{margin-right:var(--ue);padding-right:var(--uw)}
.dpo-u-r::before,.dpo-u-l::before{content:'';position:absolute;width:var(--uw);top:calc(-1 * var(--ug));bottom:50%;border-bottom:var(--lw) dashed var(--lc)}
.dpo-u-r::before{right:100%;border-left:var(--lw) dashed var(--lc)}
.dpo-u-l::before{left:100%;border-right:var(--lw) dashed var(--lc)}
.dpo-leer{font-size:12.5px;color:#8b8b8b;border:1px dashed rgba(60,55,48,0.3);border-radius:12px;padding:10px 18px;background:rgba(255,255,255,0.6)}
.dpo-fil-row{display:flex;justify-content:center;align-items:flex-start}
.dpo-col{position:relative;display:flex;flex-direction:column;align-items:center}

/* ── Neutral = Vorlage ── */
.dpo.n{--lc:#c4c4c4;--lw:1px;--sh:34px;--ug:14px;--ue:24px;--uw:30px;padding:34px 34px 34px;color:#1c1c1c;font-family:-apple-system,BlinkMacSystemFont,'Inter','Segoe UI',Roboto,sans-serif}
.dpo.n .dpo-logo{position:absolute;left:12px;top:8px;width:118px;height:92px;overflow:hidden}
.dpo.n .dpo-logo img{height:92px;width:auto;display:block}
.dpo.n .dpo-v1{height:20px}
.dpo.n .dpo-v2{height:18px}
.dpo.n .dpo-box{border-radius:6px;padding:11px 22px 11px 42px}
.dpo.n .dpo-txt{border-left:1px solid #c8c8c8;padding:1px 0 1px 12px}
.dpo.n .dpo-titel{font-size:7.5px;letter-spacing:.24em;text-transform:uppercase;font-weight:500}
.dpo.n .dpo-name{font-weight:600;line-height:1.15;margin-top:2px}
.dpo.n .dpo-beschr{font-size:8.5px;margin-top:4px}
.dpo.n .dpo-lin{min-width:225px;background:linear-gradient(180deg,#2c2c2c 0%,#1f1f1f 100%);color:#fff;box-shadow:0 2px 4px rgba(0,0,0,0.10),0 10px 26px rgba(0,0,0,0.16)}
.dpo.n .dpo-lin .dpo-txt{border-left-color:rgba(255,255,255,0.38)}
.dpo.n .dpo-lin .dpo-titel,.dpo.n .dpo-lin .dpo-beschr{color:rgba(255,255,255,0.86)}
.dpo.n .dpo-lin .dpo-name{font-size:17px}
.dpo.n .dpo-stb{min-width:196px;background:#eeeeee;box-shadow:0 1px 2px rgba(0,0,0,0.05),0 6px 18px rgba(0,0,0,0.05)}
.dpo.n .dpo-unter .dpo-stb{min-width:178px}
.dpo.n .dpo-stb .dpo-titel{color:#333}
.dpo.n .dpo-stb .dpo-name{font-size:14.5px;color:#1c1c1c}
.dpo.n .dpo-stb .dpo-beschr{color:#444}
.dpo.n .dpo-fil-row{margin-top:0}
.dpo.n .dpo-col{padding:18px 8px 0}
.dpo.n .dpo-col::before{content:'';position:absolute;top:0;left:0;right:0;border-top:var(--lw) solid var(--lc)}
.dpo.n .dpo-col:first-child::before{left:50%}
.dpo.n .dpo-col:last-child::before{right:50%}
.dpo.n .dpo-col:only-child::before{display:none}
.dpo.n .dpo-col::after{content:'';position:absolute;top:0;left:50%;height:18px;border-left:var(--lw) solid var(--lc)}
.dpo.n .dpo-karte{width:150px;box-sizing:border-box;background:#f8f8f8;border-radius:6px;padding:14px 11px 12px;box-shadow:0 1px 2px rgba(0,0,0,0.06),0 4px 14px rgba(0,0,0,0.06)}
.dpo.n .dpo-k-name{font-size:14.5px;font-weight:700;color:#1a1a1a;padding-left:3px}
.dpo.n .dpo-k-gf{margin:8px 0 0 16px;border-left:1px solid #c4c4c4;padding:1px 0 1px 11px}
.dpo.n .dpo-k-gf small{display:block;font-size:8.5px;color:#555;margin-top:3px;line-height:1.35}
.dpo.n .dpo-k-gf b{display:block;font-size:12.5px;font-weight:700;color:#1a1a1a;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.dpo.n .dpo-k-gf b.offen{font-style:italic;font-weight:500;color:#777}
.dpo.n .dpo-k-liste{display:flex;flex-direction:column;gap:6px;margin-top:14px}
.dpo.n .dpo-k-p{background:#efefef;border:1px solid #e7e7e7;border-radius:4px;padding:6px 11px;font-size:12.5px;font-weight:600;color:#1a1a1a}

/* ── Farbig ── */
.dpo.f{--lc:${ORG_LINIE};--lw:2px;--sh:30px;--ug:12px;--ue:24px;--uw:36px;padding:16px 28px 30px;color:#3f3f3f;letter-spacing:-.005em}
.dpo.f .dpo-v{height:22px;border-radius:2px}
.dpo.f .dpo-stab-links,.dpo.f .dpo-stab-rechts{padding-top:12px;padding-bottom:12px;gap:12px}
.dpo.f .dpo-box{border-radius:18px;padding:11px 22px;min-width:200px;border:1px solid rgba(60,55,48,0.10);transition:transform .15s ease,box-shadow .15s ease}
.dpo.f .dpo-box:hover,.dpo.f .dpo-team:hover{transform:translateY(-2px)}
.dpo.f .dpo-titel{font-size:10px;letter-spacing:.09em;text-transform:uppercase;font-weight:700;opacity:.72}
.dpo.f .dpo-name{font-size:15.5px;font-weight:700;line-height:1.2}
.dpo.f .dpo-beschr{font-size:11px;opacity:.8;margin-top:2px}
.dpo.f .dpo-col{padding:22px 8px 0}
.dpo.f .dpo-col::before{content:'';position:absolute;top:0;left:0;right:0;border-top:2px solid var(--lc)}
.dpo.f .dpo-col::after{content:'';position:absolute;top:0;left:calc(50% - 1px);height:22px;border-left:2px solid var(--lc)}
.dpo.f .dpo-col:first-child::before{left:calc(50% - 1px);height:22px;border-left:2px solid var(--lc);border-top-left-radius:16px}
.dpo.f .dpo-col:last-child::before{right:calc(50% - 1px);height:22px;border-right:2px solid var(--lc);border-top-right-radius:16px}
.dpo.f .dpo-col:first-child::after,.dpo.f .dpo-col:last-child::after{display:none}
.dpo.f .dpo-col:only-child::before{display:none}
.dpo.f .dpo-col:only-child::after{display:block}
.dpo.f .dpo-team{width:168px;border-radius:18px;overflow:hidden;background:rgba(255,255,255,0.92);border:1px solid rgba(60,55,48,0.10);transition:transform .15s ease,box-shadow .15s ease}
.dpo.f .dpo-team-kopf{padding:10px 12px 11px}
.dpo.f .dpo-team-zeile{display:flex;align-items:center;justify-content:space-between;gap:6px}
.dpo.f .dpo-team-name{font-size:14px;font-weight:700;line-height:1.2}
.dpo.f .dpo-zahl{font-size:10.5px;font-weight:700;border-radius:999px;padding:2px 7px;flex:none}
.dpo.f .dpo-gf{margin-top:8px;min-width:0}
.dpo.f .dpo-gf small{display:block;font-size:10px;font-weight:500;opacity:.8;margin-top:2px;line-height:1.35}
.dpo.f .dpo-gf b{display:block;font-size:13px;font-weight:650;line-height:1.2;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.dpo.f .dpo-offen b{font-style:italic;opacity:.75}
.dpo.f .dpo-team-liste{padding:8px 8px 9px;display:flex;flex-direction:column;gap:4px}
.dpo.f .dpo-person{border-radius:10px;padding:5px 10px;font-size:12.5px;font-weight:550}
.dpo.f .dpo-team-leer{font-size:11.5px;color:#a3a3a3;padding:2px 6px;font-style:italic}

.org-farbe{-webkit-appearance:none;appearance:none;width:34px;height:30px;padding:2px;border:1px solid rgba(255,255,255,0.95);border-radius:9px;background:#fff;cursor:pointer;box-shadow:0 2px 6px rgba(60,55,48,0.13);flex:none}
.org-farbe::-webkit-color-swatch-wrapper{padding:0}
.org-farbe::-webkit-color-swatch{border:1px solid rgba(60,55,48,0.15);border-radius:6px}
.org-farbe::-moz-color-swatch{border:1px solid rgba(60,55,48,0.15);border-radius:6px}
`;

function _orgRgb(hex) {
    const m = /^#([0-9a-f]{6})$/i.exec(hex || '');
    const n = m ? parseInt(m[1], 16) : 0x3f3f3f;
    return [n >> 16, (n >> 8) & 255, n & 255];
}
// Dunkler Hintergrund ⇒ weisse Schrift, heller ⇒ Kohle.
function _orgHell(hex) { const [r, g, b] = _orgRgb(hex); return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.6; }
function _orgSchrift(hex) { return _orgHell(hex) ? '#3f3f3f' : '#ffffff'; }
function _orgMisch(hex, weiss) {
    const [r, g, b] = _orgRgb(hex);
    const f = c => Math.round(c + (255 - c) * weiss);
    return `rgb(${f(r)},${f(g)},${f(b)})`;
}
function _orgRgba(hex, a) { const [r, g, b] = _orgRgb(hex); return `rgba(${r},${g},${b},${a})`; }
// Fläche: Verlauf von leicht aufgehellt zur Farbe, Schatten in der eigenen Farbe.
function _orgFlaeche(hex) {
    return `background:linear-gradient(135deg,${_orgMisch(hex, 0.22)} 0%,${hex} 75%);color:${_orgSchrift(hex)};box-shadow:0 1px 2px rgba(60,55,48,0.10),0 10px 24px ${_orgRgba(_orgHell(hex) ? '#3c3730' : hex, _orgHell(hex) ? 0.12 : 0.30)}`;
}
function _orgStelleFarbe(s) { return s.farbe || (_orgIstStab(s) ? ORG_STANDARD.STAB : ORG_STANDARD.LINIE); }
function _orgStufenFarbe(farben, stufe) { return (farben && farben[stufe]) || ORG_STANDARD[stufe]; }

function _orgNamen(filialen) {
    const anzahl = {};
    filialen.forEach(f => f.personen.forEach(p => { const k = (p.vorname || '').trim().toLowerCase(); anzahl[k] = (anzahl[k] || 0) + 1; }));
    return p => {
        if (p.kurzname) return p.kurzname;   // Kurzname aus dem MA-Stamm hat Vorrang
        const v = (p.vorname || '').trim();
        return anzahl[v.toLowerCase()] > 1 && p.nachname ? `${v} ${p.nachname.trim()[0]}.` : v;
    };
}

function _orgIstStab(s) { return s.art === 'STAB' || s.art === 'STAB_R'; }

function _orgHtml(d, modus) {
    const farbig = (modus || _orgModus) === 'farbig';
    const box = (s, zusatz) => {
        const art = _orgIstStab(s) ? 'dpo-stb' : 'dpo-lin';
        const stil = farbig ? ` style="${_orgFlaeche(_orgStelleFarbe(s))}"` : '';
        return `<div class="dpo-box ${art} ${zusatz || ''}"${stil}><div class="dpo-txt">${s.titel ? `<div class="dpo-titel">${esc(s.titel)}</div>` : ''}<div class="dpo-name">${esc(s.name)}</div>${s.beschreibung ? `<div class="dpo-beschr">${esc(s.beschreibung)}</div>` : ''}</div></div>`;
    };
    // Mehrere Stab-Stellen auf derselben Seite: die erste hängt an der Hauptlinie, jede weitere gestrichelt an der darüber.
    const kette = (items, i, seite) => {
        if (i >= items.length) return '';
        const cls = i === 0 ? (seite === 'r' ? 'dpo-an-l' : 'dpo-an-r') : (seite === 'r' ? 'dpo-u-r' : 'dpo-u-l');
        return `<div class="dpo-kette">${box(items[i], cls)}${i + 1 < items.length ? `<div class="dpo-unter">${kette(items, i + 1, seite)}</div>` : ''}</div>`;
    };
    const name = _orgNamen(d.filialen);
    const voll = p => `${p.vorname || ''} ${p.nachname || ''}`.trim();
    const gfBez = d.gfBezeichnung == null ? 'Geschäftsführung' : d.gfBezeichnung;
    const gfLabel = gfBez ? `<small>${esc(gfBez)}</small>` : '';
    // Stab-Stellen hängen an der Linie unter der Linien-Stelle, die in der Liste davor steht.
    const gruppen = [];
    d.stellen.forEach(s => {
        if (!_orgIstStab(s) || !gruppen.length) gruppen.push({ linie: _orgIstStab(s) ? null : s, stab: [] });
        if (_orgIstStab(s)) gruppen[gruppen.length - 1].stab.push(s);
    });
    let html = `<div class="dpo ${farbig ? 'f' : 'n'}">`;
    if (!farbig) html += `<div class="dpo-logo"><img src="${location.origin}/img/onecrew-logo.webp" alt="OneCrew"></div>`;
    if (!gruppen.length)
        html += '<div class="dpo-leer">Obere Stellen (Lizenznehmer, Supervisor, HR …) noch nicht erfasst</div><div class="dpo-v dpo-v2"></div>';
    gruppen.forEach(g => {
        if (g.linie) html += box(g.linie) + `<div class="dpo-v ${g.stab.length ? 'dpo-v1' : 'dpo-v2'}"></div>`;
        if (!g.stab.length) return;
        const links = g.stab.filter(s => s.art !== 'STAB_R');
        const rechts = g.stab.filter(s => s.art === 'STAB_R');
        html += `<div class="dpo-stab-row">
            <div class="dpo-stab-links">${kette(links, 0, 'l')}</div>
            <div class="dpo-linie-mitte"></div>
            <div class="dpo-stab-rechts">${kette(rechts, 0, 'r')}</div>
        </div><div class="dpo-v dpo-v2"></div>`;
    });
    if (!d.filialen.length) html += '<div class="dpo-leer">Keine FIX-M-Verträge in diesem Monat</div>';

    if (!farbig) {
        html += '<div class="dpo-fil-row">' + d.filialen.map(f => {
            const gf = f.personen.filter(p => p.istGf);
            const mgr = f.personen.filter(p => !p.istGf);
            const gfHtml = gf.length
                ? gf.map(p => `<div class="dpo-k-gf" title="${esc(voll(p))}"><b>${esc(name(p))}</b>${gfLabel}</div>`).join('')
                : `<div class="dpo-k-gf"><b class="offen">GF offen</b>${gfLabel}</div>`;
            return `<div class="dpo-col"><div class="dpo-karte">
                <div class="dpo-k-name">${esc(f.name)}</div>${gfHtml}
                ${mgr.length ? `<div class="dpo-k-liste">${mgr.map(p => `<div class="dpo-k-p" title="${esc(voll(p))}">${esc(name(p))}</div>`).join('')}</div>` : ''}
            </div></div>`;
        }).join('') + '</div></div>';
        return html;
    }

    const filHex = _orgStufenFarbe(d.farben, 'FILIALE');
    const mgrHex = _orgStufenFarbe(d.farben, 'MANAGER');
    const mgrZeile = _orgHell(mgrHex) ? `background:rgba(60,55,48,0.04);color:#3f3f3f` : `background:${mgrHex};color:${_orgSchrift(mgrHex)}`;
    const zahlStil = _orgHell(filHex) ? 'background:rgba(60,55,48,0.10)' : 'background:rgba(255,255,255,0.22)';
    html += '<div class="dpo-fil-row">' + d.filialen.map(f => {
        const gf = f.personen.filter(p => p.istGf);
        const mgr = f.personen.filter(p => !p.istGf);
        const gfHtml = gf.length
            ? gf.map(p => `<div class="dpo-gf" title="${esc(voll(p))} — Geschäftsführer/in"><b>${esc(name(p))}</b>${gfLabel}</div>`).join('')
            : `<div class="dpo-gf dpo-offen"><b>GF offen</b>${gfLabel}</div>`;
        return `<div class="dpo-col">
            <div class="dpo-team" style="box-shadow:0 1px 2px rgba(60,55,48,0.08),0 12px 28px ${_orgRgba(_orgHell(filHex) ? '#3c3730' : filHex, _orgHell(filHex) ? 0.12 : 0.22)}">
                <div class="dpo-team-kopf" style="background:linear-gradient(135deg,${_orgMisch(filHex, 0.22)} 0%,${filHex} 80%);color:${_orgSchrift(filHex)}">
                    <div class="dpo-team-zeile"><span class="dpo-team-name">${esc(f.name)}</span><span class="dpo-zahl" style="${zahlStil}" title="Manager">${mgr.length}</span></div>
                    ${gfHtml}
                </div>
                <div class="dpo-team-liste">${mgr.length
                    ? mgr.map(p => `<div class="dpo-person" style="${mgrZeile}" title="${esc(voll(p))}">${esc(name(p))}</div>`).join('')
                    : '<div class="dpo-team-leer">keine Manager</div>'}</div>
            </div>
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
                <div style="display:inline-flex;background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:2px">
                    <button id="orgModusNeutral" onclick="orgModusSetzen('neutral')" style="border:none;border-radius:10px;padding:5px 12px;font-size:13px;font-weight:600;cursor:pointer">Neutral</button>
                    <button id="orgModusFarbig" onclick="orgModusSetzen('farbig')" style="border:none;border-radius:10px;padding:5px 12px;font-size:13px;font-weight:600;cursor:pointer">Farbig</button>
                </div>
                ${_orgDarfBearbeiten() ? `<button id="orgEditBtn" onclick="orgBearbeiten()" style="${_orgGlas}">✎ Stellen &amp; Farben</button>` : ''}
                <button onclick="orgDrucken()" style="${_orgKohle}">🖨 Drucken</button>
                <button onclick="orgSchliessen()" style="${_orgGlas};padding:4px 10px">✕</button>
            </div>
            <div style="flex:1;min-height:0;display:flex;gap:12px;padding:0 10px 10px">
                <div id="orgBody" style="flex:1;min-width:0;overflow:auto;border-radius:14px"><div style="color:#8b8b8b;padding:20px;font-size:12.5px">Wird geladen…</div></div>
                <div id="orgEditPanel" style="display:none;width:420px;flex:none;overflow-y:auto;background:linear-gradient(165deg,#eeece4 0%,#e7e4db 50%,#dfdcd1 100%);border-radius:14px;padding:14px 16px"></div>
            </div>
        </div>`;
    ov.onclick = (e) => { if (e.target === ov) orgSchliessen(); };
    document.body.appendChild(ov);
    _orgAnzeigen();
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

function orgModusSetzen(m) {
    _orgModus = m === 'farbig' ? 'farbig' : 'neutral';
    try { localStorage.setItem('orgModus', _orgModus); } catch (_) {}
    _orgAnzeigen();
}

function _orgAnzeigen() {
    const el = document.getElementById('orgBody');
    [['orgModusNeutral', 'neutral'], ['orgModusFarbig', 'farbig']].forEach(([id, m]) => {
        const b = document.getElementById(id);
        if (b) { b.style.background = _orgModus === m ? '#3f3f3f' : 'transparent'; b.style.color = _orgModus === m ? '#fff' : '#3f3f3f'; }
    });
    if (!el) return;
    el.style.background = _orgModus === 'farbig'
        ? '#f6f3ee radial-gradient(rgba(60,55,48,0.10) 1px,transparent 1.2px) 0 0/18px 18px'
        : '#f7f7f7';
    if (!_orgData) return;
    el.innerHTML = _orgHtml(_orgEdit ? { ..._orgData, stellen: _orgEdit.stellen, farben: _orgEdit.farben, gfBezeichnung: _orgEdit.gfBezeichnung } : _orgData);
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
    const daten = _orgEdit ? { ..._orgData, stellen: _orgEdit.stellen, farben: _orgEdit.farben, gfBezeichnung: _orgEdit.gfBezeichnung } : _orgData;
    const farbig = _orgModus === 'farbig';
    const w = window.open('', '_blank');
    if (!w) { alert('Bitte Pop-ups für OneCrew erlauben, damit gedruckt werden kann.'); return; }
    // @page margin 0 ⇒ der Browser druckt keine Kopf-/Fusszeile (Datum, about:blank); Rand kommt vom body.
    w.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Organigramm ${ORG_MONATE[daten.month - 1]} ${daten.year}</title>
        <style>@page{size:A4 landscape;margin:0}html,body{margin:0}body{padding:10mm;background:${farbig ? '#fff' : '#f7f7f7'};font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        h1{font-size:13px;font-weight:600;color:#8b8b8b;margin:0 0 6px;text-align:right}#o{width:277mm;height:${farbig ? 180 : 190}mm;overflow:hidden}${ORG_CSS}</style></head>
        <body>${farbig ? `<h1>Organigramm · Stand ${ORG_MONATE[daten.month - 1]} ${daten.year}</h1>` : ''}<div id="o">${_orgHtml(daten, _orgModus)}</div>
        <script>window.onload=function(){var o=document.getElementById('o'),b=o.firstElementChild,s=Math.min(1,o.clientWidth/b.scrollWidth,o.clientHeight/b.scrollHeight);if(s<1){b.style.zoom=s;}setTimeout(function(){window.print();},150);};<\/script></body></html>`);
    w.document.close();
}

// ── Stellen & Farben bearbeiten (Seitenleiste mit Live-Vorschau) ─────────
function orgBearbeiten() {
    if (!_orgData) return;
    const stellen = (_orgData.stellen || []).map(s => ({ titel: s.titel, name: s.name, beschreibung: s.beschreibung || '', art: s.art, farbe: s.farbe || null }));
    _orgEdit = {
        stellen: stellen.length ? stellen : [
            { titel: 'Lizenznehmer', name: '', beschreibung: '', art: 'LINIE', farbe: null },
            { titel: 'Supervisor', name: '', beschreibung: '', art: 'LINIE', farbe: null },
            { titel: 'HR', name: '', beschreibung: '', art: 'STAB', farbe: null },
        ],
        farben: { ...(_orgData.farben || {}) },
        gfBezeichnung: _orgData.gfBezeichnung == null ? 'Geschäftsführung' : _orgData.gfBezeichnung,
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
        <div style="font-size:14px;font-weight:700;color:#3f3f3f;margin:18px 0 2px">Text unter dem GF-Namen</div>
        <div style="font-size:12px;color:#646464;margin-bottom:8px">Gilt für alle Filialen. Leer lassen = Zeile weglassen.</div>
        <input id="orgGfBez" value="${esc(_orgEdit.gfBezeichnung)}" placeholder="leer = keine Zeile" maxlength="160" oninput="_orgEdit.gfBezeichnung=this.value;_orgAnzeigen()" style="${_orgInp};width:100%;box-sizing:border-box">
        <div style="font-size:14px;font-weight:700;color:#3f3f3f;margin:18px 0 2px">Farben der Stufen darunter</div>
        <div style="font-size:12px;color:#646464;margin-bottom:8px">Die Farben gelten für die Darstellung «Farbig».</div>
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
            <input value="${esc(s.beschreibung || '')}" placeholder="Beschreibung, z.B. Menschen · Entwicklung · Kultur" maxlength="160" oninput="_orgEdit.stellen[${i}].beschreibung=this.value;_orgAnzeigen()" style="${_orgInp};grid-column:1/-1">
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

function orgStelleDazu() { _orgEdit.stellen.push({ titel: '', name: '', beschreibung: '', art: 'LINIE', farbe: null }); _orgEditRender(); _orgAnzeigen(); }
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
            body: JSON.stringify({ stellen: _orgEdit.stellen, farben: _orgEdit.farben, gfBezeichnung: _orgEdit.gfBezeichnung }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { alert(j.message || j.error || ('Fehler ' + r.status)); return; }
        orgBearbeitenAbbrechen();
        await orgLaden();
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
    finally { if (btn) { btn.disabled = false; btn.textContent = 'Speichern'; } }
}
