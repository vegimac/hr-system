// ══════════════════════════════════════════════════════════════════════
// deductions.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// ABZÜGE / DEDUCTION RULES
// ══════════════════════════════════════════════

let dedCompanyId   = null;
let dedCompanyName = '';
let dedRules       = [];

const DED_CATEGORIES = [
    { code: '195', name: 'Spezial Zulagen' },
    { code: '500', name: 'AHV / IV / EO' },
    { code: '501', name: 'FAK' },
    { code: '510', name: 'Arbeitslosenversicherung' },
    { code: '511', name: 'ALV Zusatz' },
    { code: '520', name: 'Krankenpflegevers.' },
    { code: '530', name: 'Krankengeldversicherung' },
    { code: '540', name: 'Unfallversicherung' },
    { code: '541', name: 'UV Zusatz' },
    { code: '545', name: 'Mutterschaftsvers.' },
    { code: '550', name: 'Berufliche Vorsorge' },
    { code: '560', name: 'Quellensteuer' },
    { code: '600', name: 'Weitere Abzüge' },
];

// Swiss standard deduction templates
const SWISS_DEFAULTS = [
    { categoryCode:'500', categoryName:'AHV / IV / EO',          name:'AHV/IV/EO 18–64',          type:'percent', rate:5.30,  basisType:'gross', minAge:18, maxAge:64, sortOrder:10 },
    { categoryCode:'500', categoryName:'AHV / IV / EO',          name:'AHV/IV/EO 65+ (Freibetrag CHF 1\'400/Mt.)', type:'percent', rate:5.30, basisType:'gross', freibetragMonthly:1400, minAge:65, maxAge:null, sortOrder:20 },
    { categoryCode:'510', categoryName:'Arbeitslosenversicherung',name:'Festangestellter',         type:'percent', rate:1.10,  basisType:'gross', minAge:18, maxAge:64, sortOrder:10 },
    { categoryCode:'511', categoryName:'ALV Zusatz',              name:'Festangestellter',         type:'percent', rate:0.50,  basisType:'gross', minAge:18, maxAge:64, sortOrder:10 },
    { categoryCode:'530', categoryName:'Krankengeldversicherung', name:'Arbeitnehmer-Anteil',      type:'percent', rate:0.75,  basisType:'gross', minAge:null, maxAge:null, sortOrder:10 },
    { categoryCode:'540', categoryName:'Unfallversicherung',      name:'NBU Festangestellter',     type:'percent', rate:1.029, basisType:'gross', minAge:null, maxAge:null, sortOrder:10 },
    { categoryCode:'550', categoryName:'Berufliche Vorsorge',     name:'BVG 25–34 Jahre',          type:'percent', rate:7.00,  basisType:'bvg',   coordinationDeduction:2143.75, minAge:25, maxAge:34, sortOrder:10 },
    { categoryCode:'550', categoryName:'Berufliche Vorsorge',     name:'BVG 35–44 Jahre',          type:'percent', rate:10.00, basisType:'bvg',   coordinationDeduction:2143.75, minAge:35, maxAge:44, sortOrder:20 },
    { categoryCode:'550', categoryName:'Berufliche Vorsorge',     name:'BVG 45–54 Jahre',          type:'percent', rate:15.00, basisType:'bvg',   coordinationDeduction:2143.75, minAge:45, maxAge:54, sortOrder:30 },
    { categoryCode:'550', categoryName:'Berufliche Vorsorge',     name:'BVG 55–64/65 Jahre',       type:'percent', rate:18.00, basisType:'bvg',   coordinationDeduction:2143.75, minAge:55, maxAge:64, sortOrder:40 },
    { categoryCode:'560', categoryName:'Quellensteuer',           name:'Arbeitnehmer vollbeschäftigt', type:'percent', rate:4.50, basisType:'gross', onlyQuellensteuer:true, minAge:null, maxAge:null, sortOrder:10 },
];

async function openDeductionDrawer(companyId, name) {
    dedCompanyId   = companyId;
    dedCompanyName = name;
    document.getElementById('dedDrawerTitle').textContent = 'Abzüge – ' + name;
    document.getElementById('dedDrawerSub').textContent   = 'Sozialversicherungen, BVG, Quellensteuer';
    document.getElementById('deductionDrawer').classList.add('open');
    await loadDeductions();
}

function closeDeductionDrawer() {
    document.getElementById('deductionDrawer').classList.remove('open');
    dedCompanyId = null;
}

async function loadDeductions() {
    if (!dedCompanyId) return;
    try {
        const res = await fetch(`/api/companyprofiles/${dedCompanyId}/deductions`, { headers: ah() });
        dedRules  = await res.json();
        renderDeductions();
    } catch { document.getElementById('dedCatList').innerHTML = '<p style="color:#dc2626">Fehler beim Laden.</p>'; }
}

