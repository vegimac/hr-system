// ═══════════════════════════════════════════════════════════════════════════
//  ARBEITSMUSTER im McAdmin (Walter 15.10.2026) — Präfix szm…
//  Wochenraster Mo–So × Stunde aus den Stempeln der Sidebar-Filiale: Ø Besetzung
//  pro Feld und wer dort wie oft arbeitet (Dienstplanung). Rechnung: Services/ArbeitsMuster.cs,
//  Endpoint GET /api/reports/stempel-muster. Hilfsfunktionen aus stempel-berichte.js.
// ═══════════════════════════════════════════════════════════════════════════

const _SZM_TAGE = ['Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag', 'Sonntag'];
const _SZM_KURZ = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];
let _szmData = null, _szmMa = null, _szmZelle = null;

function szmInit() {
    const f = document.getElementById('szmFrom');
    if (f && !f.value) _szmDreiMonate();
    szmLoad();
}

function _szmDreiMonate() {
    const gestern = new Date(); gestern.setDate(gestern.getDate() - 1);
    const von = new Date(gestern.getFullYear(), gestern.getMonth() - 3, gestern.getDate() + 1);
    document.getElementById('szmFrom').value = _szIso(von);
    document.getElementById('szmTo').value = _szIso(gestern);
}

function szmZeitraum(art) {
    if (art === 'drei') { _szmDreiMonate(); szmLoad(); }
    else _szZeitraumSetzen('szm', art);
}

async function szmLoad() {
    const j = await _szLaden('szm', 'stempel-muster');
    if (!j) return;
    _szmData = j;
    if (_szmMa && !j.mitarbeiter.some(m => m.id === _szmMa)) _szmMa = null;
    _szmZelle = null;
    szmRender();
}

function _szmName(id) {
    const m = _szmData?.mitarbeiter.find(x => x.id === id);
    return m ? `${m.vorname || ''} ${m.nachname || ''}`.trim() : '?';
}

function szmMaWaehlen(id) {
    _szmMa = (_szmMa === id) ? null : id;
    szmRender();
}

function szmZelleWaehlen(wt, std) {
    _szmZelle = (_szmZelle && _szmZelle.wt === wt && _szmZelle.std === std) ? null : { wt, std };
    szmRender();
}

