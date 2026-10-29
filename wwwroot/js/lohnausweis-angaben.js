// Lohnausweis-Angaben (Swissdec Basistest 03_05, Wegleitung 2026).
// Person: Mitarbeiter → Zulagen. Unternehmen: Filial-Stammdaten.

const LA_KANTONE = ['AG','AI','AR','BE','BL','BS','FR','GE','GL','GR','JU','LU','NE','NW','OW','SG','SH','SO','SZ','TG','TI','UR','VD','VS','ZG','ZH'];

function laEsc(s) {
    return String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

function laKantonSelect(id, wert) {
    const opts = ['<option value="">—</option>']
        .concat(LA_KANTONE.map(k => `<option value="${k}"${wert === k ? ' selected' : ''}>${k}</option>`));
    return `<select id="${id}" class="f-input">${opts.join('')}</select>`;
}

function laStil() {
    if (document.getElementById('laAngabenStil')) return;
    const s = document.createElement('style');
    s.id = 'laAngabenStil';
    s.textContent = `
        #laAngabenOverlay { padding: 28px !important; align-items: center !important; justify-content: center !important; }
        #laAngabenOverlay .la-blatt {
            width: min(1640px, calc(100vw - 96px)) !important;
            max-width: 1640px !important;
            height: auto !important;
            max-height: calc(100vh - 64px) !important;
            display: flex; flex-direction: column; overflow: hidden;
            padding: 0; border-radius: 18px; box-sizing: border-box;
        }
        #laAngabenOverlay .modal-hd { background: transparent; padding: 16px 22px 12px; border-bottom: 1px solid rgba(60,55,48,0.08); }
        #laAngabenOverlay .modal-hd-title { font-size: 18px; }
        #laAngabenOverlay .la-unter { font-size: 13px; line-height: 1.4; color: #646464; font-weight: 500; margin-top: 3px; }
        #laAngabenOverlay .modal-body { padding: 16px 22px; overflow: auto; }
        #laAngabenOverlay.la-kompakt { padding: 10px !important; }
        #laAngabenOverlay.la-kompakt .modal-hd { padding: 10px 16px 8px; }
        #laAngabenOverlay.la-kompakt .modal-body { padding: 8px 14px 10px; overflow: hidden; }
        #laAngabenOverlay.la-kompakt .modal-ft { padding: 8px 14px; }
        #laAngabenOverlay.la-kompakt .la-blatt {
            width: min(1160px, calc(100vw - 24px)) !important;
            max-width: 1160px !important;
            max-height: calc(100vh - 20px) !important; height: auto;
        }
        #laAngabenOverlay.la-kompakt .la-firma { grid-template-columns: 1fr; gap: 6px; }
        #laAngabenOverlay.la-kompakt .la-badge {
            width: 54px; min-width: 54px; flex: 0 0 54px; box-sizing: border-box;
        }
        #laAngabenOverlay.la-kompakt .la-einzeile {
            display: flex; align-items: center; justify-content: flex-start;
            gap: 10px; padding: 5px 10px;
        }
        #laAngabenOverlay.la-kompakt .la-einzeile .la-titel {
            margin: 0; width: 300px; flex: 0 0 300px; white-space: nowrap; overflow: visible;
        }
        #laAngabenOverlay.la-kompakt .la-einzeile-rest {
            display: flex; align-items: center; gap: 8px; flex: none; justify-content: flex-start;
        }
        #laAngabenOverlay.la-kompakt .la-einzeile .la-check { width: auto; margin: 0; white-space: nowrap; }
        #laAngabenOverlay.la-kompakt .la-einzeile-checks { display: flex; gap: 6px; }
        #laAngabenOverlay.la-kompakt .la-inline {
            display: flex; flex-direction: row; align-items: center; gap: 6px;
            width: auto; margin: 0; white-space: nowrap;
        }
        #laAngabenOverlay.la-kompakt .f-input,
        #laAngabenOverlay.la-kompakt input.yp-text,
        #laAngabenOverlay.la-kompakt .lqsel-btn {
            padding: 3px 8px !important; font-size: 13px !important; line-height: 1.2;
            height: 28px; min-height: 0 !important; box-sizing: border-box;
        }
        #laAngabenOverlay.la-kompakt .yp-cal-btn {
            width: 28px; height: 28px; min-height: 0; border-radius: 8px;
        }
        #laAngabenOverlay.la-kompakt .yp-field-wrap { width: auto; align-items: center; }
        #laAngabenOverlay.la-kompakt .la-pos21 { width: auto; max-width: none; }
        #laAngabenOverlay.la-kompakt .la-pos21 .f-input { width: 108px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-z14 { width: 360px; max-width: 360px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-w-org { width: 220px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-w-name { width: 168px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-w-str { width: 200px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-w-plz { width: 72px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-w-ort { width: 148px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-w-tel { width: 140px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-zeile-kurz {
            display: flex; width: auto; margin: 0; gap: 8px; align-items: center;
        }
        #laAngabenOverlay.la-kompakt .la-zeile-kurz .yp-field-wrap { width: 148px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-zeile-kurz .yp-field-wrap > input.yp-text { width: 108px; flex: none; }
        #laAngabenOverlay.la-kompakt .la-zeile-kurz .la-kanton .lqsel-wrap,
        #laAngabenOverlay.la-kompakt .la-zeile-kurz .la-kanton .lqsel-btn { width: 64px; max-width: 64px; }
        #laAngabenOverlay.la-kompakt .la-unterzeile {
            display: flex; align-items: center; justify-content: flex-start;
            gap: 10px; padding: 3px 10px 3px 74px;
        }
        #laAngabenOverlay.la-kompakt .la-unter-name {
            width: 300px; flex: 0 0 300px; font-size: 13px; font-weight: 700; color: #3f3f3f; white-space: nowrap;
        }
        #laAngabenOverlay.la-kompakt .la-neben {
            font-size: 13px; font-weight: 700; color: #3f3f3f; white-space: nowrap;
        }
        #laAngabenOverlay.la-kompakt .la-adr {
            display: flex; flex-direction: column; gap: 8px;
            padding: 2px 10px 8px 74px; align-items: flex-start;
        }
        #laAngabenOverlay.la-kompakt .la-adr-zeile {
            display: flex; gap: 12px; align-items: flex-end; justify-content: flex-start;
        }
        #laAngabenOverlay.la-kompakt .la-adr .f-label {
            display: flex; flex-direction: column; gap: 3px; margin: 0; width: max-content;
            font-size: 11px; font-weight: 650; color: #8b8b8b;
        }
        #laAngabenOverlay.la-kompakt .la-einzeile .la-korrektur { margin: 0 0 0 8px; }
        #laAngabenOverlay.la-kompakt .la-karte { padding: 4px 8px; }
        #laAngabenOverlay.la-kompakt .la-titel { font-size: 13px; }
        #laAngabenOverlay.la-kompakt .la-text { font-size: 11px; line-height: 1.25; }
        #laAngabenOverlay.la-kompakt .la-satz { margin-top: 2px; }
        #laAngabenOverlay.la-kompakt .la-feld,
        #laAngabenOverlay.la-kompakt .la-zeile,
        #laAngabenOverlay.la-kompakt .la-checks { margin-top: 4px; }
        #laAngabenOverlay.la-kompakt .la-zeile-kurz,
        #laAngabenOverlay.la-kompakt .la-unterzeile .la-checks { margin-top: 0; }
        #laAngabenOverlay.la-kompakt .la-span2 .la-checks { grid-template-columns: 1fr 1fr; }
        #laAngabenOverlay.la-kompakt .la-kontakt .la-nach { grid-template-columns: 1.2fr 1fr 0.9fr; gap: 6px; margin-top: 4px; }
        #laAngabenOverlay.la-kompakt .la-gueltig { padding: 0; gap: 8px 12px; }
        #laAngabenOverlay .modal-ft { padding: 12px 22px; background: rgba(255,255,255,0.28); }
        #laAngabenOverlay .la-raster { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 14px; align-items: start; }
        #laAngabenOverlay .la-karte {
            background: rgba(255,255,255,0.48);
            border: 1px solid rgba(255,255,255,0.78);
            border-radius: 14px;
            padding: 12px 14px 12px;
            min-width: 0;
            box-shadow: inset 0 1px 0 rgba(255,255,255,0.85), 0 4px 10px rgba(60,55,48,0.04);
        }
        #laAngabenOverlay .la-breit { grid-column: 1 / -1; }
        #laAngabenOverlay .la-gueltig {
            display: flex; flex-wrap: wrap; gap: 10px 16px; align-items: flex-end;
            padding: 2px 2px 4px;
        }
        #laAngabenOverlay .la-gueltig .f-input { width: 110px; }
        #laAngabenOverlay .la-gueltig #laGueltigBis {
            font-size: 13px; font-weight: 650; color: #3f3f3f; padding-bottom: 8px;
        }
        #laAngabenOverlay .la-gruppe {
            grid-column: 1 / -1;
            background: rgba(255,255,255,0.28);
            border: 1px solid rgba(255,255,255,0.72);
            border-radius: 16px;
            padding: 12px 14px 14px;
        }
        #laAngabenOverlay .la-gruppe-kopf { display: flex; gap: 10px; align-items: flex-start; margin-bottom: 10px; }
        #laAngabenOverlay .la-innen { display: grid; grid-template-columns: 1fr 1fr; gap: 10px; }
        #laAngabenOverlay .la-innen.drei { grid-template-columns: 1fr 1fr 1fr; }
        #laAngabenOverlay .la-teil {
            background: rgba(255,255,255,0.62);
            border: 1px solid rgba(255,255,255,0.9);
            border-radius: 12px;
            padding: 10px 12px;
            min-width: 0;
        }
        #laAngabenOverlay .la-teil h4 { margin: 0 0 2px; font-size: 13.5px; font-weight: 700; color: #3f3f3f; }
        #laAngabenOverlay .la-kopf { display: flex; gap: 8px; align-items: flex-start; }
        #laAngabenOverlay .la-badge {
            flex: 0 0 auto; min-width: 32px; height: 22px; padding: 0 6px;
            border-radius: 7px; background: #3f3f3f; color: #fff;
            font-size: 11px; font-weight: 700; letter-spacing: 0.02em;
            display: flex; align-items: center; justify-content: center;
        }
        #laAngabenOverlay .la-titel { margin: 0 0 2px; font-size: 14px; font-weight: 700; color: #3f3f3f; }
        #laAngabenOverlay .la-text { margin: 0; font-size: 12.5px; line-height: 1.4; color: #646464; }
        #laAngabenOverlay .la-satz { margin: 4px 0 0; font-size: 11px; line-height: 1.3; color: #8b8b8b; }
        #laAngabenOverlay .la-feld { margin-top: 6px; }
        #laAngabenOverlay .la-feld .lqsel-wrap { display: block; width: 100%; }
        #laAngabenOverlay .la-feld .f-input,
        #laAngabenOverlay .la-feld select,
        #laAngabenOverlay .la-feld textarea,
        #laAngabenOverlay .la-zeile .f-input,
        #laAngabenOverlay .lqsel-btn {
            width: 100%; box-sizing: border-box;
            padding: 4px 8px !important; font-size: 12.5px !important; min-height: 28px;
        }
        #laAngabenOverlay .f-label { font-size: 11px; margin-bottom: 2px; }
        #laAngabenOverlay .la-zeile {
            display: grid; grid-template-columns: minmax(0, 1fr) 52px; gap: 8px; margin-top: 8px;
        }
        #laAngabenOverlay .la-zeile > * { min-width: 0; }
        #laAngabenOverlay .la-kanton .lqsel-wrap,
        #laAngabenOverlay .la-kanton .lqsel-btn {
            width: 52px; min-width: 0; max-width: 52px; padding-left: 6px !important; padding-right: 4px !important;
        }
        #laAngabenOverlay .la-kanton .lqsel-btn { gap: 2px; }
        #laAngabenOverlay .la-kanton .lqsel-panel { min-width: 72px; }
        #laAngabenOverlay .la-checks { display: grid; grid-template-columns: 1fr 1fr 1fr 1fr; gap: 6px; margin-top: 6px; }
        #laAngabenOverlay .la-check {
            display: flex; gap: 6px; align-items: center;
            padding: 5px 8px; border-radius: 8px;
            border: 1px solid rgba(60,55,48,0.16);
            background: rgba(255,255,255,0.4);
            font-size: 12px; line-height: 1.25; color: #3f3f3f; cursor: pointer;
        }
        #laAngabenOverlay .la-check input { margin: 0; }
        #laAngabenOverlay .la-hinweis {
            margin: 0 14px 2px; padding: 7px 10px 7px 12px; border-radius: 10px;
            background: #fff4e5; border: 1px solid #e8b15a; border-left: 4px solid #c2410c;
            color: #7c2d12; font-size: 12.5px; line-height: 1.35;
        }
        #laAngabenOverlay .la-warn-titel {
            font-size: 11px; font-weight: 800; letter-spacing: .06em;
            text-transform: uppercase; color: #c2410c; margin-bottom: 1px;
        }
        #laAngabenOverlay .la-karte.la-korrigiert {
            background: #fff4e5;
            border-color: #e8b15a;
            box-shadow: inset 4px 0 0 #c2410c;
        }
        #laAngabenOverlay .la-korrektur {
            margin: 8px 0 0; font-size: 12.5px; font-weight: 650;
            color: #c2410c; line-height: 1.4;
        }
        #laAngabenOverlay .la-speichern {
            background: #3f3f3f; color: #fff; border: none; border-radius: 12px;
            padding: 7px 16px; font-size: 13px; font-weight: 600; cursor: pointer;
        }
        #laAngabenOverlay .la-nach { display: grid; grid-template-columns: 1fr 80px 1fr 1fr 1.4fr 70px 1fr; gap: 6px; margin-top: 6px; }
        @media (max-width: 1100px) {
            #laAngabenOverlay .la-raster,
            #laAngabenOverlay .la-checks,
            #laAngabenOverlay .la-innen,
            #laAngabenOverlay .la-innen.drei,
            #laAngabenOverlay .la-nach { grid-template-columns: 1fr 1fr; }
            #laAngabenOverlay .la-zeile { grid-template-columns: minmax(0, 1fr) 52px; }
        }
        body.theme-dark #laAngabenOverlay .la-karte,
        body.theme-dark #laAngabenOverlay .la-gruppe,
        body.theme-dark #laAngabenOverlay .la-teil { background: rgba(255,255,255,0.06); border-color: rgba(255,255,255,0.12); }
        body.theme-dark #laAngabenOverlay .la-teil h4 { color: #f3f0ea; }
        body.theme-dark #laAngabenOverlay .la-titel { color: #f3f0ea; }
        body.theme-dark #laAngabenOverlay .la-text,
        body.theme-dark #laAngabenOverlay .la-unter { color: #c8c2b8; }
        body.theme-dark #laAngabenOverlay .la-check { color: #f3f0ea; background: rgba(255,255,255,0.05); }
        body.theme-dark #laAngabenOverlay.la-kompakt .la-unter-name,
        body.theme-dark #laAngabenOverlay.la-kompakt .la-neben { color: #f3f0ea; }
        #laAngabenOverlay.la-kompakt .la-maske .la-einzeile,
        #laAngabenOverlay.la-kompakt .la-maske .la-unterzeile {
            display: grid; grid-template-columns: 54px 270px minmax(0, 1fr);
            column-gap: 16px; align-items: center; justify-content: stretch;
            padding: 5px 12px;
        }
        #laAngabenOverlay.la-kompakt .la-maske .la-unterzeile { padding-top: 2px; padding-bottom: 2px; }
        #laAngabenOverlay.la-kompakt .la-maske .la-titel,
        #laAngabenOverlay.la-kompakt .la-maske .la-unter-name {
            width: auto; flex: none; margin: 0; font-size: 13px; white-space: nowrap;
        }
        #laAngabenOverlay.la-kompakt .la-maske .la-werte {
            display: grid; grid-template-columns: minmax(200px, 1.25fr) minmax(190px, 1.15fr) minmax(160px, 1fr) minmax(180px, 1.05fr);
            column-gap: 16px; align-items: center; flex: none; width: 100%;
        }
        #laAngabenOverlay.la-kompakt .la-maske .la-adr {
            display: flex; flex-direction: column; gap: 8px; align-items: stretch;
            padding: 2px 12px 10px;
        }
        #laAngabenOverlay.la-kompakt .la-maske .la-adr-zeile {
            display: grid;
            grid-template-columns: 54px 270px minmax(200px, 1.25fr) minmax(190px, 1.15fr) minmax(160px, 1fr) minmax(180px, 1.05fr);
            column-gap: 16px; align-items: end; justify-content: stretch;
        }
        #laAngabenOverlay.la-kompakt .la-maske .la-karte { padding: 0; }
        #laAngabenOverlay.la-kompakt .la-maske .la-badge {
            width: 54px; min-width: 54px; max-width: 54px; flex: none;
        }
        #laAngabenOverlay.la-kompakt .la-maske .la-inline .yp-field-wrap,
        #laAngabenOverlay.la-kompakt .la-maske .la-slot .yp-field-wrap {
            width: 100%; flex: 1 1 auto; display: inline-flex; align-items: center;
        }
        #laAngabenOverlay.la-kompakt .la-maske .yp-cal-btn {
            width: 16px; height: 16px; min-height: 0; padding: 0; border-radius: 0;
            background: transparent; border: none; box-shadow: none;
        }
        #laAngabenOverlay.la-kompakt .la-maske .la-wert,
        #laAngabenOverlay.la-kompakt .la-maske input.yp-text,
        #laAngabenOverlay.la-kompakt .la-maske .lqsel-btn {
            height: 22px !important; min-height: 0 !important; padding: 0 2px !important;
            font-size: 14.5px !important; font-weight: 700 !important;
            background: transparent !important; border: none !important;
            border-radius: 0 !important; box-shadow: none !important;
            border-bottom: 1px solid rgba(60,55,48,0.28) !important;
        }
    `;
    document.head.appendChild(s);
}

function laTri(id, wert, firmaJa) {
    const v = wert === true ? 'ja' : wert === false ? 'nein' : '';
    const stand = firmaJa ? 'ja' : 'nein';
    return `<div class="la-feld"><select id="${id}" class="f-input">
            <option value=""${v === '' ? ' selected' : ''}>wie Unternehmen (${stand})</option>
            <option value="ja"${v === 'ja' ? ' selected' : ''}>Ja, für diese Person</option>
            <option value="nein"${v === 'nein' ? ' selected' : ''}>Nein, für diese Person</option>
        </select></div>`;
}

function laModus(id, wert) {
    const v = wert || '';
    return `<div class="la-feld"><select id="${id}" class="f-input" onchange="laModusZeigen()">
        <option value=""${v === '' ? ' selected' : ''}>wie Unternehmen</option>
        <option value="EIGEN"${v === 'EIGEN' ? ' selected' : ''}>eigene Angabe</option>
        <option value="KEIN"${v === 'KEIN' ? ' selected' : ''}>keine, auch wenn das Unternehmen eine hat</option>
    </select></div>`;
}

function laKarte(badge, titel, text, inhalt, breit, id) {
    const spalte = breit === 2 ? ' la-span2' : (breit ? ' la-breit' : '');
    return `<article class="la-karte${spalte}"${id ? ` id="${id}"` : ''}>
        <div class="la-kopf">
            <span class="la-badge">${badge}</span>
            <div>
                <h3 class="la-titel">${titel}</h3>
                <p class="la-text">${text}</p>
            </div>
        </div>
        ${inhalt}
    </article>`;
}

function laTriWert(id) {
    const v = document.getElementById(id)?.value;
    if (v === 'ja') return true;
    if (v === 'nein') return false;
    return null;
}

function laOverlay() {
    laStil();
    let el = document.getElementById('laAngabenOverlay');
    if (el) return el;
    el = document.createElement('div');
    el.id = 'laAngabenOverlay';
    el.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(40,36,30,0.42);z-index:3200;align-items:center;justify-content:center;overflow:hidden';
    el.innerHTML = `<div class="modal la-blatt" onclick="event.stopPropagation()">
        <div class="modal-hd" style="flex:0 0 auto">
            <div>
                <div class="modal-hd-title" id="laAngabenTitel">Lohnausweis</div>
                <div class="la-unter" id="laAngabenUnter"></div>
            </div>
            <button type="button" class="modal-x" onclick="laAngabenSchliessen()">×</button>
        </div>
        <div id="laAngabenWarn" class="la-hinweis" style="display:none"></div>
        <div class="modal-body" id="laAngabenBody" style="flex:1 1 auto;overflow:auto"></div>
        <div class="modal-ft" style="flex:0 0 auto">
            <button type="button" class="btn btn-outline" onclick="laAngabenSchliessen()">Abbrechen</button>
            <button type="button" id="laAngabenSave" class="la-speichern">Speichern</button>
        </div>
    </div>`;
    el.addEventListener('click', e => { if (e.target === el) laAngabenSchliessen(); });
    document.body.appendChild(el);
    return el;
}

function laAngabenSchliessen() {
    const el = document.getElementById('laAngabenOverlay');
    if (el) el.style.display = 'none';
}

async function laPersonKarte(employeeId) {
    const box = document.getElementById('laAngabenKarte');
    if (!box || !employeeId) return;
    box.innerHTML = '<div class="emp-placeholder"><span>Lohnausweis wird geladen…</span></div>';
    try {
        const r = await fetch(`/api/employees/${employeeId}/lohnausweis-angaben`, { headers: ah() });
        if (!r.ok) { box.innerHTML = ''; return; }
        const d = await r.json();
        const p = d.person || {};
        const art = p.art === 'RENTE' ? 'Rentenbescheinigung' : 'Lohnausweis';
        const extras = [];
        if (p.boxF != null) extras.push('F ' + (p.boxF ? 'ja' : 'nein'));
        if (p.boxG != null) extras.push('G ' + (p.boxG ? 'ja' : 'nein'));
        if (p.kinderzulageAhv) extras.push('Kinderzulagen Ausgleichskasse');
        if (p.nachgenuss) extras.push('Besoldungsnachgenuss');
        if (p.umzugBetrag) extras.push('Umzugskosten');
        box.innerHTML = `
            <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:0">
                <span>Lohnausweis</span>
                <button type="button" class="btn-emp-add" onclick="laPersonOeffnen(${employeeId})">Angaben</button>
            </div>
            <div style="font-size:13px;color:#3f3f3f;margin:0 0 14px;line-height:1.45">
                ${laEsc(art)}${extras.length ? ' · ' + laEsc(extras.join(' · ')) : ''}
                <div style="font-size:12px;color:#8b8b8b;margin-top:2px">Nicht gesetzte Punkte gelten wie beim Unternehmen${d.firmaName ? ' (' + laEsc(d.firmaName) + ')' : ''}.</div>
            </div>`;
    } catch {
        box.innerHTML = '';
    }
}

async function laPersonOeffnen(employeeId, ab) {
    const url = `/api/employees/${employeeId}/lohnausweis-angaben` + (ab ? '?ab=' + ab : '');
    const r = await fetch(url, { headers: ah() });
    if (!r.ok) { alert('Lohnausweis-Angaben konnten nicht geladen werden.'); return; }
    const d = await r.json();
    const p = d.person || {};
    p.staende = d.staende || [];
    const f = d.firma || {};
    window._laStaende = p.staende;
    window._laGeladenAb = p.gueltigAb;
    const ov = laOverlay();
    ov.classList.remove('la-kompakt');
    document.getElementById('laAngabenTitel').textContent = 'Lohnausweis der Person';
    document.getElementById('laAngabenUnter').textContent =
        'Leer heisst: wie die Filiale' + (d.firmaName ? ' ' + d.firmaName : '') + '. «Einer von …» setzt das Programm, sobald dieselbe Person im Jahr bei zwei Arbeitgebern Lohn hat.';
    document.getElementById('laAngabenBody').innerHTML = laPersonHtml(p, f);
    document.getElementById('laAngabenSave').onclick = () => laPersonSpeichern(employeeId);
    laGueltigAnbinden(jahr => laPersonOeffnen(employeeId, jahr));
    laGeschlossen(d.geschlossen);
    const spesenModus = document.getElementById('laSpesenModus');
    if (spesenModus && f.spesenDatum && f.spesenKanton) spesenModus.dataset.firma = '1';
    laPersonKorrekturHaengen();
    laModusZeigen();
    ov.style.display = 'flex';
}

function laDatum(iso) {
    if (!iso || String(iso).length < 10) return '';
    const s = String(iso);
    return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
}

function laFirmaStand(text) {
    return `<p class="la-satz">${laEsc(text)}</p>`;
}

function laGrantFelder(modusId, datumId, kantonId, datum, kanton) {
    return `<div data-la-wenn="${modusId}=EIGEN" class="la-zeile">
        <label class="f-label">Datum der Genehmigung<input type="date" id="${datumId}" class="f-input" value="${laEsc(datum || '')}"></label>
        <label class="f-label la-kanton">Kt.${laKantonSelect(kantonId, kanton)}</label>
    </div>`;
}

function laPersonHtml(p, f) {
    const firmaGrant = (datum, kanton) => datum && kanton
        ? 'Filiale: Kanton ' + kanton + ', ' + laDatum(datum)
        : 'Filiale: keine Genehmigung';
    const art = `<div class="la-feld"><select id="laArt" class="f-input">
            <option value="LOHN"${p.art !== 'RENTE' ? ' selected' : ''}>Lohnausweis (Buchstabe A)</option>
            <option value="RENTE"${p.art === 'RENTE' ? ' selected' : ''}>Rentenbescheinigung (Buchstabe B)</option>
        </select></div>`;
    return `<div class="la-raster">
        <div class="la-breit">${laGueltigLeiste({ gueltigAb: p.gueltigAb, gueltigBis: p.gueltigBis, staende: p.staende })}</div>
        ${laKarte('A/B', 'Lohn oder Rente',
            'A = Lohnausweis, B = Rentenbescheinigung. Beträge kommen aus dem Lohnlauf.',
            art)}
        ${laKarte('F', 'Unentgeltliche Beförderung',
            'Kreuz, wenn der Arbeitgeber die Fahrt Wohn–Arbeit bezahlt.',
            laTri('laBoxF', p.boxF, !!f.boxF))}
        ${laKarte('G', 'Kantine oder Lunch-Checks',
            'Kreuz, wenn der Arbeitgeber die Verpflegung bezahlt. Eigenanteil: leer.',
            laTri('laBoxG', p.boxG, !!f.boxG))}
        ${laKarte('14', 'Nicht bewertete Nebenleistungen',
            'Text in Ziffer 14, z. B. verbilligte Abgabe eines Personenwagens.',
            laFirmaStand('Filiale: ' + (f.ziffer14 || 'kein Text'))
            + laModus('laZ14Modus', p.ziffer14Modus)
            + `<div data-la-wenn="laZ14Modus=EIGEN" class="la-feld"><textarea id="laZ14Text" class="f-input" rows="2">${laEsc(p.ziffer14Text || '')}</textarea></div>`)}
        ${laKarte('15', 'Expatriate-Ruling',
            'Satz: «Expatriateruling durch Kanton … am … genehmigt».',
            laFirmaStand(firmaGrant(f.expatDatum, f.expatKanton))
            + laModus('laExpatModus', p.expatModus)
            + laGrantFelder('laExpatModus', 'laExpatDatum', 'laExpatKanton', p.expatDatum, p.expatKanton))}
        ${laKarte('2.2', 'Geschäftswagen, Kosten abklären',
            '«Privatanteil Geschäftsfahrzeug im Veranlagungsverfahren abzuklären».',
            laTri('laWagenAb', p.wagenAbklaeren, !!f.wagenAbklaeren))}
        ${laKarte('2.2', 'Geschäftswagen, 0,9 Prozent',
            '«Privatanteil wird vom Arbeitnehmer bezahlt».',
            laTri('laWagenMin', p.wagenMinimal, !!f.wagenMinimal))}
        ${laKarte('13', 'Spesenreglement genehmigt',
            '«Spesenreglement durch Kanton … am … genehmigt». Dann kein Kreuz in 13.1.1.',
            laFirmaStand(firmaGrant(f.spesenDatum, f.spesenKanton))
            + laModus('laSpesenModus', p.spesenModus)
            + laGrantFelder('laSpesenModus', 'laSpesenDatum', 'laSpesenKanton', p.spesenDatum, p.spesenKanton))}
        ${laKarte('13.1.1', 'Randziffer 52',
            'Kreuz nur ohne genehmigtes Spesenreglement.',
            laTri('laRz52', p.rz52, !!f.rz52)
            + `<div id="laRz52Korrektur" class="la-korrektur" style="display:none"></div>`,
            false, 'laRz52Karte')}
        ${laKarte('15', 'Verkehrswert der Beteiligung',
            '«Verkehrswert durch Kanton … am … genehmigt».',
            laFirmaStand(firmaGrant(f.wertDatum, f.wertKanton))
            + laModus('laWertModus', p.wertModus)
            + laGrantFelder('laWertModus', 'laWertDatum', 'laWertKanton', p.wertDatum, p.wertKanton))}
        ${laKarte('15', 'Kinderzulagen der Ausgleichskasse',
            '«Kinderzulagen im LA nicht enthalten. Auszahlung durch Ausgleichskasse».',
            `<label class="la-check"><input type="checkbox" id="laKinder"${p.kinderzulageAhv ? ' checked' : ''}> Direkt durch die Ausgleichskasse</label>`)}
        ${laKarte('15', 'Umzugskosten',
            'Nur dieses Jahr. «Umzugskosten von CHF … bezahlt».',
            `<div class="la-zeile">
                <label class="f-label">Betrag CHF<input id="laUmzugBetrag" class="f-input" type="number" step="0.05" min="0" value="${p.umzugBetrag ?? ''}"></label>
                <label class="f-label">Jahr<input id="laUmzugJahr" class="f-input" type="number" value="${p.umzugJahr ?? ''}"></label>
            </div>`)}
        ${laKarte('15', 'Beteiligung noch ohne Einkommen',
            '«Mitarbeiterbeteiligung ohne steuerbares Einkommen. Grund: …». Mehrere Gründe möglich.',
            laModus('laBetModus', p.beteiligungModus)
            + `<div data-la-wenn="laBetModus=EIGEN" class="la-checks">
                ${laCheck('laBetG', 'Gesperrte Mitarbeiteroptionen', p.beteiligungGesperrt)}
                ${laCheck('laBetN', 'Nicht börsenkotierte Optionen', p.beteiligungNichtKotiert)}
                ${laCheck('laBetA', 'Anwartschaften auf Aktien', p.beteiligungAnwartschaft)}
                ${laCheck('laBetU', 'Unechte Mitarbeiterbeteiligungen', p.beteiligungUnecht)}
            </div>`,
            true)}
        ${laKarte('4', 'Besoldungsnachgenuss',
            'Nur dieses Jahr. Betrag in Ziffer 4, Adresse in Ziffer 15. Nur wenn der Betrag nicht schon im Lohnlauf steckt.',
            `<label class="la-check"><input type="checkbox" id="laNach"${p.nachgenuss ? ' checked' : ''} onchange="laModusZeigen()"> Für diese Person erfassen</label>
            <div data-la-wenn="laNach=on" id="laNachBlock" class="la-nach">
                <label class="f-label">Betrag CHF<input id="laNachBetrag" class="f-input" type="number" step="0.05" min="0" value="${p.nachgenussBetrag ?? ''}"></label>
                <label class="f-label">Jahr<input id="laNachJahr" class="f-input" type="number" value="${p.nachgenussJahr ?? ''}"></label>
                <label class="f-label">Name<input id="laNachName" class="f-input" value="${laEsc(p.nachgenussName || '')}"></label>
                <label class="f-label">Vorname<input id="laNachVorname" class="f-input" value="${laEsc(p.nachgenussVorname || '')}"></label>
                <label class="f-label">Strasse<input id="laNachStrasse" class="f-input" value="${laEsc(p.nachgenussStrasse || '')}"></label>
                <label class="f-label">PLZ<input id="laNachPlz" class="f-input" value="${laEsc(p.nachgenussPlz || '')}"></label>
                <label class="f-label">Ort<input id="laNachOrt" class="f-input" value="${laEsc(p.nachgenussOrt || '')}"></label>
            </div>`,
            true)}
        ${laKarte('15', 'Quellensteuer',
            'Steht automatisch in Ziffer 15, sobald die Person im Jahr quellensteuerpflichtig ist. Frist: 31. März des Folgejahres.',
            '')}
        ${laKarte('I', 'Kontaktperson',
            'Für Rückfragen der Steuerverwaltung. Leer heisst: wie die Filiale. Steht unten beim Arbeitgeber.',
            laFirmaStand(f.kontaktPerson
                ? 'Filiale: ' + f.kontaktPerson + (f.kontaktTelefon ? ', ' + f.kontaktTelefon : '')
                : 'Filiale: keine Kontaktperson')
            + laModus('laKontaktModus', p.kontaktModus)
            + `<div data-la-wenn="laKontaktModus=EIGEN">${laKontaktFelder('la', p)}</div>`,
            true)}
    </div>`;
}

function laKontaktFelder(prefix, v) {
    const val = id => laEsc((v && v[id]) || '');
    return `<div class="la-nach">
        <label class="f-label">Organisation<input id="${prefix}KontaktOrg" class="f-input" value="${val('kontaktOrg')}"></label>
        <label class="f-label">Name<input id="${prefix}KontaktPerson" class="f-input" value="${val('kontaktPerson')}"></label>
        <label class="f-label">Telefon<input id="${prefix}KontaktTel" class="f-input" value="${val('kontaktTelefon')}"></label>
        <label class="f-label">Strasse<input id="${prefix}KontaktStrasse" class="f-input" value="${val('kontaktStrasse')}"></label>
        <label class="f-label">PLZ<input id="${prefix}KontaktPlz" class="f-input" value="${val('kontaktPlz')}"></label>
        <label class="f-label">Ort<input id="${prefix}KontaktOrt" class="f-input" value="${val('kontaktOrt')}"></label>
    </div>`;
}

function laCheck(id, label, on) {
    return `<label class="la-check"><input type="checkbox" id="${id}"${on ? ' checked' : ''}> ${label}</label>`;
}

function laNum(id) {
    const v = document.getElementById(id)?.value;
    if (v === undefined || v === null || String(v).trim() === '') return null;
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
}

async function laPersonSpeichern(employeeId) {
    const body = {
        gueltigAb: parseInt(document.getElementById('laGueltigAb')?.value, 10) || null,
        art: document.getElementById('laArt').value,
        boxF: laTriWert('laBoxF'),
        boxG: laTriWert('laBoxG'),
        ziffer14Modus: document.getElementById('laZ14Modus').value || null,
        ziffer14Text: document.getElementById('laZ14Text')?.value || null,
        expatModus: document.getElementById('laExpatModus').value || null,
        expatDatum: document.getElementById('laExpatDatum').value || null,
        expatKanton: document.getElementById('laExpatKanton').value || null,
        wagenAbklaeren: laTriWert('laWagenAb'),
        wagenMinimal: laTriWert('laWagenMin'),
        spesenModus: document.getElementById('laSpesenModus').value || null,
        spesenDatum: document.getElementById('laSpesenDatum').value || null,
        spesenKanton: document.getElementById('laSpesenKanton').value || null,
        rz52: laTriWert('laRz52'),
        wertModus: document.getElementById('laWertModus').value || null,
        wertDatum: document.getElementById('laWertDatum').value || null,
        wertKanton: document.getElementById('laWertKanton').value || null,
        beteiligungModus: document.getElementById('laBetModus').value || null,
        beteiligungGesperrt: !!document.getElementById('laBetG')?.checked,
        beteiligungNichtKotiert: !!document.getElementById('laBetN')?.checked,
        beteiligungAnwartschaft: !!document.getElementById('laBetA')?.checked,
        beteiligungUnecht: !!document.getElementById('laBetU')?.checked,
        kinderzulageAhv: !!document.getElementById('laKinder').checked,
        umzugBetrag: laNum('laUmzugBetrag'),
        umzugJahr: laNum('laUmzugJahr'),
        nachgenuss: !!document.getElementById('laNach').checked,
        nachgenussBetrag: laNum('laNachBetrag'),
        nachgenussJahr: laNum('laNachJahr'),
        nachgenussName: document.getElementById('laNachName').value,
        nachgenussVorname: document.getElementById('laNachVorname').value,
        nachgenussStrasse: document.getElementById('laNachStrasse').value,
        nachgenussPlz: document.getElementById('laNachPlz').value,
        nachgenussOrt: document.getElementById('laNachOrt').value,
        kontaktModus: document.getElementById('laKontaktModus').value || null,
        kontaktOrg: document.getElementById('laKontaktOrg')?.value || null,
        kontaktStrasse: document.getElementById('laKontaktStrasse')?.value || null,
        kontaktPlz: document.getElementById('laKontaktPlz')?.value || null,
        kontaktOrt: document.getElementById('laKontaktOrt')?.value || null,
        kontaktPerson: document.getElementById('laKontaktPerson')?.value || null,
        kontaktTelefon: document.getElementById('laKontaktTel')?.value || null,
    };
    const r = await fetch(`/api/employees/${employeeId}/lohnausweis-angaben`, {
        method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    if (!r.ok) {
        const err = await r.json().catch(() => ({}));
        alert(err.message || 'Speichern fehlgeschlagen.');
        return;
    }
    laAngabenSchliessen();
    laPersonKarte(employeeId);
}

function laFassungFuerJahr(payload) {
    const jahr = new Date().getFullYear();
    const liste = (payload.fassungen && payload.fassungen.length) ? payload.fassungen : [payload];
    return liste.find(x => x.gueltigAb <= jahr && (x.gueltigBis == null || x.gueltigBis >= jahr)) || liste[0];
}

async function laFirmaOeffnen(companyProfileId, ab) {
    if (!companyProfileId) return;
    const box = document.getElementById('laFilialeAnsicht-' + companyProfileId);
    if (box) return laFirmaInAnsicht(companyProfileId, ab, box);
    const url = `/api/companyprofiles/${companyProfileId}/lohnausweis-angaben` + (ab ? '?ab=' + ab : '');
    const r = await fetch(url, { headers: ah() });
    if (!r.ok) { alert('Die Unternehmensangaben konnten nicht geladen werden.'); return; }
    const f = await r.json();
    window._laStaende = f.staende || [];
    window._laGeladenAb = f.gueltigAb;
    const ov = laOverlay();
    ov.classList.add('la-kompakt');
    document.getElementById('laAngabenTitel').textContent = 'Lohnausweis für das Unternehmen';
    document.getElementById('laAngabenUnter').textContent =
        'Gilt für alle Personen dieser Filiale, bis eine Person davon abweicht.';
    document.getElementById('laAngabenBody').innerHTML = laFirmaHtml(f);
    const plzFeld = document.getElementById('laFKontaktPlz');
    const ortFeld = document.getElementById('laFKontaktOrt');
    if (plzFeld && ortFeld && !String(ortFeld.value || '').trim()) laPlzOrt(plzFeld);
    document.getElementById('laAngabenSave').onclick = () => laFirmaSpeichern(companyProfileId);
    laGueltigAnbinden(jahr => laFirmaOeffnen(companyProfileId, jahr));
    laGeschlossen(f.geschlossen);
    laFirmaHinweisHaengen();
    ov.style.display = 'flex';
}

async function laFirmaInAnsicht(companyProfileId, ab, box) {
    const url = `/api/companyprofiles/${companyProfileId}/lohnausweis-angaben` + (ab ? '?ab=' + ab : '');
    const r = await fetch(url, { headers: ah() });
    if (!r.ok) { alert('Die Unternehmensangaben konnten nicht geladen werden.'); return; }
    const f = await r.json();
    if (!ab) {
        const gezeigt = laFassungFuerJahr(f);
        if (gezeigt && gezeigt.gueltigAb && Number(gezeigt.gueltigAb) !== Number(f.gueltigAb))
            return laFirmaInAnsicht(companyProfileId, gezeigt.gueltigAb, box);
    }
    window._laStaende = f.staende || [];
    window._laGeladenAb = f.gueltigAb;
    const liste = (f.fassungen && f.fassungen.length) ? f.fassungen : [f];
    const historie = liste.filter(x => Number(x.gueltigAb) !== Number(f.gueltigAb))
        .sort((a, b) => b.gueltigAb - a.gueltigAb);
    const alt = historie.map(h => `<div class="la-hist-zeile">
        <div class="la-hist-wann">${laEsc(laFilialeZeitraum(h))}</div>
        <div class="la-hist-was">${laEsc(laFilialeKurz(h))}</div>
    </div>`).join('');
    box.innerHTML = `<div class="la-filiale-stapel">
        <section class="la-filiale">
            <div class="ov-card-h la-kopfzeile">
                <span>Lohnausweis</span>
                ${laGueltigLeiste(f)}
                <span class="la-aktionen">
                    <button type="button" class="btn btn-outline" style="font-size:12px;padding:4px 14px" onclick="laFilialeAnsichtLaden(${companyProfileId})">Abbrechen</button>
                    <button type="button" class="btn btn-primary" id="laAnsichtSave" style="font-size:12px;padding:4px 14px">Speichern</button>
                </span>
            </div>
            ${laMaske(f, true)}
        </section>
        ${historie.length ? `<section class="stm-karte-ansicht la-historie">
            <div class="ov-card-h"><span>Historie</span></div>
            ${alt}
        </section>` : ''}
    </div>`;
    const plzFeld = document.getElementById('laFKontaktPlz');
    const ortFeld = document.getElementById('laFKontaktOrt');
    if (plzFeld && ortFeld && !String(ortFeld.value || '').trim()) laPlzOrt(plzFeld);
    document.getElementById('laAnsichtSave').onclick = () => laFirmaSpeichern(companyProfileId);
    laGueltigAnbinden(jahr => laFirmaInAnsicht(companyProfileId, jahr, box));
    laGeschlossen(f.geschlossen);
    laFirmaHinweisHaengen();
}

const laKorrekturSatz = 'Korrektur: das Kreuz in Ziffer 13.1.1 entfällt, weil das Spesenreglement gilt. Noch nicht gespeichert.';

function laSpesenDerFirma() {
    return !!(document.getElementById('laFSpesenDatum')?.value
        && document.getElementById('laFSpesenKanton')?.value);
}

function laKorrekturZeichnen(aktiv) {
    const karte = document.getElementById('laRz52Karte');
    const hin = document.getElementById('laRz52Korrektur');
    if (karte) karte.classList.toggle('la-korrigiert', aktiv);
    if (hin) {
        hin.style.display = aktiv ? '' : 'none';
        hin.textContent = aktiv ? laKorrekturSatz : '';
    }
}

function laFirmaKorrektur() {
    const voll = laSpesenDerFirma();
    const box = document.getElementById('laFRz52');
    if (box) {
        if (voll && box.checked) box.checked = false;
        box.disabled = voll;
    }
    laKorrekturZeichnen(voll);
}

function laFirmaHinweisZeigen() {
    laFirmaKorrektur();
}

function laPersonSpesenWirksam() {
    const modus = document.getElementById('laSpesenModus')?.value || '';
    if (modus === 'KEIN') return false;
    if (modus === 'EIGEN') {
        return !!(document.getElementById('laSpesenDatum')?.value
            && document.getElementById('laSpesenKanton')?.value);
    }
    return document.getElementById('laSpesenModus')?.dataset.firma === '1';
}

function laPersonKorrektur() {
    const voll = laPersonSpesenWirksam();
    const sel = document.getElementById('laRz52');
    if (sel && voll && sel.value === 'ja') sel.value = 'nein';
    laKorrekturZeichnen(voll);
}

function laPersonKorrekturHaengen() {
    ['laSpesenModus', 'laSpesenDatum', 'laSpesenKanton', 'laRz52'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', laPersonKorrektur);
    });
    laPersonKorrektur();
}

function laFirmaHinweisHaengen() {
    ['laFSpesenDatum', 'laFSpesenKanton', 'laFRz52'].forEach(id => {
        document.getElementById(id)?.addEventListener('change', laFirmaHinweisZeigen);
    });
    laFirmaHinweisZeigen();
}

function laFirmaGrant(datumId, kantonId, datum, kanton, kurz) {
    const datumLabel = kurz ? 'Datum' : 'Datum der Genehmigung';
    const zeile = kurz ? ' la-zeile-kurz' : '';
    const inline = kurz ? ' la-inline' : '';
    return `<div class="la-zeile${zeile}">
        <label class="f-label${inline}">${datumLabel}<input type="date" id="${datumId}" class="f-input" value="${laEsc(datum || '')}"></label>
        <label class="f-label la-kanton${inline}">Kt.${laKantonSelect(kantonId, kanton)}</label>
    </div>`;
}

function laEinzeile(badge, titel, inhalt, breit, id) {
    const spalte = breit ? ' la-span2' : '';
    return `<article class="la-karte la-einzeile${spalte}"${id ? ` id="${id}"` : ''}>
        <span class="la-badge">${badge}</span>
        <h3 class="la-titel">${titel}</h3>
        <div class="la-werte">${inhalt}</div>
    </article>`;
}

function laTeil(titel, text, inhalt) {
    return `<div class="la-teil">
        <h4>${titel}</h4>
        <p class="la-text">${text}</p>
        ${inhalt}
    </div>`;
}

function laGruppe(badge, titel, text, innen) {
    return `<section class="la-gruppe">
        <div class="la-gruppe-kopf">
            <span class="la-badge">${badge}</span>
            <div>
                <h3 class="la-titel">${titel}</h3>
                <p class="la-text">${text}</p>
            </div>
        </div>
        ${innen}
    </section>`;
}

function laGueltigLeiste(d) {
    const ab = d.gueltigAb || 2000;
    const staende = d.staende || [];
    const bisText = d.gueltigBis ? 'bis ' + d.gueltigBis : 'bis offen';
    const opts = staende.map(s =>
        `<option value="${s.ab}"${Number(s.ab) === Number(ab) ? ' selected' : ''}>ab ${s.ab}${s.bis ? ' bis ' + s.bis : ' · offen'}</option>`).join('');
    return `<div class="la-gueltig">
        <label class="f-label">Gültig ab
            <input id="laGueltigAb" class="f-input" type="number" min="1990" max="2100" value="${ab}">
        </label>
        <span id="laGueltigBis">${bisText}</span>
        ${opts ? `<label class="f-label">Fassung<select id="laFassung" class="f-input">${opts}</select></label>` : ''}
        <p class="la-satz" style="flex:1 1 220px">Bis bleibt offen. Ein späteres Gültig-ab schliesst diese Fassung per Ende des Vorjahres.</p>
    </div>`;
}

function laGueltigAnbinden(neuLaden) {
    const inp = document.getElementById('laGueltigAb');
    const sel = document.getElementById('laFassung');
    const staende = window._laStaende || [];
    const zieh = (jahr, laden) => {
        const stand = staende.find(s => Number(s.ab) === jahr);
        const bis = document.getElementById('laGueltigBis');
        if (stand) {
            if (bis) bis.textContent = stand.bis ? 'bis ' + stand.bis : 'bis offen';
            if (laden && jahr !== window._laGeladenAb) neuLaden(jahr);
        } else if (bis && jahr) {
            bis.textContent = 'neue Fassung · bisherige gilt bis ' + (jahr - 1);
            laSaveKnopf(false);
        }
    };
    if (inp) inp.onchange = () => zieh(parseInt(inp.value, 10), false);
    if (sel) sel.onchange = () => {
        const jahr = parseInt(sel.value, 10);
        if (inp) inp.value = String(jahr);
        zieh(jahr, true);
    };
}

function laSaveKnopf(zu) {
    ['laAngabenSave', 'laAnsichtSave'].forEach(id => {
        const save = document.getElementById(id);
        if (save) save.disabled = !!zu;
    });
}

function laGeschlossen(zu) {
    laSaveKnopf(zu);
    if (!zu) return;
    document.querySelectorAll('.la-filiale input, .la-filiale select, .la-filiale textarea, #laAngabenBody input, #laAngabenBody select, #laAngabenBody textarea').forEach(el => {
        if (el.id === 'laFassung') return;
        el.disabled = true;
    });
}

function laPlzOrt(el) {
    if (!el || typeof plzLookupGeneric !== 'function') return;
    const plz = String(el.value || '').replace(/\D/g, '');
    if (plz.length < 4) return;
    plzLookupGeneric(plz, 'laFKontaktOrt', null, null, null);
}

function laWahl(id, label, on, edit) {
    const hat = on ? ' hat' : '';
    if (!edit) return `<span class="la-wahl${hat}">${laEsc(label)}</span>`;
    return `<label class="la-wahl${hat}"><input type="checkbox" id="${id}"${on ? ' checked' : ''}>${laEsc(label)}</label>`;
}

function laWert(text, cls) {
    const s = text == null ? '' : String(text).trim();
    return `<span class="la-wert${cls ? ' ' + cls : ''}${s ? ' hat' : ''}">${s ? laEsc(s) : ''}</span>`;
}

function laEingabe(id, wert, cls, edit, attrs) {
    if (!edit) return laWert(wert, cls);
    return `<input id="${id}" class="la-wert ${cls}" value="${laEsc(wert || '')}"${attrs ? ' ' + attrs : ''}>`;
}

function laDatumKt(datumId, kantonId, datum, kanton, edit) {
    const dat = edit
        ? `<input type="date" id="${datumId}" class="la-wert" value="${laEsc(datum || '')}">`
        : laWert(datum ? laDatum(datum) : '', '');
    const kt = edit
        ? `<span class="la-kanton">${laKantonSelect(kantonId, kanton)}</span>`
        : laWert(kanton || '', '');
    return `<span class="la-inline"><span class="la-mini-l">Datum</span><span class="la-slot la-w-dat">${dat}</span></span><span class="la-inline"><span class="la-mini-l">Kt.</span><span class="la-slot la-w-kt">${kt}</span></span>`;
}

function laUnter(name, inhalt) {
    return `<div class="la-unterzeile"><span></span><span class="la-unter-name">${name}</span><div class="la-werte">${inhalt}</div></div>`;
}

function laAdr(label, innen) {
    return `<label class="f-label">${label}${innen}</label>`;
}

function laMaske(f, edit) {
    const pos21 = f.pos21 == null || f.pos21 === 0 ? '' : f.pos21;
    const geld = pos21 === '' ? '' : (edit ? String(pos21) : Number(pos21).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }));
    const geldFeld = edit
        ? `<input id="laFPos21" class="la-wert la-w-geld" type="number" step="0.05" min="0" title="ESTV-Pauschale CHF 645, wenn der Arbeitgeber alles bezahlt" value="${laEsc(pos21)}">`
        : laWert(geld, 'la-w-geld');
    const hinweis = edit ? '<div id="laRz52Korrektur" class="la-korrektur" style="display:none"></div>' : '';
    return `<div class="la-maske">
        ${laEinzeile('F', 'Unentgeltliche Beförderung', laWahl('laFBoxF', 'Ziffer F ankreuzen', !!f.boxF, edit))}
        ${laEinzeile('G', 'Kantinenverpflegung / Lunch-Checks', laWahl('laFBoxG', 'Ziffer G ankreuzen', !!f.boxG, edit))}
        ${laEinzeile('2.1', 'Verpflegung, Unterkunft', `<span class="la-inline"><span class="la-mini-l">CHF / Monat</span>${geldFeld}</span>`)}
        ${laEinzeile('2.2', 'Privatanteil Geschäftsfahrzeug', laWahl('laFWagenAb', 'Privatanteil abklären', !!f.wagenAbklaeren, edit) + laWahl('laFWagenMin', 'Arbeitnehmer zahlt 0,9 %', !!f.wagenMinimal, edit))}
        ${laEinzeile('13', 'Spesenvergütungen', laDatumKt('laFSpesenDatum', 'laFSpesenKanton', f.spesenDatum, f.spesenKanton, edit))}
        ${laEinzeile('13.1.1', 'Reise, Verpflegung, Übernachtung', laWahl('laFRz52', 'Kreuz in Ziffer 13.1.1', !!f.rz52, edit) + hinweis, false, edit ? 'laRz52Karte' : '')}
        ${laEinzeile('14', 'Weitere Gehaltsnebenleistungen', laEingabe('laF14', f.ziffer14, 'la-w-art', edit))}
        <article class="la-karte">
            <div class="la-einzeile"><span class="la-badge">15</span><h3 class="la-titel">Bemerkungen</h3></div>
            ${laUnter('Expatriate', laDatumKt('laFExpatDatum', 'laFExpatKanton', f.expatDatum, f.expatKanton, edit))}
            ${laUnter('Verkehrswert', laDatumKt('laFWertDatum', 'laFWertKanton', f.wertDatum, f.wertKanton, edit))}
            ${laUnter('Beteiligung', laWahl('laFBetG', 'Gesperrte Optionen', !!f.betGesperrt, edit) + laWahl('laFBetN', 'Nicht börsenkotiert', !!f.betNichtKotiert, edit) + laWahl('laFBetA', 'Anwartschaften', !!f.betAnwartschaft, edit) + laWahl('laFBetU', 'Unechte Beteiligung', !!f.betUnecht, edit))}
        </article>
        <article class="la-karte">
            <div class="la-einzeile"><span class="la-badge">I</span><h3 class="la-titel">Bestätigung</h3></div>
            <div class="la-adr">
                <div class="la-adr-zeile">
                    <span></span>
                    ${laAdr('Organisation', laEingabe('laFKontaktOrg', f.kontaktOrg, 'la-w-org', edit))}
                    ${laAdr('Name', laEingabe('laFKontaktPerson', f.kontaktPerson, 'la-w-name', edit))}
                    ${laAdr('Telefon', laEingabe('laFKontaktTel', f.kontaktTelefon, 'la-w-tel', edit))}
                </div>
                <div class="la-adr-zeile">
                    <span></span>
                    ${laAdr('Strasse', laEingabe('laFKontaktStrasse', f.kontaktStrasse, 'la-w-str', edit))}
                    ${laAdr('PLZ', laEingabe('laFKontaktPlz', f.kontaktPlz, 'la-w-plz', edit, 'inputmode="numeric" maxlength="10" oninput="laPlzOrt(this)" onblur="laPlzOrt(this)"'))}
                    ${laAdr('Ort', laEingabe('laFKontaktOrt', f.kontaktOrt, 'la-w-ort', edit))}
                </div>
            </div>
        </article>
    </div>`;
}

