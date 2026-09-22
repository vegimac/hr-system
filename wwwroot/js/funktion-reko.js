// ── Funktionen prüfen (Walter-Vorgabe 22.09.2026) ─────────────────────────
// easy@work liefert die Funktion OHNE Historie (/positions ist ein Pivot ohne
// from/to). Bis zum Sync-Riegel vom 22.09.2026 schrieb der Sync die heutige
// Funktion auf JEDEN Vertragsabschnitt — alte Verträge tragen deshalb die
// falsche Funktion (falscher rückwirkender Vertragsausdruck, falscher
// Zeugnis-Werdegang). Hier wird sie aus dem LOHN gegen das L-GAV-Raster
// zurückgerechnet. Übernommen werden NUR eindeutige Treffer und NUR Verträge,
// die in der Vergangenheit geendet haben.

let _frZeilen = [];

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
        const r = await fetch('/api/employments/funktion-rekonstruktion', { headers: ah(), cache: 'no-store' });
        if (!r.ok) throw new Error('HTTP ' + r.status);
        const d = await r.json();
        _frZeilen = d.zeilen || [];
        st.textContent = `${d.geprueft} abgelaufene Verträge geprüft · ${d.vorschlaege} Vorschläge`;
        if (d.vorschlaege > 0) document.getElementById('frCommitBtn').style.display = '';
        frRender();
    } catch (e) {
        st.textContent = '';
        alertEl.innerHTML = `<div style="background:#fef2f2;border:1px solid #fecaca;color:#991b1b;padding:10px 12px;border-radius:8px;font-size:13px">Fehler: ${_frEsc(e.message)}</div>`;
    }
}

function frRender() {
    const box = document.getElementById('frResults');
    if (!_frZeilen.length) { box.innerHTML = '<div class="card" style="padding:18px;color:#64748b">Keine abgelaufenen Verträge gefunden.</div>'; return; }
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
            <td style="padding:7px 10px"><span style="font-size:11px;font-weight:700;padding:2px 8px;border-radius:9px;background:${sich[0]};color:${sich[1]}">${sich[2]}</span></td>
            <td style="padding:7px 10px;font-size:11.5px;color:#64748b">${_frEsc(z.begruendung)}</td>
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
                <th style="padding:8px 10px">Sicherheit</th>
                <th style="padding:8px 10px">Begründung</th>
            </tr></thead>
            <tbody>${rows}</tbody>
        </table></div>`;
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