function renderDeductions() {
    const el = document.getElementById('dedCatList');

    // Alle benutzten Kategorien + leere Standard-Kategorien als Hinweis zeigen
    const usedCodes = [...new Set(dedRules.map(r => r.categoryCode))];
    const allCodes  = [...new Set([...DED_CATEGORIES.map(c => c.code), ...usedCodes])].sort();

    let html = '';
    allCodes.forEach(code => {
        const cat    = DED_CATEGORIES.find(c => c.code === code);
        const cName  = cat ? cat.name : (dedRules.find(r => r.categoryCode === code)?.categoryName || code);
        const rules  = dedRules.filter(r => r.categoryCode === code);

        html += `<div class="ded-cat" id="dedcat-${code}">
            <div class="ded-cat-head" onclick="toggleDedCat('${code}')">
                <span class="ded-cat-code">${code}</span>
                <span class="ded-cat-name">${cName}</span>
                <span style="font-size:11px;color:#94a3b8;margin-right:8px">${rules.length} Regel${rules.length !== 1 ? 'n' : ''}</span>
                <span class="ded-cat-chevron">▾</span>
            </div>
            <div class="ded-cat-body">
                ${rules.length === 0 ? `<div style="padding:12px 16px;color:#94a3b8;font-size:12.5px;font-style:italic">Keine Regeln – klick auf + hinzufügen</div>` : ''}
                ${rules.map(r => `
                <div class="ded-rule">
                    <span class="ded-rule-name">${r.name}</span>
                    <span class="ded-rule-rate">${r.type === 'percent' ? Number(r.rate).toFixed(4).replace(/\.?0+$/, '') + '%' : 'CHF ' + Number(r.rate).toFixed(2)}</span>
                    <span class="ded-rule-basis">${r.basisType === 'bvg' ? 'BVG-Basis' : 'Brutto'}</span>
                    <span class="ded-rule-age">${r.minAge != null || r.maxAge != null ? (r.minAge ?? '0') + '–' + (r.maxAge ?? '∞') + ' J.' : 'Alle'}</span>
                    ${r.freibetragMonthly ? `<span style="font-size:10.5px;color:#7c3aed;background:#f5f3ff;padding:1px 5px;border-radius:4px">Freibetrag CHF ${Number(r.freibetragMonthly).toFixed(0)}/Mt.</span>` : ''}
                    <span class="ded-rule-acts">
                        <button class="btn-stamp-edit" onclick="openDedEdit(${r.id})">✎</button>
                        <button class="btn-stamp-del"  onclick="deleteDed(${r.id})">✕</button>
                    </span>
                </div>`).join('')}
                <div class="ded-rule-add">
                    <button class="btn btn-outline" style="font-size:11.5px;padding:3px 10px" onclick="openDedEdit(null, '${code}', '${cName.replace(/'/g,"\\'")}')">+ Regel hinzufügen</button>
                </div>
            </div>
        </div>`;
    });

    if (!html) html = '<p style="color:#94a3b8;text-align:center;padding:24px">Noch keine Abzüge erfasst. Klick auf «Standardabzüge übernehmen».</p>';
    el.innerHTML = html;
}

function toggleDedCat(code) {
    document.getElementById('dedcat-' + code)?.classList.toggle('collapsed');
}

function openDedEdit(ruleId, presetCode, presetName) {
    const rule = ruleId ? dedRules.find(r => r.id === ruleId) : null;
    document.getElementById('dedEditTitle').textContent = rule ? 'Abzug bearbeiten' : 'Abzug hinzufügen';
    document.getElementById('dedEditId').value    = rule?.id ?? '';
    document.getElementById('dedEditName').value  = rule?.name ?? '';
    document.getElementById('dedEditType').value  = rule?.type ?? 'percent';
    document.getElementById('dedEditRate').value  = rule?.rate ?? '';
    document.getElementById('dedEditBasis').value = rule?.basisType ?? 'gross';
    document.getElementById('dedEditCoord').value      = rule?.coordinationDeduction ?? '';
    document.getElementById('dedEditFreibetrag').value = rule?.freibetragMonthly ?? '';
    document.getElementById('dedEditMinAge').value     = rule?.minAge ?? '';
    document.getElementById('dedEditMaxAge').value = rule?.maxAge ?? '';
    document.getElementById('dedEditFrom').value   = rule?.validFrom ?? new Date().toISOString().slice(0,10);
    document.getElementById('dedEditTo').value     = rule?.validTo ?? '';
    document.getElementById('dedEditQst').checked  = rule?.onlyQuellensteuer ?? false;
    document.getElementById('dedEditSort').value   = rule?.sortOrder ?? 10;

    // Kategorie-Dropdown setzen
    const sel = document.getElementById('dedCatSel');
    const catCode = rule?.categoryCode ?? presetCode ?? '500';
    const catName = rule?.categoryName ?? presetName ?? '';
    // Versuche, passende Option zu finden
    let found = false;
    for (const opt of sel.options) {
        if (opt.value.startsWith(catCode + '|')) { sel.value = opt.value; found = true; break; }
    }
    if (!found) sel.selectedIndex = 0;

    document.getElementById('dedEditModal').classList.add('open');
    onDedBasisChange();
    onDedTypeChange();
}

