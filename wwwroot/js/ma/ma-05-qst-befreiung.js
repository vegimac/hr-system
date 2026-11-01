// Behörden-Befreiung erfassen — Modal mit direktem Upload ODER bestehendem Doku
async function openQstBefreiungModal() {
    if (!selectedEmployeeId) return;
    // Walter-Vorgabe 13.06.2026: jetzt das gleiche schlanke Auswahl+Upload-
    // Modal wie für Ausweis-Doku — nur mit kind='behoerden_befreiung'. Das
    // Modal filtert/highlights "Befreiung/Behörde/Quellensteuer"-Typen,
    // Direct-Pick verknüpft sofort, Upload-Pfad nimmt Gültig-ab/bis aus dem
    // Upload-Modal. Alles unten als toter Fallback-Code für Notfall.
    return openAusweisDokuModal(selectedEmployeeId, 'behoerden_befreiung');

    // ──────────────────────────────────────────────────────────────────
    // ALTE VARIANTE (deaktiviert seit 13.06.2026, bleibt als Fallback)
    // ──────────────────────────────────────────────────────────────────
    /* eslint-disable */
    const empId = selectedEmployeeId;
    if (typeof loadEmpDokumente === 'function') {
        try { await loadEmpDokumente(empId); } catch {}
    }
    if (typeof _dokState !== 'undefined' && _dokState.empId === empId) {
        // Passenden Typ in der Dokument-Struktur vorauswählen — Name-Match
        // auf „Quellensteuer Befreiung" o.ä. (kein linked_field_code für
        // diese Befreiung). Fallback: irgendetwas mit „befreiung" im Namen.
        let foundTyp = null, foundKat = null;
        const tax = Array.isArray(_dokState.taxonomy) ? _dokState.taxonomy : [];
        const isQstName = n => /quellensteuer\s*befreiung|qst\s*befreiung/i.test(n || '');
        for (const k of tax) {
            for (const t of (k.typen || [])) {
                if (isQstName(t.name)) { foundTyp = t; foundKat = k; break; }
            }
            if (foundTyp) break;
        }
        if (!foundTyp) {
            for (const k of tax) {
                for (const t of (k.typen || [])) {
                    if (/befreiung/i.test(t.name || '')) { foundTyp = t; foundKat = k; break; }
                }
                if (foundTyp) break;
            }
        }
        _dokState.selectedKategorieId = foundKat?.id || null;
        _dokState.selectedTypId       = foundTyp?.id || null;

        _dokState.afterUpload = async (newDokId, _raw, form) => {
            if (!newDokId) return;
            try {
                const res = await fetch(`/api/employees/${empId}/qst-befreiung`, {
                    method: 'PATCH',
                    headers: { ...ah(), 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        befreit:    true,
                        dokumentId: newDokId,
                        gueltigAb:  form?.gueltigVon || null,
                        gueltigBis: form?.gueltigBis || null
                    })
                });
                if (!res.ok) {
                    const j = await res.json().catch(() => null);
                    alert(j?.message || `Befreiung speichern fehlgeschlagen (${res.status})`);
                    return;
                }
                loadQuellensteuerTab(empId);
            } catch (e) {
                alert('Verbindungsfehler: ' + e.message);
            }
        };

        if (typeof openDokUploadModal === 'function') {
            openDokUploadModal();
        } else {
            alert('Doku-Upload-Modal nicht geladen. Bitte Seite neu laden.');
            _dokState.afterUpload = null;
        }
        return;
    }
    // Fallback: wenn _dokState nicht initialisiert ist, zum alten Modal-
    // Pfad weiterlaufen (toter Code unten — Sicherheits-Backup).
    // ──────────────────────────────────────────────────────────────────
    // ALTE VARIANTE (deaktiviert seit 13.06.2026, bleibt als Fallback)
    // ──────────────────────────────────────────────────────────────────
    const [dokRes, taxRes] = await Promise.all([
        fetch(`/api/documents/by-employee/${selectedEmployeeId}`, { headers: ah() }),
        fetch('/api/documents/taxonomie', { headers: ah() })
    ]);
    const dokumente = dokRes.ok ? await dokRes.json() : [];
    const taxonomy  = taxRes.ok ? await taxRes.json() : [];

    // Walter-Vorgabe 28.05.2026 (Teil 2): Typ-Picker beim Hochladen optisch
    // wie die Dokumente-Verwaltung (Kategorie-Pill + Typ-Name, klickbare Zeilen)
    // statt plain <select>. Reihenfolge: QST/Befreiungs-Typen zuerst (Treffer auf
    // Typ-Namen wie „Quellensteuer Befreiung" — der liegt unter „Lohn / Arbeitszeit",
    // nicht unter „Ämter & Behörden"), dann die übrigen Typen darunter.
    const _allTypes = (taxonomy || []).flatMap(k =>
        (k.typen || []).map(t => ({ id: t.id, name: t.name, katName: k.name, katId: k.id }))
    );
    const _isQstTyp = t =>
        /befreiung|quellensteuer|qst|ämter|behörd|bestätigung/i.test(t.name + ' ' + t.katName);
    const _typQst   = _allTypes.filter(_isQstTyp);
    const _typRest  = _allTypes.filter(t => !_isQstTyp(t));
    _typQst.sort((a, b) => a.name.localeCompare(b.name));
    _typRest.sort((a, b) => (a.katName || '').localeCompare(b.katName || '') || a.name.localeCompare(b.name));
    // Default-Auswahl: bevorzugt der Typ „Quellensteuer Befreiung" — sonst der
    // erste QST-/Befreiungs-Typ. Wenn keiner existiert, bleibt die Auswahl leer.
    const _defaultTyp = _typQst.find(t => /quellensteuer\s*befreiung|qst\s*befreiung/i.test(t.name))
                     || _typQst.find(t => /befreiung/i.test(t.name))
                     || _typQst[0]
                     || null;

    // Walter-Vorgabe 28.05.2026: Dokument-Picker wie Dokumente-Verwaltung —
    // klickbare Tabelle mit Kategorie-Pill / Typ / Beschreibung+Datum statt
    // plain <select>. Nutzt dieselben CSS-Klassen (.dok-table, .dok-cat-pill)
    // und denselben Slug (dokCatSlug aus documents.js), damit das Modal
    // visuell mit der Dokumenten-Verwaltung übereinstimmt.
    const fmtDate = (iso) => {
        if (!iso) return '–';
        const s = String(iso).slice(0, 10);
        if (s.length !== 10) return '–';
        return s.slice(8, 10) + '.' + s.slice(5, 7) + '.' + s.slice(0, 4);
    };
    const dokSorted = (dokumente || [])
        .slice()
        .sort((a, b) => String(b.geaendertAm || b.hochgeladenAm || '')
                         .localeCompare(String(a.geaendertAm || a.hochgeladenAm || '')));
    const isBehoerden = d =>
        /ämter|behörd|quellensteuer|qst/i.test((d.kategorieName || '') + ' ' + (d.dokumentTypName || ''));
    // Behörden/QST-Treffer zuerst, dann der Rest — innerhalb beider Gruppen
    // bleibt die neueste-zuerst-Sortierung erhalten.
    const dokOrdered = [
        ...dokSorted.filter(isBehoerden),
        ...dokSorted.filter(d => !isBehoerden(d)),
    ];
    const slug = (typeof dokCatSlug === 'function')
        ? dokCatSlug
        : (n => (n || '').toLowerCase()
            .replace(/ä/g,'ae').replace(/ö/g,'oe').replace(/ü/g,'ue').replace(/ß/g,'ss')
            .replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,''));
    const renderDokRow = d => {
        const catSlug = slug(d.kategorieName);
        const erstelltIso = d.erstelltAm || d.gueltigVon || d.hochgeladenAm;
        const dateLine = `${fmtDate(erstelltIso)} · ${fmtDate(d.geaendertAm)} · ${fmtDate(d.zugriffAm)}`;
        const ext = ((d.filenameOriginal || '').toLowerCase().match(/\.[^.]+$/) || [''])[0];
        const isPdf = d.mimeType === 'application/pdf';
        const isImg = (d.mimeType || '').startsWith('image/');
        const icon  = isPdf ? '📄' : isImg ? '🖼️' : '📎';
        const fname = esc(d.filenameOriginal || ('Dokument #' + d.id));
        const bem   = d.bemerkung ? `<b>${esc(d.bemerkung)}</b>` : `<span style="color:#cbd5e1">${fname}</span>`;
        const nameLine = d.bemerkung
            ? `${icon} ${bem} <span style="color:#94a3b8;font-weight:400;font-size:11.5px">· ${fname}</span>`
            : `${icon} ${bem}`;
        return `<tr data-dok-id="${d.id}"
                    data-dok-cat="${esc((d.kategorieName || '').toLowerCase())}"
                    data-dok-search="${esc((d.filenameOriginal||'') + ' ' + (d.bemerkung||'') + ' ' + (d.kategorieName||'') + ' ' + (d.dokumentTypName||'')).toLowerCase()}"
                    onclick="qstBefSelectDok(${d.id})"
                    style="cursor:pointer">
            <td><span class="dok-cat-pill cat-${catSlug}">${esc(d.kategorieName || '')}</span></td>
            <td style="color:#475569">${esc(d.dokumentTypName || '')}</td>
            <td>${nameLine}<br><span class="dok-meta-inline">${dateLine}</span></td>
        </tr>`;
    };
    // Walter-Vorgabe 28.05.2026 (Teil 3): Oberkategorie-Chips über der Liste
    // — wie der Kategorie-Tree links in der Dokumenten-Verwaltung. Reihenfolge
    // wie in der Taxonomie selbst, Anzahl pro Kategorie in einem grauen Badge.
    // Default-Chip „Alle" — sobald der User auf eine Kategorie klickt, blendet
    // die Liste alles andere aus. Kombiniert mit dem Text-Filter darunter.
    const catCounts = {};
    dokOrdered.forEach(d => {
        const k = d.kategorieName || '';
        catCounts[k] = (catCounts[k] || 0) + 1;
    });
    const taxOrder = (taxonomy || []).map(k => k.name).filter(n => catCounts[n]);
    // Eventuell vorhandene Kategorien, die NICHT in der Taxonomie stehen (Alt-Daten),
    // hängen ans Ende, damit kein Doku im Filter verloren geht.
    Object.keys(catCounts).forEach(n => { if (!taxOrder.includes(n)) taxOrder.push(n); });
    const renderCatChip = (label, count, value, active) => {
        const catSlug = value ? slug(value) : '';
        const pillClass = value ? `dok-cat-pill cat-${catSlug}` : '';
        const baseStyle = active
            ? 'background:#0f172a;color:#fff;border:1px solid #0f172a'
            : 'background:#fff;color:#0f172a;border:1px solid #e2e8f0';
        // Wenn nicht aktiv UND eine Kategorie-Farbe existiert, zeigen wir die Pill
        // mit der Original-Farbe (wie in der Doku-Verwaltung). Aktive Chip ist
        // immer dunkel/weiss als klarer Selected-Marker.
        if (active || !value) {
            return `<button type="button" class="qst-bef-chip" data-cat="${esc((value||'').toLowerCase())}"
                            onclick="qstBefFilterCat('${esc(value||'').replace(/'/g,'\\\'')}', this)"
                            style="${baseStyle};padding:4px 10px;border-radius:999px;font-size:11.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:6px">
                        ${esc(label)}
                        <span style="background:${active ? 'rgba(255,255,255,.2)' : '#f1f5f9'};color:${active ? '#fff' : '#64748b'};padding:1px 6px;border-radius:999px;font-size:10.5px;font-weight:600">${count}</span>
                    </button>`;
        }
        return `<button type="button" class="qst-bef-chip ${pillClass}" data-cat="${esc((value||'').toLowerCase())}"
                        onclick="qstBefFilterCat('${esc(value||'').replace(/'/g,'\\\'')}', this)"
                        style="padding:4px 10px;border-radius:999px;font-size:11.5px;font-weight:600;cursor:pointer;border:1px solid transparent;display:inline-flex;align-items:center;gap:6px">
                    ${esc(label)}
                    <span style="background:rgba(15,23,42,.08);color:#64748b;padding:1px 6px;border-radius:999px;font-size:10.5px;font-weight:600">${count}</span>
                </button>`;
    };
    const dokChipsHtml = dokOrdered.length
        ? `<div id="qstBefDokChips" style="display:flex;flex-wrap:wrap;gap:6px;margin-bottom:8px">
             ${renderCatChip('Alle', dokOrdered.length, '', true)}
             ${taxOrder.map(n => renderCatChip(n, catCounts[n], n, false)).join('')}
           </div>`
        : '';
    const dokTableHtml = dokOrdered.length
        ? `<div style="border:1px solid #e2e8f0;border-radius:6px;max-height:280px;overflow-y:auto;background:#fff">
             <table class="dok-table" id="qstBefDokTable" style="font-size:12px">
               <thead><tr><th style="width:160px">Kategorie</th><th style="width:130px">Typ</th><th>Beschreibung<div style="font-weight:400;font-size:10px;color:#94a3b8;margin-top:2px;text-transform:none;letter-spacing:0">Erstellt · Geändert · Geöffnet</div></th></tr></thead>
               <tbody>${dokOrdered.map(renderDokRow).join('')}</tbody>
             </table>
           </div>
           <div id="qstBefDokNoMatch" style="display:none;padding:14px;text-align:center;color:#94a3b8;font-size:12px">Kein Dokument entspricht der Auswahl.</div>`
        : `<div style="padding:18px;text-align:center;color:#dc2626;font-size:12px;background:#fef2f2;border:1px dashed #fecaca;border-radius:6px">
             ⚠ Bei diesem Mitarbeiter sind noch keine Dokumente hochgeladen. Bitte unter <strong>A</strong> direkt das Bestätigungsschreiben hochladen.
           </div>`;
    const dokSearchHtml = dokOrdered.length > 4
        ? `<input type="text" id="qstBefDokSearch" placeholder="Filtern (Name, Bemerkung, Typ)…"
                  oninput="qstBefDokApplyFilters()"
                  style="width:100%;padding:6px 8px;border:1px solid #cbd5e1;border-radius:5px;font-size:12px;margin-bottom:8px">`
        : '';
    const dokSelectedLabel = dokOrdered.length
        ? `<div id="qstBefDokSelected" style="font-size:11.5px;color:#94a3b8;margin-top:6px">Noch kein Dokument ausgewählt — Zeile anklicken.</div>`
        : '';

    // --- Typ-Picker (Option A) im gleichen Look wie der Doku-Picker ---
    // Aufbau: 2-spaltige Tabelle mit Kategorie-Pill + Typ-Name. QST-/Befreiungs-
    // relevante Typen sind oben gruppiert (visuell hervorgehoben), die übrigen
    // Typen darunter zum Aufklappen, damit das Modal kompakt bleibt.
    const renderTypRow = (t, opts) => {
        const o = opts || {};
        const catSlug = slug(t.katName);
        const isDefault = _defaultTyp && _defaultTyp.id === t.id;
        const sel = isDefault ? 'background:#ece9e2;outline:2px solid #1a1a1a;outline-offset:-2px' : '';
        const hidden = o.hidden ? 'display:none' : '';
        const extraClass = o.rest ? 'qst-typ-rest' : '';
        const style = [hidden, 'cursor:pointer', sel].filter(Boolean).join(';');
        return `<tr class="${extraClass}" data-typ-id="${t.id}" data-typ-search="${esc((t.name + ' ' + t.katName).toLowerCase())}"
                    onclick="qstBefSelectTyp(${t.id})"
                    style="${style}">
            <td><span class="dok-cat-pill cat-${catSlug}">${esc(t.katName || '')}</span></td>
            <td style="color:#0f172a;font-weight:${o.highlight ? 600 : 500}">${esc(t.name || '')}</td>
        </tr>`;
    };
    const _hasRest = _typRest.length > 0;
    const typTableHtml = _allTypes.length
        ? `<div style="border:1px solid #e2e8f0;border-radius:6px;background:#fff;max-height:240px;overflow-y:auto">
             <table class="dok-table" id="qstBefTypTable" style="font-size:12px">
               <thead><tr><th style="width:180px">Kategorie</th><th>Typ</th></tr></thead>
               <tbody>
                 ${_typQst.length ? `<tr><td colspan="2" style="background:#f8fafc;color:#64748b;font-size:10.5px;text-transform:uppercase;letter-spacing:.04em;padding:6px 12px">Für QST-Befreiung empfohlen</td></tr>` : ''}
                 ${_typQst.map(t => renderTypRow(t, { highlight: true })).join('')}
                 ${_hasRest ? `<tr id="qstBefTypRestToggleRow"><td colspan="2" style="padding:6px 12px;background:#f8fafc">
                    <button type="button" id="qstBefTypRestToggle" onclick="qstBefTypToggleRest()"
                            style="background:transparent;border:none;color:#1a1a1a;font-size:11.5px;cursor:pointer;font-weight:600">
                        Weitere Typen anzeigen (${_typRest.length}) ▾
                    </button></td></tr>` : ''}
                 ${_typRest.map(t => renderTypRow(t, { hidden: true, rest: true })).join('')}
               </tbody>
             </table>
           </div>
           <input type="hidden" id="qstBefTyp" value="${_defaultTyp ? _defaultTyp.id : ''}">
           <div id="qstBefTypSelected" style="font-size:11.5px;color:${_defaultTyp ? '#0f5132' : '#dc2626'};margin-top:6px">
              ${_defaultTyp
                ? `✓ ausgewählt: <b>${esc(_defaultTyp.name)}</b> <span style="color:#94a3b8">· ${esc(_defaultTyp.katName)}</span>`
                : '⚠ Kein Doku-Typ verfügbar — bitte in der Dokument-Verwaltung anlegen.'}
           </div>`
        : `<input type="hidden" id="qstBefTyp" value="">
           <div style="font-size:11.5px;color:#dc2626;padding:8px 0">⚠ Kein Doku-Typ verfügbar — bitte in der Dokument-Verwaltung anlegen.</div>`;

    const html = `
    <div id="qstBefreiungModal" style="position:fixed;inset:0;background:rgba(15,23,42,.55);z-index:9000;display:flex;align-items:center;justify-content:center;padding:20px"
         onclick="if(event.target===this)document.getElementById('qstBefreiungModal').remove()">
      <div style="background:#fff;border-radius:14px;max-width:780px;width:100%;padding:24px;box-shadow:0 20px 50px rgba(0,0,0,.25);max-height:92vh;overflow-y:auto">
        <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:14px">
            <div style="font-size:16px;font-weight:700;color:#0f172a">Behörden-Befreiung erfassen</div>
            <button onclick="document.getElementById('qstBefreiungModal').remove()" style="background:transparent;border:none;font-size:20px;color:#64748b;cursor:pointer">×</button>
        </div>
        <div style="background:#fef3c7;border-left:3px solid #f59e0b;padding:10px 12px;border-radius:6px;font-size:12px;color:#78350f;margin-bottom:16px">
            Befreiung gilt nur mit gültigem Bestätigungsschreiben der Steuerbehörde. Datei direkt hochladen <strong>oder</strong> ein bereits abgelegtes Dokument verlinken.
        </div>

        <!-- Option A: Datei direkt hochladen -->
        <div style="border:1px solid #cbd5e1;border-radius:8px;padding:14px;margin-bottom:12px">
            <div style="font-size:12px;font-weight:700;color:#5a5348;margin-bottom:8px;text-transform:uppercase;letter-spacing:.04em">A · Datei hochladen</div>
            <input type="file" id="qstBefFile" accept=".pdf,image/*" style="width:100%;font-size:12.5px"
                   onchange="qstBefFileChanged(this)">
            <div id="qstBefFileLabel" style="font-size:11.5px;color:#64748b;margin-top:6px"></div>
            <div style="margin-top:12px">
                <label style="display:block;font-size:12px;font-weight:600;color:#374151;margin-bottom:6px">Doku-Typ</label>
                ${typTableHtml}
            </div>
        </div>

        <div style="text-align:center;color:#94a3b8;font-size:11px;margin:8px 0;text-transform:uppercase;letter-spacing:.08em">— oder —</div>

        <!-- Option B: Bestehendes Dokument auswählen -->
        <div style="border:1px solid #cbd5e1;border-radius:8px;padding:14px;margin-bottom:16px">
            <div style="font-size:12px;font-weight:700;color:#5a5348;margin-bottom:8px;text-transform:uppercase;letter-spacing:.04em">B · Bestehendes Dokument</div>
            <input type="hidden" id="qstBefDok" value="">
            ${dokChipsHtml}
            ${dokSearchHtml}
            ${dokTableHtml}
            ${dokSelectedLabel}
        </div>

        <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-bottom:18px">
            <div>
                <label style="display:block;font-size:12px;font-weight:600;color:#374151;margin-bottom:6px">Gültig ab *</label>
                <input type="date" id="qstBefAb" style="width:100%;padding:8px;border:1px solid #cbd5e1;border-radius:5px;font-size:13px">
            </div>
            <div>
                <label style="display:block;font-size:12px;font-weight:600;color:#374151;margin-bottom:6px">Gültig bis</label>
                <input type="date" id="qstBefBis" style="width:100%;padding:8px;border:1px solid #cbd5e1;border-radius:5px;font-size:13px" placeholder="leer = unbefristet">
            </div>
        </div>
        <div id="qstBefStatus" style="font-size:12px;color:#64748b;margin-bottom:10px"></div>
        <div style="display:flex;justify-content:flex-end;gap:8px">
            <button onclick="document.getElementById('qstBefreiungModal').remove()" style="background:#fff;border:1px solid #cbd5e1;color:#475569;padding:8px 16px;border-radius:6px;font-size:13px;cursor:pointer">Abbrechen</button>
            <button id="qstBefSaveBtn" onclick="qstBefreiungSpeichern()" style="background:#16a34a;color:#fff;border:none;padding:8px 16px;border-radius:6px;font-size:13px;font-weight:600;cursor:pointer">Befreiung speichern</button>
        </div>
      </div>
    </div>`;
    document.body.insertAdjacentHTML('beforeend', html);
}

