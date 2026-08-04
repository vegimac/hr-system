// ══════════════════════════════════════════════════════════════════════
// fibu.js — Buchhaltungs-Bereich (Walter-Vorgabe 24.05.2026)
//
// Eigener Menüpunkt „Fibu" für die Rolle `buchhaltung` (+ admin). Enthält
// Fibu-Journal + Buchhaltungs-Saldo-Liste — aus dem Lohnlauf hierher gezogen,
// damit der Lohnlauf sauber bleibt. Filiale folgt dem globalen Sidebar-Selektor
// (fixedCompanyProfileId), Periode = eigene Jahr/Monat-Wahl. Server prüft den
// Filial-Zugriff (user_branch_access) zusätzlich serverseitig.
// ══════════════════════════════════════════════════════════════════════

async function fibuInit() {
    // Filiale anzeigen (aus globalem Selektor).
    const lbl = document.getElementById('fibuBranchLabel');
    const b = (typeof allBranches !== 'undefined' ? allBranches : [])
        .find(x => x.id === fixedCompanyProfileId);
    if (lbl) lbl.textContent = b
        ? `${b.restaurantCode ? b.restaurantCode + ' – ' : ''}${b.branchName || b.companyName}`
        : 'Bitte oben eine Filiale wählen';

    // Periode defaulten (Walter-Vorgabe 04.08.2026): die ÄLTESTE noch nicht
    // definitiv abgeschlossene Periode der Filiale — das ist die Periode, an
    // der gerade gearbeitet wird (provisorisch/BEI_HR zählt als offen, analog
    // Stichtag-Konvention). Keine offene Periode → die neueste abgeschlossene
    // (Journal bleibt einsehbar). Gar keine Periode → aktueller Monat.
    const y = document.getElementById('fibuYear');
    const m = document.getElementById('fibuMonth');
    if (!y || !m) return;
    const now = new Date();
    let py = now.getFullYear(), pm = now.getMonth() + 1;
    try {
        if (typeof fixedCompanyProfileId !== 'undefined' && fixedCompanyProfileId) {
            const r = await fetch(`/api/payroll-perioden?companyProfileId=${fixedCompanyProfileId}`, { headers: ah() });
            if (r.ok) {
                const list = await r.json();
                const offen = list
                    .filter(p => p.status !== 'abgeschlossen')
                    .sort((a, b) => (a.year - b.year) || (a.month - b.month));
                const pick = offen[0] || list[0]; // Liste kommt absteigend → [0] = neueste
                if (pick) { py = pick.year; pm = pick.month; }
            }
        }
    } catch (_) { /* Fallback: aktueller Monat */ }
    y.value = py;
    m.value = String(pm);
    fibuSyncEntryDate();
}

// Buchungsdatum Abacus (EntryDate im AbaConnect-XML) auf den Monatsletzten
// der gewählten Periode setzen (Treuhänder-Empfehlung 04.08.2026: Default
// Monatsletzter, aber vom Benutzer änderbar). Läuft bei Init + bei jeder
// Jahr-/Monat-Änderung — überschreibt damit bewusst eine manuelle Wahl,
// sobald die PERIODE wechselt (das alte Datum wäre dann sicher falsch).
function fibuSyncEntryDate() {
    const y = parseInt(document.getElementById('fibuYear')?.value || '0', 10);
    const m = parseInt(document.getElementById('fibuMonth')?.value || '0', 10);
    const d = document.getElementById('fibuEntryDate');
    if (!d || !y || !m) return;
    const last = new Date(y, m, 0); // Tag 0 des Folgemonats = Monatsletzter
    d.value = `${y}-${String(m).padStart(2, '0')}-${String(last.getDate()).padStart(2, '0')}`;
}

function _fibuParams() {
    const cid = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
    const y = parseInt(document.getElementById('fibuYear')?.value || '0', 10);
    const m = parseInt(document.getElementById('fibuMonth')?.value || '0', 10);
    if (!cid)      { alert('Bitte oben im Sidebar eine Filiale wählen.'); return null; }
    if (!y || !m)  { alert('Bitte Jahr und Monat wählen.'); return null; }
    return { cid, y, m };
}

async function fibuFibuJournal() {
    const p = _fibuParams(); if (!p) return;
    await _fibuFetchPdf(
        `/api/payroll/fibu-journal?companyProfileId=${p.cid}&year=${p.y}&month=${p.m}`,
        `Fibu-Journal_${p.cid}_${p.y}-${String(p.m).padStart(2, '0')}.pdf`);
}

async function fibuSaldoListe() {
    const p = _fibuParams(); if (!p) return;
    await _fibuFetchPdf(
        `/api/payroll/saldo-liste-buchhaltung?companyProfileId=${p.cid}&year=${p.y}&month=${p.m}`,
        `Lohn-Saldi-Buchhaltung_${p.cid}_${p.y}-${String(p.m).padStart(2, '0')}.pdf`);
}

// Abacus-Export (E3, Walter 04.08.2026): AbaConnect-XML «FIBU / XML Buchungen»
// v2014.00 — Import-Datei wie DTA/LSE → Direkt-Download via saveBlobAsk
// (KEIN Vorschaufenster; XML kann der Browser nicht sinnvoll anzeigen).
async function fibuAbacusExport() {
    const p = _fibuParams(); if (!p) return;
    const statusEl = document.getElementById('fibuStatus');
    if (statusEl) statusEl.textContent = '⏳ Abacus-XML wird erstellt…';
    // Wählbares FIBU-Buchungsdatum (leer → Server-Default Monatsletzter).
    const ed = document.getElementById('fibuEntryDate')?.value || '';
    const edParam = ed ? `&entryDate=${ed}` : '';
    try {
        const r = await fetch(
            `/api/payroll/fibu-abaconnect?companyProfileId=${p.cid}&year=${p.y}&month=${p.m}${edParam}`,
            { headers: ah() });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); if (j && (j.error || j.message)) msg = j.message || j.error; } catch (_) {}
            if (statusEl) statusEl.textContent = '';
            alert('Export nicht möglich: ' + msg);
            return;
        }
        const blob = await r.blob();
        if (statusEl) statusEl.textContent = '';
        await saveBlobAsk(blob, `AbaConnect-Fibu_${p.cid}_${p.y}-${String(p.m).padStart(2, '0')}.xml`);
    } catch (e) {
        if (statusEl) statusEl.textContent = '';
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function _fibuFetchPdf(url, filename) {
    const statusEl = document.getElementById('fibuStatus');
    if (statusEl) statusEl.textContent = '⏳ PDF wird erstellt…';
    try {
        const r = await fetch(url, { headers: ah() });
        if (!r.ok) {
            let msg = 'HTTP ' + r.status;
            try { const j = await r.json(); if (j && (j.error || j.message)) msg = j.error || j.message; } catch (_) {}
            if (statusEl) statusEl.textContent = '';
            alert('Konnte nicht erstellt werden: ' + msg);
            return;
        }
        const blob = await r.blob();
        if (statusEl) statusEl.textContent = '';
        // PDF im Vorschaufenster zeigen (Drucken/Herunterladen/Schliessen).
        await previewFileModal(blob, filename);
    } catch (e) {
        if (statusEl) statusEl.textContent = '';
        alert('Verbindungsfehler: ' + e.message);
    }
}
