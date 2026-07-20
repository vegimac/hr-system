// ══════════════════════════════════════════════════════════════════════
// aerzte.js — Ärzte-Verzeichnis (Walter-Vorgabe 16.07.2026)
//
// Systemeinstellungen → Ärzte: behandelnde Ärztinnen/Ärzte der MA.
// Verwendet im Mutterschafts-Modul («Brief an den behandelnden Arzt»).
// Liste + Erfassen/Bearbeiten/Löschen. ⋮-Menü nach Icon-Button-Standard.
// ══════════════════════════════════════════════════════════════════════

let _azListe = [];

async function aerzteInit() {
    const el = document.getElementById('aerzteList');
    if (!el) return;
    el.innerHTML = '<div style="padding:24px;color:#8b8b8b">Lade…</div>';
    try {
        const r = await fetch('/api/aerzte?all=true', { headers: ah() });
        if (!r.ok) { el.innerHTML = `<div style="padding:24px;color:#b91c1c">Fehler beim Laden (${r.status})</div>`; return; }
        _azListe = await r.json();
        aerzteRender();
    } catch (e) {
        el.innerHTML = `<div style="padding:24px;color:#b91c1c">Verbindungsfehler: ${e.message}</div>`;
    }
}

function aerzteRender() {
    const el = document.getElementById('aerzteList');
    if (!el) return;
    if (!_azListe.length) {
        el.innerHTML = '<div style="padding:24px;color:#8b8b8b">Noch keine Ärzte erfasst — «+ Neuer Arzt» oben rechts.</div>';
        return;
    }
    const esc = t => String(t ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;');
    el.innerHTML = `
    <table style="width:100%;border-collapse:collapse;font-size:13px">
        <thead><tr style="text-align:left;color:#8b8b8b;font-size:11px;text-transform:uppercase;letter-spacing:.05em">
            <th style="padding:8px 10px">Name</th>
            <th style="padding:8px 10px">Fachgebiet</th>
            <th style="padding:8px 10px">Praxis</th>
            <th style="padding:8px 10px">Ort</th>
            <th style="padding:8px 10px">Telefon</th>
            <th style="padding:8px 10px">E-Mail</th>
            <th style="padding:8px 10px"></th>
        </tr></thead>
        <tbody>
        ${_azListe.map(a => `
            <tr style="border-top:1px solid rgba(139,139,139,0.18);${a.aktiv ? '' : 'opacity:0.5'}">
                <td style="padding:8px 10px;font-weight:600;color:#3f3f3f">${esc([a.titel, a.vorname, a.nachname].filter(Boolean).join(' '))}${a.aktiv ? '' : ' <span style="font-size:10.5px;color:#8b8b8b">(inaktiv)</span>'}</td>
                <td style="padding:8px 10px">${esc(a.fachgebiet)}</td>
                <td style="padding:8px 10px">${esc(a.praxisName)}</td>
                <td style="padding:8px 10px">${esc([a.plz, a.ort].filter(Boolean).join(' '))}</td>
                <td style="padding:8px 10px;white-space:nowrap">${esc(a.telefon)}</td>
                <td style="padding:8px 10px">${esc(a.email)}</td>
                <td style="padding:8px 10px;text-align:right">
                    <div class="dok-menu-wrap" style="display:inline-block">
                        <button class="dok-menu-btn" onclick="azToggleMenu(event, ${a.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="azMenu-${a.id}">
                            <button class="dok-menu-item" onclick="azOpenEdit(${a.id})">Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="azDelete(${a.id})">Löschen</button>
                        </div>
                    </div>
                </td>
            </tr>`).join('')}
        </tbody>
    </table>`;
}

function azToggleMenu(ev, id) {
    ev.stopPropagation();
    const menu = document.getElementById(`azMenu-${id}`);
    const wasOpen = menu?.classList.contains('show');
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    if (menu && !wasOpen) menu.classList.add('show');
}
document.addEventListener('click', () => document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show')));

// ── Erfassen / Bearbeiten (Liquid-Modal) ─────────────────────────────────
let _azEditId = null;

function _azEnsureModal() {
    // Altes Modal ohne Arztbestätigungs-Block nach Deploy neu aufbauen.
    const existing = document.getElementById('azModal');
    if (existing && !document.getElementById('azDokBlock')) existing.remove();
    if (document.getElementById('azModal')) return;
    const div = document.createElement('div');
    div.id = 'azModal';
    div.style.cssText = 'display:none;position:fixed;inset:0;background:rgba(30,27,22,0.45);z-index:9000;align-items:center;justify-content:center';
    const fld = (id, label, ph = '') => `
        <div>
            <label style="font-size:11.5px;font-weight:700;color:#646464">${label}</label>
            <input type="text" id="${id}" placeholder="${ph}" style="width:100%;padding:7px 10px;border:1px solid #cbd5e1;border-radius:8px;font-size:13px;background:white">
        </div>`;
    div.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 22px 70px rgba(60,55,48,0.22);max-width:600px;width:94%;max-height:92vh;overflow-y:auto;padding:22px 24px">
        <div style="display:flex;align-items:center;justify-content:space-between;margin-bottom:14px">
            <div id="azModalTitle" style="font-size:16px;font-weight:800;color:#3f3f3f">Arzt erfassen</div>
            <button onclick="azClose()" style="background:none;border:none;font-size:20px;color:#8b8b8b;cursor:pointer">×</button>
        </div>
        <div id="azDokBlock" style="margin-bottom:14px;padding:10px 12px;background:rgba(255,255,255,0.55);border:1px solid rgba(139,139,139,0.28);border-radius:10px;font-size:12.5px;color:#3f3f3f">
            <div style="font-size:11.5px;font-weight:700;color:#646464;margin-bottom:4px">Arztbestätigung (zum Abschreiben)</div>
            <div id="azDokContent">—</div>
        </div>
        <div style="display:grid;grid-template-columns:120px 1fr 1fr;gap:10px 12px">
            ${fld('azTitel', 'Titel', 'Dr. med.')}
            ${fld('azVorname', 'Vorname')}
            ${fld('azNachname', 'Nachname (Pflicht)')}
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px;margin-top:10px">
            ${fld('azFachgebiet', 'Fachgebiet', 'z.B. Gynäkologie/Geburtshilfe')}
            ${fld('azPraxis', 'Praxis / Institution', 'z.B. Frauenzentrum Sursee')}
        </div>
        <div style="display:grid;grid-template-columns:2fr 90px 1fr;gap:10px 12px;margin-top:10px">
            ${fld('azStrasse', 'Strasse Nr.')}
            ${fld('azPlz', 'PLZ')}
            ${fld('azOrt', 'Ort')}
        </div>
        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px 12px;margin-top:10px">
            ${fld('azTelefon', 'Telefon', '+41 41 …')}
            ${fld('azEmail', 'E-Mail')}
        </div>
        <div style="margin-top:10px">
            ${fld('azBemerkung', 'Bemerkung')}
        </div>
        <label style="display:flex;align-items:center;gap:8px;margin-top:12px;font-size:13px;cursor:pointer">
            <input type="checkbox" id="azAktiv" checked style="width:16px;height:16px;cursor:pointer"> aktiv (in Auswahllisten sichtbar)
        </label>
        <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:20px">
            <button onclick="azClose()" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
            <button onclick="azSave()" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Speichern</button>
        </div>
    </div>`;
    document.body.appendChild(div);
}

let _azDokEmpId = null;
let _azDokId = null;

async function azLoadDokHint() {
    const box = document.getElementById('azDokContent');
    if (!box) return;
    _azDokEmpId = null;
    _azDokId = null;
    const empId = window.activeEmpId || window.selectedEmployeeId || null;
    if (!empId) {
        box.innerHTML = `<span style="color:#8b8b8b">Kein Mitarbeiter fokussiert — zuerst beim MA die Mutterschaft öffnen, dann erscheint hier die Arztbestätigung zum Abschreiben.</span>`;
        return;
    }
    box.innerHTML = `<span style="color:#8b8b8b">Lade…</span>`;
    try {
        const r = await fetch(`/api/pregnancies?employeeId=${empId}`, { headers: ah() });
        if (!r.ok) {
            box.innerHTML = `<span style="color:#8b8b8b">Keine Schwangerschaft für den fokussierten MA gefunden.</span>`;
            return;
        }
        const list = await r.json();
        const p = (list || []).find(x => x.arztbestaetigungDokumentId)
               || (list || [])[0]
               || null;
        if (!p) {
            box.innerHTML = `<span style="color:#8b8b8b">Keine Schwangerschaft für den fokussierten MA gefunden.</span>`;
            return;
        }
        _azDokEmpId = empId;
        _azDokId = p.arztbestaetigungDokumentId || null;
        const name = p.arztbestaetigungDokumentName
            || (p.arztbestaetigungDokument && (p.arztbestaetigungDokument.bemerkung || p.arztbestaetigungDokument.filenameOriginal))
            || (_azDokId ? ('Dokument #' + _azDokId) : null);
        const empLabel = [p.employeeFirstName || p.firstName, p.employeeLastName || p.lastName].filter(Boolean).join(' ')
            || `MA #${empId}`;
        if (_azDokId) {
            box.innerHTML = `<div style="display:flex;align-items:center;justify-content:space-between;gap:10px">
                <span style="overflow:hidden;text-overflow:ellipsis;white-space:nowrap" title="${String(name || '').replace(/"/g, '&quot;')}">📄 ${name || 'Arztbestätigung'} <span style="color:#8b8b8b;font-weight:500">(${empLabel})</span></span>
                <button type="button" onclick="azOpenDokument()" style="flex-shrink:0;background:#3f3f3f;color:#fff;border:none;border-radius:10px;padding:6px 12px;cursor:pointer;font-size:12px;font-weight:700">Anschauen</button>
            </div>`;
        } else {
            box.innerHTML = `<span style="color:#8b8b8b">Bei ${empLabel} ist noch keine Arztbestätigung verknüpft — bitte bei der Schwangerschaftserfassung verbinden.</span>`;
        }
    } catch (e) {
        box.innerHTML = `<span style="color:#b91c1c">Laden fehlgeschlagen: ${e.message}</span>`;
    }
}

function azOpenDokument() {
    if (!_azDokId || !_azDokEmpId) return;
    if (typeof qstOpenBefreiungsDok === 'function') {
        qstOpenBefreiungsDok(_azDokEmpId, _azDokId, { sticky: true });
    } else if (typeof dokOpenPreviewPanel === 'function') {
        dokOpenPreviewPanel(_azDokId, { sticky: true });
    } else {
        alert('Vorschau-Modul nicht geladen.');
    }
}

function azOpenNew() {
    _azEnsureModal();
    _azEditId = null;
    document.getElementById('azModalTitle').textContent = 'Arzt erfassen';
    ['azTitel','azVorname','azNachname','azFachgebiet','azPraxis','azStrasse','azPlz','azOrt','azTelefon','azEmail','azBemerkung']
        .forEach(id => { const el = document.getElementById(id); if (el) el.value = ''; });
    document.getElementById('azAktiv').checked = true;
    document.getElementById('azModal').style.display = 'flex';
    azLoadDokHint();
}

function azOpenEdit(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const a = _azListe.find(x => x.id === id);
    if (!a) return;
    _azEnsureModal();
    _azEditId = id;
    document.getElementById('azModalTitle').textContent = 'Arzt bearbeiten';
    document.getElementById('azTitel').value      = a.titel || '';
    document.getElementById('azVorname').value    = a.vorname || '';
    document.getElementById('azNachname').value   = a.nachname || '';
    document.getElementById('azFachgebiet').value = a.fachgebiet || '';
    document.getElementById('azPraxis').value     = a.praxisName || '';
    document.getElementById('azStrasse').value    = a.strasse || '';
    document.getElementById('azPlz').value        = a.plz || '';
    document.getElementById('azOrt').value        = a.ort || '';
    document.getElementById('azTelefon').value    = a.telefon || '';
    document.getElementById('azEmail').value      = a.email || '';
    document.getElementById('azBemerkung').value  = a.bemerkung || '';
    document.getElementById('azAktiv').checked    = a.aktiv !== false;
    document.getElementById('azModal').style.display = 'flex';
    azLoadDokHint();
}

function azClose() {
    const m = document.getElementById('azModal');
    if (m) m.style.display = 'none';
    if (typeof dokClosePreviewPanel === 'function') dokClosePreviewPanel();
}

async function azSave() {
    const dto = {
        titel:      document.getElementById('azTitel').value || null,
        vorname:    document.getElementById('azVorname').value || '',
        nachname:   document.getElementById('azNachname').value || '',
        fachgebiet: document.getElementById('azFachgebiet').value || null,
        praxisName: document.getElementById('azPraxis').value || null,
        strasse:    document.getElementById('azStrasse').value || null,
        plz:        document.getElementById('azPlz').value || null,
        ort:        document.getElementById('azOrt').value || null,
        telefon:    document.getElementById('azTelefon').value || null,
        email:      document.getElementById('azEmail').value || null,
        bemerkung:  document.getElementById('azBemerkung').value || null,
        aktiv:      document.getElementById('azAktiv').checked
    };
    if (!dto.nachname.trim()) return alert('Bitte mindestens den Nachnamen erfassen.');
    const url = _azEditId ? `/api/aerzte/${_azEditId}` : '/api/aerzte';
    const r = await fetch(url, {
        method: _azEditId ? 'PUT' : 'POST',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify(dto)
    });
    if (!r.ok) { let t = await r.text(); try { t = JSON.parse(t).message || t; } catch(_){} return alert('Speichern fehlgeschlagen: ' + t); }
    azClose();
    aerzteInit();
}

async function azDelete(id) {
    document.querySelectorAll('.dok-menu.show').forEach(m => m.classList.remove('show'));
    const a = _azListe.find(x => x.id === id);
    const ja = await liquidConfirm(
        `Arzt «${[a?.titel, a?.vorname, a?.nachname].filter(Boolean).join(' ')}» wirklich löschen?`,
        { title: 'Arzt löschen?', yesLabel: 'Ja, löschen', noLabel: 'Nein' });
    if (!ja) return;
    const r = await fetch(`/api/aerzte/${id}`, { method: 'DELETE', headers: ah() });
    if (!r.ok) return alert('Löschen fehlgeschlagen: ' + r.status);
    aerzteInit();
}