function laFirmaHtml(f) {
    return `<div class="la-breit" style="margin-bottom:8px">${laGueltigLeiste(f)}</div>${laMaske(f, true)}`;
}

async function laFirmaSpeichern(companyProfileId) {
    const body = {
        gueltigAb: parseInt(document.getElementById('laGueltigAb')?.value, 10) || null,
        boxF: !!document.getElementById('laFBoxF').checked,
        boxG: !!document.getElementById('laFBoxG').checked,
        pos21: (() => {
            const v = (document.getElementById('laFPos21').value || '').trim();
            if (!v) return null;
            const n = parseFloat(v);
            return Number.isFinite(n) ? n : null;
        })(),
        ziffer14: document.getElementById('laF14').value,
        expatDatum: document.getElementById('laFExpatDatum').value || null,
        expatKanton: document.getElementById('laFExpatKanton').value || null,
        wagenAbklaeren: !!document.getElementById('laFWagenAb').checked,
        wagenMinimal: !!document.getElementById('laFWagenMin').checked,
        spesenDatum: document.getElementById('laFSpesenDatum').value || null,
        spesenKanton: document.getElementById('laFSpesenKanton').value || null,
        rz52: !!document.getElementById('laFRz52').checked,
        wertDatum: document.getElementById('laFWertDatum').value || null,
        wertKanton: document.getElementById('laFWertKanton').value || null,
        betGesperrt: !!document.getElementById('laFBetG').checked,
        betNichtKotiert: !!document.getElementById('laFBetN').checked,
        betAnwartschaft: !!document.getElementById('laFBetA').checked,
        betUnecht: !!document.getElementById('laFBetU').checked,
        kontaktOrg: document.getElementById('laFKontaktOrg').value,
        kontaktStrasse: document.getElementById('laFKontaktStrasse').value,
        kontaktPlz: document.getElementById('laFKontaktPlz').value,
        kontaktOrt: document.getElementById('laFKontaktOrt').value,
        kontaktPerson: document.getElementById('laFKontaktPerson').value,
        kontaktTelefon: document.getElementById('laFKontaktTel').value,
    };
    const r = await fetch(`/api/companyprofiles/${companyProfileId}/lohnausweis-angaben`, {
        method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' }, body: JSON.stringify(body),
    });
    if (!r.ok) {
        const err = await r.json().catch(() => ({}));
        alert(err.message || 'Speichern fehlgeschlagen.');
        return;
    }
    if (typeof laFirmaAnsichtAktualisieren === 'function')
        laFirmaAnsichtAktualisieren(companyProfileId, body.boxF, body.boxG, body.pos21);
    laAngabenSchliessen();
    laFilialeAnsichtLaden(companyProfileId);
}

