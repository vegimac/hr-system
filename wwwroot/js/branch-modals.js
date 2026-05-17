// ══════════════════════════════════════════════════════════════════════
// branch-modals.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// AUTO FERIEN-GELD-AUSZAHLUNG DEZEMBER (pro Filiale)
// ══════════════════════════════════════════════

let autoFerienGeldModalBranchId = null;

function openAutoFerienGeldModal(id, currentValue) {
    autoFerienGeldModalBranchId = id;
    document.getElementById('afgAktiv').checked = !!currentValue;
    document.getElementById('autoFerienGeldModal').style.display = 'flex';
}

function closeAutoFerienGeldModal() {
    document.getElementById('autoFerienGeldModal').style.display = 'none';
    autoFerienGeldModalBranchId = null;
}

async function saveAutoFerienGeld() {
    if (!autoFerienGeldModalBranchId) return;
    const aktiv = document.getElementById('afgAktiv').checked;
    try {
        const res = await fetch(`/api/companyprofiles/${autoFerienGeldModalBranchId}/auto-ferien-geld-dezember`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ aktiv })
        });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        // Lokale Kopien aktualisieren
        const b = allBranches.find(x => x.id === autoFerienGeldModalBranchId);
        if (b) b.autoFerienGeldAuszahlungDezember = aktiv;
        if (selectedCompanyProfile?.id === autoFerienGeldModalBranchId) {
            selectedCompanyProfile.autoFerienGeldAuszahlungDezember = aktiv;
        }
        if (selectedBranch?.id === autoFerienGeldModalBranchId) {
            selectedBranch.autoFerienGeldAuszahlungDezember = aktiv;
            renderFilialenDetail(selectedBranch);
        }
        closeAutoFerienGeldModal();
        loadFilialen();
    } catch { alert('Verbindungsfehler.'); }
}

// ══════════════════════════════════════════════
// KARENZ-SETTINGS (pro Filiale)
// ══════════════════════════════════════════════

function karenzjahrBasisLabel(code) {
    return code === 'KALENDERJAHR' ? 'Kalenderjahr (01.01.–31.12.)' : 'Arbeitsjahr (ab MA-Eintritt)';
}

let karenzModalBranchId = null;

function openKarenzModal(branchId, basis, tageMax, tageMaxUnfall, bvgWartefrist) {
    karenzModalBranchId = branchId;
    document.getElementById('kzBasis').value = basis || 'ARBEITSJAHR';
    document.getElementById('kzTageMax').value = tageMax ?? 14;
    document.getElementById('kzTageMaxUnfall').value = tageMaxUnfall ?? 2;
    document.getElementById('kzBvgWartefrist').value = bvgWartefrist ?? 3;
    document.getElementById('karenzModal').style.display = 'flex';
}

function closeKarenzModal() {
    document.getElementById('karenzModal').style.display = 'none';
    karenzModalBranchId = null;
}

async function saveKarenz() {
    if (!karenzModalBranchId) return;
    const basis          = document.getElementById('kzBasis').value;
    const tageMax        = Number(document.getElementById('kzTageMax').value);
    const tageMaxUnfall  = Number(document.getElementById('kzTageMaxUnfall').value);
    const bvgWartefrist  = parseInt(document.getElementById('kzBvgWartefrist').value, 10);
    if (!['ARBEITSJAHR','KALENDERJAHR'].includes(basis)) { alert('Basis ungültig.'); return; }
    if (!Number.isFinite(tageMax) || tageMax < 0 || tageMax > 365) { alert('Karenz-Tage Krank muss zwischen 0 und 365 liegen.'); return; }
    if (!Number.isFinite(tageMaxUnfall) || tageMaxUnfall < 0 || tageMaxUnfall > 365) { alert('Karenz-Tage Unfall muss zwischen 0 und 365 liegen.'); return; }
    if (!Number.isFinite(bvgWartefrist) || bvgWartefrist < 0 || bvgWartefrist > 24) { alert('BVG-Wartefrist muss zwischen 0 und 24 Monaten liegen.'); return; }
    try {
        const res = await fetch(`/api/companyprofiles/${karenzModalBranchId}/karenz`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ karenzjahrBasis: basis, karenzTageMax: tageMax, karenzTageMaxUnfall: tageMaxUnfall, bvgWartefristMonate: bvgWartefrist })
        });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        const b = allBranches.find(x => x.id === karenzModalBranchId);
        if (b) { b.karenzjahrBasis = basis; b.karenzTageMax = tageMax; b.karenzTageMaxUnfall = tageMaxUnfall; b.bvgWartefristMonate = bvgWartefrist; }
        if (selectedCompanyProfile?.id === karenzModalBranchId) {
            selectedCompanyProfile.karenzjahrBasis = basis;
            selectedCompanyProfile.karenzTageMax = tageMax;
            selectedCompanyProfile.karenzTageMaxUnfall = tageMaxUnfall;
            selectedCompanyProfile.bvgWartefristMonate = bvgWartefrist;
        }
        if (selectedBranch?.id === karenzModalBranchId) {
            selectedBranch.karenzjahrBasis = basis;
            selectedBranch.karenzTageMax = tageMax;
            selectedBranch.karenzTageMaxUnfall = tageMaxUnfall;
            selectedBranch.bvgWartefristMonate = bvgWartefrist;
            renderFilialenDetail(selectedBranch);
        }
        closeKarenzModal();
        loadFilialen();
    } catch { alert('Verbindungsfehler.'); }
}

