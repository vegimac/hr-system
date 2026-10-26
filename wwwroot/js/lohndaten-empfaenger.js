// ══════════════════════════════════════════════════════════════════════
// lohndaten-empfaenger.js — Zentraler Empfänger-Katalog (Walter 17.10.2026)
//
// System → Lohn-Stammdaten → Lohndaten-Empfänger.
// Stammdaten = nur Swissdec-Import (nicht editierbar/löschbar).
// Filiale wählt zu und erfasst Mitglied/SSL + Gültigkeit.
// API: /api/lohndaten-empfaenger
// ══════════════════════════════════════════════════════════════════════

const LE_ARTEN = [
    ['AUSGLEICHSKASSE', 'Ausgleichskasse (AHV)'],
    ['FAK',             'Familienausgleichskasse (FAK)'],
    ['KTG',             'KTG-Versicherung'],
    ['UVG',             'UVG-Versicherung'],
    ['UVGZ',            'UVG-Zusatzversicherung'],
    ['BVG',             'BVG / Pensionskasse'],
    ['QST',             'Quellensteuer'],
    ['LOHNAUSWEIS',     'Lohnausweis'],
    ['ANDERE',          'Andere'],
];

let _leListe = [];
let _leFilterArt = '';
let _leFilterZug = false; // nur Filial-zugeordnete anzeigen
let _leEditId = null;

function leArtLabel(code) {
    const f = LE_ARTEN.find(a => a[0] === code);
    return f ? f[1] : (code || 'Andere');
}

