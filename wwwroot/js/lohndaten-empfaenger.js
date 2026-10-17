// ══════════════════════════════════════════════════════════════════════
// lohndaten-empfaenger.js — Zentraler Empfänger-Katalog (Walter 17.10.2026)
//
// System → Verzeichnisse & Vorgaben → Lohndaten-Empfänger.
// Stammdaten (Art, Name, UID, Adresse, Kassennummer, Support) einmal hier;
// Filiale wählt nur noch zu und erfasst Mitglied/SSL + Gültigkeit.
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

function leRenderFilter() {
    const bar = document.getElementById('leFilterBar');
    if (!bar) return;
    const counts = {};
    for (const e of _leListe) counts[e.art] = (counts[e.art] || 0) + 1;
    const pill = (art, label, n) => {
        const aktiv = _leFilterArt === art;
        return `<button type="button" onclick="leSetFilter('${art}')" style="border:1px solid ${aktiv ? '#3f3f3f' : 'rgba(60,55,48,0.20)'};background:${aktiv ? '#3f3f3f' : 'transparent'};color:${aktiv ? '#fff' : '#3f3f3f'};border-radius:999px;padding:5px 12px;font-size:12px;font-weight:600;cursor:pointer">${leEsc(label)}${n != null ? ` · ${n}` : ''}</button>`;
    };
    bar.innerHTML = pill('', 'Alle', _leListe.length)
        + LE_ARTEN.filter(([c]) => counts[c]).map(([c, l]) => pill(c, l, counts[c])).join('');
}

function leSetFilter(art) {
    _leFilterArt = art || '';
    leRenderFilter();
    leRender();
}