function laGhostZeile(nr, name, wert) {
    const hat = !!(wert && String(wert).trim());
    return `<div class="la-ghost-row${hat ? ' hat' : ''}">
        <span class="la-ghost-nr">${nr}</span>
        <span class="la-ghost-name">${name}</span>
        <span class="la-ghost-wert">${hat ? laEsc(wert) : ''}</span>
    </div>`;
}

function laFilialeGefuellt(f) {
    const geld = v => (v != null && v !== '' && Number(v) !== 0)
        ? 'CHF ' + Number(v).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '';
    const grant = (datum, kanton) => (datum && kanton) ? 'Kanton ' + kanton + ', ' + laDatum(datum) : '';
    const wagen = [f.wagenAbklaeren ? 'Privatanteil abklären' : '', f.wagenMinimal ? 'Arbeitnehmer zahlt 0,9 %' : ''].filter(Boolean).join(' · ');
    const bet = [
        f.betGesperrt ? 'gesperrte Optionen' : '',
        f.betNichtKotiert ? 'nicht börsenkotiert' : '',
        f.betAnwartschaft ? 'Anwartschaften' : '',
        f.betUnecht ? 'unechte Beteiligung' : '',
    ].filter(Boolean).join(' · ');
    const bem = [
        grant(f.expatDatum, f.expatKanton) ? 'Expatriate ' + grant(f.expatDatum, f.expatKanton) : '',
        grant(f.wertDatum, f.wertKanton) ? 'Verkehrswert ' + grant(f.wertDatum, f.wertKanton) : '',
        bet,
    ].filter(Boolean).join(' · ');
    const kontakt = [f.kontaktPerson, f.kontaktTelefon, f.kontaktOrg,
        [f.kontaktStrasse, [f.kontaktPlz, f.kontaktOrt].filter(Boolean).join(' ')].filter(Boolean).join(', ')
    ].filter(Boolean).join(' · ');
    return { geld: geld(f.pos21), wagen, bem, kontakt, grant13: grant(f.spesenDatum, f.spesenKanton), z14: f.ziffer14 || '' };
}