function leEsc(t) {
    return String(t ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

async function leInit() {
    const el = document.getElementById('leList');
    if (!el) return;
    el.innerHTML = '<div style="padding:24px;color:#8b8b8b">Lade…</div>';
    try {
        const r = await fetch('/api/lohndaten-empfaenger?all=true', { headers: ah(), cache: 'no-store' });
        if (!r.ok) { el.innerHTML = `<div style="padding:24px;color:#b91c1c">Fehler beim Laden (${r.status})</div>`; return; }
        _leListe = await r.json();
        leRenderFilter();
        leRender();
    } catch (e) {
        el.innerHTML = `<div style="padding:24px;color:#b91c1c">Verbindungsfehler: ${leEsc(e.message)}</div>`;
    }
}

function leGefiltert() {
    return _leListe.filter(e => {
        if (_leFilterArt && e.art !== _leFilterArt) return false;
        if (_leFilterZug && !(e.zuordnungen > 0)) return false;
        return true;
    });
}

function leRenderFilter() {
    const bar = document.getElementById('leFilterBar');
    if (!bar) return;
    const counts = {};
    let nZug = 0;
    for (const e of _leListe) {
        counts[e.art] = (counts[e.art] || 0) + 1;
        if (e.zuordnungen > 0) nZug++;
    }
    const pill = (onclick, aktiv, label, n) =>
        `<button type="button" onclick="${onclick}" style="border:1px solid ${aktiv ? '#3f3f3f' : 'rgba(60,55,48,0.20)'};background:${aktiv ? '#3f3f3f' : 'transparent'};color:${aktiv ? '#fff' : '#3f3f3f'};border-radius:999px;padding:5px 12px;font-size:12px;font-weight:600;cursor:pointer">${leEsc(label)}${n != null ? ` · ${n}` : ''}</button>`;
    // «Zugeordnet» ist Zusatzfilter (kombinierbar mit Art)
    bar.innerHTML = pill("leSetFilter('')", !_leFilterArt, 'Alle', _leListe.length)
        + pill('leToggleZug()', _leFilterZug, 'Zugeordnet', nZug)
        + LE_ARTEN.filter(([c]) => counts[c]).map(([c, l]) => {
            const n = _leFilterZug
                ? _leListe.filter(e => e.art === c && e.zuordnungen > 0).length
                : counts[c];
            return pill(`leSetFilter('${c}')`, _leFilterArt === c, l, n);
        }).join('');
}

function leSetFilter(art) {
    _leFilterArt = art || '';
    leRenderFilter();
    leRender();
}

function leToggleZug() {
    _leFilterZug = !_leFilterZug;
    leRenderFilter();
    leRender();
}

function leRender() {
    const el = document.getElementById('leList');
    if (!el) return;
    const list = leGefiltert();
    if (!list.length) {
        el.innerHTML = '<div style="padding:24px;color:#8b8b8b">Keine Empfänger'
            + (_leFilterZug ? ' mit Filial-Zuordnung' : '')
            + (_leFilterArt ? ' in dieser Art' : '')
            + '.</div>';
        return;
    }
    const byArt = {};
    for (const e of list) {
        (byArt[e.art] ||= []).push(e);
    }
    const th = 'padding:4px 8px;text-align:left;font-size:10.5px;font-weight:600;color:#8b8b8b;text-transform:uppercase;letter-spacing:.04em;border-bottom:1px solid rgba(60,55,48,0.12);white-space:nowrap';
    const td = 'padding:6px 8px;vertical-align:middle;border-top:1px solid rgba(139,139,139,0.14);font-size:12.5px;color:#3f3f3f';
    let html = '';
    for (const [code, label] of LE_ARTEN) {
        const items = byArt[code];
        if (!items?.length) continue;
        items.sort((a, b) => (a.bezeichnung || '').localeCompare(b.bezeichnung || '', 'de')
            || (a.kassennummer || '').localeCompare(b.kassennummer || '', 'de'));
        html += `<div class="cp-empf-gruppe"><div class="cp-empf-titel">${leEsc(label)}</div>`;
        html += `<table style="width:100%;border-collapse:collapse;table-layout:fixed;margin-bottom:2px"><thead><tr>
            <th style="${th};width:30%">Name</th>
            <th style="${th};width:18%">Nr.</th>
            <th style="${th};width:7%">Kt.</th>
            <th style="${th};width:24%">E-Mail</th>
            <th style="${th};width:15%">Zuordnung</th>
            <th style="${th};width:6%"></th>
        </tr></thead><tbody>`;
        for (const e of items) {
            const name = `${leEsc(e.bezeichnung)}${e.zusatz ? ` <span class="le-zusatz">— ${leEsc(e.zusatz)}</span>` : ''}${e.isActive ? '' : ' <span style="font-size:10.5px;color:#8b8b8b">(inaktiv)</span>'}`;
            const tip = [e.strasse, e.postfach, [e.plz, e.ort].filter(Boolean).join(' '), e.uidNummer].filter(Boolean).join(', ');
            const nZug = e.zuordnungen || 0;
            const zuord = nZug > 0
                ? `<span class="le-zuord">${nZug} ${nZug === 1 ? 'Filiale' : 'Filialen'}</span>`
                : '<span class="le-offen">nicht zugeordnet</span>';
            html += `<tr class="${nZug > 0 ? 'le-zugeordnet' : ''}" style="${e.isActive ? '' : 'opacity:0.5'}"${tip ? ` title="${leEsc(tip)}"` : ''}>
                <td class="le-name" style="${td};overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${name}</td>
                <td style="${td};font-family:ui-monospace,Menlo,monospace;font-size:11.5px;white-space:nowrap">${leEsc(e.kassennummer || '–')}${leKassenTypText(e.kassenTyp) ? ` <span style="font-family:inherit;color:#646464">${leEsc(leKassenTypText(e.kassenTyp))}</span>` : ''}</td>
                <td style="${td};white-space:nowrap">${e.kantonCode ? leEsc(e.kantonCode) : '–'}</td>
                <td style="${td};overflow:hidden;text-overflow:ellipsis;white-space:nowrap;color:#646464">${leEsc(e.supportEmail || '–')}</td>
                <td style="${td};white-space:nowrap">${zuord}</td>
                <td style="${td};text-align:right;overflow:visible">
                    <div style="position:relative;display:inline-block">
                        <button type="button" class="dok-menu-btn" onclick="leToggleMenu(event, ${e.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="leMenu-${e.id}">
                            <button type="button" class="dok-menu-item" onclick="dokCloseAllMenus();leOpenEdit(${e.id})">Bearbeiten</button>
                        </div>
                    </div>
                </td>
            </tr>`;
        }
        html += `</tbody></table></div>`;
    }
    const known = new Set(LE_ARTEN.map(a => a[0]));
    const rest = list.filter(e => !known.has(e.art));
    if (rest.length) {
        html += `<div class="cp-empf-gruppe"><div class="cp-empf-titel">Weitere</div>`;
        for (const e of rest) {
            html += `<div style="padding:6px 8px;border-top:1px solid rgba(139,139,139,0.14);font-size:12.5px">${leEsc(e.bezeichnung)} (${leEsc(e.art)})</div>`;
        }
        html += `</div>`;
    }
    el.innerHTML = html;
}

function leToggleMenu(ev, id) {
    ev.stopPropagation();
    const menu = document.getElementById(`leMenu-${id}`);
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (menu && !wasOpen) menu.classList.add('show');
}
document.addEventListener('click', () => document.querySelectorAll('#leList .dok-menu.show').forEach(m => m.classList.remove('show')));

function leEnsureModal() {
    const alt = document.getElementById('leModal');
    if (alt && !document.getElementById('leUidUnbekannt')) alt.remove();
    if (document.getElementById('leModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(255,255,255,0.95);border-radius:10px;font-size:13px;background:#fff;box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9);box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const div = document.createElement('div');
    div.id = 'leModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:320;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div style="position:absolute;top:72px;left:50%;transform:translateX(-50%);width:min(680px,94vw);max-height:calc(100vh - 84px);overflow:auto;background:linear-gradient(165deg, #eeece4 0%, #e7e4db 50%, #dfdcd1 100%);border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px">
        <div id="leModalTitle" style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:14px">Empfänger erfassen</div>
        <div style="display:grid;grid-template-columns:1fr 2fr;gap:10px 12px">
            <label style="${lbl}">Art
                <select id="leArt" onchange="leKasseLabelSetzen()" style="${inp}">${LE_ARTEN.map(a => `<option value="${a[0]}">${a[1]}</option>`).join('')}</select>
            </label>
            <label style="${lbl}"><span id="leBezLbl">Bezeichnung</span><span class="pflicht-stern">*</span><input id="leBez" style="${inp}"></label>
            <label style="${lbl}">Zusatz<input id="leZusatz" style="${inp}"></label>
            <label style="${lbl}"><span id="leKassenLbl">Nummer der Kasse / Vers.-Nr.</span><input id="leKassenNr" placeholder="z.B. 003.000" style="${inp}" onblur="leKasseLabelSetzen()"><div id="leKassenArt" style="font-size:12px;font-weight:600;color:#3f3f3f;margin-top:4px;display:none"></div><div id="leKassenFehler" style="font-size:12px;color:#9b2c2c;font-weight:600;margin-top:4px;display:none"></div></label>
            <label style="${lbl};grid-column:span 2"><span id="leUidLbl">UID-Nummer</span> <span style="font-weight:400;color:#8b8b8b">(Swissdec)</span>
                <input id="leUid" placeholder="CHE-XXX.XXX.XXX" style="${inp}" oninput="leUidGetippt()">
                <span id="leUidUnbekannt" style="display:flex;align-items:center;gap:8px;margin-top:6px;font-size:12.5px;font-weight:600;color:#3f3f3f;cursor:pointer">
                    <input type="checkbox" id="leUidUnbekanntChk" onchange="leUidUnbekanntSetzen()" style="width:14px;height:14px;accent-color:#3f3f3f"> unbekannt
                </span>
                <span class="f-hint" style="display:block;margin-top:3px;font-size:11px;font-weight:400;color:#8b8b8b">Prüfziffer beim Verlassen des Feldes. CHE-999.999.997 ist ungültig.</span>
            </label>
            <label style="${lbl};grid-column:span 2">Strasse<input id="leStrasse" style="${inp}"></label>
            <label style="${lbl}">Postfach<input id="lePostfach" style="${inp}"></label>
            <label style="${lbl}">PLZ / Ort
                <div style="display:flex;gap:8px">
                    <input id="lePlz" maxlength="10" inputmode="numeric" style="${inp};width:90px;flex:none"
                           oninput="if(this.value.length>=4)lePlzLookup()" onblur="lePlzLookup()">
                    <input id="leOrt" style="${inp}">
                </div>
                <div id="lePlzHint" style="font-size:11px;font-weight:400;margin-top:3px;min-height:14px"></div>
            </label>
            <label style="${lbl}">Kanton (QST / FAK)<input id="leKanton" placeholder="AG, BE, …" maxlength="2" style="${inp}"></label>
            <label style="${lbl}">Support-E-Mail<input id="leMail" type="email" style="${inp}"></label>
            <label style="${lbl};grid-column:span 2">Bemerkung<input id="leBem" style="${inp}"></label>
            <label style="display:flex;align-items:center;gap:8px;grid-column:span 2;font-size:13px;color:#3f3f3f;cursor:pointer;margin-top:4px">
                <input type="checkbox" id="leAktiv" checked style="width:14px;height:14px;accent-color:#3f3f3f"> Aktiv (in Filial-Auswahl sichtbar)
            </label>
        </div>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:18px">
            <button type="button" onclick="leClose()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:999px;padding:8px 18px;font-size:13px;font-weight:600;cursor:pointer">Abbrechen</button>
            <button type="button" id="leSaveBtn" onclick="leSave()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 20px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 2px 8px rgba(60,55,48,0.2)">Speichern</button>
        </div>
    </div>`;
    div.addEventListener('click', (e) => { if (e.target === div) leClose(); });
    document.body.appendChild(div);
    if (window.SwissUid) {
        SwissUid.feldPruefungBinden('leBez', { art: 'pflicht', label: 'Bezeichnung' });
        SwissUid.feldPruefungBinden('leUid', { art: 'uid' });
        SwissUid.plzAdresseUeberall(div);
    }
}

function lePlzLookup() {
    const plz = document.getElementById('lePlz')?.value || '';
    if (!/^\d{3,10}$/.test(plz.trim())) return;
    if (typeof plzLookupGeneric === 'function')
        plzLookupGeneric(plz, 'leOrt', 'leKanton', null, 'lePlzHint');
}

function leOpenNew() {
    leEnsureModal();
    _leEditId = null;
    document.getElementById('leModalTitle').textContent = 'Empfänger erfassen';
    const set = (id, v) => { const el = document.getElementById(id); if (el) el.value = v ?? ''; };
    set('leArt', 'AUSGLEICHSKASSE');
    ['leBez','leZusatz','leKassenNr','leUid','leStrasse','lePostfach','lePlz','leOrt','leKanton','leMail','leBem'].forEach(id => set(id, ''));
    leUidZustand('');
    const akt = document.getElementById('leAktiv'); if (akt) akt.checked = true;
    const hint = document.getElementById('lePlzHint'); if (hint) hint.innerHTML = '';
    leKasseLabelSetzen();
    document.getElementById('leModal').style.display = 'block';
}

function leKassenTypAusNummer(art, nummer) {
    if (art !== 'AUSGLEICHSKASSE' && art !== 'FAK') return '';
    const block = String(nummer || '').trim().split('.')[0].trim();
    if (!/^\d+$/.test(block)) return '';
    const n = parseInt(block, 10);
    if ((n >= 1 && n <= 25) || n === 150) return 'KANTONAL';
    if (n >= 28) return 'VERBAND';
    return '';
}

function leKassenTypText(typ) {
    if (typ === 'KANTONAL') return 'kantonal';
    if (typ === 'VERBAND') return 'Verband';
    return '';
}

function leKasseBegriff(art) {
    if (art === 'AUSGLEICHSKASSE') return 'Nummer der Ausgleichskasse';
    if (art === 'FAK') return 'Nummer der Familienausgleichskasse';
    return '';
}

function leKasseFehler(art, nummer) {
    const begriff = leKasseBegriff(art);
    if (!begriff) return '';
    const t = (nummer || '').trim();
    if (!t) return '';
    const erster = t.split('.')[0];
    const n = parseInt(erster, 10);
    if (!/^\d+$/.test(erster) || !(n > 0))
        return begriff + ': der erste Block muss grösser als 0 sein (nicht «000.003»).';
    return '';
}

function leIstUidUnbekannt(v) {
    const u = String(v || '').trim().toUpperCase();
    return u === 'UNKNOWN' || u === 'UNBEKANNT' || u === 'UNK';
}

function leUidUnbekanntSetzen() {
    const chk = document.getElementById('leUidUnbekanntChk');
    const inp = document.getElementById('leUid');
    if (!chk || !inp) return;
    if (chk.checked) {
        inp.value = 'unbekannt';
        inp.readOnly = true;
        if (window.SwissUid) SwissUid.feldFehlerLoeschen(inp);
    } else {
        if (leIstUidUnbekannt(inp.value)) inp.value = '';
        inp.readOnly = false;
    }
}

function leUidGetippt() {
    const chk = document.getElementById('leUidUnbekanntChk');
    const inp = document.getElementById('leUid');
    if (!chk || !inp || chk.checked) return;
    if (leIstUidUnbekannt(inp.value)) {
        chk.checked = true;
        inp.readOnly = true;
        if (window.SwissUid) SwissUid.feldFehlerLoeschen(inp);
    }
}

function leUidZustand(wert) {
    const chk = document.getElementById('leUidUnbekanntChk');
    const inp = document.getElementById('leUid');
    const unbekannt = leIstUidUnbekannt(wert);
    if (chk) chk.checked = unbekannt;
    if (inp) {
        inp.readOnly = unbekannt;
        if (unbekannt) inp.value = 'unbekannt';
    }
}

function leKasseLabelSetzen() {
    const art = document.getElementById('leArt')?.value || '';
    const lbl = document.getElementById('leKassenLbl');
    if (lbl) lbl.textContent = leKasseBegriff(art) || (art === 'UVG' || art === 'UVGZ' || art === 'KTG' || art === 'BVG' ? 'Versicherernummer' : 'Nummer der Kasse / Vers.-Nr.');
    const bez = document.getElementById('leBezLbl');
    if (bez) bez.textContent = art === 'UVG' ? 'Name des UVG-Versicherers'
        : art === 'BVG' ? 'Name'
        : (art === 'UVGZ' || art === 'KTG') ? 'Versicherungsname' : 'Bezeichnung';
    const uid = document.getElementById('leUidLbl');
    if (uid) uid.textContent = (art === 'UVG' || art === 'BVG') ? 'UID-BFS' : 'UID-Nummer';
    const artZeile = document.getElementById('leKassenArt');
    const typText = leKassenTypText(leKassenTypAusNummer(art, document.getElementById('leKassenNr')?.value));
    if (artZeile) {
        artZeile.style.display = typText ? 'block' : 'none';
        artZeile.textContent = typText;
    }
    leKassePruefen();
}

function leKassePruefen() {
    const box = document.getElementById('leKassenFehler');
    const msg = leKasseFehler(document.getElementById('leArt')?.value, document.getElementById('leKassenNr')?.value);
    if (box) {
        box.style.display = msg ? 'block' : 'none';
        box.textContent = msg;
    }
    return msg;
}

function leOpenEdit(id) {
    const e = _leListe.find(x => x.id === id);
    if (!e) return;
    leEnsureModal();
    _leEditId = id;
    document.getElementById('leModalTitle').textContent = 'Empfänger bearbeiten';
    const set = (id, v) => { const el = document.getElementById(id); if (el) { el.value = v ?? ''; if (window.SwissUid) SwissUid.feldFehlerLoeschen(el); } };
    set('leArt', e.art || 'ANDERE');
    set('leBez', e.bezeichnung);
    set('leZusatz', e.zusatz);
    set('leKassenNr', e.kassennummer);
    set('leUid', e.uidNummer);
    leUidZustand(e.uidNummer);
    set('leStrasse', e.strasse);
    set('lePostfach', e.postfach);
    set('lePlz', e.plz);
    set('leOrt', e.ort);
    set('leKanton', e.kantonCode);
    set('leMail', e.supportEmail);
    set('leBem', e.bemerkung);
    const akt = document.getElementById('leAktiv'); if (akt) akt.checked = e.isActive !== false;
    leKasseLabelSetzen();
    document.getElementById('leModal').style.display = 'block';
}

function leClose() {
    const m = document.getElementById('leModal');
    if (m) m.style.display = 'none';
}

async function leSave() {
    const val = (id) => document.getElementById(id)?.value?.trim() || null;
    const btn = document.getElementById('leSaveBtn');
    const body = {
        art: val('leArt'), bezeichnung: val('leBez'), zusatz: val('leZusatz'),
        kassennummer: val('leKassenNr'), uidNummer: val('leUid'),
        strasse: val('leStrasse'), postfach: val('lePostfach'),
        plz: val('lePlz'), ort: val('leOrt'), kantonCode: val('leKanton'),
        supportEmail: val('leMail'), bemerkung: val('leBem'),
        isActive: document.getElementById('leAktiv')?.checked !== false,
    };
    if (window.SwissUid && !(await SwissUid.felderPruefen(document.getElementById('leModal')))) {
        showToast('Bitte die markierten Felder korrigieren.', 'error');
        return;
    }
    const kasseMsg = leKassePruefen();
    if (kasseMsg) { showToast(kasseMsg, 'error'); return; }
    if (!body.bezeichnung) { showToast('Bezeichnung fehlt.', 'error'); return; }
    if (btn) btn.disabled = true;
    try {
        const url = _leEditId == null ? '/api/lohndaten-empfaenger' : `/api/lohndaten-empfaenger/${_leEditId}`;
        const method = _leEditId == null ? 'POST' : 'PUT';
        const r = await fetch(url, {
            method, headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(body),
        });
        if (!r.ok) {
            let msg = 'Speichern fehlgeschlagen.';
            try { const j = await r.json(); if (j.message) msg = j.message; } catch (_) {}
            showToast(msg, 'error'); return;
        }
        leClose();
        showToast('Gespeichert.', 'success');
        await leInit();
    } catch (_) {
        showToast('Verbindungsfehler.', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}

async function leDelete(id) {
    const e = _leListe.find(x => x.id === id);
    if (!e) return;
    const n = e.zuordnungen || 0;
    const frage = n > 0
        ? `Empfänger «${e.bezeichnung}» löschen? Er ist ${n} Filiale(n) zugeordnet — die Zuordnungen werden mitgelöscht.`
        : `Empfänger «${e.bezeichnung}» löschen?`;
    const ok = await liquidConfirm(frage, { title: 'Empfänger löschen', yesLabel: 'Löschen', noLabel: 'Abbrechen' });
    if (!ok) return;
    try {
        let r = await fetch(`/api/lohndaten-empfaenger/${id}`, { method: 'DELETE', headers: ah() });
        if (r.status === 409) {
            const j = await r.json().catch(() => ({}));
            const ok2 = await liquidConfirm(
                (j.message || 'Empfänger ist in Verwendung.') + ' Trotzdem löschen?',
                { title: 'Trotzdem löschen?', yesLabel: 'Löschen', noLabel: 'Abbrechen' });
            if (!ok2) return;
            r = await fetch(`/api/lohndaten-empfaenger/${id}?force=true`, { method: 'DELETE', headers: ah() });
        }
        if (!r.ok) { showToast('Löschen fehlgeschlagen.', 'error'); return; }
        showToast('Gelöscht.', 'success');
        await leInit();
    } catch (_) { showToast('Verbindungsfehler.', 'error'); }
}

// ── Swissdec Empfängerliste (monatlich, Walter 17.10.2026) ─────────────
let _leSwissFile = null;
let _leSwissDiffs = null;

function leSwissdecImport() {
    leSwissEnsureModal();
    _leSwissFile = null;
    _leSwissDiffs = null;
    const fileInp = document.getElementById('leSwissFile');
    if (fileInp) fileInp.value = '';
    const prev = document.getElementById('leSwissPreview');
    if (prev) prev.innerHTML = '<div style="color:#8b8b8b;font-size:12.5px">Lade aktuelle Liste von swissdec.ch…</div>';
    const btn = document.getElementById('leSwissCommitBtn');
    if (btn) { btn.disabled = true; btn.style.opacity = '0.45'; }
    document.getElementById('leSwissModal').style.display = 'block';
    // Ein Klick: Download + Vorschau (manuelle Datei bleibt Fallback)
    leSwissDownload({ skipSaveDialog: true });
}

function leSwissEnsureModal() {
    if (document.getElementById('leSwissModal')) return;
    const div = document.createElement('div');
    div.id = 'leSwissModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:330;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div style="position:absolute;top:72px;left:50%;transform:translateX(-50%);width:min(720px,94vw);max-height:calc(100vh - 84px);overflow:auto;background:linear-gradient(165deg, #eeece4 0%, #e7e4db 50%, #dfdcd1 100%);border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px">
        <div style="margin-bottom:14px">
            <div style="font-size:15px;font-weight:700;color:#3f3f3f">Empfängerliste aktualisieren</div>
            <div style="font-size:12px;color:#8b8b8b">Quelle: swissdec.ch/data-receiver — empfohlen monatlich</div>
        </div>
        <div style="display:flex;flex-wrap:wrap;gap:8px;margin-bottom:12px">
            <button type="button" onclick="leSwissDownload()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 16px;font-size:13px;font-weight:600;cursor:pointer">XLSX herunterladen</button>
            <a href="https://www.swissdec.ch/data-receiver" target="_blank" rel="noopener" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:12px;padding:8px 16px;font-size:13px;font-weight:600;text-decoration:none">Seite öffnen</a>
            <label style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#3f3f3f;border-radius:12px;padding:8px 16px;font-size:13px;font-weight:600;cursor:pointer">
                Datei wählen…
                <input id="leSwissFile" type="file" accept=".xlsx,.xlsm" style="display:none" onchange="leSwissFileChosen(this)">
            </label>
        </div>
        <div id="leSwissDrop" ondragover="event.preventDefault();this.style.borderColor='#3f3f3f'" ondragleave="this.style.borderColor='rgba(60,55,48,0.20)'" ondrop="leSwissDrop(event)"
             style="border:1.5px dashed rgba(60,55,48,0.20);border-radius:12px;padding:18px;text-align:center;font-size:12.5px;color:#8b8b8b;margin-bottom:12px;background:rgba(255,255,255,0.35)">
            Excel hier ablegen (Empfaengerliste.xlsx)
        </div>
        <div id="leSwissPreview" style="max-height:42vh;overflow:auto;font-size:12.5px"></div>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:16px">
            <button type="button" onclick="leSwissClose()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(60,55,48,0.18);color:#646464;border-radius:999px;padding:8px 18px;font-size:13px;font-weight:600;cursor:pointer">Schliessen</button>
            <button type="button" id="leSwissCommitBtn" disabled onclick="leSwissCommit()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:8px 20px;font-size:13px;font-weight:600;cursor:pointer;opacity:0.45;box-shadow:0 2px 8px rgba(60,55,48,0.2)">Korrekturen übernehmen</button>
        </div>
    </div>`;
    div.addEventListener('click', (e) => { if (e.target === div) leSwissClose(); });
    document.body.appendChild(div);
}

function leSwissClose() {
    const m = document.getElementById('leSwissModal');
    if (m) m.style.display = 'none';
}

async function leSwissDownload(opts) {
    const skipSave = !!(opts && opts.skipSaveDialog);
    const prev = document.getElementById('leSwissPreview');
    try {
        if (prev) prev.innerHTML = '<div style="color:#8b8b8b">Lade XLSX von swissdec.ch…</div>';
        const r = await fetch('/api/lohndaten-empfaenger/swissdec-xlsx', { headers: ah() });
        if (!r.ok) {
            let msg = 'Download fehlgeschlagen.';
            try { const j = await r.json(); if (j.message) msg = j.message; } catch (_) {}
            showToast(msg, 'error');
            if (prev) prev.innerHTML = `<div style="color:#b91c1c">${leEsc(msg)}</div>
                <div style="margin-top:8px;color:#646464;font-size:12.5px">Bitte «Datei wählen» oder die XLSX von
                <a href="https://www.swissdec.ch/data-receiver" target="_blank" rel="noopener" style="color:#3f3f3f;font-weight:600">swissdec.ch/data-receiver</a> ablegen.</div>`;
            return;
        }
        const blob = await r.blob();
        const name = (r.headers.get('Content-Disposition') || '').match(/filename="?([^";]+)"?/)?.[1]
            || `Swissdec_Empfaengerliste_${new Date().toISOString().slice(0, 10)}.xlsx`;
        // Speichern-unter nur auf expliziten Knopf — beim Auto-Start stört der Dialog
        if (!skipSave) {
            if (typeof saveBlobAsk === 'function') await saveBlobAsk(blob, name);
            else {
                const a = document.createElement('a');
                a.href = URL.createObjectURL(blob); a.download = name; a.click();
            }
        }
        _leSwissFile = new File([blob], name, { type: blob.type || 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
        await leSwissPreview();
    } catch (e) {
        showToast('Download fehlgeschlagen — bitte Datei von swissdec.ch laden.', 'error');
        if (prev) prev.innerHTML = '<div style="color:#b91c1c">Download fehlgeschlagen — Datei manuell wählen.</div>';
    }
}

function leSwissFileChosen(inp) {
    const f = inp?.files?.[0];
    if (!f) return;
    _leSwissFile = f;
    leSwissPreview();
}

function leSwissDrop(ev) {
    ev.preventDefault();
    const zone = document.getElementById('leSwissDrop');
    if (zone) zone.style.borderColor = 'rgba(60,55,48,0.20)';
    const f = ev.dataTransfer?.files?.[0];
    if (!f) return;
    _leSwissFile = f;
    leSwissPreview();
}

async function leSwissPreview() {
    if (!_leSwissFile) return;
    const prev = document.getElementById('leSwissPreview');
    if (prev) prev.innerHTML = '<div style="color:#8b8b8b">Vergleiche…</div>';
    const fd = new FormData();
    // Feldname «file» = Parameter SwissdecImport(IFormFile? file)
    fd.append('file', _leSwissFile, _leSwissFile.name || 'Empfaengerliste.xlsx');
    try {
        // Kein Content-Type aus ah() — Browser muss multipart boundary setzen
        const r = await fetch('/api/lohndaten-empfaenger/swissdec-import?dryRun=true', {
            method: 'POST',
            headers: { Authorization: `Bearer ${authToken}` },
            body: fd,
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            const msg = j.message || j.error || (r.status === 403
                ? 'Nur Admin darf die Swissdec-Liste importieren.'
                : `Vorschau fehlgeschlagen (HTTP ${r.status})`);
            if (prev) prev.innerHTML = `<div style="color:#b91c1c">${leEsc(msg)}</div>`;
            return;
        }
        _leSwissDiffs = j;
        const rows = (j.diffs || []);
        const byArt = {};
        for (const d of rows) byArt[d.art] = (byArt[d.art] || 0) + 1;
        const artSum = Object.keys(byArt).sort().map(a => `${leArtLabel(a)}: ${byArt[a]}`).join(' · ');
        let html = `<div style="margin-bottom:8px;color:#3f3f3f"><b>${j.neu || 0}</b> neu · <b>${j.aenderung || 0}</b> Korrektur · ${j.gleich || 0} unverändert
            <div style="font-size:11.5px;color:#8b8b8b;margin-top:4px">Swissdec-Liste: ${j.total ?? rows.length + (j.gleich || 0)} Empfängerzeilen${artSum ? ` — davon offen: ${leEsc(artSum)}` : ''}</div>
            </div>`;
        if (!rows.length) {
            html += '<div style="color:#16a34a">Alles aktuell — nichts zu übernehmen.</div>';
        } else {
            html += `<table style="width:100%;border-collapse:collapse"><thead><tr style="text-align:left;color:#8b8b8b;font-size:11px;text-transform:uppercase">
                <th style="padding:4px 6px">Status</th><th style="padding:4px 6px">Art</th><th style="padding:4px 6px">Empfänger</th><th style="padding:4px 6px">Nr.</th><th style="padding:4px 6px">Änderung</th>
            </tr></thead><tbody>`;
            for (const d of rows.slice(0, 200)) {
                const st = d.status === 'neu'
                    ? '<span style="color:#16a34a;font-weight:700">neu</span>'
                    : '<span style="color:#b45309;font-weight:700">Korrektur</span>';
                html += `<tr style="border-top:1px solid rgba(60,55,48,0.10)">
                    <td style="padding:5px 6px">${st}</td>
                    <td style="padding:5px 6px">${leEsc(leArtLabel(d.art))}</td>
                    <td style="padding:5px 6px;font-weight:600">${leEsc(d.bezeichnung)}</td>
                    <td style="padding:5px 6px;font-family:ui-monospace,Menlo,monospace;font-size:11.5px">${leEsc(d.kassennummer || d.kantonCode || '')}</td>
                    <td style="padding:5px 6px;color:#646464;font-size:11.5px">${leEsc(d.aenderungText || '—')}</td>
                </tr>`;
            }
            html += '</tbody></table>';
            if (rows.length > 200) html += `<div style="margin-top:6px;color:#8b8b8b">… und ${rows.length - 200} weitere</div>`;
        }
        if (prev) prev.innerHTML = html;
        const btn = document.getElementById('leSwissCommitBtn');
        const can = (j.neu || 0) + (j.aenderung || 0) > 0;
        if (btn) {
            btn.disabled = !can;
            btn.style.opacity = can ? '1' : '0.45';
            btn.textContent = can
                ? `${j.neu || 0} neu / ${j.aenderung || 0} Korrekturen übernehmen`
                : 'Nichts zu übernehmen';
        }
    } catch (_) {
        if (prev) prev.innerHTML = '<div style="color:#b91c1c">Verbindungsfehler.</div>';
    }
}

async function leSwissCommit() {
    if (!_leSwissFile) return;
    const ok = await liquidConfirm(
        `${_leSwissDiffs?.neu || 0} neue Empfänger anlegen und ${_leSwissDiffs?.aenderung || 0} Stammdaten korrigieren? Filial-Nummern bleiben unberührt.`,
        { title: 'Swissdec-Liste übernehmen', yesLabel: 'Übernehmen', noLabel: 'Abbrechen' });
    if (!ok) return;
    const fd = new FormData();
    fd.append('file', _leSwissFile, _leSwissFile.name || 'Empfaengerliste.xlsx');
    const btn = document.getElementById('leSwissCommitBtn');
    if (btn) btn.disabled = true;
    try {
        const r = await fetch('/api/lohndaten-empfaenger/swissdec-import?dryRun=false', {
            method: 'POST',
            headers: { Authorization: `Bearer ${authToken}` },
            body: fd,
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) {
            showToast(j.message || j.error || (r.status === 403
                ? 'Nur Admin darf importieren.'
                : `Übernahme fehlgeschlagen (HTTP ${r.status})`), 'error');
            return;
        }
        showToast(j.message || 'Übernommen.', 'success');
        leSwissClose();
        await leInit();
    } catch (_) {
        showToast('Verbindungsfehler.', 'error');
    } finally {
        if (btn) btn.disabled = false;
    }
}