function qstBefFileChanged(input) {
    const lbl = document.getElementById('qstBefFileLabel');
    if (input.files && input.files[0]) {
        const f = input.files[0];
        lbl.textContent = `Ausgewählt: ${f.name} (${Math.round(f.size/1024)} KB)`;
        // Bei Datei-Auswahl: Bestehendes-Doku-Picker zurücksetzen (Option A wins).
        const hidden = document.getElementById('qstBefDok');
        if (hidden) hidden.value = '';
        document.querySelectorAll('#qstBefDokTable tr[data-dok-id]').forEach(tr => {
            tr.style.background = '';
            tr.style.outline = '';
        });
        const sel = document.getElementById('qstBefDokSelected');
        if (sel) {
            sel.innerHTML = 'Noch kein Dokument ausgewählt — Zeile anklicken.';
            sel.style.color = '#94a3b8';
        }
    } else {
        lbl.textContent = '';
    }
}

// Walter-Vorgabe 28.05.2026: Dokument-Picker im qstBefreiungModal — Klick auf
// Zeile setzt den verdeckten Wert + markiert die Zeile blau. Optionaler
// Live-Filter darüber (Filename / Bemerkung / Kategorie / Typ).
function qstBefSelectDok(dokId) {
    const hidden = document.getElementById('qstBefDok');
    if (hidden) hidden.value = String(dokId);
    // Datei-Upload-Feld räumen (Option B wins)
    const fileInp = document.getElementById('qstBefFile');
    if (fileInp) fileInp.value = '';
    const lbl = document.getElementById('qstBefFileLabel');
    if (lbl) lbl.textContent = '';
    // Zeile markieren
    let picked = null;
    document.querySelectorAll('#qstBefDokTable tr[data-dok-id]').forEach(tr => {
        if (parseInt(tr.getAttribute('data-dok-id'), 10) === dokId) {
            tr.style.background = '#ece9e2';
            tr.style.outline = '2px solid #1a1a1a';
            tr.style.outlineOffset = '-2px';
            picked = tr;
        } else {
            tr.style.background = '';
            tr.style.outline = '';
        }
    });
    const sel = document.getElementById('qstBefDokSelected');
    if (sel && picked) {
        // Beschreibung der ausgewählten Zeile (Kategorie / Typ / Beschreibung)
        const tds = picked.querySelectorAll('td');
        const kat = tds[0] ? tds[0].innerText.trim() : '';
        const typ = tds[1] ? tds[1].innerText.trim() : '';
        const beschr = tds[2] ? tds[2].innerText.trim().split('\n')[0] : '';
        sel.innerHTML = `✓ ausgewählt: <b>${esc(beschr)}</b> <span style="color:#94a3b8">· ${esc(kat)} / ${esc(typ)}</span>`;
        sel.style.color = '#0f5132';
    }
}