function closeDedEdit() {
    document.getElementById('dedEditModal').classList.remove('open');
}

function onDedCatChange() { /* könnte Standardwerte vorbelegen */ }

function onDedBasisChange() {
    const isBvg = document.getElementById('dedEditBasis').value === 'bvg';
    document.getElementById('dedEditCoord').disabled = !isBvg;
    if (!isBvg) document.getElementById('dedEditCoord').value = '';
}

function onDedTypeChange() {
    const isFixed = document.getElementById('dedEditType').value === 'fixed';
    document.getElementById('dedRateLabel').textContent = isFixed ? 'Betrag (CHF)' : 'Satz (%)';
}

async function saveDedEdit() {
    const id   = document.getElementById('dedEditId').value;
    const [catCode, catName] = document.getElementById('dedCatSel').value.split('|');
    const rate = parseFloat(document.getElementById('dedEditRate').value);
    if (!catCode || isNaN(rate)) { alert('Bitte Kategorie und Satz ausfüllen.'); return; }

    const coord     = document.getElementById('dedEditCoord').value;
    const freibetrag = document.getElementById('dedEditFreibetrag').value;
    const minA      = document.getElementById('dedEditMinAge').value;
    const maxA      = document.getElementById('dedEditMaxAge').value;
    const toVal     = document.getElementById('dedEditTo').value;

    const payload = {
        categoryCode:          catCode,
        categoryName:          catName,
        name:                  document.getElementById('dedEditName').value,
        type:                  document.getElementById('dedEditType').value,
        rate,
        basisType:             document.getElementById('dedEditBasis').value,
        coordinationDeduction: coord ? parseFloat(coord) : null,
        freibetragMonthly:     freibetrag ? parseFloat(freibetrag) : null,
        minAge:                minA ? parseInt(minA) : null,
        maxAge:                maxA ? parseInt(maxA) : null,
        onlyQuellensteuer:     document.getElementById('dedEditQst').checked,
        validFrom:             document.getElementById('dedEditFrom').value,
        validTo:               toVal || null,
        sortOrder:             parseInt(document.getElementById('dedEditSort').value) || 10,
    };

    try {
        const url    = id ? `/api/companyprofiles/${dedCompanyId}/deductions/${id}` : `/api/companyprofiles/${dedCompanyId}/deductions`;
        const method = id ? 'PUT' : 'POST';
        const res    = await fetch(url, {
            method,
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        if (!res.ok) { alert('Fehler beim Speichern.'); return; }
        closeDedEdit();
        await loadDeductions();
    } catch { alert('Verbindungsfehler.'); }
}

async function deleteDed(id) {
    if (!confirm('Abzug löschen?')) return;
    try {
        const res = await fetch(`/api/companyprofiles/${dedCompanyId}/deductions/${id}`, { method: 'DELETE', headers: ah() });
        if (!res.ok) { alert('Fehler beim Löschen.'); return; }
        await loadDeductions();
    } catch { alert('Verbindungsfehler.'); }
}

async function addDefaultDeductions() {
    if (!dedCompanyId) return;
    if (!confirm(`Schweizer Standardabzüge für «${dedCompanyName}» anlegen?\nBestehende Abzüge bleiben erhalten.`)) return;

    const today = new Date().toISOString().slice(0, 10);
    let created = 0;
    for (const d of SWISS_DEFAULTS) {
        const payload = {
            categoryCode:          d.categoryCode,
            categoryName:          d.categoryName,
            name:                  d.name,
            type:                  d.type,
            rate:                  d.rate,
            basisType:             d.basisType,
            coordinationDeduction: d.coordinationDeduction ?? null,
            minAge:                d.minAge ?? null,
            maxAge:                d.maxAge ?? null,
            onlyQuellensteuer:     d.onlyQuellensteuer ?? false,
            validFrom:             today,
            validTo:               null,
            sortOrder:             d.sortOrder,
        };
        const res = await fetch(`/api/companyprofiles/${dedCompanyId}/deductions`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify(payload),
        });
        if (res.ok) created++;
    }
    await loadDeductions();
    alert(`${created} Standardabzüge wurden angelegt.`);
}

async function copyDeductionsToAll() {
    if (!dedCompanyId) return;
    if (!confirm(
        `Alle Abzugsregeln von «${dedCompanyName}» in ALLE anderen Filialen kopieren?\n\n` +
        `⚠ Bestehende Abzüge der anderen Filialen werden dabei deaktiviert und durch die Regeln von «${dedCompanyName}» ersetzt.\n\n` +
        `Fortfahren?`
    )) return;

    const res = await fetch(`/api/companyprofiles/${dedCompanyId}/deductions/copy-to-all`, {
        method: 'POST',
        headers: ah(),
    });

    if (!res.ok) {
        const err = await res.text();
        alert('Fehler: ' + err);
        return;
    }

    const data = await res.json();
    alert(`✓ ${data.message}`);
}