function leRender() {
    const el = document.getElementById('leList');
    if (!el) return;
    const list = _leFilterArt ? _leListe.filter(e => e.art === _leFilterArt) : _leListe;
    if (!list.length) {
        el.innerHTML = '<div style="padding:24px;color:#8b8b8b">Noch keine Empfänger'
            + (_leFilterArt ? ' in dieser Art' : '')
            + ' — «+ Neuer Empfänger» oben rechts.</div>';
        return;
    }
    const byArt = {};
    for (const e of list) {
        (byArt[e.art] ||= []).push(e);
    }
    let html = '';
    for (const [code, label] of LE_ARTEN) {
        const items = byArt[code];
        if (!items?.length) continue;
        html += `<div style="font-size:11px;font-weight:700;color:#8b8b8b;text-transform:uppercase;letter-spacing:.05em;margin:14px 0 6px;padding:0 4px">${leEsc(label)}</div>`;
        html += `<table style="width:100%;border-collapse:collapse;font-size:13px;margin-bottom:4px"><tbody>`;
        for (const e of items) {
            const adr = [e.strasse, e.postfach, [e.plz, e.ort].filter(Boolean).join(' ')].filter(Boolean).join(' · ');
            const meta = [
                e.kassennummer ? `Nr. ${leEsc(e.kassennummer)}` : null,
                e.uidNummer ? leEsc(e.uidNummer) : null,
                e.kantonCode ? `<span style="border:1px solid rgba(60,55,48,0.18);border-radius:6px;padding:1px 6px;font-size:11px;font-weight:600">${leEsc(e.kantonCode)}</span>` : null,
                e.supportEmail ? leEsc(e.supportEmail) : null,
                (e.zuordnungen > 0) ? `${e.zuordnungen} Filiale(n)` : '<span style="color:#b45309">nicht zugeordnet</span>',
            ].filter(Boolean).join(' · ');
            html += `<tr style="border-top:1px solid rgba(139,139,139,0.18);${e.isActive ? '' : 'opacity:0.5'}">
                <td style="padding:9px 10px;min-width:0">
                    <div style="font-weight:600;color:#3f3f3f">${leEsc(e.bezeichnung)}${e.zusatz ? ` <span style="font-weight:400;color:#8b8b8b">— ${leEsc(e.zusatz)}</span>` : ''}${e.isActive ? '' : ' <span style="font-size:10.5px;color:#8b8b8b">(inaktiv)</span>'}</div>
                    <div style="font-size:11.5px;color:#8b8b8b;margin-top:2px">${leEsc(adr) || '–'}</div>
                    <div style="font-size:11.5px;color:#646464;margin-top:2px">${meta}</div>
                </td>
                <td style="padding:9px 10px;text-align:right;width:48px;vertical-align:top">
                    <div class="dok-menu-wrap" style="display:inline-block">
                        <button class="dok-menu-btn" onclick="leToggleMenu(event, ${e.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="leMenu-${e.id}">
                            <button class="dok-menu-item" onclick="leOpenEdit(${e.id})">Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="leDelete(${e.id})">Löschen</button>
                        </div>
                    </div>
                </td>
            </tr>`;
        }
        html += `</tbody></table>`;
    }
    // Unbekannte Arten
    const known = new Set(LE_ARTEN.map(a => a[0]));
    const rest = list.filter(e => !known.has(e.art));
    if (rest.length) {
        html += `<div style="font-size:11px;font-weight:700;color:#8b8b8b;margin:14px 0 6px">WEITERE</div>`;
        for (const e of rest) {
            html += `<div style="padding:8px 10px;border-top:1px solid rgba(139,139,139,0.18)">${leEsc(e.bezeichnung)} (${leEsc(e.art)})</div>`;
        }
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
    if (document.getElementById('leModal')) return;
    const inp = 'width:100%;margin-top:3px;padding:7px 10px;border:1px solid rgba(255,255,255,0.95);border-radius:10px;font-size:13px;background:#fff;box-shadow:0 2px 6px rgba(60,55,48,0.13), inset 0 1px 0 rgba(255,255,255,0.9);box-sizing:border-box;font-family:inherit;color:#3f3f3f';
    const lbl = 'display:block;font-size:11.5px;font-weight:600;color:#8b8b8b';
    const div = document.createElement('div');
    div.id = 'leModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;z-index:320;background:rgba(40,36,30,0.38);backdrop-filter:blur(2px)';
    div.innerHTML = `
    <div style="position:absolute;top:50%;left:50%;transform:translate(-50%,-50%);width:min(680px,94vw);max-height:92vh;overflow:auto;background:linear-gradient(165deg, #eeece4 0%, #e7e4db 50%, #dfdcd1 100%);border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 25px 60px rgba(60,55,48,0.22);padding:22px 24px">
        <div id="leModalTitle" style="font-size:15px;font-weight:700;color:#3f3f3f;margin-bottom:14px">Empfänger erfassen</div>
        <div style="display:grid;grid-template-columns:1fr 2fr;gap:10px 12px">
            <label style="${lbl}">Art
                <select id="leArt" style="${inp}">${LE_ARTEN.map(a => `<option value="${a[0]}">${a[1]}</option>`).join('')}</select>
            </label>
            <label style="${lbl}">Bezeichnung<span class="pflicht-stern">*</span><input id="leBez" style="${inp}"></label>
            <label style="${lbl}">Zusatz<input id="leZusatz" style="${inp}"></label>
            <label style="${lbl}">Nummer der Kasse / Vers.-Nr.<input id="leKassenNr" placeholder="z.B. 046.000 oder S122" style="${inp}"></label>
            <label style="${lbl};grid-column:span 2">UID-Nummer <span style="font-weight:400;color:#8b8b8b">(Swissdec)</span>
                <input id="leUid" placeholder="CHE-XXX.XXX.XXX" style="${inp}">
                <span class="f-hint" style="display:block;margin-top:3px;font-size:11px;font-weight:400;color:#8b8b8b">Prüfziffer beim Verlassen des Feldes</span>
            </label>
            <label style="${lbl};grid-column:span 2">Strasse<input id="leStrasse" style="${inp}"></label>
            <label style="${lbl}">Postfach<input id="lePostfach" style="${inp}"></label>
            <label style="${lbl}">PLZ / Ort
                <div style="display:flex;gap:8px">
                    <input id="lePlz" maxlength="4" inputmode="numeric" style="${inp};width:80px;flex:none"
                           oninput="if(this.value.length===4)lePlzLookup()" onblur="lePlzLookup()">
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
    if (!/^\d{4}$/.test(plz.trim())) return;
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
    const akt = document.getElementById('leAktiv'); if (akt) akt.checked = true;
    const hint = document.getElementById('lePlzHint'); if (hint) hint.innerHTML = '';
    document.getElementById('leModal').style.display = 'block';
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
    set('leStrasse', e.strasse);
    set('lePostfach', e.postfach);
    set('lePlz', e.plz);
    set('leOrt', e.ort);
    set('leKanton', e.kantonCode);
    set('leMail', e.supportEmail);
    set('leBem', e.bemerkung);
    const akt = document.getElementById('leAktiv'); if (akt) akt.checked = e.isActive !== false;
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