// Walter-Vorgabe 28.05.2026 (Teil 3): kombinierter Filter aus Kategorie-Chip
// + Text-Suche. _qstBefDokCat hält den aktiven Chip-Wert (leer = „Alle").
let _qstBefDokCat = '';
function qstBefDokApplyFilters() {
    const inp = document.getElementById('qstBefDokSearch');
    const needle = (inp ? inp.value : '').toLowerCase().trim();
    const cat = (_qstBefDokCat || '').toLowerCase();
    let shown = 0;
    document.querySelectorAll('#qstBefDokTable tr[data-dok-id]').forEach(tr => {
        const hay   = tr.getAttribute('data-dok-search') || '';
        const trCat = tr.getAttribute('data-dok-cat')    || '';
        const okText = !needle || hay.includes(needle);
        const okCat  = !cat    || trCat === cat;
        const match  = okText && okCat;
        tr.style.display = match ? '' : 'none';
        if (match) shown++;
    });
    const empty = document.getElementById('qstBefDokNoMatch');
    if (empty) empty.style.display = shown === 0 ? 'block' : 'none';
}

// Legacy-Alias — wird nicht mehr von neuen DOM-Listenern aufgerufen, bleibt
// aber stehen falls externe Caller die alte Signatur erwarten.
function qstBefFilterDok(_q) { qstBefDokApplyFilters(); }

// Kategorie-Chip klicken: aktiven Chip markieren + Liste filtern.
function qstBefFilterCat(catValue, btnEl) {
    _qstBefDokCat = catValue || '';
    // Chip-Highlights aktualisieren: aktive Chip dunkel-gefüllt, alle anderen
    // zurück auf Original-Pill-Optik bzw. neutrale „Alle"-Optik.
    const chips = document.querySelectorAll('#qstBefDokChips .qst-bef-chip');
    chips.forEach(c => {
        const isActive = c === btnEl;
        const catAttr  = c.getAttribute('data-cat') || '';
        const isAlle   = !catAttr;
        // Klassen-Reset
        c.className = 'qst-bef-chip';
        if (isActive) {
            c.style.background = '#0f172a';
            c.style.color      = '#fff';
            c.style.border     = '1px solid #0f172a';
            const badge = c.querySelector('span');
            if (badge) { badge.style.background = 'rgba(255,255,255,.2)'; badge.style.color = '#fff'; }
        } else if (isAlle) {
            c.style.background = '#fff';
            c.style.color      = '#0f172a';
            c.style.border     = '1px solid #e2e8f0';
            const badge = c.querySelector('span');
            if (badge) { badge.style.background = '#f1f5f9'; badge.style.color = '#64748b'; }
        } else {
            // Kategorie-Pill in Original-Farbe (via .dok-cat-pill CSS-Klasse)
            const slugFn = (typeof dokCatSlug === 'function')
                ? dokCatSlug
                : (n => (n || '').toLowerCase()
                    .replace(/ä/g,'ae').replace(/ö/g,'oe').replace(/ü/g,'ue').replace(/ß/g,'ss')
                    .replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,''));
            c.className = `qst-bef-chip dok-cat-pill cat-${slugFn(catAttr)}`;
            c.style.background = '';
            c.style.color      = '';
            c.style.border     = '1px solid transparent';
            const badge = c.querySelector('span');
            if (badge) { badge.style.background = 'rgba(15,23,42,.08)'; badge.style.color = '#64748b'; }
        }
    });
    qstBefDokApplyFilters();
}

// Walter-Vorgabe 28.05.2026: Typ-Picker (Option A) — Klick auf eine Zeile
// markiert den gewählten Doku-Typ; identische Highlight-Optik wie der
// Doku-Picker oben.
function qstBefSelectTyp(typId) {
    const hidden = document.getElementById('qstBefTyp');
    if (hidden) hidden.value = String(typId);
    let picked = null;
    document.querySelectorAll('#qstBefTypTable tr[data-typ-id]').forEach(tr => {
        if (parseInt(tr.getAttribute('data-typ-id'), 10) === typId) {
            tr.style.background = '#ece9e2';
            tr.style.outline = '2px solid #1a1a1a';
            tr.style.outlineOffset = '-2px';
            picked = tr;
        } else {
            tr.style.background = '';
            tr.style.outline = '';
        }
    });
    const sel = document.getElementById('qstBefTypSelected');
    if (sel && picked) {
        const tds = picked.querySelectorAll('td');
        const kat = tds[0] ? tds[0].innerText.trim() : '';
        const typ = tds[1] ? tds[1].innerText.trim() : '';
        sel.innerHTML = `✓ ausgewählt: <b>${esc(typ)}</b> <span style="color:#94a3b8">· ${esc(kat)}</span>`;
        sel.style.color = '#0f5132';
    }
}

// Aufklapp-Toggle für die „weiteren Typen" unterhalb der QST-empfohlenen Liste.
function qstBefTypToggleRest() {
    const rows = document.querySelectorAll('#qstBefTypTable tr.qst-typ-rest');
    const btn  = document.getElementById('qstBefTypRestToggle');
    if (!rows.length) return;
    const isHidden = rows[0].style.display === 'none';
    rows.forEach(r => { r.style.display = isHidden ? '' : 'none'; });
    if (btn) {
        const total = rows.length;
        btn.innerHTML = isHidden
            ? `Weitere Typen ausblenden ▴`
            : `Weitere Typen anzeigen (${total}) ▾`;
    }
}

