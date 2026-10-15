// Lohnmeldungen (ELM) — Bereich Zertifikat (Walter 15.10.2026, Swissdec-Zertifizierung):
// Laufzeit zeigen, vor Ablauf warnen, mit einem Knopf erneuern — für den Benutzer, ohne Fachbegriffe.
// Abgelaufen ⇒ derselbe Knopf führt durch die neue Registrierung (Angaben → Brief von Swissdec → Passwort).
// Server: ElmZertifikatController (api/elm/zertifikat), Regeln ElmZertifikatLaufzeit.

let _elmz = null;            // letzter Stand vom Server
let _elmzForm = false;       // Registrierungsformular offen
let _elmzMeldung = null;     // { ok, text }
let _elmzBusy = false;
let _elmzPwVorschlag = '';

function _elmzEsc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
}

function _elmzDatum(iso, mitZeit = false) {
    if (!iso) return '–';
    const d = new Date(iso);
    if (isNaN(d)) return '–';
    const p = n => String(n).padStart(2, '0');
    const t = `${p(d.getDate())}.${p(d.getMonth() + 1)}.${d.getFullYear()}`;
    return mitZeit ? `${t} ${p(d.getHours())}:${p(d.getMinutes())}` : t;
}

const _ELMZ_FARBE = {
    ok:   { punkt: '#16a34a', text: '#166534', grund: 'rgba(22,163,74,0.10)' },
    warn: { punkt: '#d97706', text: '#92400e', grund: 'rgba(217,119,6,0.12)' },
    rot:  { punkt: '#dc2626', text: '#991b1b', grund: 'rgba(220,38,38,0.10)' },
};

const _ELMZ_FELD = 'width:100%;box-sizing:border-box;padding:9px 12px;font-size:13px;color:#3f3f3f;background:#fff;'
    + 'border:1px solid rgba(255,255,255,0.95);border-radius:10px;'
    + 'box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9)';

function elmzInit() {
    _elmzForm = false;
    _elmzMeldung = null;
    elmzLaden();
}

async function elmzLaden() {
    const box = document.getElementById('elmzZertifikat');
    if (!box) return;
    try {
        const r = await fetch('/api/elm/zertifikat', { headers: ah() });
        if (!r.ok) { box.innerHTML = `<div style="color:#991b1b">Fehler ${r.status} beim Laden.</div>`; return; }
        _elmz = await r.json();
    } catch (e) {
        box.innerHTML = `<div style="color:#991b1b">Server nicht erreichbar.</div>`;
        return;
    }
    elmzZeichnen();
}

function _elmzAmpel(stufe, sua) {
    const f = _ELMZ_FARBE[stufe] || _ELMZ_FARBE.rot;
    const wort = !sua.vorhanden ? 'Fehlt'
        : sua.restTage < 0 ? 'Abgelaufen'
        : stufe === 'ok' ? 'Gültig' : 'Läuft bald ab';
    return `<span style="display:inline-flex;align-items:center;gap:7px;padding:4px 12px;border-radius:999px;background:${f.grund};
        color:${f.text};font-weight:700;font-size:12px"><span style="width:9px;height:9px;border-radius:50%;background:${f.punkt}"></span>${wort}</span>`;
}

function _elmzBalken(sua) {
    if (!sua.vorhanden || !sua.gueltigAb || !sua.gueltigBis) return '';
    const von = new Date(sua.gueltigAb).getTime(), bis = new Date(sua.gueltigBis).getTime();
    const anteil = Math.max(0, Math.min(1, (bis - Date.now()) / Math.max(1, bis - von)));
    const f = _ELMZ_FARBE[sua.stufe] || _ELMZ_FARBE.rot;
    return `<div style="height:8px;border-radius:999px;background:rgba(60,55,48,0.12);overflow:hidden;margin:10px 0 4px">
        <div style="height:100%;width:${(anteil * 100).toFixed(1)}%;background:${f.punkt};border-radius:999px"></div></div>
        <div style="display:flex;justify-content:space-between;font-size:11px;color:#8b8b8b">
            <span>ausgestellt ${_elmzDatum(sua.gueltigAb)}</span><span>gültig bis ${_elmzDatum(sua.gueltigBis)}</span></div>`;
}

