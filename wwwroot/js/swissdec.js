// ═══════════════════════════════════════════════════════════════════════════
//  SWISSDEC ELM 6.0 — Admin-Bereich (Walter 27.08.2026)
//  Etappe E1: Ping + CheckInteroperability gegen Distributor/Refapps.
//  Konzept: docs/swissdec-elm6-konzept.md. NUR manuell auslösen (Richtlinien
//  Kap. 4: Ping nie automatisieren).
// ═══════════════════════════════════════════════════════════════════════════

function swissdecInit() {
    const el = document.getElementById('elmUrl');
    if (el && !el.value) el.value = localStorage.getItem('elmEndpointUrl') || '';
    const y = document.getElementById('elmAnnualYear');
    if (y && !y.value) y.value = new Date().getFullYear();
}

function elmSetUrl(url) {
    const el = document.getElementById('elmUrl');
    if (el) { el.value = url; localStorage.setItem('elmEndpointUrl', url); }
}

async function _elmCall(pfad, label) {
    const out = document.getElementById('elmResult');
    const url = (document.getElementById('elmUrl')?.value || '').trim();
    if (!url) { if (out) out.innerHTML = '<div style="color:#b91c1c">Bitte zuerst die Endpoint-URL eintragen (Refapps Receiver oder Distributor).</div>'; return; }
    localStorage.setItem('elmEndpointUrl', url);
    if (out) out.innerHTML = `<div style="color:#64748b">⏳ ${label} läuft…</div>`;
    try {
        const r = await fetch(`/api/elm/${pfad}`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ url })
        });
        const j = await r.json();
        if (!r.ok) { out.innerHTML = `<div style="color:#b91c1c">Fehler: ${esc(j?.message || j?.error || ('HTTP ' + r.status))}</div>`; return; }
        const okBadge = j.ok
            ? `<span style="background:#dcfce7;color:#166534;padding:2px 10px;border-radius:8px;font-weight:700">✓ Antwort erhalten</span>`
            : `<span style="background:#fee2e2;color:#b91c1c;padding:2px 10px;border-radius:8px;font-weight:700">✗ ${esc(j.error || 'fehlgeschlagen')}</span>`;
        out.innerHTML = `
            <div style="margin-bottom:8px">${okBadge}
                <span style="color:#64748b;margin-left:8px">HTTP ${j.httpStatus || '—'} · ${j.dauerMs} ms</span></div>
            ${j.responseXml ? `<div style="font-weight:700;margin:6px 0 4px">Antwort</div>
                <pre style="background:#1f2937;color:#d1fae5;padding:10px 12px;border-radius:10px;max-height:340px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(j.responseXml)}</pre>` : ''}
            <details style="margin-top:6px"><summary style="cursor:pointer;color:#64748b;font-size:12px">Gesendete Anfrage anzeigen</summary>
                <pre style="background:#f6f3ee;border:1px solid #e7e1d8;padding:10px 12px;border-radius:10px;max-height:280px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(j.requestXml || '')}</pre></details>`;
    } catch (e) {
        if (out) out.innerHTML = `<div style="color:#b91c1c">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function elmPing() { _elmCall('ping', 'Ping'); }
function elmCheckInterop() { _elmCall('check-interoperability', 'CheckInteroperability'); }

// ── E2: Jahresmeldung AHV (XML) ─────────────────────────────────────────────
let _elmAnnualXml = null;
let _elmAnnualYearBuilt = null;

async function elmAnnualBuild() {
    const out = document.getElementById('elmAnnualResult');
    const dlBtn = document.getElementById('elmAnnualDlBtn');
    const year = parseInt(document.getElementById('elmAnnualYear')?.value || '0', 10);
    if (!year) { out.innerHTML = '<div style="color:#b91c1c">Bitte ein Lohnjahr angeben.</div>'; return; }
    _elmAnnualXml = null;
    if (dlBtn) dlBtn.style.display = 'none';
    out.innerHTML = '<div style="color:#64748b">⏳ Jahresmeldung wird erzeugt und gegen die ELM-6.0-Schemas geprüft…</div>';
    try {
        const r = await fetch(`/api/elm/annual-ahv/${year}`, { headers: ah() });
        const j = await r.json();
        if (!r.ok) { out.innerHTML = `<div style="color:#b91c1c">Fehler: ${esc(j?.message || j?.error || ('HTTP ' + r.status))}</div>`; return; }
        const fmtChf = v => (v ?? 0).toLocaleString('de-CH', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
        const badge = j.valid
            ? '<span style="background:#dcfce7;color:#166534;padding:2px 10px;border-radius:8px;font-weight:700">✓ XSD-valid</span>'
            : '<span style="background:#fee2e2;color:#b91c1c;padding:2px 10px;border-radius:8px;font-weight:700">✗ nicht valid</span>';
        let html = `<div style="margin-bottom:8px">${badge}
            <span style="color:#64748b;margin-left:8px">${j.personen} Personen · AHV-Lohnsumme CHF ${fmtChf(j.totalAhv)} · ALV CHF ${fmtChf(j.totalAlv)}</span></div>`;
        if ((j.xsdFehler || []).length)
            html += `<div style="font-weight:700;color:#b91c1c;margin:6px 0 4px">Schema-Fehler</div>
                <pre style="background:#fee2e2;color:#7f1d1d;padding:8px 10px;border-radius:10px;max-height:220px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(j.xsdFehler.join('\n'))}</pre>`;
        if ((j.warnungen || []).length)
            html += `<div style="font-weight:700;color:#92400e;margin:6px 0 4px">Hinweise (${j.warnungen.length})</div>
                <ul style="margin:0 0 6px;padding-left:18px;color:#92400e;max-height:200px;overflow:auto">${j.warnungen.map(w => `<li>${esc(w)}</li>`).join('')}</ul>`;
        if (j.xml) {
            _elmAnnualXml = j.xml;
            _elmAnnualYearBuilt = year;
            if (dlBtn) dlBtn.style.display = '';
            html += `<details style="margin-top:6px"><summary style="cursor:pointer;color:#64748b;font-size:12px">XML ansehen (${Math.round(j.xml.length / 1024)} KB)</summary>
                <pre style="background:#1f2937;color:#d1fae5;padding:10px 12px;border-radius:10px;max-height:380px;overflow:auto;font-size:11px;white-space:pre-wrap">${esc(j.xml)}</pre></details>`;
        }
        out.innerHTML = html;
    } catch (e) {
        out.innerHTML = `<div style="color:#b91c1c">Verbindungsfehler: ${esc(e.message)}</div>`;
    }
}

function elmAnnualDownload() {
    if (!_elmAnnualXml) return;
    const blob = new Blob([_elmAnnualXml], { type: 'application/xml' });
    saveBlobAsk(blob, `elm-jahresmeldung-ahv-${_elmAnnualYearBuilt || 'jahr'}.xml`);
}