async function qstBefreiungSpeichern() {
    const empId = selectedEmployeeId;
    const fileInput = document.getElementById('qstBefFile');
    const file = fileInput?.files?.[0] || null;
    let dokId = parseInt(document.getElementById('qstBefDok').value || '0', 10);
    const ab    = document.getElementById('qstBefAb').value;
    const bis   = document.getElementById('qstBefBis').value;
    const typId = parseInt(document.getElementById('qstBefTyp').value || '0', 10);
    const statusEl = document.getElementById('qstBefStatus');
    const saveBtn  = document.getElementById('qstBefSaveBtn');

    if (!ab)             { alert('Bitte das Gültig-ab-Datum eintragen.'); return; }
    if (!file && !dokId) { alert('Bitte eine Datei hochladen ODER ein bestehendes Dokument auswählen.'); return; }
    if (file && !typId)  { alert('Bitte den Dokument-Typ wählen.'); return; }

    saveBtn.disabled = true;
    saveBtn.textContent = 'Speichern…';

    // Walter-Vorgabe 26.05.2026: Direkt-Upload — erst Datei hochladen, dann
    // die zurückgekommene Dokument-ID an den Befreiungs-PATCH übergeben.
    if (file) {
        try {
            statusEl.textContent = 'Datei wird hochgeladen…';
            // Branch-Code des aktuellen MA aus dem aktiven Vertrag
            const empBranch = selectedEmployee?.employments?.find(e => e.isActive)?.companyProfile?.restaurantCode
                           ?? (typeof allBranches !== 'undefined'
                               ? (allBranches.find(b => b.id === (selectedEmployee?.companyProfileId
                                                                || fixedCompanyProfileId))?.restaurantCode)
                               : null);
            const form = new FormData();
            form.append('file', file);
            form.append('employeeId',   String(empId));
            form.append('dokumentTypId', String(typId));
            if (empBranch) form.append('branchCode', empBranch);
            form.append('bemerkung', 'QST-Befreiung Bestätigungsschreiben');
            // WICHTIG: KEIN ah() benutzen — das setzt Content-Type:application/json
            // und der Browser kann dann den multipart/form-data-Boundary nicht mehr
            // selbst setzen → Server meldet „The file field is required".
            // Nur den Authorization-Header schicken (analog js/documents.js → dokUpload).
            const upRes = await fetch('/api/documents/upload', {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${authToken}` },
                body: form
            });
            if (!upRes.ok) {
                const txt = await upRes.text().catch(() => '');
                throw new Error(txt || 'Upload fehlgeschlagen');
            }
            const upBody = await upRes.json();
            dokId = upBody.id;
            statusEl.textContent = '';
        } catch (e) {
            statusEl.innerHTML = `<span style="color:#dc2626">${esc(e.message || 'Upload-Fehler')}</span>`;
            saveBtn.disabled = false;
            saveBtn.textContent = 'Befreiung speichern';
            return;
        }
    }

    const res = await fetch(`/api/employees/${empId}/qst-befreiung`, {
        method: 'PATCH', headers: { ...ah(), 'Content-Type':'application/json' },
        body: JSON.stringify({ befreit: true, dokumentId: dokId, gueltigAb: ab, gueltigBis: bis || null })
    });
    if (await window.lohnEditLock.handleResponse(res)) { saveBtn.disabled = false; saveBtn.textContent = 'Befreiung speichern'; return; }
    if (!res.ok) {
        const body = await res.clone().json().catch(() => ({}));
        alert(body.message || 'Fehler beim Speichern.');
        saveBtn.disabled = false;
        saveBtn.textContent = 'Befreiung speichern';
        return;
    }
    document.getElementById('qstBefreiungModal').remove();
    loadQuellensteuerTab(empId);
}

// QST-Version per Datum beenden (Walter 04.09.2026, Heirat mit CH/C).
async function qstErfassungBeenden(empId, qstId, validTo) {
    if (!empId || !qstId || !validTo) return;
    const ok = typeof liquidConfirm === 'function'
        ? await liquidConfirm(`QST-Erfassung per ${formatDate(validTo)} beenden?`, { yesLabel: 'Beenden' })
        : confirm(`QST-Erfassung per ${formatDate(validTo)} beenden?`);
    if (!ok) return;
    try {
        const r = await fetch(`/api/employees/${empId}/quellensteuer/${qstId}/beenden`, {
            method: 'POST', headers: ah(), body: JSON.stringify({ validTo, grund: 'Heirat mit CH/C-Partner — Befreiung (KS 45)' }),
        });
        const j = await r.json().catch(() => ({}));
        if (!r.ok) { alert(j.message || j.error || ('Fehler HTTP ' + r.status)); return; }
        if (typeof showToast === 'function') showToast(`✓ QST-Erfassung per ${formatDate(validTo)} beendet`, 'success');
        loadQuellensteuerTab(empId);
    } catch (e) { alert('Verbindungsfehler: ' + e.message); }
}

// Walter-Vorgabe 20.08.2026: roter Banner «Ehepartner-Angaben unvollständig»
// — der Lohnlauf ist gesperrt, bis Nationalität/Bewilligung/Erwerbstätig-
// Frage/Arbeitgeber des Ehepartners im Familie-Tab erfasst sind.
function renderQstPartnerBanner(pflicht) {
    if (!pflicht?.partnerDatenFehlen) return '';
    const maengel = (pflicht.partnerDatenMaengel || [])
        .map(m => `<li style="margin:2px 0">${esc(m)}</li>`).join('');
    return `
    <div style="background:#fef2f2;border:1px solid #fecaca;border-radius:12px;padding:12px 16px;margin-bottom:14px">
        <div style="font-weight:700;color:#b91c1c;font-size:13.5px;display:flex;align-items:center;gap:8px">
            ⚠ Ehepartner-Angaben unvollständig — Lohnlauf gesperrt
        </div>
        <ul style="margin:6px 0 8px 18px;padding:0;font-size:12.5px;color:#7f1d1d">${maengel}</ul>
        <div style="display:flex;gap:8px;flex-wrap:wrap;align-items:center">
            <button onclick="switchEmpTab('familie')"
                    style="background:#3f3f3f;color:#fff;border:1px solid #1a1a1a;padding:5px 14px;border-radius:12px;font-size:12px;font-weight:600;cursor:pointer">
                → Ehepartner im Familie-Tab vervollständigen
            </button>
            ${qstMailButtonHtml(selectedEmployeeId, 'partner-angaben')}
        </div>
        <div id="qstMailResult_partner-angaben"></div>
    </div>`;
}

// Walter-Vorgabe 20.08.2026: orange Tarif-Plausibilitäts-Warnungen (KS 45)
// — reine Hinweise, kein Block (verheiratet⇒B/C, C⇒Partner arbeitet, H-Regeln,
// A-Kinderziffer nur mit Behördenbewilligung).
function renderQstTarifWarnBanner(pflicht) {
    const w = pflicht?.tarifWarnungen || [];
    if (!w.length) return '';
    return `
    <div style="background:#fffbeb;border:1px solid #fde68a;border-radius:12px;padding:12px 16px;margin-bottom:14px">
        <div style="font-weight:700;color:#92400e;font-size:13px">Tarif prüfen (KS 45)</div>
        <ul style="margin:6px 0 0 18px;padding:0;font-size:12.5px;color:#854d0e">
            ${w.map(x => `<li style="margin:2px 0">${esc(x)}</li>`).join('')}
        </ul>
    </div>`;
}

function renderQuellensteuerTab(el, entries, pflicht, vorschlag, korrekturen) {
    // Walter-Vorgabe 26.05.2026: Pflicht-Banner OBEN (vor allem anderen).
    // Walter-Vorgabe 23.08.2026: die Tarif-Plausibilitäts-Warnungen wandern
    // vom Kopf-Banner DIREKT in die AKTUELL-Zeile der QST-Liste (roter Code
    // + Warntext + richtiger Vorschlags-Code) — kein renderQstTarifWarnBanner
    // mehr im Kopf.
    const banner = renderQstPflichtBanner(pflicht)
                 // Walter-Vorgabe 20.08.2026: Ehepartner-Angaben unvollständig
                 // (blockt Lohnlauf).
                 + renderQstPartnerBanner(pflicht)
                 // Walter-Vorgabe 04.08.2026: Kantonswechsel-Hinweis direkt
                 // darunter — Wohnkanton ≠ Kanton der aktuellen QST-Version
                 // → «🚚 Umzug erfassen» (Monatsregel Kreisschreiben 45).
                 + renderQstUmzugBanner(entries);
    // Bewilligungen + QST unter dem Pflicht-Banner. Bank steht darüber
    // (ausserhalb dieses Containers — Walter 19.07.2026).
    const permitsHtml = renderPermitListHtml(_permitHistoryCache || []);
    // Walter-Vorgabe 07.06.2026: Doku-Button neben „Bewilligungen" (analog
    // Bank-Tab) — öffnet die Dokumenten-Verwaltung gefiltert auf den
    // Permit-Dokument-Typ (linked_field_code='permit').
    // Titel-Knopf «Doku» entfernt (Walter 25.09.2026): Doku pro Bewilligung in der Zeile.
    const permitsSection = `
    <div style="margin-bottom:22px">
        <div class="emp-section-title" style="display:flex;align-items:center;justify-content:space-between;margin-top:0">
            <span style="display:inline-flex;align-items:center;gap:8px">
                Bewilligungen
                <!-- Doku pro Bewilligung in der Zeile, nicht neben dem Titel (Walter 25.09.2026) -->
                ${selectedEmployee?.zemisNumber ? `<span style="font-size:11px;font-weight:600;color:#6b7280;background:#ece9e2;border-radius:999px;padding:2px 10px;text-transform:none;letter-spacing:0" title="ZEMIS-Nummer (Ausländerregister) — von der Ausweis-Rückseite">ZEMIS ${esc(selectedEmployee.zemisNumber)}</span>` : ''}
                ${(window._permHistCount || 0) > 0 ? `<button id="permHistPill" onclick="permHistToggle()" title="Ältere Bewilligungen ein-/ausblenden" style="font-size:11px;font-weight:600;color:#6b7280;background:#ece9e2;border:none;border-radius:999px;padding:2px 10px;cursor:pointer;text-transform:none;letter-spacing:0">🕘 History (${window._permHistCount})</button>` : ''}
            </span>
            ${isOpsRole() ? `
            <button class="btn-emp-add" onclick="openPermitHistoryModal(null)">
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
                Neue Bewilligung
            </button>` : ''}
        </div>
        ${permitsHtml}
    </div>
    <div class="emp-section-title" style="margin-top:6px;display:flex;align-items:center;gap:10px">Quellensteuer
        <span style="flex:1"></span>
        ${isOpsRole() ? `<button class="btn-emp-add" style="background:#fff;border:1px solid rgba(60,55,48,0.3);color:#3f3f3f" onclick="openQstBefreiungModal()" title="Bestätigungsschreiben der Steuerbehörde hinterlegen — befreit den MA von der QST-Pflicht">
            📄 Behörden-Befreiung
        </button>
        <button class="btn-emp-add" onclick="openQstFromTab(null)">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
            Neue QST-Version
        </button>` : ''}
    </div>`;
    // „+ Neuer Eintrag" sitzt jetzt im Header (empTabActionBar,
    // Walter-Vorgabe 01.06.2026) — Toolbar hier versteckt.
    const toolbar = `
    <div class="emp-familie-toolbar" style="display:none">
        <button class="btn-emp-add" onclick="openQstFromTab(null)">
            Neuer Eintrag
        </button>
    </div>`;

    if (!entries.length) {
        el.innerHTML = banner + permitsSection + toolbar + `
        <div class="emp-placeholder">
            <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#cbd5e1" stroke-width="1.5"><rect x="2" y="3" width="20" height="14" rx="2"/><path d="M8 21h8M12 17v4"/></svg>
            <span>Keine Quellensteuer-Einträge erfasst</span>
        </div>`;
        return;
    }

    // Neueste zuerst
    const sorted = [...entries].sort((a, b) => (b.validFrom ?? '').localeCompare(a.validFrom ?? ''));
    let html = banner + permitsSection + toolbar;

    sorted.forEach(e => {
        const isCurrent = !e.validTo;
        const vonStr   = e.validFrom ? formatDate(e.validFrom) : '–';
        const bisStr   = e.validTo   ? formatDate(e.validTo)   : '…';
        const kanton   = e.steuerkanton   ?? '–';
        // Walter-Vorgabe 23.08.2026: IMMER aus den gerechneten Feldern
        // zusammensetzen (tarifCode+Kinder+Kirchensteuer) — qstCode ist nur
        // Cache und war vereinzelt inkonsistent (zeigte C0N, gerechnet A).
        const code     = e.tarifCode
            ? `${e.tarifCode}${e.anzahlKinder ?? 0}${e.kirchensteuer ? 'Y' : 'N'}`
            : (e.qstCode ?? '–');
        // Tarif-Warnungen direkt in der AKTUELL-Zeile (Walter 23.08.2026):
        // falscher Code ROT, daneben der richtige Server-Vorschlag in Grün.
        const zeilenWarnungen = (isCurrent && Array.isArray(pflicht?.tarifWarnungen))
            ? pflicht.tarifWarnungen : [];
        const vorschlagCode = (vorschlag && vorschlag.qstCode) ? vorschlag.qstCode : '';
        // Gemischter Konkubinatsfall (Walter 25.08.2026): KEIN Vorschlag —
        // stattdessen «Mit QST-Behörde abklären» in Orange.
        const abklaeren = !!(vorschlag && vorschlag.abklaerungNoetig);
        const codeHtml = zeilenWarnungen.length
            ? `<strong style="color:#b91c1c">⚠ ${code}</strong>`
              + (abklaeren
                    ? ` <span style="color:#b45309;font-weight:700">→ Mit QST-Behörde abklären</span>`
                    : (vorschlagCode && vorschlagCode !== code
                        ? ` <span style="color:#15803d;font-weight:700">→ richtig wäre ${vorschlagCode}</span>` : ''))
            : `<strong>${code}</strong>`;
        let warnZeilenHtml = zeilenWarnungen.length
            ? `<div style="font-size:12px;color:#b91c1c;margin-top:5px;line-height:1.45">${zeilenWarnungen.map(x => '⚠ ' + esc(x)).join('<br>')}</div>`
            : '';
        // Konkubinats-Sonderfall positiv erklären (Walter 25.08.2026: «zeigen,
        // dass obwohl ein Kind, wegen K-Partner A0N richtig ist») — grüne
        // Info-Zeile, wenn der erfasste Code dem Vorschlag entspricht.
        if (isCurrent && !zeilenWarnungen.length && vorschlag?.konkubinatInfo
            && vorschlagCode && vorschlagCode === code) {
            warnZeilenHtml += `<div style="font-size:12px;color:#15803d;margin-top:5px;line-height:1.45">✓ ${esc(vorschlag.konkubinatInfo)}</div>`;
        }
        // Walter 05.09.2026: Tarifbestätigung der Behörde ist das Mass aller
        // Dinge — weicht sie vom Vorschlag ab, das sichtbar sagen (grün).
        // Walter 07.09.2026: NICHT, wenn der Server Warnungen liefert (Familie
        // nach der Bestätigung geändert) — dann gilt die rote Warnung.
        if (isCurrent && e.dokumentId && vorschlagCode && vorschlagCode !== code && !zeilenWarnungen.length) {
            warnZeilenHtml += `<div style="font-size:12px;color:#15803d;margin-top:5px;line-height:1.45">✓ Tarif gemäss Tarifbestätigung der Steuerbehörde — gilt anstelle des Vorschlags (${esc(vorschlagCode)}).</div>`;
        }
        const kinder   = e.anzahlKinder   ?? 0;
        const kirche   = e.kirchensteuer  ? 'mit Kirchensteuer' : 'ohne Kirchensteuer';
        const pct      = e.prozentsatz    ? ` · ${Number(e.prozentsatz).toFixed(2)} %` : '';
        const gemeinde = e.qstGemeinde    ? ` · ${e.qstGemeinde}` : '';
        const teleRaw  = e.telearbeitProzent;
        const teleHtml = (teleRaw === 0 || teleRaw)
            ? ` · Telearbeit ${Number(teleRaw).toLocaleString('de-CH', { maximumFractionDigits: 2 })} %`
            : '';
        const eaIso    = (e.erfahrenAm || e.validFrom || '').toString().slice(0, 10);
        const eaStr    = eaIso ? formatDate(eaIso) : '';
        const eaSpaeter = eaIso && e.validFrom && eaIso.slice(0, 10) > String(e.validFrom).slice(0, 10);
        const eaHtml   = eaStr
            ? ` · erfahren ${eaStr}${eaSpaeter ? ' <span title="Wissensdatum nach Gültig-ab — Zwischenmonate per QST-Korrektur" style="color:#b45309;font-weight:600">(versetzt)</span>' : ''}`
            : '';

        html += `
        <div class="emp-family-card" style="border-left:3px solid ${isCurrent ? '#1a1a1a' : '#e2e8f0'};margin-bottom:12px">
            <div class="emp-family-card-head">
                <div>
                    <div class="emp-family-name" style="display:flex;align-items:center;gap:8px">
                        ${isCurrent ? '<span style="background:#f6f3ee;color:#6b7280;font-size:10px;font-weight:700;padding:2px 7px;border-radius:10px;letter-spacing:.04em">AKTUELL</span>' : ''}
                        <span>${vonStr} bis ${bisStr}</span>
                    </div>
                    <div style="font-size:12px;color:#64748b;margin-top:3px">
                        Kanton <strong>${kanton}</strong> · Code ${codeHtml} · ${kinder} Kinder · ${kirche}${pct}${gemeinde}${teleHtml}${eaHtml}
                        <!-- Walter 30.08.2026: Klartext-Erklärung zum Tarif.
                             Rein lokal aus der Tabelle qst_erklaerung — kein
                             externer Dienst, keine Daten nach draussen. -->
                        <button type="button" class="qst-warum-btn"
                                onclick="event.stopPropagation();qstErklaerungOeffnen(${selectedEmployeeId}, ${e.id})"
                                title="Erklärt in Alltagssprache, wie dieser Tarifcode zustande kommt">💡 Warum dieser Tarif?</button>
                    </div>${warnZeilenHtml}
                    <!-- Δ-Herleitungs-Diff in der Liste entfernt (Walter 04.09.2026:
                         «diese Erklärung brauche ich hier nicht») — die Herleitung
                         bleibt über «Warum dieser Tarif?» erreichbar. -->
                </div>
                <!-- Tarifbestätigung als Beleg (Walter 21.08.2026) — auch bei
                     gesperrten Einträgen verknüpfbar (reiner Beleg, kein Lock). -->
                ${docIconBtn({ docId: e.dokumentId, was: 'Tarifbestätigung', style: 'margin-left:auto;margin-right:8px',
                    verknuepfen: `openAusweisDokuModal(${selectedEmployeeId},'qst_tarif',{qstEntryId:${e.id}})`,
                    loesen: `qstTarifDokUnlink(${e.id})` })}
                ${e.inLohnVerwendet
                    ? `<span style="display:inline-flex;align-items:center;gap:6px;flex-shrink:0;margin-left:4px">
                        <span title="Tarif eingefroren (definitiv abgerechnet). «Erfahren am» kann noch nachgetragen werden — Korrektur der Zwischenmonate läuft automatisch." style="display:inline-flex;align-items:center;gap:4px;font-size:11px;font-weight:600;color:#b91c1c;background:#fee2e2;padding:4px 10px;border-radius:12px;cursor:help">🔒 verwendet${e.verwendetBis ? ' bis ' + e.verwendetBis.slice(5, 7) + '/' + e.verwendetBis.slice(0, 4) : ''}</span>
                        <button type="button" class="fam-tile-doc" onclick="openQstFromTab(${e.id})" title="Wissensdatum «Erfahren am» nachtragen">Erfahren am</button>
                       </span>`
                    : `<div class="dok-menu-wrap" style="flex-shrink:0">
                        <button class="dok-menu-btn" onclick="qstToggleMenu(event, ${e.id})" title="Aktionen">⋮</button>
                        <div class="dok-menu" id="qstMenu-${e.id}">
                            <button class="dok-menu-item" onclick="openQstFromTab(${e.id})">Bearbeiten</button>
                            <button class="dok-menu-item danger" onclick="deleteQstEntry(${e.id})">Löschen</button>
                        </div>
                       </div>`}
            </div>
            ${qstKorrSubrows(korrekturen, e.id)}
        </div>`;
    });

    el.innerHTML = html;
}

// K4.1 (Walter 29.08.2026): Herleitungs-Diff lesbar machen — die Server-Pfade
// («partner.erwerbstaetig: False → True») werden in deutsche Labels übersetzt.
function qstDiffLabel(d) {
    // Daten NIE im ISO-/US-Format anzeigen (Walter 04.09.2026): yyyy-mm-dd → dd.mm.yyyy
    return String(d).replace(/\b(\d{4})-(\d{2})-(\d{2})\b/g, '$3.$2.$1')
        .replace(/^resultat\.qstCode/, 'Code')
        .replace(/^resultat\.tarifCode/, 'Tarif')
        .replace(/^resultat\.kinderziffer/, 'Kinderziffer')
        .replace(/^resultat\.kirchensteuer/, 'Kirchensteuer')
        .replace(/^resultat\.spezielBewilligt/, 'Behördenbewilligung')
        .replace(/^resultat\.prozentsatz/, 'Prozentsatz')
        .replace(/^resultat\.kategorie/, 'Kategorie')
        .replace(/^zivilstandSeit/, 'Zivilstand seit')
        .replace(/^zivilstand/, 'Zivilstand')
        .replace(/^konfession/, 'Konfession')
        .replace(/^nationalitaet/, 'Nationalität')
        .replace(/^steuerkanton/, 'Steuerkanton')
        .replace(/^wohnKanton/, 'Wohnkanton')
        .replace(/^wohnLand/, 'Wohnland')
        .replace(/^gemeindeBfs/, 'Gemeinde (BFS)')
        .replace(/^wochenaufenthalt/, 'Wochenaufenthalt')
        .replace(/^grenzgaenger/, 'Grenzgänger')
        .replace(/^partner\./, 'Partner: ')
        .replace(/^konkubinat\./, 'Konkubinat: ')
        .replace(/^kinder\[(\d+)\]\./, (m, i) => `Kind ${Number(i) + 1}: `)
        .replace(/erwerbstaetig/, 'erwerbstätig')
        .replace(/lebtImHaushalt/, 'im Haushalt')
        .replace(/inErstausbildung/, 'Erstausbildung')
        .replace(/gemeinsamesKind/, 'gemeinsames Kind')
        .replace(/maHatHoeheresEinkommen/, 'MA höheres Einkommen')
        .replace(/bewilligungBis/, 'Bewilligung bis')
        .replace(/qstBerechtigBis/, 'QST-berechtigt bis')
        .replace(/arbeitgeber/, 'Arbeitgeber')
        .replace(/geburtsdatum/, 'Geburtsdatum')
        .replace(/\bTrue\b/g, 'Ja').replace(/\bFalse\b/g, 'Nein');
}

// K1 (Walter 29.08.2026): Korrektur-Posten als Unterzeilen der auslösenden
// Version — «beide Wahrheiten zeigen»: fachliche Gültigkeit + Abrechnung.
function qstKorrSubrows(korrekturen, versionId) {
    const eigene = (korrekturen || []).filter(k => k.neueVersionId === versionId);
    if (!eigene.length) return '';
    const monatsName = m => ['', 'Jan', 'Feb', 'Mär', 'Apr', 'Mai', 'Jun', 'Jul', 'Aug', 'Sep', 'Okt', 'Nov', 'Dez'][m] || m;
    const chip = st => ({
        OFFEN:       '<span style="background:#fef3c7;color:#92400e;border-radius:8px;padding:0 7px;font-size:10.5px;font-weight:600">offen — nächster Lohnlauf</span>',
        VERRECHNET:  '<span style="background:#dcfce7;color:#166534;border-radius:8px;padding:0 7px;font-size:10.5px;font-weight:600">verrechnet</span>',
        IN_DARLEHEN: '<span style="background:#dbeafe;color:#1e40af;border-radius:8px;padding:0 7px;font-size:10.5px;font-weight:600">in Darlehen</span>',
        GEMELDET:    '<span style="background:#e0e7ff;color:#3730a3;border-radius:8px;padding:0 7px;font-size:10.5px;font-weight:600">gemeldet</span>',
        VORJAHR:     '<span style="background:#fee2e2;color:#991b1b;border-radius:8px;padding:0 7px;font-size:10.5px;font-weight:600">Vorjahr — via Steuerverwaltung</span>',
    }[st] || esc(st));
    const fmt = v => (v ?? 0).toLocaleString('de-CH', { minimumFractionDigits: 2 });
    return `<div style="border-top:1px dashed rgba(60,55,48,0.18);margin-top:6px;padding:6px 0 2px 22px">
        ${eigene.sort((a, b) => (a.jahr * 100 + a.monat) - (b.jahr * 100 + b.monat)).map(k => `
        <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;font-size:12px;padding:2px 0">
            <span style="color:#8b8b8b">↳</span>
            <b style="width:70px">${monatsName(k.monat)} ${k.jahr}</b>
            <span>${esc(k.alterCode || '—')} → ${esc(k.neuerCode || '—')}</span>
            <span style="color:#8b8b8b">${fmt(k.alterBetrag)} → ${fmt(k.neuerBetrag)}</span>
            <b style="color:${k.differenz > 0 ? '#b91c1c' : '#166534'}">${k.differenz > 0 ? '+' : ''}${fmt(k.differenz)}</b>
            ${chip(k.status)}
        </div>`).join('')}
        ${(() => {
            // K3 (Walter 29.08.2026): hohe Nachbelastung → in zinsloses
            // Darlehen wandeln (Posten → IN_DARLEHEN, Verrechnung über Raten
            // statt in einem Lohnlauf). Nur bei OFFENEN Nachbelastungen.
            const offen = eigene.filter(k => k.status === 'OFFEN' && k.differenz > 0);
            if (!offen.length) return '';
            const summe = Math.round(offen.reduce((s, k) => s + k.differenz, 0) * 100) / 100;
            const ids = offen.map(k => k.id);
            const monate = offen.map(k => `${monatsName(k.monat)} ${k.jahr}`).join(', ');
            return `<div style="padding:4px 0 2px 22px">
                <button class="btn-emp-add" style="font-size:11.5px"
                        onclick='darlehenAusQst(${summe}, ${JSON.stringify(ids)}, ${JSON.stringify(monate)})'>
                    💰 In Darlehen wandeln (CHF ${fmt(summe)} in Raten statt im nächsten Lohnlauf)
                </button>
            </div>`;
        })()}
    </div>`;
}

// K3: QST-Nachbelastung → Darlehen (Konzept Kap. 4). Öffnet das Darlehen-
// Modal vorbefüllt; beim Speichern werden die Posten auf IN_DARLEHEN gesetzt
// (K2 verrechnet sie dann nicht mehr direkt im Lohnlauf).
function darlehenAusQst(summe, ids, monate) {
    // KEINE Auszahlung an den MA — das Geld ging an die Steuerbehörde.
    openDarlehenModal({ zweck: `QST-Nachzahlung ${monate}`, betrag: summe, auszahlungArt: 'KEINE' });
    window._dlQstIds = ids;   // NACH dem Öffnen setzen (openDarlehenModal resettet)
    const hint = document.getElementById('dl-hint');
    if (hint) hint.textContent = 'Aus QST-Korrektur: beim Speichern werden die Posten auf «in Darlehen» gesetzt und über die Raten statt im nächsten Lohnlauf verrechnet.';
}

// ══════════════════════════════════════════════════════════════════════
// Umzug / Kantonswechsel (Walter-Vorgabe 04.08.2026)
// ──────────────────────────────────────────────────────────────────────
// Amtliche Monatsregel (ESTV Kreisschreiben 45): der GESAMTE Umzugsmonat
// wird noch mit dem bisherigen Wohnkanton abgerechnet; der neue Kanton
// gilt ab dem 1. des Folgemonats. Beim alten Kanton zählt der letzte Tag
// des Umzugsmonats als Austritt, beim neuen der 1. des Folgemonats als
// Eintritt (Meldedaten für die Quellensteuermeldung an die Kantone).
// ══════════════════════════════════════════════════════════════════════

// Aktuelle (heute gültige) QST-Version — jüngstes validFrom, Tie-Break id
// (gleiche Auswahl wie Dashboard-Warnung qst_kanton_mismatch).
// Tarifbestätigung von der QST-Version lösen (Walter 21.08.2026) —
// das Dokument selbst bleibt im Doku-Tab erhalten.
// Dialog «Bestätigter Tarif» beim Verknüpfen der Tarifbestätigung
// (Walter 05.09.2026). Walter 07.09.2026: der GANZE Code (Buchstabe +
// Kinderziffer + Kirchensteuer) wird erfasst, und das Dokument wird daneben
// angezeigt (wie das Doku-Panel beim Ehepartner), damit HR beim Abtippen
// den Beleg vor Augen hat.
function qstTarifBestaetigungDialog(eintrag, dokumentId, dokName) {
    return new Promise(resolve => {
        document.getElementById('qstBestModal')?.remove();
        const tarife = [['A','A – Alleinstehend'],['B','B – Verheiratet, Alleinverdiener'],['C','C – Verheiratet, Doppelverdiener'],
                        ['D','D – Nebenerwerb'],['H','H – Alleinerziehend'],['L','L – Grenzgänger'],['M','M – Grenzgänger verheiratet'],
                        ['N','N – Grenzgänger Nebenerwerb'],['P','P – Pauschale'],['Q','Q – Grenzgänger alleinerz.']];
        const curT = (eintrag?.tarifCode || '').toUpperCase();
        const curK = !!eintrag?.kirchensteuer;
        const kinder = Number(eintrag?.anzahlKinder ?? 0);
        const selStyle = 'width:100%;background:#fff;border:1px solid rgba(60,55,48,0.22);border-radius:10px;padding:8px 10px;font-size:13.5px;color:#3f3f3f';
        const ov = document.createElement('div');
        ov.id = 'qstBestModal';
        ov.style.cssText = 'position:fixed;inset:0;background:rgba(30,28,25,0.45);z-index:9700;display:flex;align-items:center;justify-content:center;gap:14px;padding:16px';
        ov.innerHTML = `
            <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 50px rgba(60,55,48,0.22);max-width:440px;width:100%;padding:20px 22px;flex-shrink:0">
                <div style="font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:6px">Tarif gemäss Tarifbestätigung</div>
                <div style="font-size:12.5px;color:#646464;margin-bottom:14px">Die Bestätigung der Steuerbehörde gilt anstelle unseres Vorschlags. Erfasse den Tarifcode genau so, wie er auf der Bestätigung steht — Buchstabe, Kinderziffer und Kirchensteuer.</div>
                <div style="display:grid;grid-template-columns:1fr;gap:10px;margin-bottom:12px">
                    <div>
                        <label style="font-size:11.5px;font-weight:700;color:#646464;display:block;margin-bottom:4px">Tarif (erster Buchstabe)</label>
                        <select id="qstBestTarif" style="${selStyle}">
                            ${tarife.map(([v,l]) => `<option value="${v}" ${v === curT ? 'selected' : ''}>${l}</option>`).join('')}
                        </select>
                    </div>
                    <div style="display:grid;grid-template-columns:1fr 1fr;gap:10px">
                        <div>
                            <label style="font-size:11.5px;font-weight:700;color:#646464;display:block;margin-bottom:4px">Kinderziffer</label>
                            <select id="qstBestKinder" style="${selStyle}">
                                ${[0,1,2,3,4,5,6,7,8,9].map(n => `<option value="${n}" ${n === kinder ? 'selected' : ''}>${n}${n === kinder ? ' (aus Familie)' : ''}</option>`).join('')}
                            </select>
                        </div>
                        <div>
                            <label style="font-size:11.5px;font-weight:700;color:#646464;display:block;margin-bottom:4px">Kirchensteuer</label>
                            <select id="qstBestKirche" style="${selStyle}">
                                <option value="N" ${!curK ? 'selected' : ''}>N – ohne</option>
                                <option value="Y" ${curK ? 'selected' : ''}>Y – mit</option>
                            </select>
                        </div>
                    </div>
                </div>
                <div style="font-size:12.5px;color:#3f3f3f;margin-bottom:16px">Bestätigter Code: <strong id="qstBestCodePreview" style="font-size:15px;letter-spacing:.04em"></strong></div>
                <div style="display:flex;justify-content:flex-end;gap:10px">
                    <button id="qstBestNo" style="background:rgba(255,255,255,0.55);color:#3f3f3f;border:1px solid rgba(139,139,139,0.35);border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Abbrechen</button>
                    <button id="qstBestYes" style="background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:9px 18px;cursor:pointer;font-size:13.5px;font-weight:700">Verknüpfen &amp; übernehmen</button>
                </div>
            </div>
            <div id="qstBestDocPanel" class="side-docpanel" style="display:none;background:#fff;border-radius:14px;flex:1;min-width:380px;padding:14px;flex-direction:column;gap:8px;box-shadow:0 24px 48px rgba(0,0,0,0.25)">
                <div style="display:flex;align-items:center;justify-content:space-between;gap:8px">
                    <div id="qstBestDocName" style="font-size:12.5px;font-weight:700;color:#3f3f3f;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:0">${esc(dokName || 'Tarifbestätigung')}</div>
                    <button id="qstBestDocZoom" style="background:rgba(255,255,255,0.6);border:1px solid #e2ddd3;color:#3f3f3f;border-radius:10px;padding:6px 13px;font-size:12.5px;font-weight:600;cursor:pointer;flex-shrink:0" title="Im grossen Vorschaufenster öffnen (mit Drucken/Zoom)">⤢ Vergrössern</button>
                </div>
                <div id="qstBestDocView" class="side-docview" style="flex:1;min-height:0;overflow:auto;display:flex;align-items:flex-start;justify-content:center;background:#f6f3ee;border-radius:10px"></div>
            </div>`;
        document.body.appendChild(ov);
        const selT = ov.querySelector('#qstBestTarif');
        const selN = ov.querySelector('#qstBestKinder');
        const selK = ov.querySelector('#qstBestKirche');
        const upd = () => { ov.querySelector('#qstBestCodePreview').textContent = `${selT.value}${selN.value}${selK.value}`; };
        [selT, selN, selK].forEach(el => el.onchange = upd);
        upd();
        let objUrl = null;
        const done = v => { if (objUrl) URL.revokeObjectURL(objUrl); ov.remove(); resolve(v); };
        ov.querySelector('#qstBestNo').onclick = () => done(null);
        ov.querySelector('#qstBestYes').onclick = () => done({
            buchstabe: selT.value,
            anzahlKinder: parseInt(selN.value, 10),
            kirchensteuer: selK.value === 'Y'
        });
        // Dokument-Vorschau (best-effort, gleiche Mechanik wie famShowSpouseDoc).
        if (dokumentId) {
            const previewUrl = `/api/documents/preview/${dokumentId}`;
            ov.querySelector('#qstBestDocZoom').onclick = () => previewUrlFetch(previewUrl, dokName || 'tarifbestaetigung', ah());
            fetch(previewUrl, { headers: ah() }).then(async pr => {
                if (!pr.ok) return;
                const blob = await pr.blob();
                objUrl = URL.createObjectURL(blob);
                const view = ov.querySelector('#qstBestDocView');
                view.innerHTML = (blob.type || '').startsWith('image/')
                    ? `<img src="${objUrl}" style="max-width:100%;max-height:100%;object-fit:contain">`
                    : `<iframe src="${objUrl}" style="width:100%;height:100%;border:none"></iframe>`;
                ov.querySelector('#qstBestDocPanel').style.display = 'flex';
            }).catch(() => { /* Panel bleibt unsichtbar */ });
        }
    });
}

async function qstTarifDokUnlink(entryId) {
    if (!(await liquidConfirm('Verknüpfung zur Tarifbestätigung lösen? Das Dokument selbst bleibt erhalten.'))) return;
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}/dokument`, {
            method: 'PATCH',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ dokumentId: null })
        });
        if (!res.ok) { alert('Lösen fehlgeschlagen.'); return; }
        loadQuellensteuerTab(selectedEmployeeId);
    } catch {
        alert('Verbindungsfehler.');
    }
}

