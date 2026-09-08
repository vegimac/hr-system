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
    elmStammLoad();
    tmInit();
}

// ── Testmandant «Muster AG» (Walter 07.09.2026) — nur Testinstanz ──────────
// Karte erscheint nur, wenn der Server INSTANCE_LABEL gesetzt hat (Test).
async function tmInit() {
    const card = document.getElementById('tmCard');
    if (!card) return;
    try {
        const r = await fetch('/api/swissdec/testmandant/status', { headers: ah() });
        if (!r.ok) return;
        const j = await r.json();
        if (!j.istTestinstanz) return;      // Produktiv: Karte bleibt unsichtbar
        card.style.display = '';
        tmRenderStatus(j);
    } catch (_) {}
}
function tmRenderStatus(j) {
    const el = document.getElementById('tmStatus');
    if (!el) return;
    const csv = Object.entries(j.csv || {}).map(([n, ok]) => `${ok ? '✓' : '✗'} ${n}`).join(' · ');
    const fil = (j.filialen || []).length
        ? `<table style="border-collapse:collapse;font-size:12px;margin-top:6px"><thead><tr>${['Code','Bezeichnung','Ort','BUR-Nr.','BFS-Gemeinde'].map(h => `<th style="text-align:left;padding:2px 10px 2px 0;color:#8b8b8b;font-weight:600">${h}</th>`).join('')}</tr></thead><tbody>`
          + j.filialen.map(f => `<tr>${[f.restaurantCode, f.branchName, f.city, f.burNummer, f.bfsGemeindeNr].map(v => `<td style="padding:2px 10px 2px 0">${esc(String(v ?? '–'))}</td>`).join('')}</tr>`).join('')
          + '</tbody></table>'
        : '<div style="color:#8b8b8b;margin-top:4px">Noch keine Filialen des Testmandanten.</div>';
    el.innerHTML = `<div>Dateien: ${csv}</div>`
        + `<div style="margin-top:4px">Hauptsitz: <b>${j.hauptsitz ? esc(j.hauptsitz.name + ' · ' + j.hauptsitz.uid) : '– noch nicht angelegt –'}</b></div>`
        + fil;
}
async function tmSchritt(nr, vorschau) {
    const out = document.getElementById('tmErgebnis');
    if (!vorschau && !(await liquidConfirm(`Schritt ${nr} jetzt ANLEGEN? (Vorschau vorher angeschaut?)`))) return;
    out.innerHTML = '⏳ …';
    try {
        const qs = [];
        // 5b hat eigene Filterfelder in seiner Zeile (Walter 08.09.2026); 4a/4c nutzen die oberen.
        const nurEl   = document.getElementById(String(nr) === '5b' ? 'tmNur5b'   : 'tmNur');
        const monatEl = document.getElementById(String(nr) === '5b' ? 'tmMonat5b' : 'tmMonat');
        if (['4', '4c', '5b'].includes(String(nr)) && nurEl?.value.trim()) qs.push(`nur=${encodeURIComponent(nurEl.value.trim())}`);
        if (['4c', '5b'].includes(String(nr)) && monatEl?.value) qs.push(`monat=${encodeURIComponent(monatEl.value)}`);
        const nur = qs.length ? '?' + qs.join('&') : '';
        const r = await fetch(`/api/swissdec/testmandant/schritt${nr}/${vorschau ? 'vorschau' : 'anlegen'}${nur}`,
            { method: vorschau ? 'GET' : 'POST', headers: ah() });
        const j = await r.json().catch(() => null);
        if (!r.ok) { out.innerHTML = `<span style="color:#b91c1c">✗ ${esc(j?.message || j?.error || ('HTTP ' + r.status))}</span>`; return; }
        const kopf = `<div style="font-weight:700;margin-bottom:6px">${vorschau ? '🔍 Vorschau' : '✓ Angelegt'} — ${esc(j.schritt)} · ${j.aktionen.length} Aktionen</div>`;
        const rows = j.aktionen.map(a => {
            const felder = Object.entries(a.felder || {}).filter(([, v]) => v != null && v !== '')
                .map(([k, v]) => `<span style="white-space:nowrap"><span style="color:#8b8b8b">${esc(k)}:</span> ${esc(String(v))}</span>`).join(' · ');
            const farbe = a.typ === 'anlegen' ? '#15803d' : '#b45309';
            return `<div style="padding:6px 0;border-top:1px solid #eee"><span style="color:${farbe};font-weight:700">${a.typ === 'anlegen' ? '＋ anlegen' : '↻ aktualisieren'}</span> <b>${esc(a.objekt)}</b> ${esc(a.was)}<div style="margin-top:2px;line-height:1.6">${felder}</div></div>`;
        }).join('');
        const hinw = (j.hinweise || []).length
            ? `<div style="margin-top:8px;background:#fdf1dc;border:1px solid #f3d9a4;border-radius:8px;padding:8px 10px;color:#7c5a10">${j.hinweise.map(h => '⚠ ' + esc(h)).join('<br>')}</div>` : '';
        out.innerHTML = kopf + rows + hinw;
        if (!vorschau) tmInit();
    } catch (e) { out.innerHTML = `<span style="color:#b91c1c">Verbindungsfehler: ${esc(e.message)}</span>`; }
}

// ── E3: Stammdaten Rechtseinheit ────────────────────────────────────────────
// Nummern kommen aus dem Empfänger-Katalog (nur Anzeige); erfasst werden hier
// nur UID der Rechtseinheit + «versichert seit» (Walter 28.08.2026).
const _elmStFields = { elmStUvgSeit: 'uvgVersichertSeit', elmStBvgSeit: 'bvgVersichertSeit' };

