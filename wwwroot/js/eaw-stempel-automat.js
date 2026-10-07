// ══════════════════════════════════════════════════════════════════════
// Stempel-Automat (Walter 07.10.2026): Stempelzeiten für viele Monate und
// Filialen nachholen. Je Filiale und Kalendermonat ein normaler Import
// (gleicher Hintergrund-Job wie «Importieren» im Stempelzeiten-Sync) — neue
// Stempel werden angelegt, bestehende aktualisiert, abgeschlossene
// Lohnperioden bleiben gesperrt. Eine Filiale arbeitet ihre Monate der Reihe
// nach ab, höchstens zwei Filialen gleichzeitig.
// ══════════════════════════════════════════════════════════════════════
let _saLaeuft = false;
let _saStopp = false;

function _saEsc(s) {
    return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
function _saIso(d) {
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}
function _saCh(iso) { return `${iso.slice(8, 10)}.${iso.slice(5, 7)}.${iso.slice(0, 4)}`; }
const _saMonatsnamen = ['Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'];
function _saMonatLabel(iso) { return `${_saMonatsnamen[+iso.slice(5, 7) - 1]} ${iso.slice(0, 4)}`; }

// Kalendermonate zwischen von und bis; Rand-Monate auf von/bis gekürzt.
function _saMonate(von, bis) {
    const out = [];
    let d = new Date(von + 'T00:00:00');
    while (_saIso(d) <= bis) {
        const ende = _saIso(new Date(d.getFullYear(), d.getMonth() + 1, 0));
        out.push([_saIso(d), ende > bis ? bis : ende]);
        d = new Date(d.getFullYear(), d.getMonth() + 1, 1);
    }
    return out;
}

async function saInit() {
    const box = document.getElementById('saFilialen');
    if (!box || box.dataset.geladen === '1') return;
    try {
        const r = await fetch('/api/easywork/mapped-branches', { headers: ah() });
        const liste = r.ok ? await r.json() : [];
        box.innerHTML = liste.map(b => `
            <label class="sa-pill"><input type="checkbox" class="sa-fil" value="${b.id}" data-name="${_saEsc(b.name)}" checked> ${_saEsc(b.name)}</label>`).join('')
            || '<span style="color:#8b8b8b;font-size:13px">Keine Filiale mit easy@work-Zuordnung.</span>';
        box.dataset.geladen = '1';
    } catch (e) {
        box.innerHTML = `<span style="color:#b91c1c;font-size:13px">Filialen nicht ladbar: ${_saEsc(e.message)}</span>`;
    }
    const von = document.getElementById('saVon'), bis = document.getElementById('saBis');
    if (von && !von.value) saSchnell('vorjahr');
}

function saAlle(an) { document.querySelectorAll('#saFilialen .sa-fil').forEach(c => { c.checked = an; }); }

function saSchnell(art) {
    const von = document.getElementById('saVon'), bis = document.getElementById('saBis');
    const heute = new Date(), j = heute.getFullYear();
    let a, b;
    if (art === 'vorjahr')     { a = new Date(j - 1, 0, 1); b = new Date(j - 1, 11, 31); }
    else if (art === 'jahr')   { a = new Date(j, 0, 1);     b = heute; }
    else if (art === 'zwei')   { a = new Date(j - 1, 0, 1); b = heute; }
    else return;
    von.value = _saIso(a); bis.value = _saIso(b);
    von.dispatchEvent(new Event('change')); bis.dispatchEvent(new Event('change'));
}

function saAnhalten() {
    if (!_saLaeuft) return;
    _saStopp = true;
    const k = document.getElementById('saStoppBtn');
    if (k) { k.disabled = true; k.textContent = 'Hält nach dem laufenden Monat an…'; }
}

// Startet einen Import-Job und wartet auf das Ergebnis.
async function _saMonatImportieren(filialeId, von, bis, ueberspringen, onPhase) {
    const r = await fetch('/api/easywork/sync/timepunches/commit-async', {
        method: 'POST',
        headers: { ...ah(), 'Content-Type': 'application/json' },
        body: JSON.stringify({
            companyProfileId: filialeId, from: von, to: bis,
            employeeCutoffOverride: von < '2025-01-01' ? von : null,
            ignoreMissing: ueberspringen,
        }),
    });
    const j = await r.json().catch(() => ({}));
    if (!r.ok || !j.jobId) throw new Error(j.message || j.error || `HTTP ${r.status}`);
    let fehlerInFolge = 0;
    while (true) {
        await new Promise(res => setTimeout(res, 1500));
        if (window.SessionGuard?.aktivitaet) window.SessionGuard.aktivitaet();
        let job;
        try {
            const s = await fetch(`/api/easywork/sync/employees/job/${j.jobId}`, { headers: ah() });
            job = await s.json();
            if (!s.ok) throw new Error(job.error || `HTTP ${s.status}`);
            fehlerInFolge = 0;
        } catch (e) {
            if (++fehlerInFolge >= 5) throw new Error('Job-Status nicht abrufbar: ' + e.message);
            continue;
        }
        if (job.status === 'running') { onPhase(job.phase || ''); continue; }
        if (job.status === 'error') throw new Error(job.error || 'Import fehlgeschlagen');
        return job.result || {};
    }
}

async function saStarten() {
    if (_saLaeuft) return;
    const von = document.getElementById('saVon').value;
    const bis = document.getElementById('saBis').value;
    const ueberspringen = document.getElementById('saUeberspringen').checked;
    const filialen = [...document.querySelectorAll('#saFilialen .sa-fil:checked')]
        .map(c => ({ id: +c.value, name: c.dataset.name }));
    const out = document.getElementById('saErgebnis');
    if (!von || !bis) { out.innerHTML = '<div class="sa-hinweis sa-rot">Bitte Von und Bis angeben.</div>'; return; }
    if (von > bis) { out.innerHTML = '<div class="sa-hinweis sa-rot">«Von» muss vor «Bis» liegen.</div>'; return; }
    if (!filialen.length) { out.innerHTML = '<div class="sa-hinweis sa-rot">Bitte mindestens eine Filiale wählen.</div>'; return; }

    const monate = _saMonate(von, bis);
    const total = monate.length * filialen.length;
    const ok = await liquidConfirm(
        `${filialen.length} Filiale(n) × ${monate.length} Monat(e) = ${total} Importe, vom ${_saCh(von)} bis ${_saCh(bis)}.\n\n` +
        `Neue Stempel werden angelegt, bestehende aktualisiert. Abgeschlossene Lohnperioden bleiben gesperrt. ` +
        `Das kann lange dauern — bitte dieses Fenster offen lassen.`,
        { title: 'Stempelzeiten-Automat starten?', yesLabel: 'Starten', noLabel: 'Abbrechen' });
    if (!ok) return;

    _saLaeuft = true; _saStopp = false;
    const startBtn = document.getElementById('saStartBtn'), stoppBtn = document.getElementById('saStoppBtn');
    startBtn.disabled = true; stoppBtn.disabled = false; stoppBtn.textContent = 'Anhalten';

    const stand = {};
    filialen.forEach(f => stand[f.id] = { name: f.name, fertig: 0, neu: 0, geaendert: 0, unveraendert: 0, gesperrt: 0, nichtZugeordnet: new Set(), meldungen: [], aktuell: '' });
    let erledigt = 0;
    const t0 = Date.now();

    const zeichnen = (fertig) => {
        const pct = total ? Math.round(erledigt / total * 100) : 100;
        const min = Math.round((Date.now() - t0) / 60000);
        const zeilen = filialen.map(f => {
            const s = stand[f.id];
            const nz = s.nichtZugeordnet.size
                ? `<div class="sa-klein">Nicht zugeordnet (übersprungen): ${[...s.nichtZugeordnet].map(_saEsc).join(', ')}</div>` : '';
            const mel = s.meldungen.length ? `<div class="sa-klein sa-rot">${s.meldungen.map(_saEsc).join('<br>')}</div>` : '';
            return `<tr>
                <td>${_saEsc(s.name)}${s.aktuell ? `<div class="sa-klein">⏳ ${_saEsc(s.aktuell)}</div>` : ''}${nz}${mel}</td>
                <td class="r">${s.fertig}/${monate.length}</td>
                <td class="r">${s.neu}</td>
                <td class="r">${s.geaendert}</td>
                <td class="r">${s.unveraendert}</td>
                <td class="r">${s.gesperrt}</td>
            </tr>`;
        }).join('');
        const meldungen = filialen.reduce((n, f) => n + stand[f.id].meldungen.length, 0);
        const kopf = fertig
            ? `<div class="sa-hinweis ${_saStopp ? '' : meldungen ? 'sa-gelb' : 'sa-gruen'}">${_saStopp ? '⏸ Angehalten' : '✓ Fertig'} — ${erledigt} von ${total} Importen in ${min} Min.${meldungen ? ` · <strong>${meldungen} Monat(e) mit Meldung</strong> (rot in der Tabelle) — nach dem Beheben nur diese nochmals laufen lassen.` : ''}</div>`
            : `<div class="sa-hinweis">⏳ ${erledigt} von ${total} Importen erledigt (${pct} %) · ${min} Min.
                 <div class="sa-balken"><div style="width:${Math.max(2, pct)}%"></div></div></div>`;
        out.innerHTML = `${kopf}
            <table class="sa-tab"><thead><tr>
                <th>Filiale</th><th class="r">Monate</th><th class="r">Neu</th><th class="r">Geändert</th>
                <th class="r">Unverändert</th><th class="r">Gesperrt</th>
            </tr></thead><tbody>${zeilen}</tbody></table>`;
    };
    zeichnen(false);

    const filialeAbarbeiten = async (f) => {
        const s = stand[f.id];
        for (const [mv, mb] of monate) {
            if (_saStopp) break;
            s.aktuell = _saMonatLabel(mv);
            zeichnen(false);
            try {
                const res = await _saMonatImportieren(f.id, mv, mb, ueberspringen, (phase) => {
                    s.aktuell = `${_saMonatLabel(mv)} · ${phase}`; zeichnen(false);
                });
                s.neu          += res.inserted || 0;
                s.geaendert    += res.updated || 0;
                s.unveraendert += res.unchanged || 0;
                s.gesperrt     += res.lockedSkipped || 0;
                (res.skippedMissingEmployees || []).forEach(m => s.nichtZugeordnet.add(m.eawEmployeeName || `easy@work-ID ${m.eawEmployeeId}`));
                if (res.isBlocked) {
                    const namen = [...(res.missingEmployees || []), ...(res.ambiguousEmployees || [])]
                        .map(m => m.eawEmployeeName || `easy@work-ID ${m.eawEmployeeId}`);
                    s.meldungen.push(`${_saMonatLabel(mv)}: nicht importiert — ${(res.notes || []).join(' ')}${namen.length ? ' (' + namen.join(', ') + ')' : ''}`);
                }
            } catch (e) {
                s.meldungen.push(`${_saMonatLabel(mv)}: ${e.message}`);
            }
            s.fertig++; erledigt++;
            s.aktuell = '';
            zeichnen(false);
        }
    };

    try {
        let naechste = 0;
        const arbeiter = async () => {
            while (naechste < filialen.length && !_saStopp) await filialeAbarbeiten(filialen[naechste++]);
        };
        await Promise.all(Array.from({ length: Math.min(2, filialen.length) }, arbeiter));
    } finally {
        _saLaeuft = false;
        startBtn.disabled = false; stoppBtn.disabled = true; stoppBtn.textContent = 'Anhalten';
        zeichnen(true);
    }
}