function _qstCurrentVersion(entries) {
    const d = new Date();
    const today = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
    const valid = (entries || []).filter(e =>
        (e.validFrom || '') <= today && (!e.validTo || e.validTo >= today));
    if (!valid.length) return null;
    valid.sort((a, b) => (b.validFrom || '').localeCompare(a.validFrom || '') || ((b.id || 0) - (a.id || 0)));
    return valid[0];
}

let _qstUmzugAlt = null;   // Kanton der aktuellen QST-Version
let _qstUmzugNeu = null;   // Wohnkanton des MA (Ziel-Kanton)

function renderQstUmzugBanner(entries) {
    const emp = selectedEmployee;
    if (!emp || !emp.cantonCode) return '';
    const cur = _qstCurrentVersion(entries);
    if (!cur || !cur.steuerkanton) return '';
    const wohn = String(emp.cantonCode).trim().toUpperCase();
    const qstK = String(cur.steuerkanton).trim().toUpperCase();
    if (!wohn || !qstK || wohn === qstK) return '';
    // Kantonswechsel BEREITS erfasst (Walter 08.08.2026): existiert eine
    // künftige Version mit dem Wohnkanton (z.B. via Umzugsdatum-Bestätigung
    // im Wohnort-Dialog), grünen Info-Banner zeigen statt nochmal zu fordern.
    const dNow = new Date();
    const todayIso = `${dNow.getFullYear()}-${String(dNow.getMonth() + 1).padStart(2, '0')}-${String(dNow.getDate()).padStart(2, '0')}`;
    const erfasst = (entries || []).find(e =>
        String(e.steuerkanton || '').trim().toUpperCase() === wohn
        && (e.validFrom || '') > todayIso);
    if (erfasst) {
        const f = (iso) => `${String(iso).slice(8, 10)}.${String(iso).slice(5, 7)}.${String(iso).slice(0, 4)}`;
        return `
        <div style="background:#f0fdf4;border:1px solid #86efac;border-left:4px solid #16a34a;border-radius:8px;padding:10px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px">
            <span style="font-size:16px">✅</span>
            <div style="font-size:12.5px;color:#166534">
                <b>Kantonswechsel erfasst:</b> Umzugsmonat noch ${esc(qstK)}, <b>${esc(wohn)}</b> gilt ab <b>${f(erfasst.validFrom)}</b> (Kreisschreiben 45).
            </div>
        </div>`;
    }
    return `
    <div style="background:#fff7ed;border:1px solid #fdba74;border-left:4px solid #ea580c;border-radius:8px;padding:12px 14px;margin-bottom:14px;display:flex;align-items:center;gap:10px;flex-wrap:wrap">
        <span style="font-size:18px">🚚</span>
        <div style="flex:1;min-width:200px">
            <div style="font-weight:700;color:#9a3412;font-size:13px">Wohnkanton ${esc(wohn)} ≠ QST-Kanton ${esc(qstK)}</div>
            <div style="color:#c2410c;font-size:12px;margin-top:2px">
                Die aktuelle QST-Version rechnet mit Kanton ${esc(qstK)}, der MA wohnt aber in ${esc(wohn)}.
                Bei einem Umzug: der ganze Umzugsmonat bleibt ${esc(qstK)}, ${esc(wohn)} gilt ab dem 1. des Folgemonats (Kreisschreiben 45).
            </div>
        </div>
        <button onclick="openQstUmzugModal('${esc(qstK)}','${esc(wohn)}')" style="background:#ea580c;color:#fff;border:none;padding:8px 14px;border-radius:6px;font-size:12px;font-weight:600;cursor:pointer;margin-left:auto;white-space:nowrap">
            🚚 Umzug erfassen
        </button>
    </div>`;
}

