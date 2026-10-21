// Kontoplan (Fibu): eine Zeile pro Lohnart.
// Aufwand 4000/4001/4055 kommt vom Vertrag — keine Kostenstellen-Zeilen
// (Walter 20.10.2026). Nur wenn das Gegenkonto wirklich abweicht
// (z.B. 13. ML Management 2016 / Crew 2017), bleibt eine eigene Zeile.

let _kpRows = [];
let _kpArten = [];

// Journal-Zeilen ohne Lohnposition: Nettolohn, Rückstellungen, Akonto, Übertrag.
const KP_BUCHUNG = new Set(['1010', '1050', '1060', '1080', '2010', '4070']);

const KP_SOLL_VERTRAG = { CREW: '4000', FLEX: '4001', MGMT: '4055' };

async function kpInit() {
    const tbody = document.getElementById('kpTableBody');
    if (tbody) tbody.innerHTML = '<tr><td colspan="6" style="padding:24px;color:#94a3b8;font-size:13px;text-align:center">Lade Kontoplan…</td></tr>';
    try {
        const [r, a] = await Promise.all([
            fetch('/api/lohn-konto-mapping', { headers: ah() }),
            fetch('/api/lohn-konto-mapping/nach-lohnart', { headers: ah() })
        ]);
        if (!r.ok || !a.ok) {
            const status = !r.ok ? r.status : a.status;
            if (tbody) tbody.innerHTML = `<tr><td colspan="6" style="padding:24px;color:#dc2626;font-size:13px">Konnte Kontoplan nicht laden (HTTP ${status}).</td></tr>`;
            return;
        }
        _kpRows = await r.json() || [];
        _kpArten = await a.json() || [];
        kpRender();
    } catch (e) {
        if (tbody) tbody.innerHTML = `<tr><td colspan="6" style="padding:24px;color:#dc2626;font-size:13px">Verbindungsfehler: ${e.message}</td></tr>`;
    }
}

function kpGruppeKey(m) {
    return String(m.position);
}

function kpHatLohnart(m) {
    return /^\d{4}$/.test(String(m.lohnartCode || '')) || /^\d+\.\d+$/.test(String(m.lohnartCode || ''));
}

function kpOhneModell(text) {
    return String(text || '')
        .replace(/\s+Crew\s+Fix\s*\/\s*MTP/ig, '')
        .replace(/\s+Crew\s+Flex/ig, '')
        .replace(/\s+Crew\s+Fix/ig, '')
        .replace(/\s+Manager/ig, '')
        .replace(/\s+Gerant/ig, '')
        .replace(/\s{2,}/g, ' ')
        .trim();
}

function kpModellName(modell) {
    if (modell === 'FLEX') return 'Flex';
    if (modell === 'MGMT') return 'Management';
    if (modell === 'CREW') return 'Crew';
    return '';
}

// Zeilen, die sich nur im Aufwandskonto des Vertrags unterscheiden, werden eine.
// Das Konto 4000/4001/4055 kann auf der Soll- oder auf der Habenseite stehen
// (Rückstellung Vormonat bucht 2019 an 4000).
function kpZusammenziehen(liste) {
    const buckets = new Map();
    liste.forEach(m => {
        const key = [
            m.subPosition ?? '',
            m.mwstKonto || '',
            m.mwstCode || '',
            m.isVormonat ? '1' : '0'
        ].join('|');
        if (!buckets.has(key)) buckets.set(key, []);
        buckets.get(key).push(m);
    });
    return [...buckets.values()].map(zeilen => {
        const modelle = [...new Set(zeilen.map(m => m.modell).filter(Boolean))];
        const sollVomVertrag = modelle.length >= 2
            && zeilen.every(m => !m.modell || KP_SOLL_VERTRAG[m.modell] === m.fibukonto)
            && new Set(zeilen.map(m => m.gegenkonto)).size === 1;
        const habenVomVertrag = modelle.length >= 2
            && zeilen.every(m => !m.modell || KP_SOLL_VERTRAG[m.modell] === m.gegenkonto)
            && new Set(zeilen.map(m => m.fibukonto)).size === 1;
        return { zeilen, sollVomVertrag, habenVomVertrag };
    });
}