function elmzZeichnen() {
    const box = document.getElementById('elmzZertifikat');
    if (!box || !_elmz) return;
    const s = _elmz, sua = s.sua, reg = s.registrierung;

    const gross = !sua.vorhanden ? 'Kein Zertifikat'
        : sua.restTage < 0 ? `Abgelaufen am ${_elmzDatum(sua.gueltigBis)}`
        : sua.restTage === 0 ? 'Läuft heute ab'
        : `Noch ${sua.restTage} Tag${sua.restTage === 1 ? '' : 'e'} gültig`;

    let knopf;
    // Ein laufender neuer Antrag sperrt das Erneuern nur, wenn der Antrag des Zertifikats unbekannt ist.
    if (reg.laufend && !(sua.antragBekannt && sua.aktion === 'erneuern')) knopf = '';
    else if (sua.aktion === 'erneuern')
        knopf = `<button type="button" class="btn btn-primary" onclick="elmzErneuern()" ${_elmzBusy ? 'disabled' : ''}>Zertifikat erneuern</button>`;
    else if (sua.aktion === 'neu_registrieren')
        knopf = `<button type="button" class="btn btn-primary" onclick="elmzFormOeffnen()" ${_elmzBusy ? 'disabled' : ''}>${sua.vorhanden ? 'Neues Zertifikat beantragen' : 'Zertifikat beantragen'}</button>`;
    else
        knopf = `<button type="button" class="btn btn-primary" disabled title="Erneuern ist ab ${_elmzDatum(sua.erneuernAb)} möglich">Zertifikat erneuern</button>`;

    const autoZeile = sua.vorhanden && sua.aktion !== 'neu_registrieren' && sua.erneuerungen < sua.maxErneuerungen
        ? 'OneCrew erneuert das Zertifikat kurz vor Ablauf selbst. Der Knopf ist für den Fall, dass Sie es früher tun möchten.'
        : '';
    const autoFehler = s.auto?.fehler
        ? `<div style="margin-top:12px;padding:10px 12px;border-radius:10px;background:${_ELMZ_FARBE.rot.grund};color:${_ELMZ_FARBE.rot.text};font-size:13px">
            <b>Automatische Erneuerung am ${_elmzDatum(s.auto.letzterVersuch, true)} gescheitert:</b> ${_elmzEsc(s.auto.fehler)}</div>`
        : '';
    const meldung = _elmzMeldung
        ? `<div style="margin-top:12px;padding:10px 12px;border-radius:10px;font-size:13px;
              background:${_elmzMeldung.ok ? _ELMZ_FARBE.ok.grund : _ELMZ_FARBE.rot.grund};
              color:${_elmzMeldung.ok ? _ELMZ_FARBE.ok.text : _ELMZ_FARBE.rot.text}">${_elmzEsc(_elmzMeldung.text)}</div>`
        : '';

    const erp = s.erp
        ? `Programm-Zertifikat von OneCrew: gültig bis ${_elmzDatum(s.erp.gueltigBis)}`
          + (s.erp.stufe === 'ok' ? '' : ' — wird mit dem nächsten OneCrew-Update ersetzt')
        : 'Programm-Zertifikat von OneCrew: nicht installiert';

    box.innerHTML = `
        <div style="display:flex;align-items:flex-start;gap:16px;flex-wrap:wrap">
            <div style="flex:1;min-width:280px">
                <div style="font-size:12px;color:#8b8b8b;text-transform:uppercase;letter-spacing:.04em">Zertifikat der Firma</div>
                <div style="display:flex;align-items:center;gap:12px;margin-top:4px;flex-wrap:wrap">
                    <span style="font-size:22px;font-weight:700;color:#3f3f3f">${gross}</span>${_elmzAmpel(sua.stufe, sua)}
                </div>
                ${_elmzBalken(sua)}
                <div style="font-size:13px;color:#646464;margin-top:10px;line-height:1.5">${_elmzEsc(sua.text)}</div>
                ${autoZeile ? `<div style="font-size:12px;color:#8b8b8b;margin-top:6px">${autoZeile}</div>` : ''}
            </div>
            <div style="display:flex;gap:8px;flex-wrap:wrap;align-self:center">
                ${knopf}
                <button type="button" class="btn btn-secondary" onclick="elmzLaden()">Aktualisieren</button>
            </div>
        </div>
        ${autoFehler}${meldung}
        ${reg.laufend ? _elmzRegistrierungLaeuft(reg) : ''}
        ${_elmzForm && !reg.laufend ? _elmzFormHtml(s.vorlage) : ''}
        <div style="margin-top:16px;padding-top:10px;border-top:1px solid rgba(60,55,48,0.12);font-size:12px;color:#8b8b8b;display:flex;gap:18px;flex-wrap:wrap">
            <span>Mit diesem Zertifikat unterschreibt OneCrew die Lohnmeldungen im Namen der Firma.</span>
            <span>${sua.vorhanden ? `Erneuert: ${sua.erneuerungen} von ${sua.maxErneuerungen}` : ''}</span>
            <span>${_elmzEsc(erp)}</span>
            <span>Verbunden mit: ${_elmzEsc(s.ziel?.name || '–')}</span>
        </div>`;
}