function laFilialeZeitraum(f) {
    if (!f.gueltigAb) return '';
    return f.gueltigBis ? f.gueltigAb + '–' + f.gueltigBis : 'seit ' + f.gueltigAb;
}

function laAnsichtMaske(f) {
    return laMaske(f, false);
}

function laFilialeBlatt(f, id, mitKnopf) {
    const zeit = laFilialeZeitraum(f);
    return `<section class="la-filiale">
        <div class="ov-card-h" style="margin:0 2px 8px">
            <span>Lohnausweis${zeit ? ' <span class="ov-count">' + laEsc(zeit) + '</span>' : ''}</span>
            ${mitKnopf ? `<button type="button" class="btn btn-primary" style="font-size:12px;padding:4px 14px" onclick="laFirmaVonFiliale(${id})">Bearbeiten</button>` : ''}
        </div>
        ${laAnsichtMaske(f)}
    </section>`;
}

function laFilialeKurz(f) {
    const g = laFilialeGefuellt(f);
    const teile = [
        f.boxF ? 'F' : '',
        f.boxG ? 'G' : '',
        g.geld ? '2.1 ' + g.geld : '',
        g.wagen ? '2.2 ' + g.wagen : '',
        g.grant13 ? '13 ' + g.grant13 : '',
        f.rz52 ? '13.1.1' : '',
        g.z14 ? '14 ' + g.z14 : '',
        g.bem ? '15 ' + g.bem : '',
        g.kontakt ? 'I ' + g.kontakt : '',
    ].filter(Boolean);
    return teile.length ? teile.join(' · ') : 'keine Einträge';
}