function kpRender() {
    const tbody = document.getElementById('kpTableBody');
    if (!tbody) return;
    const q = (document.getElementById('kpSearch')?.value || '').toLowerCase().trim();

    let arten = _kpArten;
    let rows = _kpRows.filter(m => KP_BUCHUNG.has(String(m.position)));
    if (q) {
        arten = arten.filter(m =>
            (m.code || '').toLowerCase().includes(q) ||
            (m.bezeichnung || '').toLowerCase().includes(q) ||
            (m.soll || '').toLowerCase().includes(q) ||
            (m.haben || '').toLowerCase().includes(q) ||
            (m.hinweis || '').toLowerCase().includes(q));
        rows = rows.filter(m =>
            String(m.position).includes(q) ||
            (m.lohnartCode || '').toLowerCase().includes(q) ||
            (m.fibukonto || '').toLowerCase().includes(q) ||
            (m.gegenkonto || '').toLowerCase().includes(q) ||
            (m.bezeichnung || '').toLowerCase().includes(q));
    }

    if (!arten.length && !rows.length) {
        const info = document.getElementById('kpInfo');
        if (info) info.textContent = '0 Lohnpositionen';
        tbody.innerHTML = '<tr><td colspan="6" style="padding:24px;color:#94a3b8;font-size:13px;text-align:center">Keine Einträge.</td></tr>';
        return;
    }

    const groups = {};
    rows.forEach(m => { (groups[kpGruppeKey(m)] ||= []).push(m); });

    const esc = s => String(s ?? '').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
    const sortKeys = keys => keys.sort((a, b) => {
        const na = Number(a), nb = Number(b);
        if (!Number.isNaN(na) && !Number.isNaN(nb)) return na - nb;
        return String(a).localeCompare(String(b), 'de');
    });
    const buchKeys = sortKeys(Object.keys(groups));

    let body = '';
    let anzahl = 0;
    function abschnitt(titel) {
        body += `<tr><td colspan="6" style="background:#e7e4db;padding:10px 12px;font-weight:700;font-size:12px;letter-spacing:.04em;text-transform:uppercase;color:#57534e">${titel}</td></tr>`;
    }
    function artZeile(m) {
        anzahl++;
        const soll = m.ohneBuchung
            ? '—'
            : (m.soll === 'VERTRAG'
                ? '<span title="Crew 4000 · Flex 4001 · Management 4055">vom Vertrag</span>'
                : (m.zugeordnet ? esc(m.soll) : '<span style="color:#b91c1c">offen</span>'));
        const haben = m.ohneBuchung ? '—' : (m.haben ? esc(m.haben) : '');
        const ag = (m.agSoll && m.agHaben) ? `AG ${esc(m.agSoll)} / ${esc(m.agHaben)}` : '';
        const hinweis = m.ohneBuchung ? 'keine Buchung' : (m.hinweis || '');
        const extra = [ag, esc(hinweis)].filter(Boolean).join(' · ');
        body += `<tr data-art="${esc(m.code)}" style="border-top:1px solid #f1f5f9">
            <td style="padding:6px 10px;color:#64748b;font-family:monospace">${esc(m.code)}</td>
            <td style="padding:6px 10px"><span class="kp-bez">${esc(m.bezeichnung)}</span></td>
            <td style="padding:6px 10px;font-family:monospace;font-weight:700;color:#166534"><span class="kp-soll">${soll}</span></td>
            <td style="padding:6px 10px;font-family:monospace;font-weight:700;color:#b91c1c"><span class="kp-gegen">${haben}</span></td>
            <td class="kp-extra" style="padding:6px 10px;font-size:12px;color:#78716c">${extra}</td>
            <td style="padding:6px 10px"></td>
        </tr>`;
    }
    function zeile(block) {
        anzahl++;
        const m = block.zeilen[0];
        const ids = block.zeilen.map(z => z.id).join(',');
        const lohnart = kpHatLohnart(m) ? esc(m.lohnartCode) : '';
        let bez = kpOhneModell(m.bezeichnung);
        const kontoVertrag = `<span title="Crew 4000 · Flex 4001 · Management 4055 — kommt vom Vertrag">vom Vertrag</span>`;
        let soll = `<span class="kp-soll">${esc(m.fibukonto)}</span>`;
        let gegen = `<span class="kp-gegen">${esc(m.gegenkonto)}</span>`;
        if (block.sollVomVertrag) soll = `<span class="kp-soll">${kontoVertrag}</span>`;
        else if (block.habenVomVertrag) gegen = `<span class="kp-gegen">${kontoVertrag}</span>`;
        else {
            const name = kpModellName(m.modell);
            if (name && block.zeilen.length === 1) bez = `${bez} · ${name}`;
        }
        const vorm = m.isVormonat ? '<span style="color:#b45309;font-size:10.5px;font-weight:600">Vormonat</span>' : '';
        const pz = (m.mwstProzent != null && Number(m.mwstProzent) > 0) ? ` · ${Number(m.mwstProzent)}%` : '';
        const mwst = m.mwstKonto
            ? `<span style="background:#ecfdf5;color:#047857;padding:1px 7px;border-radius:7px;font-size:10.5px;font-weight:600;white-space:nowrap" title="Abacus TaxAccount/TaxCode">MWST ${esc(m.mwstKonto)} / ${esc(m.mwstCode)}${pz}</span>`
            : '';
        const seite = block.sollVomVertrag ? 'soll' : (block.habenVomVertrag ? 'haben' : '');
        body += `<tr data-ids="${ids}" data-vertrag="${seite ? '1' : '0'}" data-seite="${seite}" style="border-top:1px solid #f1f5f9">
            <td style="padding:6px 10px;color:#64748b;font-family:monospace">${lohnart}</td>
            <td style="padding:6px 10px"><span class="kp-bez">${esc(bez)}</span></td>
            <td style="padding:6px 10px;font-family:monospace;font-weight:700;color:#166534">${soll}</td>
            <td style="padding:6px 10px;font-family:monospace;font-weight:700;color:#b91c1c">${gegen}</td>
            <td class="kp-extra" style="padding:6px 10px">${vorm}${vorm && mwst ? ' ' : ''}${mwst}</td>
            <td style="padding:6px 10px;text-align:right">
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'kp-${m.id}')" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="dokMenu-kp-${m.id}">
                        <button class="dok-menu-item" onclick="dokCloseAllMenus();kpEdit('${ids}')">Bearbeiten</button>
                    </div>
                </div>
            </td>
        </tr>`;
    }
    if (arten.length) abschnitt('Lohnpositionen');
    arten.forEach(artZeile);
    if (buchKeys.length) abschnitt('Buchungen, keine Lohnposition');
    buchKeys.forEach(key => {
        kpZusammenziehen(groups[key]).forEach(zeile);
    });
    const info = document.getElementById('kpInfo');
    const offen = arten.filter(m => !m.zugeordnet).length;
    if (info) info.textContent = offen
        ? `${arten.length} Lohnpositionen · ${offen} ohne Konto`
        : `${arten.length} Lohnpositionen`;
    tbody.innerHTML = body;
}

