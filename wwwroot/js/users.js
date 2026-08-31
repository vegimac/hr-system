// ══════════════════════════════════════════════════════════════════════
// users.js — extrahiert aus index.html (Phase A)
// ══════════════════════════════════════════════════════════════════════

// ══════════════════════════════════════════════
// BENUTZER
// ══════════════════════════════════════════════
async function loadUsers() {
    // View-as/Testmodus-Karte nur für den Superadmin zeigen (Backend erzwingt es zusätzlich).
    const impCard = document.getElementById('impersonateCard');
    // Walter 31.08.2026: Entscheidung am ECHTEN Zustand (aus /api/auth/me),
    // nicht am localStorage — der konnte veraltet sein und die Karte
    // fälschlich zeigen oder verstecken.
    if (impCard) impCard.style.display = (currentUser?.isSuperAdmin && !currentUser?.impersonating) ? '' : 'none';
    // Mirus-Mail-Vorschau nur für admin (Endpoint ist admin-only).
    const mirusBtn = document.getElementById('mirusDigestPreviewBtn');
    if (mirusBtn) mirusBtn.style.display = (currentUser?.role === 'admin') ? '' : 'none';
    const tbody = document.getElementById('userTbody');
    tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;padding:28px;color:#94a3b8">Lade...</td></tr>`;
    try {
        const res = await fetch('/api/users', { headers: ah() });
        const users = await res.json();
        // Sortierung nach Vorname, dann Nachname (Walter-Vorgabe 13.07.2026 —
        // gleiche Konvention wie alle MA-Listen).
        users.sort((a, b) =>
            String(a.firstName || '').localeCompare(String(b.firstName || ''), 'de', { sensitivity: 'base' })
            || String(a.lastName || '').localeCompare(String(b.lastName || ''), 'de', { sensitivity: 'base' }));
        tbody.innerHTML = '';
        users.forEach(u => {
            // Super-Admin (Walter-Vorgabe 15.05.2026) wirkt als Badge UND ersetzt
            // die Role-Anzeige — egal welche darunter liegende Role gesetzt ist.
            const rc = u.isSuperAdmin
                        ? 'b-super-admin'
                        : (u.role === 'admin'     ? 'b-admin'
                        :  u.role === 'superuser' ? 'b-superuser'
                        :  u.role === 'lowuser'   ? 'b-lowuser'
                                                  : 'b-user');
            const rLabel = u.isSuperAdmin ? 'Super-Admin' : roleName(u.role);
            const bText = u.role === 'admin'
                ? '<span style="color:#94a3b8;font-size:12px">Alle</span>'
                : (u.branches?.length > 0
                    ? u.branches.map(b => `<span class="badge b-code" style="margin:1px;font-size:10px">${b.code}</span>`).join('')
                    : '<span style="color:#94a3b8;font-size:12px">Keine</span>');
            const dt = new Date(u.createdAt).toLocaleDateString('de-CH');
            // "Letzter Login": Datum + Uhrzeit (de-CH), oder "—" wenn noch nie eingeloggt.
            const llt = u.lastLoginAt
                ? new Date(u.lastLoginAt).toLocaleString('de-CH', { day:'2-digit', month:'2-digit', year:'numeric', hour:'2-digit', minute:'2-digit' })
                : '<span style="color:#94a3b8">—</span>';
            // Walter-Vorgabe 14.06.2026: ⋮-Menü statt direkter Bearbeiten/Löschen-
            // Buttons — Standard für alle Tabellen-Aktionen. Wiederverwendung
            // von dok-menu-btn + dokToggleMenu/dokCloseAllMenus aus documents.js.
            const canDelete = !u.isSuperAdmin
                && !(u.role === 'admin' && !(typeof currentUser !== 'undefined' && currentUser?.isSuperAdmin));
            const safeUsername = (u.username || '').replace(/'/g, "\\'");
            const menuHtml = `
                <div style="position:relative;display:inline-block">
                    <button class="dok-menu-btn" onclick="dokToggleMenu(event, 'user-${u.id}')" title="Aktionen">⋮</button>
                    <div class="dok-menu" id="dokMenu-user-${u.id}">
                        <button class="dok-menu-item" onclick="dokCloseAllMenus();openUserModal(${u.id})">Bearbeiten</button>
                        ${canDelete
                            ? `<button class="dok-menu-item danger" onclick="dokCloseAllMenus();deleteUser(${u.id},'${safeUsername}')">Löschen</button>`
                            : ''}
                    </div>
                </div>`;
            tbody.innerHTML += `<tr>
                <td>
                    <div style="display:flex;align-items:center;gap:9px">
                        <div style="width:30px;height:30px;background:#e2e8f0;border-radius:50%;display:flex;align-items:center;justify-content:center;font-weight:700;font-size:12px;color:#475569;flex-shrink:0">${((u.firstName||u.username||'?')[0]).toUpperCase()}</div>
                        <div>
                            <div style="font-weight:600">${u.firstName ? u.firstName + ' ' + (u.lastName||'') : u.username}${u.hasSignature ? ' <span title="Unterschrift hinterlegt" style="font-size:11px">✍️</span>' : ''}</div>
                            ${u.phone ? `<div style="font-size:11px;color:#94a3b8">${u.phone}</div>` : ''}
                        </div>
                    </div>
                </td>
                <td style="color:#64748b">${u.email}</td>
                <td><span class="badge ${rc}">${rLabel}</span></td>
                <td>${bText}</td>
                <td><span class="badge ${u.isActive ? 'b-active' : 'b-inactive'}">${u.isActive ? 'Aktiv' : 'Inaktiv'}</span></td>
                <td style="color:#94a3b8;font-size:12px">${dt}</td>
                <td style="color:#475569;font-size:12px">${llt}</td>
                <td>${menuHtml}</td>
            </tr>`;
        });
    } catch {
        tbody.innerHTML = `<tr><td colspan="8" style="text-align:center;padding:28px;color:#dc2626">Fehler beim Laden.</td></tr>`;
    }
}

function umUpdateBranchVisibility() {
    const role      = document.getElementById('umRole').value;
    const isAdmin   = role === 'admin';
    const hint      = document.getElementById('umBranchHint');
    const grid      = document.getElementById('umBranches');
    // Filialen-Auswahl ist IMMER aktiv (auch für Admin) — der Hint erklärt nur den Effekt.
    grid.style.opacity = '1';
    grid.style.pointerEvents = 'auto';
    if (isAdmin) {
        hint.textContent = 'Administrator sieht standardmässig alle Filialen. Wähle hier optional, '
                         + 'auf welche der Admin direkten Zugriff haben soll (leer = alle).';
    } else {
        hint.textContent = 'Wähle die Filialen, auf die dieser Benutzer Zugang hat.';
    }

    // HR-Team-Toggle für Admins + Buchhaltung + lowuser ausblenden
    // (für sie irrelevant — lowuser hat eh keinen HR-Bereich).
    const hrGroup = document.getElementById('umIsHrTeam')?.closest('.f-group');
    if (hrGroup) hrGroup.style.display = (isAdmin || role === 'buchhaltung' || role === 'lowuser') ? 'none' : 'block';
}

// ── «Aus Mitarbeiter übernehmen» (Walter-Vorgabe 13.07.2026) ────────────
// Für GF-Konten: MA im Picker wählen → Name/E-Mail/Telefon aus dem MA-
// Datensatz übernehmen, Rolle auf «user» setzen, Filiale(n) der aktiven
// Verträge anhaken. Nur beim ERSTELLEN sichtbar.
let _umEmpByLabel = {};
async function umFillFromEmpPicker() {
    const dl = document.getElementById('umFromEmpList');
    const grp = document.getElementById('umFromEmpGroup');
    const inp = document.getElementById('umFromEmp');
    if (!dl || !grp) return;
    grp.style.display = 'block';
    if (inp) inp.value = '';
    try {
        const emps = (await loadEmployeeLookup())
            .filter(e => e.isActive && !e.isPayrollExcluded);
        _umEmpByLabel = {};
        dl.innerHTML = emps.map(e => {
            const label = `${e.firstName || ''} ${e.lastName || ''} (${e.employeeNumber || ''})`.trim();
            _umEmpByLabel[label] = e;
            return `<option value="${label.replace(/"/g, '&quot;')}"></option>`;
        }).join('');
    } catch (_) { /* Picker best-effort */ }
}
async function umFromEmpPicked() {
    const inp = document.getElementById('umFromEmp');
    const e = inp ? _umEmpByLabel[inp.value] : null;
    if (!e) return;   // erst bei exakter Auswahl aus der Liste reagieren
    try {
        const r = await fetch(`/api/employees/${e.id}`, { headers: ah(), cache: 'no-store' });
        if (!r.ok) return;
        const emp = await r.json();
        document.getElementById('umFirstName').value = emp.firstName || '';
        document.getElementById('umLastName').value  = emp.lastName  || '';
        document.getElementById('umEmail').value     = emp.email     || '';
        document.getElementById('umPhone').value     = emp.phoneMobile || '';
        // Rolle GF vorschlagen + Filialen der aktiven Verträge anhaken
        const roleSel = document.getElementById('umRole');
        if (roleSel) { roleSel.value = 'user'; if (typeof umUpdateBranchVisibility === 'function') umUpdateBranchVisibility(); }
        const cpids = new Set((e.employments || [])
            .filter(v => v.isActive !== false && (!v.contractEndDate || new Date(v.contractEndDate) >= new Date()))
            .map(v => Number(v.companyProfileId)).filter(Boolean));
        document.querySelectorAll('#umBranches input[type=checkbox]').forEach(cb => {
            if (cpids.has(Number(cb.value))) cb.checked = true;
        });
    } catch (_) { /* best-effort */ }
}

function openUserModal(userId = null) {
    editingUserId = userId;
    document.getElementById('userModalAlert').innerHTML = '';
    document.getElementById('userModalTitle').textContent = userId ? 'Benutzer bearbeiten' : 'Benutzer erstellen';
    // MA-Übernahme-Picker nur beim Erstellen (Walter 13.07.2026)
    const feGrp = document.getElementById('umFromEmpGroup');
    if (feGrp) feGrp.style.display = 'none';
    if (!userId) umFillFromEmpPicker();
    document.getElementById('umPwHint').textContent = userId ? 'Leer lassen = Passwort unverändert' : 'Pflichtfeld';
    document.getElementById('umStatusGroup').style.display = userId ? 'block' : 'none';

    // Unterschrift-Sektion: nur für bestehende User. Bei neuen Usern erst
    // nach dem Erstellen verfügbar (Upload braucht eine User-ID).
    // Walter-Vorgabe 14.06.2026: Section-Titel + Group gemeinsam ein-/ausblenden.
    const sigGroup = document.getElementById('umSignatureGroup');
    const sigTitle = document.getElementById('umSignatureSectionTitle');
    const sigStatus = document.getElementById('umSigStatus');
    if (sigStatus) sigStatus.textContent = '';
    if (sigGroup) sigGroup.style.display = userId ? 'block' : 'none';
    if (sigTitle) sigTitle.style.display = userId ? 'block' : 'none';
    if (userId) umLoadSignaturePreview(userId);

    // Build branch checkboxes
    const cont = document.getElementById('umBranches');
    cont.innerHTML = '';
    allBranches.forEach(b => {
        cont.innerHTML += `
            <label class="branch-cb">
                <input type="checkbox" name="branch" value="${b.id}">
                <div>
                    <div class="branch-cb-name">${b.branchName || b.companyName}</div>
                    <div class="branch-cb-code">${b.restaurantCode ? '#' + b.restaurantCode : ''} · ${b.city || ''}</div>
                </div>
            </label>`;
    });

    // Passwort-Felder zurücksetzen (inkl. Live-Match-Indicator)
    document.getElementById('umPassword').value        = '';
    document.getElementById('umPasswordConfirm').value = '';
    if (typeof umCheckPwMatch === 'function') umCheckPwMatch();

    if (userId) {
        fetch('/api/users', { headers: ah() }).then(r => r.json()).then(users => {
            const u = users.find(x => x.id === userId);
            if (!u) return;
            document.getElementById('umFirstName').value = u.firstName || '';
            document.getElementById('umLastName').value  = u.lastName  || '';
            document.getElementById('umEmail').value     = u.email;
            document.getElementById('umPhone').value     = u.phone     || '';
            document.getElementById('umRole').value      = u.role;
            document.getElementById('umActive').value    = u.isActive.toString();
            document.getElementById('umIsHrTeam').checked = !!u.isHrTeam;
            document.getElementById('umCanCompanyDokumente').checked = !!u.canCompanyDokumente;
            document.getElementById('umMirusDigest').checked = !!u.receivesMirusChangeDigest;
            document.getElementById('umIdleTimeout').value = u.idleTimeoutMinutes ?? '';
            document.getElementById('umMaxSession').value  = u.maxSessionMinutes ?? '';
            const ids = u.branches?.map(b => b.id) || [];
            document.querySelectorAll('#umBranches input[type=checkbox]').forEach(cb => {
                cb.checked = ids.includes(parseInt(cb.value));
            });
            // Sichtbare Bereiche: null (noch nicht definiert) => alle anhaken.
            umSetAreas(u.allowedAreas ?? null);
            umUpdateBranchVisibility();
        });
    } else {
        document.getElementById('umFirstName').value = '';
        document.getElementById('umLastName').value  = '';
        document.getElementById('umEmail').value     = '';
        document.getElementById('umPhone').value     = '';
        document.getElementById('umRole').value      = 'user';
        document.getElementById('umActive').value    = 'true';
        document.getElementById('umIsHrTeam').checked = false;
        document.getElementById('umCanCompanyDokumente').checked = false;
        document.getElementById('umMirusDigest').checked = false;
        document.getElementById('umIdleTimeout').value = '';
        document.getElementById('umMaxSession').value  = '';
        umSetAreas(null);   // neuer User: standardmässig alle Bereiche sichtbar
        umUpdateBranchVisibility();
    }
    document.getElementById('userModalBg').classList.add('open');
}

function closeUserModal() { document.getElementById('userModalBg').classList.remove('open'); editingUserId = null; }

// ── Sichtbare Bereiche (8 Menüpunkte) — Walter 28.06.2026 ─────────────
// arr = Array von Bereichs-Schlüsseln, oder null => alle anhaken (Default für
// neue/legacy User, die noch keine eigene Auswahl haben).
function umSetAreas(arr) {
    document.querySelectorAll('#umAreas input[type=checkbox]').forEach(cb => {
        const a = cb.getAttribute('data-area');
        cb.checked = (arr == null) ? true : arr.includes(a);
    });
}
function umAreasSetAll(v) {
    document.querySelectorAll('#umAreas input[type=checkbox]').forEach(cb => { cb.checked = v; });
}
function umGetAreas() {
    return Array.from(document.querySelectorAll('#umAreas input[type=checkbox]:checked'))
        .map(cb => cb.getAttribute('data-area'));
}

// ── Unterschrift-Verwaltung (im Benutzer-Modal) ────────────────────────
async function umLoadSignaturePreview(userId) {
    const prev = document.getElementById('umSigPreview');
    const rmBtn = document.getElementById('umSigRemoveBtn');
    if (!prev) return;
    // Cache-bust mit Timestamp, damit nach Upload sofort die neue Version erscheint
    const url = `/api/users/${userId}/signature?_=${Date.now()}`;
    try {
        const r = await fetch(url, { headers: ah() });
        if (r.ok) {
            const blob = await r.blob();
            const objUrl = URL.createObjectURL(blob);
            prev.innerHTML = `<img src="${objUrl}" alt="Unterschrift" style="max-width:100%;max-height:100%;object-fit:contain">`;
            if (rmBtn) rmBtn.style.display = '';
        } else {
            prev.textContent = 'Keine Unterschrift hinterlegt';
            if (rmBtn) rmBtn.style.display = 'none';
        }
    } catch {
        prev.textContent = 'Keine Unterschrift hinterlegt';
        if (rmBtn) rmBtn.style.display = 'none';
    }
}

async function umUploadSignature(file) {
    if (!file || !editingUserId) return;
    if (file.size > 2 * 1024 * 1024) {
        document.getElementById('umSigStatus').textContent = 'Datei zu groß (max 2 MB).';
        document.getElementById('umSigStatus').style.color = '#b91c1c';
        return;
    }
    const status = document.getElementById('umSigStatus');
    status.textContent = 'Lade hoch…';
    status.style.color = '#64748b';

    const fd = new FormData();
    fd.append('file', file, file.name || 'signature.png');
    try {
        const r = await fetch(`/api/users/${editingUserId}/signature`, {
            method: 'PUT',
            headers: { 'Authorization': `Bearer ${authToken}` },
            body: fd
        });
        if (!r.ok) {
            const txt = await r.text();
            status.textContent = 'Fehler: ' + (txt || ('HTTP ' + r.status));
            status.style.color = '#b91c1c';
            return;
        }
        status.textContent = '✓ Unterschrift gespeichert.';
        status.style.color = '#15803d';
        umLoadSignaturePreview(editingUserId);
        // File-Input zurücksetzen, damit gleicher File-Pick nochmal triggert
        document.getElementById('umSigFile').value = '';
    } catch (e) {
        status.textContent = 'Fehler: ' + (e?.message || e);
        status.style.color = '#b91c1c';
    }
}

async function umRemoveSignature() {
    if (!editingUserId) return;
    if (!confirm('Unterschrift wirklich entfernen?')) return;
    const status = document.getElementById('umSigStatus');
    try {
        const r = await fetch(`/api/users/${editingUserId}/signature`, {
            method: 'DELETE', headers: ah()
        });
        if (!r.ok) {
            status.textContent = 'Fehler: HTTP ' + r.status;
            status.style.color = '#b91c1c';
            return;
        }
        status.textContent = 'Unterschrift entfernt.';
        status.style.color = '#64748b';
        umLoadSignaturePreview(editingUserId);
    } catch (e) {
        status.textContent = 'Fehler: ' + (e?.message || e);
        status.style.color = '#b91c1c';
    }
}

function umCheckPwMatch() {
    const pw   = document.getElementById('umPassword').value;
    const pw2  = document.getElementById('umPasswordConfirm').value;
    const hint = document.getElementById('umPwMatchHint');
    const conf = document.getElementById('umPasswordConfirm');
    if (!pw && !pw2) {
        hint.textContent = 'Schützt vor Tippfehlern';
        hint.style.color = '';
        conf.style.borderColor = '';
        return;
    }
    if (pw && pw2 && pw === pw2) {
        hint.textContent = '✓ Übereinstimmend';
        hint.style.color = '#15803d';
        conf.style.borderColor = '#22c55e';
        return;
    }
    if (pw2) {
        hint.textContent = '✗ Stimmt nicht überein';
        hint.style.color = '#b91c1c';
        conf.style.borderColor = '#ef4444';
        return;
    }
    // pw vorhanden, pw2 leer
    hint.textContent = 'Bitte zum Bestätigen nochmal eintragen';
    hint.style.color = '#a16207';
    conf.style.borderColor = '';
}

async function saveUser() {
    const alertEl = document.getElementById('userModalAlert');
    const showErr = (msg) => { alertEl.innerHTML = `<div class="alert alert-err">${msg}</div>`; alertEl.scrollIntoView({behavior:'smooth', block:'nearest'}); };
    alertEl.innerHTML = '';

    const firstName = document.getElementById('umFirstName').value.trim();
    const lastName  = document.getElementById('umLastName').value.trim();
    const email     = document.getElementById('umEmail').value.trim();
    const phone     = document.getElementById('umPhone').value.trim() || null;
    const password  = document.getElementById('umPassword').value;
    const passwordConfirm = document.getElementById('umPasswordConfirm').value;
    const role      = document.getElementById('umRole').value;
    const isActive  = document.getElementById('umActive').value === 'true';
    const isHrTeam  = document.getElementById('umIsHrTeam').checked;
    // Zugriff Filial-Dokumente (Walter 06.08.2026) — Tab «Dokumente» im Filial-Detail.
    const canCompanyDokumente = document.getElementById('umCanCompanyDokumente').checked;
    const receivesMirusChangeDigest = document.getElementById('umMirusDigest').checked;
    const branchIds = Array.from(document.querySelectorAll('#umBranches input:checked')).map(cb => parseInt(cb.value));
    const username  = `${firstName} ${lastName}`.trim() || email;

    // Session-Policy: leer = Rollen-Default (null), sonst 5–1440 (Walter 21.06.2026).
    const parsePolicy = (raw, label) => {
        const s = (raw || '').trim();
        if (s === '') return { ok: true, value: null };
        const n = parseInt(s, 10);
        if (isNaN(n) || n < 5 || n > 1440) return { ok: false, label };
        return { ok: true, value: n };
    };
    const idleP = parsePolicy(document.getElementById('umIdleTimeout').value, 'Inaktivitäts-Logout');
    const maxP  = parsePolicy(document.getElementById('umMaxSession').value, 'Max. Session-Dauer');

    // Validierung
    if (!email) { showErr('Bitte E-Mail eintragen.'); return; }
    if (!editingUserId) {
        // Beim Anlegen: Vor-/Nachname Pflicht
        if (!firstName || !lastName) { showErr('Bitte Vor- und Nachname eintragen.'); return; }
        if (!password) { showErr('Bitte ein Passwort eingeben.'); return; }
    }
    // Passwort-Wiederholung prüfen (wenn Passwort eingegeben wurde)
    if (password) {
        if (password.length < 8) { showErr('Passwort muss mindestens 8 Zeichen lang sein.'); return; }
        if (password !== passwordConfirm) { showErr('Die beiden Passwort-Eingaben stimmen nicht überein.'); return; }
    }
    if (!idleP.ok) { showErr(`${idleP.label} muss zwischen 5 und 1440 Minuten liegen (oder leer für Rollen-Standard).`); return; }
    if (!maxP.ok)  { showErr(`${maxP.label} muss zwischen 5 und 1440 Minuten liegen (oder leer für Rollen-Standard).`); return; }

    const body = { username, firstName, lastName, phone, email, password: password || null, role, isActive, isHrTeam,
                   canCompanyDokumente, receivesMirusChangeDigest, branchIds,
                   idleTimeoutMinutes: idleP.value, maxSessionMinutes: maxP.value,
                   allowedAreas: umGetAreas() };

    try {
        let res;
        if (editingUserId) {
            res = await fetch(`/api/users/${editingUserId}`, {
                method: 'PUT',
                headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
        } else {
            res = await fetch('/api/users', {
                method: 'POST',
                headers: { 'Authorization': `Bearer ${authToken}`, 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
        }
        if (!res.ok) {
            let msg = 'Fehler beim Speichern.';
            try {
                const d = await res.json();
                msg = d.message || d.title || msg;
            } catch { msg = `Server antwortete mit ${res.status}.`; }
            showErr(msg);
            return;
        }
        closeUserModal();
        showPageAlert('userPageAlert', 'Benutzer erfolgreich gespeichert.', 'ok');
        loadUsers();
    } catch (err) {
        console.error('saveUser failed:', err);
        showErr('Verbindungsfehler: ' + (err.message || 'Unbekannt'));
    }
}

async function deleteUser(id, name) {
    if (!confirm(`Benutzer «${name}» wirklich löschen?`)) return;
    try {
        const res = await fetch(`/api/users/${id}`, { method: 'DELETE', headers: ah() });
        if (res.ok) { showPageAlert('userPageAlert', 'Benutzer gelöscht.', 'ok'); loadUsers(); }
        else showPageAlert('userPageAlert', 'Fehler beim Löschen.', 'err');
    } catch { showPageAlert('userPageAlert', 'Verbindungsfehler.', 'err'); }
}

function showPageAlert(elId, msg, type) {
    const el = document.getElementById(elId);
    el.innerHTML = `<div class="alert alert-${type}" style="margin-bottom:16px">${msg}</div>`;
    setTimeout(() => el.innerHTML = '', 4000);
}

// ── Mirus-Änderungsmail: 1:1 Vorschau (ohne Versand) ───────────────────
// Filiale = immer Sidebar links oben (fixedCompanyProfileId) — kein eigenes Dropdown.
let _mirusDigestBlobUrl = null;

function mirusDigestIsOpen() {
    const modal = document.getElementById('mirusDigestPreviewModal');
    return !!(modal && modal.style.display === 'flex');
}

function mirusDigestCurrentBranch() {
    const id = (typeof fixedCompanyProfileId === 'number') ? fixedCompanyProfileId : null;
    if (!id || typeof allBranches === 'undefined') return null;
    return allBranches.find(b => b.id === id) || null;
}

function mirusDigestSyncBranchLabel() {
    const el = document.getElementById('mirusDigestBranchLabel');
    if (!el) return;
    const br = mirusDigestCurrentBranch();
    if (!br) {
        el.textContent = 'Filiale: in der Sidebar links oben wählen';
        return;
    }
    const code = br.restaurantCode || '';
    const name = br.branchName || br.companyName || ('Filiale ' + br.id);
    el.textContent = code ? `Filiale: ${code} – ${name}` : `Filiale: ${name}`;
}

function openMirusDigestPreview() {
    const modal = document.getElementById('mirusDigestPreviewModal');
    if (!modal) return;
    modal.style.display = 'flex';
    mirusDigestSyncBranchLabel();
    loadMirusDigestPreview();
}

function closeMirusDigestPreview() {
    const modal = document.getElementById('mirusDigestPreviewModal');
    if (modal) modal.style.display = 'none';
    const iframe = document.getElementById('mirusDigestFrame');
    if (iframe) iframe.src = 'about:blank';
    if (_mirusDigestBlobUrl) {
        URL.revokeObjectURL(_mirusDigestBlobUrl);
        _mirusDigestBlobUrl = null;
    }
}

function mirusDigestPreviewQuery() {
    const br = mirusDigestCurrentBranch();
    const code = (br?.restaurantCode || '').trim();
    const cpId = br?.id;
    const p = new URLSearchParams();
    if (cpId) p.set('companyProfileId', String(cpId));
    else if (code) p.set('restaurantCode', code);
    const q = p.toString();
    return { br, code, cpId, qs: q ? ('?' + q) : '' };
}

/** Vorschau per SMTP an die eigene Admin-Mail (oder walter.schaub@gmail.com). */
async function sendMirusDigestPreviewToMe() {
    const hint = document.getElementById('mirusDigestPreviewHint');
    const { qs } = mirusDigestPreviewQuery();
    let url = '/api/mirus-change-digest/send-preview-to-me' + qs;
    // Falls am User keine Mail hängt: feste Test-Adresse Walter.
    if (url.indexOf('?') >= 0) url += '&to=' + encodeURIComponent('walter.schaub@gmail.com');
    else url += '?to=' + encodeURIComponent('walter.schaub@gmail.com');
    if (hint) hint.textContent = 'Sende Vorschau-Mail…';
    try {
        const res = await fetch(url, { method: 'POST', headers: { 'Authorization': `Bearer ${authToken}` } });
        const data = await res.json().catch(() => ({}));
        if (!res.ok) {
            if (hint) hint.textContent = data.message || (`Versand fehlgeschlagen (HTTP ${res.status}).`);
            alert(data.message || ('Versand fehlgeschlagen (HTTP ' + res.status + ').'));
            return;
        }
        if (hint) hint.textContent = data.message || 'Vorschau gesendet.';
        alert(data.message || 'Vorschau-Mail gesendet.');
    } catch (e) {
        if (hint) hint.textContent = 'Netzwerkfehler beim Versand.';
        alert('Netzwerkfehler beim Versand.');
    }
}

async function loadMirusDigestPreview() {
    const iframe = document.getElementById('mirusDigestFrame');
    const hint = document.getElementById('mirusDigestPreviewHint');
    if (!iframe) return;
    mirusDigestSyncBranchLabel();
    const { code, cpId, qs } = mirusDigestPreviewQuery();
    const url = '/api/mirus-change-digest/preview' + qs;
    if (hint) hint.textContent = 'Lade Vorschau…';
    iframe.src = 'about:blank';
    try {
        const res = await fetch(url, { headers: { 'Authorization': `Bearer ${authToken}` } });
        const html = await res.text();
        if (!res.ok) {
            if (hint) hint.textContent = `Fehler ${res.status} — Vorschau nicht geladen.`;
            iframe.srcdoc = `<p style="font-family:sans-serif;padding:24px;color:#991b1b">Fehler ${res.status}</p>`;
            return;
        }
        if (_mirusDigestBlobUrl) URL.revokeObjectURL(_mirusDigestBlobUrl);
        _mirusDigestBlobUrl = URL.createObjectURL(new Blob([html], { type: 'text/html;charset=utf-8' }));
        iframe.src = _mirusDigestBlobUrl;
        if (hint) {
            const label = code || (cpId ? ('#' + cpId) : 'alle Filialen');            hint.textContent = `Vorschau für ${label} — letzte 24 h, wird nicht gesendet. Filiale links in der Sidebar wechseln.`;
        }
    } catch (err) {
        if (hint) hint.textContent = 'Verbindungsfehler.';
        iframe.srcdoc = `<p style="font-family:sans-serif;padding:24px;color:#991b1b">Verbindungsfehler</p>`;
    }
}