function laFilialeAnsichtHtml(f, id) {
    const jahr = new Date().getFullYear();
    const liste = (f.fassungen && f.fassungen.length) ? f.fassungen : [f];
    const aktuell = liste.find(x => x.gueltigAb <= jahr && (x.gueltigBis == null || x.gueltigBis >= jahr)) || liste[0];
    const historie = liste.filter(x => x.gueltigAb !== aktuell.gueltigAb)
        .sort((a, b) => b.gueltigAb - a.gueltigAb);
    const alt = historie.map(h => `<div class="la-hist-zeile">
        <div class="la-hist-wann">${laEsc(laFilialeZeitraum(h))}</div>
        <div class="la-hist-was">${laEsc(laFilialeKurz(h))}</div>
    </div>`).join('');
    return `<div class="la-filiale-stapel">
        ${laFilialeBlatt(aktuell, id, true)}
        ${historie.length ? `<section class="stm-karte-ansicht la-historie">
            <div class="ov-card-h"><span>Historie</span></div>
            ${alt}
        </section>` : ''}
    </div>`;
}

async function laFilialeAnsichtLaden(companyProfileId) {
    const box = document.getElementById('laFilialeAnsicht-' + companyProfileId);
    if (!box) return;
    box.innerHTML = '<div style="font-size:12px;color:#8b8b8b;padding:8px">Wird geladen…</div>';
    try {
        const r = await fetch(`/api/companyprofiles/${companyProfileId}/lohnausweis-angaben`, { headers: ah() });
        if (!r.ok) { box.innerHTML = '<div style="color:#b91c1c;padding:8px">Lohnausweis konnte nicht geladen werden.</div>'; return; }
        box.innerHTML = laFilialeAnsichtHtml(await r.json(), companyProfileId);
    } catch {
        box.innerHTML = '<div style="color:#b91c1c;padding:8px">Lohnausweis konnte nicht geladen werden.</div>';
    }
}

// Blöcke mit data-la-wenn ein- und ausblenden (Select-Wert oder Häkchen).
function laModusZeigen() {
    document.querySelectorAll('[data-la-wenn]').forEach(n => {
        const spec = n.getAttribute('data-la-wenn');
        const eq = spec.indexOf('=');
        const id = spec.slice(0, eq);
        const soll = spec.slice(eq + 1);
        const el = document.getElementById(id);
        let wert = '';
        if (el?.type === 'checkbox') wert = el.checked ? 'on' : '';
        else wert = el?.value || '';
        n.style.display = wert === soll ? '' : 'none';
    });
}