function kpEdit(idsCsv, artCode) {
    const ids = String(idsCsv).split(',').map(Number);
    const m = _kpRows.find(x => x.id === ids[0]);
    const row = artCode
        ? document.querySelector(`#kpTableBody tr[data-art="${artCode}"]`)
        : document.querySelector(`#kpTableBody tr[data-ids="${idsCsv}"]`);
    if (!m || !row) return;
    const seite = row.getAttribute('data-seite') || '';
    const esc = s => String(s ?? '').replace(/"/g,'&quot;');
    const bez = seite ? kpOhneModell(m.bezeichnung) : m.bezeichnung;
    if (seite !== 'soll') {
        const soll = row.querySelector('.kp-soll');
        if (soll) soll.innerHTML = `<input id="kpSoll_${ids[0]}" value="${esc(m.fibukonto)}" style="width:60px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-family:monospace">`;
    }
    if (seite !== 'haben') {
        const gegen = row.querySelector('.kp-gegen');
        if (gegen) gegen.innerHTML = `<input id="kpGegen_${ids[0]}" value="${esc(m.gegenkonto)}" style="width:60px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-family:monospace">`;
    }
    const extra = row.querySelector('.kp-extra');
    if (extra) {
        extra.innerHTML = `<input id="kpBez_${ids[0]}" value="${esc(bez)}" style="width:180px;padding:2px 4px;border:1px solid #d0c8b8;border-radius:5px;font-size:12px" placeholder="Bezeichnung">
            <button onclick="kpSave('${idsCsv}')" style="margin-left:6px;padding:3px 10px;border-radius:8px;border:0;background:#3f3f3f;color:#fff;font-size:12px;cursor:pointer">Speichern</button>
            <button onclick="kpRender()" style="margin-left:4px;padding:3px 10px;border-radius:8px;border:1px solid #d0c8b8;background:#fff;font-size:12px;cursor:pointer">Abbrechen</button>`;
    }
}

async function kpSave(idsCsv) {
    const ids = String(idsCsv).split(',').map(Number);
    const gegenEingabe = document.getElementById(`kpGegen_${ids[0]}`)?.value?.trim();
    const bezeichnung = document.getElementById(`kpBez_${ids[0]}`)?.value?.trim();
    const sollEingabe = document.getElementById(`kpSoll_${ids[0]}`)?.value?.trim();
    if (!bezeichnung) return;
    for (const id of ids) {
        const m = _kpRows.find(x => x.id === id);
        if (!m) continue;
        const fibukonto = sollEingabe || m.fibukonto;
        const gegenkonto = gegenEingabe || m.gegenkonto;
        const r = await fetch(`/api/lohn-konto-mapping/${id}`, {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                fibukonto, gegenkonto, bezeichnung,
                mwstKonto: m.mwstKonto, mwstCode: m.mwstCode, mwstProzent: m.mwstProzent
            })
        });
        if (!r.ok) {
            const err = await r.json().catch(() => ({}));
            alert(err.error || `Speichern fehlgeschlagen (${r.status})`);
            return;
        }
        Object.assign(m, await r.json());
    }
    kpRender();
}