async function elmStammLoad() {
    try {
        const r = await fetch('/api/elm/stammdaten', { headers: ah() });
        if (r.ok) {
            const j = await r.json();
            for (const [id, key] of Object.entries(_elmStFields)) {
                const el = document.getElementById(id);
                if (el) el.value = (j[key] || '').toString().slice(0, el.type === 'date' ? 10 : undefined) || '';
            }
            const s = document.getElementById('elmStammStatus');
            if (s && j.updatedAt) s.textContent = `Zuletzt gespeichert: ${new Date(j.updatedAt).toLocaleDateString('de-CH')}`;
        }
    } catch { /* still */ }
    elmStKatalogLoad();
    elmStHauptsitzLoad();
}

async function elmStHauptsitzLoad() {
    const box = document.getElementById('elmStUidInfo');
    if (!box) return;
    try {
        const r = await fetch('/api/hauptsitze', { headers: ah() });
        const list = r.ok ? await r.json() : [];
        const aktive = list.filter(h => h.isActive);
        if (!aktive.length) {
            box.innerHTML = '<span style="color:#b45309">Kein Hauptsitz erfasst — System → Filialen &amp; Benutzer → Hauptsitze.</span>';
        } else {
            box.innerHTML = aktive.map(h =>
                `<b>${esc(h.name)}</b> ${h.uid ? '· <span style="font-family:ui-monospace,Menlo,monospace">' + esc(h.uid) + '</span>' : '· <span style="color:#b45309">⚠ UID fehlt</span>'} · ${(h.filialen || []).length} Filiale(n)`
            ).join('<br>');
        }
    } catch { box.textContent = '—'; }
}

async function elmStKatalogLoad() {
    const box = document.getElementById('elmStKatalog');
    if (!box) return;
    try {
        const r = await fetch('/api/elm/stammdaten/vorschlag', { headers: ah() });
        const j = await r.json();
        if (!r.ok) { box.innerHTML = '<span style="color:#b91c1c">Katalog konnte nicht geladen werden.</span>'; return; }
        const w = j.werte || {};
        const zeile = (label, name, nr, kd, vertr, uid) => {
            const teile = [];
            if (name) teile.push(`<b>${esc(name)}</b>`);
            if (nr) teile.push(`Nr. ${esc(nr)}`);
            if (kd) teile.push(`Mitglied/Kunde ${esc(kd)}`);
            if (vertr) teile.push(`Sub/Vertrag ${esc(vertr)}`);
            if (uid) teile.push(`UID ${esc(uid)}`);
            return `<div style="padding:3px 0;border-bottom:1px solid rgba(60,55,48,0.08)">
                <span style="display:inline-block;width:150px;font-weight:600;color:#646464">${label}</span>
                ${teile.length ? teile.join(' · ') : '<span style="color:#b0aca4">— im Empfänger-Katalog erfassen</span>'}</div>`;
        };
        box.innerHTML =
            zeile('AHV-Ausgleichskasse', w.akName, w.akKassenNummer, w.akAbrechnungsNummer, null, null) +
            zeile('FAK', null, w.fakKassenNummer, w.fakAbrechnungsNummer, null, null) +
            zeile('UVG', w.uvgVersicherer, w.uvgVersichererNummer, w.uvgKundenNummer, w.uvgVertragsNummer, w.uvgUid) +
            zeile('UVG-Zusatz', w.uvgzVersicherer, w.uvgzVersichererNummer, w.uvgzKundenNummer, w.uvgzVertragsNummer, null) +
            zeile('KTG', w.ktgVersicherer, w.ktgVersichererNummer, w.ktgKundenNummer, w.ktgVertragsNummer, null) +
            zeile('BVG', w.bvgVersicherer, w.bvgVersichererNummer, w.bvgKundenNummer, w.bvgVertragsNummer, w.bvgUid) +
            ((j.hinweise || []).length
                ? `<div style="color:#92400e;margin-top:6px">${j.hinweise.map(esc).join('<br>')}</div>` : '');
    } catch (e) {
        box.innerHTML = `<span style="color:#b91c1c">Verbindungsfehler: ${esc(e.message)}</span>`;
    }
}

async function elmStammSave() {
    const dto = {};
    for (const [id, key] of Object.entries(_elmStFields))
        dto[key] = document.getElementById(id)?.value || null;
    const s = document.getElementById('elmStammStatus');
    try {
        const r = await fetch('/api/elm/stammdaten', {
            method: 'PUT',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(dto)
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { if (s) { s.textContent = j.message || 'Speichern fehlgeschlagen.'; s.style.color = '#b91c1c'; } return; }
        if (s) { s.textContent = '✓ Gespeichert.'; s.style.color = '#166534'; }
    } catch (e) {
        if (s) { s.textContent = 'Verbindungsfehler: ' + e.message; s.style.color = '#b91c1c'; }
    }
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
    if (!_elmAnnualXml) {
        alert('Bitte zuerst «XML erzeugen & prüfen» klicken — das XML liegt nur direkt nach dem Erzeugen bereit.');
        return;
    }
    const name = `elm-jahresmeldung-ahv-${_elmAnnualYearBuilt || 'jahr'}.xml`;
    try {
        const blob = new Blob([_elmAnnualXml], { type: 'application/xml' });
        saveBlobAsk(blob, name);
    } catch (e) {
        // Fallback ohne Blob-Konstruktor (Browser-Erweiterungen wie
        // «location-spoofing» kapern new Blob() und werfen — Walter 28.08.2026):
        // data:-URL + Anker-Klick lädt direkt in den Downloads-Ordner.
        const a = document.createElement('a');
        a.href = 'data:application/xml;charset=utf-8,' + encodeURIComponent(_elmAnnualXml);
        a.download = name;
        document.body.appendChild(a);
        a.click();
        a.remove();
    }
}