// ══════════════════════════════════════════════
// L-GAV-VOLLZUGSBEITRAG (pro Filiale)
// ══════════════════════════════════════════════
const LGAV_MONTH_LABELS = ['Januar','Februar','März','April','Mai','Juni','Juli','August','September','Oktober','November','Dezember'];
function lgavMonatLabel(n) {
    const i = (parseInt(n, 10) || 1) - 1;
    return LGAV_MONTH_LABELS[i] ?? 'Januar';
}

let lgavModalBranchId = null;
function openLgavModal(branchId, aktiv, triggerMonat, betragVoll, betragReduziert) {
    lgavModalBranchId = branchId;
    document.getElementById('lgAktiv').checked          = !!aktiv;
    document.getElementById('lgTriggerMonat').value     = String(triggerMonat ?? 1);
    document.getElementById('lgBetragVoll').value       = Number(betragVoll ?? 99).toFixed(2);
    document.getElementById('lgBetragReduziert').value  = Number(betragReduziert ?? 49.5).toFixed(2);
    document.getElementById('lgavModal').style.display  = 'flex';
}
function closeLgavModal() {
    document.getElementById('lgavModal').style.display = 'none';
    lgavModalBranchId = null;
}
async function saveLgav() {
    if (!lgavModalBranchId) return;
    const aktiv          = document.getElementById('lgAktiv').checked;
    const triggerMonat   = parseInt(document.getElementById('lgTriggerMonat').value, 10);
    const betragVoll     = Number(document.getElementById('lgBetragVoll').value);
    const betragReduziert= Number(document.getElementById('lgBetragReduziert').value);
    if (!(triggerMonat >= 1 && triggerMonat <= 12)) { alert('Trigger-Monat ungültig.'); return; }
    if (!Number.isFinite(betragVoll) || betragVoll < 0)           { alert('Voller Beitrag ungültig.'); return; }
    if (!Number.isFinite(betragReduziert) || betragReduziert < 0) { alert('Reduzierter Beitrag ungültig.'); return; }
    try {
        const res = await fetch(`/api/companyprofiles/${lgavModalBranchId}/lgav`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                lgavAktiv:            aktiv,
                lgavTriggerMonat:     triggerMonat,
                lgavBeitragVoll:      betragVoll,
                lgavBeitragReduziert: betragReduziert
            })
        });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        const b = allBranches.find(x => x.id === lgavModalBranchId);
        if (b) {
            b.lgavAktiv = aktiv; b.lgavTriggerMonat = triggerMonat;
            b.lgavBeitragVoll = betragVoll; b.lgavBeitragReduziert = betragReduziert;
        }
        if (selectedBranch?.id === lgavModalBranchId) {
            selectedBranch.lgavAktiv = aktiv;
            selectedBranch.lgavTriggerMonat = triggerMonat;
            selectedBranch.lgavBeitragVoll = betragVoll;
            selectedBranch.lgavBeitragReduziert = betragReduziert;
            renderFilialenDetail(selectedBranch);
        }
        closeLgavModal();
        loadFilialen();
    } catch { alert('Verbindungsfehler.'); }
}

// Periodenregel-Modal entfernt (Walter-Vorgabe 16.05.2026, Akonto-Lohn-Modell):
// die Lohnperiode ist immer der Kalendermonat — keine konfigurierbare
// Periodenregel mehr. openPeriodenRegelModal / closePeriodenRegelModal /
// savePeriodenRegel sind ersatzlos gestrichen. Backend-Endpoint
// /api/payroll-perioden/config bleibt vorerst für historische Daten erhalten.