function _elmzSchritt(nr, titel, zustand, inhalt = '') {
    const farbe = zustand === 'fertig' ? '#16a34a' : zustand === 'jetzt' ? '#3f3f3f' : 'rgba(60,55,48,0.25)';
    return `<div style="display:flex;gap:12px;align-items:flex-start;margin-top:12px">
        <span style="flex:0 0 26px;height:26px;border-radius:50%;display:flex;align-items:center;justify-content:center;
              font-size:12px;font-weight:700;color:#fff;background:${farbe}">${zustand === 'fertig' ? '✓' : nr}</span>
        <div style="flex:1;padding-top:3px"><div style="font-weight:600;color:${zustand === 'spaeter' ? '#8b8b8b' : '#3f3f3f'};font-size:13px">${titel}</div>${inhalt}</div>
    </div>`;
}

function _elmzRegistrierungLaeuft(reg) {
    const st = (reg.state || '').toLowerCase();
    const abgelehnt = st === 'rejected' || st === 'expired';
    const geprueft = st === 'registered' || st === 'verified';
    const schritt2 = abgelehnt
        ? `<div style="font-size:13px;color:#991b1b;margin-top:4px">${_elmzEsc(reg.text)}</div>
           <button type="button" class="btn btn-secondary" style="margin-top:8px" onclick="elmzFormOeffnen(true)">Angaben korrigieren und neu senden</button>`
        : `<div style="font-size:13px;color:#646464;margin-top:4px">${_elmzEsc(reg.text)}</div>
           <button type="button" class="btn btn-secondary" style="margin-top:8px" onclick="elmzStatus()" ${_elmzBusy ? 'disabled' : ''}>Stand bei Swissdec abfragen</button>`;
    const schritt3 = `<div style="font-size:13px;color:#646464;margin-top:4px">Swissdec schickt der Firma einen Brief mit dem Registrierungspasswort. Dieses hier eingeben:</div>
        <div style="display:flex;gap:8px;margin-top:8px;flex-wrap:wrap;max-width:520px">
            <input id="elmzPasswort" type="text" autocomplete="off" spellcheck="false" placeholder="Registrierungspasswort"
                   value="${_elmzEsc(_elmzPwVorschlag)}" style="${_ELMZ_FELD};flex:1;min-width:220px">
            <button type="button" class="btn btn-primary" onclick="elmzAbschliessen()" ${_elmzBusy ? 'disabled' : ''}>Zertifikat abholen</button>
        </div>
        ${_elmzPwVorschlag ? '<div style="font-size:12px;color:#8b8b8b;margin-top:4px">Auf dem Testsystem liefert Swissdec das Passwort direkt mit — es ist bereits eingesetzt.</div>' : ''}`;
    return `<div style="margin-top:18px;padding:14px 16px;border-radius:12px;border:1px solid rgba(60,55,48,0.16)">
        <div style="font-weight:700;color:#3f3f3f;font-size:14px">Neues Zertifikat — Antrag läuft</div>
        ${_elmzSchritt(1, `Angaben gesendet${reg.registriertAm ? ' am ' + _elmzDatum(reg.registriertAm, true) : ''}`, 'fertig')}
        ${_elmzSchritt(2, 'Swissdec prüft die Angaben', geprueft ? 'fertig' : 'jetzt', geprueft ? '' : schritt2)}
        ${_elmzSchritt(3, 'Passwort aus dem Brief eingeben', geprueft || _elmzPwVorschlag ? 'jetzt' : 'spaeter', geprueft || _elmzPwVorschlag ? schritt3 : '')}
    </div>`;
}

