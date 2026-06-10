// ══════════════════════════════════════════════════════════════════════
// fibu.js — Buchhaltungs-Bereich (Walter-Vorgabe 24.05.2026)
//
// Eigener Menüpunkt „Fibu" für die Rolle `buchhaltung` (+ admin). Enthält
// Fibu-Journal + Buchhaltungs-Saldo-Liste — aus dem Lohnlauf hierher gezogen,
// damit der Lohnlauf sauber bleibt. Filiale folgt dem globalen Sidebar-Selektor
// (fixedCompanyProfileId), Periode = eigene Jahr/Monat-Wahl. Server prüft den
// Filial-Zugriff (user_branch_access) zusätzlich serverseitig.
// ══════════════════════════════════════════════════════════════════════

function fibuInit() {
    // Filiale anzeigen (aus globalem Selektor).
    const lbl = document.getElementById('fibuBranchLabel');
    const b = (typeof allBranches !== 'undefined' ? allBranches : [])
        .find(x => x.id === fixedCompanyProfileId);
    if (lbl) lbl.textContent = b
        ? `${b.restaurantCode ? b.restaurantCode + ' – ' : ''}${b.branchName || b.companyName}`
        : 'Bitte oben eine Filiale wählen';

    // Periode defaulten (aktueller Monat), nur wenn noch leer.
    const y = document.getElementById('fibuYear');
    const m = document.getElementById('fibuMonth');
    const now = new Date();
    if (y && !y.value) y.value = now.getFullYear();
    if (m && !m.value) m.value = String(now.getMonth() + 1);
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