function openQstUmzugModal(altKanton, neuKanton) {
    if (!selectedEmployeeId) return;
    _qstUmzugAlt = altKanton;
    _qstUmzugNeu = neuKanton;

    const ktName = (code) => (typeof kantonNameFor === 'function' && kantonNameFor(code))
        ? `${code} — ${kantonNameFor(code)}` : code;

    const d = new Date();
    const todayIso = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;

    document.getElementById('qstUmzugModal')?.remove();
    const wrap = document.createElement('div');
    wrap.id = 'qstUmzugModal';
    wrap.style.cssText = 'position:fixed;inset:0;z-index:10050;display:flex;align-items:center;justify-content:center;background:rgba(60,55,48,0.34);backdrop-filter:blur(6px)';
    wrap.addEventListener('click', (ev) => { if (ev.target === wrap) closeQstUmzugModal(); });
    // Liquid-Glass-Karte (Walter-Vorgabe 01.07.2026): Off-White, Glasrand,
    // Kohle-Primärpille, heller Glas-Sekundärbutton.
    wrap.innerHTML = `
    <div style="background:#faf8f5;border:1px solid rgba(255,255,255,0.62);border-radius:16px;box-shadow:0 18px 48px rgba(60,55,48,0.22);width:min(440px,92vw);padding:22px 24px" onclick="event.stopPropagation()">
        <div id="qstUmzugBody">
            <div style="font-size:16px;font-weight:700;color:#3f3f3f;display:flex;align-items:center;gap:8px">🚚 Umzug erfassen</div>
            <div style="font-size:12px;color:#8b8b8b;margin-top:3px">${esc(selectedEmployee ? `${selectedEmployee.firstName ?? ''} ${selectedEmployee.lastName ?? ''}`.trim() : '')}</div>

            <div style="display:flex;align-items:center;gap:10px;margin:16px 0 14px;background:rgba(255,255,255,0.48);border:1px solid rgba(255,255,255,0.62);border-radius:12px;padding:10px 14px">
                <div style="flex:1">
                    <div style="font-size:10.5px;font-weight:700;color:#8b8b8b;letter-spacing:.05em;text-transform:uppercase">Bisher (QST)</div>
                    <div style="font-size:13px;font-weight:600;color:#3f3f3f;margin-top:2px">${esc(ktName(altKanton))}</div>
                </div>
                <span style="font-size:16px;color:#6b7280">→</span>
                <div style="flex:1;text-align:right">
                    <div style="font-size:10.5px;font-weight:700;color:#8b8b8b;letter-spacing:.05em;text-transform:uppercase">Neu (Wohnkanton)</div>
                    <div style="font-size:13px;font-weight:600;color:#3f3f3f;margin-top:2px">${esc(ktName(neuKanton))}</div>
                </div>
            </div>

            <label style="display:block;font-size:12px;font-weight:600;color:#646464;margin-bottom:5px">Umzugsdatum *</label>
            <input type="date" id="qstUmzugDatum" value="${todayIso}" onchange="qstUmzugUpdateInfo()" oninput="qstUmzugUpdateInfo()"
                   style="width:100%;box-sizing:border-box;background:rgba(255,255,255,0.58);border:1px solid rgba(255,255,255,0.62);border-radius:10px;padding:9px 12px;font-size:13px;color:#3f3f3f;box-shadow:0 1px 3px rgba(60,55,48,0.14)">

            <div id="qstUmzugInfo" style="margin-top:12px;background:rgba(255,255,255,0.38);border:1px solid rgba(255,255,255,0.62);border-radius:10px;padding:10px 12px;font-size:12px;color:#646464;line-height:1.5"></div>

            <div style="display:flex;justify-content:flex-end;gap:10px;margin-top:18px">
                <button onclick="closeQstUmzugModal()" style="background:rgba(255,255,255,0.55);border:1px solid rgba(255,255,255,0.62);color:#646464;padding:9px 16px;border-radius:12px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 1px 3px rgba(60,55,48,0.14)">Abbrechen</button>
                <button id="qstUmzugSaveBtn" onclick="saveQstUmzug()" style="background:#1a1a1a;border:none;color:#fff;padding:9px 18px;border-radius:12px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 3px 8px rgba(60,55,48,0.22)">Umzug speichern</button>
            </div>
        </div>
    </div>`;
    document.body.appendChild(wrap);
    qstUmzugUpdateInfo();
    if (window.lohnEditLock && typeof window.lohnEditLock.loadState === 'function') {
        // Gesperrte Tage im Date-Picker gar nicht erst anbieten — der Server
        // prüft den STICHTAG (Folgemonat) nochmals autoritativ.
        const branchId = (typeof fixedCompanyProfileId !== 'undefined') ? fixedCompanyProfileId : null;
        if (branchId) {
            window.lohnEditLock.loadState(branchId).then(state => {
                const inp = document.getElementById('qstUmzugDatum');
                if (inp && state) window.lohnEditLock.applyToDateInput(inp, state);
            }).catch(() => {});
        }
    }
}

