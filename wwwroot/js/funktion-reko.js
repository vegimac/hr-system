// ── Funktionen prüfen (Walter-Vorgabe 22.09.2026) ─────────────────────────
// easy@work liefert die Funktion OHNE Historie (/positions ist ein Pivot ohne
// from/to). Bis zum Sync-Riegel vom 22.09.2026 schrieb der Sync die heutige
// Funktion auf JEDEN Vertragsabschnitt — alte Verträge tragen deshalb die
// falsche Funktion (falscher rückwirkender Vertragsausdruck, falscher
// Zeugnis-Werdegang). Hier wird sie aus dem LOHN gegen das L-GAV-Raster
// zurückgerechnet. Übernommen werden NUR eindeutige Treffer und NUR Verträge,
// die in der Vergangenheit geendet haben.

let _frZeilen = [];
let _frFunktionen = [];
let _frAlle = false;   // false = nur offene (Standard), true = auch geprüfte

// Anzeige-Namen wie im Zeugnis (ZeugnisWerdegang.FunktionText, männliche Form).
const FR_LABEL = {
    CREW: 'Crew', HOST_CT: 'Crew-Trainer', SWING: 'Swing Manager',
    SHIFT_LEADER_1_6: 'Schichtführer in Ausbildung', SHIFT_LEADER_7_PLUS: 'Schichtführer',
    ASST_2: 'Assistant Manager', ASST_1: 'Erster Assistant Manager', REST_MANAGER: 'Geschäftsführer',
};

function _frEsc(t) {
    return String(t ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
}

function _frDatum(iso) {
    return iso ? iso.slice(8, 10) + '.' + iso.slice(5, 7) + '.' + iso.slice(0, 4) : '–';
}

async function frRun() {
    const st = document.getElementById('frStatus');
    const alertEl = document.getElementById('frAlert');
    alertEl.innerHTML = '';
    st.textContent = 'Prüfe Verträge …';
    document.getElementById('frCommitBtn').style.display = 'none';
    try {
        if (!_frFunktionen.length) {
            try {
                const rf = await fetch('/api/employments/funktionen', { headers: ah(), cache: 'no-store' });
                if (rf.ok) _frFunktionen = (await rf.json()).map(g => g.code);
            } catch (_) { /* Dropdown bleibt leer, Vorschau geht trotzdem */ }
        }
        const r = await fetch(`/api/employments/funktion-rekonstruktion?alle=${_frAlle}`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const d = await r.json();
        _frZeilen = d.zeilen || [];
        st.textContent = `${d.geprueft} abgelaufene Verträge · ${d.offen} offen · ${d.vorschlaege} Vorschläge`
                       + (_frAlle ? ' · alle angezeigt' : '');
        if (d.vorschlaege > 0) document.getElementById('frCommitBtn').style.display = '';
        frRender();
    } catch (e) {
        st.textContent = '';
        alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:13px">Fehler: ${_frEsc(e.message)}</div>`;
    }
}

function frToggleAlle() {
    _frAlle = document.getElementById('frAlleChk')?.checked ?? false;
    frRun();
}

function frRender() {
    const box = document.getElementById('frResults');
    if (!_frZeilen.length) {
        box.innerHTML = `<div class="card" style="padding:18px;color:#166534;background:#f0fdf4;border:1px solid #bbf7d0">
            ✓ Nichts offen — alle abgelaufenen Verträge sind geprüft oder stimmen mit dem Lohn überein.</div>`;
        return;
    }
    const rows = _frZeilen.map(z => {
        const neu = z.neu
            ? `<b style="color:#15803d">${_frEsc(z.neu)}</b>`
            : `<span style="color:#94a3b8">–</span>`;
        const sich = { Exakt: ['#dcfce7', '#166534', 'eindeutig'],
                       Ungefaehr: ['#fef3c7', '#92400e', 'ungefähr'],
                       Unklar: ['#f1f5f9', '#64748b', 'unklar'] }[z.sicherheit] || ['#f1f5f9', '#64748b', z.sicherheit];
        return `<tr style="border-bottom:1px solid #f1f5f9;${z.neu ? 'background:#f8fdf9' : ''}">
            <td style="padding:7px 10px">${_frEsc(z.employeeName)}<div style="font-size:11px;color:#94a3b8">${_frEsc(z.employeeNumber || '')}</div></td>
            <td style="padding:7px 10px;white-space:nowrap">${_frDatum(z.von)} – ${_frDatum(z.bis)}</td>
            <td style="padding:7px 10px">${_frEsc(z.modell)}</td>
            <td style="padding:7px 10px;text-align:right;white-space:nowrap">${z.lohn != null ? Number(z.lohn).toLocaleString('de-CH', { minimumFractionDigits: 2 }) : '–'}${z.lohnart === 'hourly' ? ' /h' : ' /Mt.'}</td>
            <td style="padding:7px 10px">${_frEsc(z.alt || '–')}</td>
            <td style="padding:7px 10px">${neu}</td>
            <td style="padding:7px 10px">
                <select data-alt="${_frEsc(z.alt || '')}" onchange="frSetzeFunktion(${z.employmentId}, this.value, this)"
                        style="font-size:12px;padding:3px 6px;border:1px solid #cbd5e1;border-radius:6px;background:#fff">
                    <option value="">– von Hand setzen –</option>
                    ${_frFunktionen.map(c => `<option value="${c}" ${c === (z.alt || '') ? 'selected' : ''}>${FR_LABEL[c] || c}</option>`).join('')}
                </select>
            </td>
            <td style="padding:7px 10px"><span style="font-size:11px;font-weight:700;padding:2px 8px;border-radius:9px;background:${sich[0]};color:${sich[1]}">${sich[2]}</span></td>
            <td style="padding:7px 10px;font-size:11.5px;color:#64748b">${_frEsc(z.begruendung)}</td>
            <td style="padding:7px 10px;text-align:center">
                <input type="checkbox" ${z.geprueft ? 'checked' : ''} title="Geprüft — verschwindet aus der Liste"
                       onchange="frSetzeGeprueft(${z.employmentId}, this.checked, this)"
                       style="width:16px;height:16px;accent-color:#3f3f3f;cursor:pointer">
            </td>
        </tr>`;
    }).join('');
    box.innerHTML = `<div class="card" style="padding:0;overflow:auto">
        <table style="width:100%;border-collapse:collapse;font-size:13px">
            <thead><tr style="background:#f8fafc;text-align:left">
                <th style="padding:8px 10px">Mitarbeiter</th>
                <th style="padding:8px 10px">Vertrag</th>
                <th style="padding:8px 10px">Modell</th>
                <th style="padding:8px 10px;text-align:right">Lohn</th>
                <th style="padding:8px 10px">gespeichert</th>
                <th style="padding:8px 10px">aus Lohn</th>
                <th style="padding:8px 10px">manuell</th>
                <th style="padding:8px 10px">Sicherheit</th>
                <th style="padding:8px 10px">Begründung</th>
                <th style="padding:8px 10px;text-align:center" title="Geprüft — Zeile verschwindet aus der Kontrollliste">erledigt</th>
            </tr></thead>
            <tbody>${rows}</tbody>
        </table></div>`;
}

// Manuelle Zuordnung EINER Zeile (Walter 22.09.2026): für die Fälle, die der
// Lohn nicht eindeutig sagt. Schreibt sofort in den Vertragsabschnitt.
async function frSetzeFunktion(employmentId, code, sel) {
    if (!code) return;
    const alt = sel.dataset.alt || '';
    sel.disabled = true;
    try {
        const r = await fetch(`/api/employments/${employmentId}/funktion`, {
            method: 'PUT', headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ jobGroupCode: code })
        });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); msg = j.message || j.error || msg; } catch (_) {}
            throw new Error(msg);
        }
        const z = _frZeilen.find(x => x.employmentId === employmentId);
        if (z) { z.alt = code; z.neu = null; z.sicherheit = 'Manuell'; z.begruendung = 'Von Hand gesetzt.'; }
        sel.dataset.alt = code;
        document.getElementById('frAlert').innerHTML =
            `<div style="background:#dcfce7;border:1px solid #bbf7d0;color:#166534;padding:8px 12px;border-radius:8px;font-size:13px">Funktion gesetzt: ${FR_LABEL[code] || code}.</div>`;
    } catch (e) {
        sel.value = alt;
        document.getElementById('frAlert').innerHTML =
            `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:8px 12px;border-radius:8px;font-size:13px">Fehler: ${_frEsc(e.message)}</div>`;
    } finally { sel.disabled = false; }
}