function _elmzFormHtml(v) {
    const f = (id, label, wert, breit = 1, ph = '') => `<label style="grid-column:span ${breit};display:flex;flex-direction:column;gap:4px;font-size:12px;color:#646464">
        ${label}<input id="elmzF_${id}" type="text" value="${_elmzEsc(wert)}" placeholder="${_elmzEsc(ph)}" style="${_ELMZ_FELD}"></label>`;
    const dom = (_elmz.domains || []).map(d => `<option value="${d}" ${d === v.domain ? 'selected' : ''}>${_elmzEsc(_elmzDomainText(d))}</option>`).join('');
    return `<div style="margin-top:18px;padding:14px 16px;border-radius:12px;border:1px solid rgba(60,55,48,0.16)">
        <div style="font-weight:700;color:#3f3f3f;font-size:14px">Neues Zertifikat beantragen</div>
        <div style="font-size:13px;color:#646464;margin:4px 0 12px">Swissdec prüft die Firma anhand der UID und eines bestehenden Versicherungsvertrags. Danach kommt ein Brief mit dem Registrierungspasswort.</div>
        <div style="display:grid;grid-template-columns:repeat(4, minmax(0,1fr));gap:12px">
            ${f('firma', 'Firma', v.firma, 2)}
            ${f('uid', 'UID', v.uid, 1, 'CHE-123.456.789')}
            ${f('kontakt', 'Ansprechperson', v.kontakt, 1)}
            ${f('plz', 'PLZ', v.plz, 1)}
            ${f('ort', 'Ort', v.ort, 1)}
            <label style="grid-column:span 2;display:flex;flex-direction:column;gap:4px;font-size:12px;color:#646464">
                Nachweis über die Versicherung<select id="elmzF_domain">${dom}</select></label>
            ${f('versicherer', 'Versicherer', v.versicherer, 2)}
            ${f('kundennummer', 'Kundennummer beim Versicherer', v.kundennummer, 1)}
            ${f('vertragsnummer', 'Vertragsnummer', v.vertragsnummer, 1)}
        </div>
        <div style="display:flex;gap:8px;justify-content:flex-end;margin-top:14px">
            <button type="button" class="btn btn-secondary" onclick="elmzFormSchliessen()">Abbrechen</button>
            <button type="button" class="btn btn-primary" onclick="elmzRegistrieren()" ${_elmzBusy ? 'disabled' : ''}>Antrag an Swissdec senden</button>
        </div>
    </div>`;
}

function _elmzDomainText(d) {
    return ({ 'UVG-LAA': 'Unfallversicherung (UVG)', 'UVGZ-LAAC': 'Unfall-Zusatzversicherung (UVGZ)',
              'KTG-AMC': 'Krankentaggeld (KTG)', 'BVG-LPP': 'Pensionskasse (BVG)' })[d] || d;
}

function elmzFormOeffnen(trotzLaufend = false) {
    _elmzForm = true;
    _elmzMeldung = null;
    if (trotzLaufend && _elmz) _elmz.registrierung.laufend = false;
    elmzZeichnen();
}

function elmzFormSchliessen() {
    _elmzForm = false;
    elmzLaden();
}

async function _elmzPost(pfad, body) {
    _elmzBusy = true;
    _elmzMeldung = { ok: true, text: 'Verbindung zu Swissdec …' };
    elmzZeichnen();
    try {
        const r = await fetch('/api/elm/zertifikat/' + pfad, { method: 'POST', headers: ah(), body: JSON.stringify(body || {}) });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { _elmzMeldung = { ok: false, text: j.message || `Fehler ${r.status}` }; return null; }
        if (j.stand) _elmz = j.stand;
        _elmzMeldung = { ok: !!j.ok, text: j.meldung || (j.ok ? 'Erledigt.' : 'Nicht erledigt.') };
        return j;
    } catch (e) {
        _elmzMeldung = { ok: false, text: 'Server nicht erreichbar.' };
        return null;
    } finally {
        _elmzBusy = false;
        elmzZeichnen();
    }
}

async function elmzErneuern() {
    await _elmzPost('erneuern');
}

async function elmzRegistrieren() {
    const w = id => (document.getElementById('elmzF_' + id)?.value || '').trim();
    const body = {
        firma: w('firma'), uid: w('uid'), kontakt: w('kontakt'), plz: w('plz'), ort: w('ort'),
        domain: w('domain'), versicherer: w('versicherer'), kundennummer: w('kundennummer'), vertragsnummer: w('vertragsnummer'),
        addresseeIdentification: _elmz?.vorlage?.addresseeIdentification || null,
    };
    if (!body.firma || !body.uid || !body.kontakt) {
        _elmzMeldung = { ok: false, text: 'Bitte Firma, UID und Ansprechperson ausfüllen.' };
        elmzZeichnen();
        return;
    }
    _elmzPwVorschlag = '';
    const j = await _elmzPost('registrieren', body);
    if (j?.ok) { _elmzForm = false; elmzZeichnen(); }
}

async function elmzStatus() {
    const j = await _elmzPost('status');
    if (j?.passwortVorschlag) { _elmzPwVorschlag = j.passwortVorschlag; elmzZeichnen(); }
}

async function elmzAbschliessen() {
    const pw = (document.getElementById('elmzPasswort')?.value || '').trim();
    if (!pw) { _elmzMeldung = { ok: false, text: 'Bitte das Registrierungspasswort aus dem Brief eingeben.' }; elmzZeichnen(); return; }
    const j = await _elmzPost('abschliessen', { passwort: pw });
    if (j?.ok) _elmzPwVorschlag = '';
}