function closeQstUmzugModal() {
    document.getElementById('qstUmzugModal')?.remove();
}

// Info-Text der Monatsregel — live beim Datumswechsel aktualisiert.
function qstUmzugUpdateInfo() {
    const inp  = document.getElementById('qstUmzugDatum');
    const info = document.getElementById('qstUmzugInfo');
    if (!inp || !info) return;
    if (!inp.value) {
        info.innerHTML = 'Bitte ein Umzugsdatum wählen.';
        return;
    }
    const [y, m, d] = inp.value.split('-').map(Number);
    let austritt, eintritt, satz;
    if (d === 1) {
        // Spezialfall Umzug am 1. (Walter 08.08.2026): kein angebrochener
        // Monat — der neue Kanton gilt ab genau diesem Tag.
        const py = m === 1 ? y - 1 : y;
        const pm = m === 1 ? 12    : m - 1;
        const plast = new Date(py, pm, 0).getDate();
        austritt = `${String(plast).padStart(2, '0')}.${String(pm).padStart(2, '0')}.${py}`;
        eintritt = `01.${String(m).padStart(2, '0')}.${y}`;
        satz = `Umzug am Monatsersten — kein angebrochener Monat: <b>${esc(_qstUmzugNeu || '?')}</b> gilt ab <b>${eintritt}</b>, <b>${esc(_qstUmzugAlt || '?')}</b> bis ${austritt}.`;
    } else {
        const lastDay = new Date(y, m, 0).getDate();                  // letzter Tag des Umzugsmonats
        const fy      = m === 12 ? y + 1 : y;
        const fm      = m === 12 ? 1     : m + 1;
        austritt = `${String(lastDay).padStart(2, '0')}.${String(m).padStart(2, '0')}.${y}`;
        eintritt = `01.${String(fm).padStart(2, '0')}.${fy}`;
        satz = `Der ganze Umzugsmonat wird noch mit <b>${esc(_qstUmzugAlt || '?')}</b> abgerechnet (bis ${austritt});
        <b>${esc(_qstUmzugNeu || '?')}</b> gilt ab <b>${eintritt}</b> (Monatsregel Kreisschreiben 45).`;
    }
    info.innerHTML = `${satz}<br>
        Meldung an die Kantone: ${esc(_qstUmzugAlt || '?')} Austritt ${austritt}, ${esc(_qstUmzugNeu || '?')} Eintritt ${eintritt}.`;
}