// «Erledigt»-Haken (Walter 22.09.2026): setzt employment.funktion_geprueft.
// Die Zeile verschwindet beim nächsten Aufruf aus der Liste — wer und wann steht
// im Aktivitäts-Log. Haken wieder entfernen holt sie zurück.
async function frSetzeGeprueft(employmentId, wert, box) {
    box.disabled = true;
    try {
        const r = await fetch(`/api/employments/${employmentId}/funktion-geprueft?wert=${wert}`,
            { method: 'PUT', headers: ah() });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const z = _frZeilen.find(x => x.employmentId === employmentId);
        if (z) z.geprueft = wert;
        const tr = box.closest('tr');
        if (tr) tr.style.opacity = wert ? '0.45' : '';
    } catch (e) {
        box.checked = !wert;
        document.getElementById('frAlert').innerHTML =
            `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:8px 12px;border-radius:8px;font-size:13px">Fehler: ${_frEsc(e.message)}</div>`;
    } finally { box.disabled = false; }
}

async function frCommit() {
    const anzahl = _frZeilen.filter(z => z.neu).length;
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm(`${anzahl} Vertragsabschnitte bekommen die aus dem Lohn ermittelte Funktion. Nur abgelaufene Verträge, nur eindeutige Treffer. Änderungen stehen im Aktivitäts-Log.`,
            { title: 'Funktionen übernehmen?', yesLabel: 'Übernehmen', noLabel: 'Abbrechen' })
        : confirm(`${anzahl} Vertragsabschnitte korrigieren?`);
    if (!ok) return;
    const st = document.getElementById('frStatus');
    st.textContent = 'Übernehme …';
    try {
        const r = await fetch('/api/employments/funktion-rekonstruktion', {
            method: 'POST', headers: { ...ah(), 'Content-Type': 'application/json' }, body: '{}'
        });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const d = await r.json();
        document.getElementById('frAlert').innerHTML =
            `<div style="background:#dcfce7;border:1px solid #bbf7d0;color:#166534;padding:10px 12px;border-radius:8px;font-size:13px">${d.uebernommen} Vertragsabschnitte korrigiert.</div>`;
        await frRun();
    } catch (e) {
        st.textContent = '';
        document.getElementById('frAlert').innerHTML =
            `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:13px">Fehler: ${_frEsc(e.message)}</div>`;
    }
}