function szmRender() {
    const box = document.getElementById('szmResult');
    const d = _szmData;
    if (!box || !d) return;
    if (!d.mitarbeiter.length) {
        box.innerHTML = '<div class="szb-leer">Keine Stempelzeiten dieser Filiale im gewählten Zeitraum.</div>';
        return;
    }
    // Nur Stunden mit Betrieb anzeigen (erste bis letzte besetzte Stunde).
    let von = 23, bis = 0;
    d.raster.forEach(tag => tag.forEach((z, std) => { if (z.besetzung > 0) { von = Math.min(von, std); bis = Math.max(bis, std); } }));
    const max = Math.max(...d.raster.flat().map(z => z.besetzung), 0.1);

    const zelle = (wt, std) => {
        const z = d.raster[wt][std];
        const aktiv = _szmZelle && _szmZelle.wt === wt && _szmZelle.std === std;
        let wert, staerke;
        if (_szmMa) {
            const a = z.ma.find(m => m.id === _szmMa)?.anteil || 0;
            wert = a ? Math.round(a * 100) + '%' : '';
            staerke = a;
        } else {
            wert = z.besetzung ? z.besetzung.toFixed(1) : '';
            staerke = z.besetzung / max;
        }
        const titel = `${_SZM_TAGE[wt]} ${std}–${std + 1} Uhr · Ø ${z.besetzung.toFixed(1)} Personen`;
        return `<td class="szm-zelle${aktiv ? ' aktiv' : ''}" title="${titel}" onclick="szmZelleWaehlen(${wt}, ${std})"
                    style="background:rgba(63,63,63,${(0.06 + staerke * 0.84).toFixed(2)});color:${staerke > 0.45 ? '#fff' : '#3f3f3f'}">${wert}</td>`;
    };

    const zeilen = [];
    for (let std = von; std <= bis; std++) {
        zeilen.push(`<tr><th class="szm-std">${String(std).padStart(2, '0')}–${String(std + 1).padStart(2, '0')}</th>
            ${[0, 1, 2, 3, 4, 5, 6].map(wt => zelle(wt, std)).join('')}</tr>`);
    }

    const legende = _szmMa
        ? `Zahl = Anteil der Wochen, in denen <b>${_szEsc(_szmName(_szmMa))}</b> in dieser Stunde gearbeitet hat · <a href="#" onclick="szmMaWaehlen(${_szmMa});return false">alle MA zeigen</a>`
        : 'Zahl = durchschnittlich anwesende Personen · Feld anklicken zeigt, wer dort arbeitet';

    box.innerHTML = `
        <div class="szm-kopf">${_szEsc(d.filiale)} · ${_szDatum(d.von)} – ${_szDatum(d.bis)} · ${d.mitarbeiter.length} Mitarbeitende</div>
        <div class="szm-layout">
            <div>
                <table class="szm-raster">
                    <thead><tr><th></th>${_SZM_KURZ.map((k, i) => `<th title="${d.wochentagAnzahl[i]} × im Zeitraum">${k}</th>`).join('')}</tr></thead>
                    <tbody>${zeilen.join('')}</tbody>
                </table>
                <div class="szm-legende">${legende}</div>
            </div>
            <div class="szm-detail">${_szmDetailHtml()}</div>
        </div>
        <div class="szm-titel">Mitarbeitende — typische Zeiten</div>
        <table class="szm-liste">
            <thead><tr><th>Mitarbeiter</th><th style="text-align:right">Std./Woche</th><th>Arbeitet meistens</th>
                ${_SZM_KURZ.map(k => `<th style="text-align:right">${k}</th>`).join('')}</tr></thead>
            <tbody>${d.mitarbeiter.map(m => `
                <tr class="${_szmMa === m.id ? 'aktiv' : ''}" onclick="szmMaWaehlen(${m.id})" title="Im Raster hervorheben">
                    <td><b>${_szEsc(m.vorname)} ${_szEsc(m.nachname)}</b> <span class="szm-nr">${_szEsc(m.nummer || '')}</span></td>
                    <td style="text-align:right">${m.stundenProWoche.toFixed(1)}</td>
                    <td>${_szEsc(m.hauptzeiten) || '<span class="szm-nr">unregelmässig</span>'}</td>
                    ${m.stundenProWochentag.map(h => `<td style="text-align:right" class="${h ? '' : 'szm-nr'}">${h ? h.toFixed(0) : '–'}</td>`).join('')}
                </tr>`).join('')}
            </tbody>
        </table>
        <div class="szm-legende">Wochentags-Spalten = Stunden im ganzen Zeitraum. «Arbeitet meistens» = Tage und Zeiten, an denen der MA in mindestens der Hälfte der Wochen gestempelt hat.</div>`;
}

function _szmDetailHtml() {
    const d = _szmData;
    if (!_szmZelle) {
        return `<div class="szm-detail-leer">Feld im Raster anklicken — hier steht dann, wer an diesem Wochentag zu dieser Stunde arbeitet.</div>`;
    }
    const { wt, std } = _szmZelle;
    const z = d.raster[wt][std];
    const liste = z.ma.length
        ? z.ma.map(m => `
            <div class="szm-ma${_szmMa === m.id ? ' aktiv' : ''}" onclick="szmMaWaehlen(${m.id})">
                <span class="szm-ma-name">${_szEsc(_szmName(m.id))}</span>
                <span class="szm-balken"><span style="width:${Math.round(m.anteil * 100)}%"></span></span>
                <span class="szm-ma-wert">${Math.round(m.anteil * 100)}%</span>
            </div>`).join('')
        : '<div class="szm-detail-leer">Niemand regelmässig.</div>';
    return `
        <div class="szm-detail-titel">${_SZM_TAGE[wt]} ${std}–${std + 1} Uhr</div>
        <div class="szm-detail-sub">Ø ${z.besetzung.toFixed(1)} Personen · ${d.wochentagAnzahl[wt]} ${_SZM_TAGE[wt]}e im Zeitraum</div>
        <div class="szm-detail-sub">Prozent = an wie vielen dieser Tage der MA hier gestempelt hat</div>
        ${liste}`;
}