async function saveQstUmzug() {
    const empId = selectedEmployeeId;
    const inp   = document.getElementById('qstUmzugDatum');
    if (!empId || !inp || !inp.value) { alert('Bitte ein Umzugsdatum angeben.'); return; }
    const btn = document.getElementById('qstUmzugSaveBtn');
    const resetBtn = () => { if (btn) { btn.disabled = false; btn.textContent = 'Umzug speichern'; } };
    if (btn) { btn.disabled = true; btn.textContent = 'Speichert…'; }
    try {
        const res = await fetch(`/api/employee-quellensteuer/${empId}/umzug`, {
            method: 'POST',
            headers: { ...ah(), 'Content-Type': 'application/json' },
            body: JSON.stringify({ umzugsDatum: inp.value, neuerKanton: _qstUmzugNeu || null })
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) { resetBtn(); return; }
        const j = await res.json().catch(() => ({}));
        if (!res.ok) {
            alert(j.message || j.error || 'Fehler beim Erfassen des Umzugs.');
            resetBtn();
            return;
        }
        // Erfolg: Meldedaten BLEIBEN im Modal sichtbar (nicht nur Toast) —
        // Walter braucht sie für die Quellensteuermeldung an die Kantone.
        const fmtIso = (iso) => iso ? `${String(iso).slice(8, 10)}.${String(iso).slice(5, 7)}.${String(iso).slice(0, 4)}` : '–';
        const body = document.getElementById('qstUmzugBody');
        if (body) {
            body.innerHTML = `
            <div style="font-size:16px;font-weight:700;color:#166534;display:flex;align-items:center;gap:8px">✅ Umzug erfasst</div>
            <div style="margin-top:12px;background:#f0fdf4;border:1px solid #86efac;border-radius:10px;padding:12px 14px;font-size:12.5px;color:#166534;line-height:1.6">
                QST-Versionen aktualisiert: Umzugsmonat noch <b>${esc(j.alterKanton || _qstUmzugAlt || '?')}</b>,
                <b>${esc(j.neuerKanton || _qstUmzugNeu || '?')}</b> gilt ab <b>${fmtIso(j.neuerKantonEintritt)}</b>.<br>
                <b>Meldung an die Kantone:</b><br>
                • ${esc(j.alterKanton || _qstUmzugAlt || '?')} — Austritt <b>${fmtIso(j.alterKantonAustritt)}</b><br>
                • ${esc(j.neuerKanton || _qstUmzugNeu || '?')} — Eintritt <b>${fmtIso(j.neuerKantonEintritt)}</b>
            </div>
            <div style="display:flex;justify-content:flex-end;margin-top:16px">
                <button onclick="closeQstUmzugModal()" style="background:#1a1a1a;border:none;color:#fff;padding:9px 18px;border-radius:12px;font-size:13px;font-weight:600;cursor:pointer;box-shadow:0 3px 8px rgba(60,55,48,0.22)">Schliessen</button>
            </div>`;
        }
        if (typeof showToast === 'function') {
            showToast(`Meldung: ${j.alterKanton || ''} Austritt ${fmtIso(j.alterKantonAustritt)}, ${j.neuerKanton || ''} Eintritt ${fmtIso(j.neuerKantonEintritt)}`, 'success');
        }
        // QST-Tab neu laden (Versionen-Liste + Banner verschwinden lassen).
        if (typeof loadQuellensteuerTab === 'function') loadQuellensteuerTab(empId);
        if (typeof reloadLohnAfterQstChange === 'function') reloadLohnAfterQstChange(empId);
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
        resetBtn();
    }
}

// Walter-Vorgabe 07.06.2026: QST-Eintrag löschen — gleiche UI-Logik wie
// bei der Bewilligung. Backend prüft im Edit-Lock-Service ob der Eintrag
// schon in einem Lohnlauf verwendet wurde.
// Walter-Vorgabe 07.06.2026: Ehegatten-Dokument hochladen — nutzt den
// normalen Doku-Upload-Dialog. Walter wählt im Modal selbst die Kategorie
// (z.B. „Persönliche Angaben → Ehegatten Dokumente"). Damit ist die UI
// unabhängig von der konkreten Dokument-Struktur — keine Hardcode-Codes,
// die brechen wenn Walter die Struktur ändert.
//
// openDokUploadModal() returnt sofort, wenn _dokState.empId noch nicht
// gesetzt ist. Wir laden daher zuerst die Doku-Daten des aktuellen MA
// (befüllt _dokState.empId + _dokState.taxonomy), dann das Modal öffnen.
async function fmOpenSpouseDocUpload() {
    if (!selectedEmployeeId) {
        alert('Bitte zuerst einen Mitarbeiter wählen.');
        return;
    }
    if (typeof loadEmpDokumente === 'function') {
        try { await loadEmpDokumente(selectedEmployeeId); } catch {}
    }
    // Walter-Vorgabe 07.06.2026 (Variante C+): Vorauswahl bevorzugt über
    // linked_field_code='spouse' (in der Dokument-Struktur als „Ehegatte
    // (Familie)" wählbar). Walter pflegt das einmal pro Dokument-Typ — z.B.
    // „Ausweis Ehegatte" mit Verknüpfung 'spouse'. Beim Klick wird dann der
    // passende TYP direkt vorausgewählt (nicht nur die Kategorie). Fallback:
    // Name-Match „Ehegatte/Ehepartner/Spouse" auf Kategorie-Ebene. Bei keinem
    // Treffer bleibt der Picker offen, Walter wählt selbst.
    try {
        const tax = (typeof _dokState !== 'undefined') ? _dokState.taxonomy : null;
        if (Array.isArray(tax)) {
            let foundTyp = null;
            let foundKat = null;
            for (const k of tax) {
                for (const t of (k.typen || [])) {
                    if (t.linkedFieldCode === 'spouse') {
                        foundTyp = t;
                        foundKat = k;
                        break;
                    }
                }
                if (foundTyp) break;
            }
            if (foundTyp) {
                _dokState.selectedKategorieId = foundKat.id;
                _dokState.selectedTypId = foundTyp.id;
            } else {
                // Fallback Name-Match auf Kategorie-Ebene
                const cat = tax.find(k => {
                    const n = (k.name || '').toLowerCase();
                    return n.includes('ehegatte') || n.includes('ehepartner') || n.includes('spouse');
                });
                if (cat) {
                    _dokState.selectedKategorieId = cat.id;
                    _dokState.selectedTypId = null;
                }
            }
        }
    } catch {}
    if (typeof openDokUploadModal === 'function') {
        openDokUploadModal();
    } else {
        alert('Doku-Upload-Modal nicht geladen. Bitte Seite neu laden.');
    }
}

async function deleteQstEntry(entryId) {
    if (!selectedEmployeeId || !entryId) return;
    if (!(await liquidConfirm('Diesen Quellensteuer-Eintrag wirklich löschen?\n\nDer aktuelle QST-Status wird automatisch neu ermittelt.'))) return;
    try {
        let res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}`, {
            method: 'DELETE', headers: ah()
        });
        if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
        if (!res.ok) {
            const j = await res.json().catch(() => ({}));
            // Admin-Force-Delete für abgeschlossene Historie-Versionen
            // (Walter 21.08.2026): Fehlerfassungen/Testdaten bereinigen.
            // Die Lohnlauf-Sperre bleibt auch für den Admin bestehen.
            if (j.error === 'QST_ABGESCHLOSSEN' && currentUser?.role === 'admin') {
                if (!(await liquidConfirm(
                    'Diese QST-Version ist abgeschlossene HISTORIE.\n\n'
                    + 'Als Admin kannst du sie trotzdem endgültig löschen — z.B. bei einer '
                    + 'Fehlerfassung. Danach fehlt dieser Zeitraum in der QST-Zeitachse.\n\n'
                    + 'Wirklich endgültig löschen?',
                    { title: 'Historie löschen (Admin)', yesLabel: 'Endgültig löschen', noLabel: 'Abbrechen' }))) return;
                res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}?force=true`, {
                    method: 'DELETE', headers: ah()
                });
                if (window.lohnEditLock && await window.lohnEditLock.handleResponse(res)) return;
                if (!res.ok) {
                    const j2 = await res.json().catch(() => ({}));
                    alert(j2.message || j2.error || 'Fehler beim Löschen.');
                    return;
                }
            } else {
                alert(j.message || j.error || 'Fehler beim Löschen.');
                return;
            }
        }
        if (typeof loadQuellensteuerTab === 'function') {
            await loadQuellensteuerTab(selectedEmployeeId);
        }
        if (typeof selectEmployee === 'function') {
            await selectEmployee(selectedEmployeeId);
        }
        if (typeof reloadLohnAfterQstChange === 'function') {
            reloadLohnAfterQstChange(selectedEmployeeId);
        }
    } catch (e) {
        alert('Verbindungsfehler: ' + e.message);
    }
}

async function openQstFromTab(entryId) {
    if (!selectedEmployeeId || !selectedEmployee) return;
    qstOpenedFromTab    = true;
    qstCurrentEmployeeId = selectedEmployeeId;
    qstCurrentEntryId    = entryId ?? null;
    // Damit openQstEntry() Steuerkanton/Gemeinde/BFS aus den MA-Stammdaten
    // automatisch vorschlagen kann, qstEmployeeData hier setzen.
    qstEmployeeData     = selectedEmployee;

    const emp = selectedEmployee;

    // Stammdaten im Modal setzen
    const setTxt = (id, val) => { const el = document.getElementById(id); if (el) el.textContent = val; };
    setTxt('qstModalSub',         `${emp.firstName ?? ''} ${emp.lastName ?? ''}`.trim());
    setTxt('qstPermitDisplay',
        emp.permitType
            ? `${emp.permitType.code}${emp.permitType.description ? ' — ' + emp.permitType.description : ''}`
            : (emp.permitTypeId ? 'Typ ' + emp.permitTypeId : '–'));
        setTxt('qstWohnortDisplay',   [emp.zipCode, stripCityCantonSuffix(emp.city)].filter(Boolean).join(' ') || '–');
    // Nationalität immer als Volltext (Walter-Vorgabe 14.05.2026) — der
    // Backend liefert nationalityName als Klartext (AppText → ISO-Tabelle).
    setTxt('qstNatDisplay',       emp.nationalityName ?? emp.nationality ?? '–');
    const _ktName = (typeof kantonNameFor === 'function') ? kantonNameFor(emp.cantonCode) : null;
    setTxt('qstKantonDisplay',    emp.cantonCode ? (_ktName ? `${emp.cantonCode} — ${_ktName}` : emp.cantonCode) : '–');
    setTxt('qstZivilstandDisplay', emp.maritalStatus ?? '–');
    // Inline-Stammzeile (Zivilstand/seit/Konfession) füllen — dieser
    // Tab-Öffnungspfad läuft NICHT über openQstModal (Walter-Bug 29.08.2026).
    if (typeof qstFillStammzeile === 'function') qstFillStammzeile();
    // Wohnsituation (Wochenaufenthalt-Zusatzadresse) laden + Lock anwenden —
    // populateQstForm ruft qstApplyWochenaufenthaltLock, braucht die Daten.
    if (typeof loadQstWochenaufenthalt === 'function') {
        await loadQstWochenaufenthalt(selectedEmployeeId);
    }

    // Verlauf laden
    try {
        const res = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer`, { headers: ah() });
        qstAllEntries = res.ok ? await res.json() : [];
    } catch { qstAllEntries = []; }
    if (typeof renderQstHistoryTabs === 'function') renderQstHistoryTabs();

    // Ehepartner-Info aus Familie-Tab anzeigen
    if (typeof loadQstPartnerInfo === 'function') {
        loadQstPartnerInfo(selectedEmployeeId);
    }

    // Kinder aus dem Familie-Tab laden (Walter-Bug 12.08.2026): dieser
    // Tab-Öffnungspfad hat den Kinder-Cache nie befüllt — deshalb stand
    // «Keine Kinder erfasst», obwohl Kinder vorhanden waren.
    if (typeof loadQstFamilyKinder === 'function') {
        await loadQstFamilyKinder(selectedEmployeeId);
    }

    // Formular befüllen
    if (entryId) {
        try {
            const r = await fetch(`/api/employees/${selectedEmployeeId}/quellensteuer/${entryId}`, { headers: ah() });
            if (r.ok) {
                const entry = await r.json();
                populateQstForm(entry);
                // Walter-Vorgabe 14.06.2026: bestehende Einträge NIE auto-
                // overwriten — Server-Vorschlag NUR als Banner zeigen.
                if (typeof qstFetchServerVorschlag === 'function') {
                    await qstFetchServerVorschlag(entry?.validFrom);
                    if (typeof qstRenderVorschlagBanner === 'function') qstRenderVorschlagBanner();
                    if (typeof qstUpdateAutoKinderHint === 'function') qstUpdateAutoKinderHint();
                }
            }
        } catch {}
    } else {
        // Neuer Eintrag → openQstEntry(null) übernimmt:
        // - Felder leeren
        // - Gültig-ab = letzter Eintrag.gültigBis + 1 Tag (oder heute)
        // - Auto-Fill Steuerkanton, Gemeinde, BFS-Nr aus Wohnadresse
        if (typeof openQstEntry === 'function') {
            await openQstEntry(null);
        } else {
            populateQstForm(null);
            const vf = document.getElementById('qstValidFrom');
            if (vf) vf.value = new Date().toISOString().slice(0, 10);
        }
    }

    // Gültig-ab-Handler binden + Kopf (Adresse/Zivilstand am Datum) sofort
    // zum aktuellen Gültig-ab laden (Walter 04.09.2026).
    if (typeof qstBindValidFromHandlers === 'function') qstBindValidFromHandlers();
    const vfKopf = document.getElementById('qstValidFrom');
    if (vfKopf && vfKopf.value && typeof qstUpdateWohnortKopf === 'function') qstUpdateWohnortKopf(vfKopf.value);

    document.getElementById('qstSaveResult').textContent = '';
    document.getElementById('qstModal').style.display = 'flex';
}

