// ══════════════════════════════════════════════════════════════════════
// liquid-select.js — generisches Liquid-Glass-Dropdown (Walter 13.07.2026)
//
// Ersetzt native <select>-Menüs durch ein Liquid-Control im App-Look.
// Das Original-Select bleibt VERSTECKT im DOM und trägt weiterhin den
// Wert — alle bestehenden IDs, onchange-Handler und Lese-Pfade
// funktionieren unverändert (Liquid-Glass-Konvention, CLAUDE.md).
//
// Verwendung (Walter-Vorgabe 16.07.2026, ABSOLUT: «immer und immer wieder
// unsere alte, schwarze Liste» — Schluss damit):
//   • ALLE <select> der App werden AUTOMATISCH umgebaut (opt-out statt
//     opt-in). Ausnahmen: multiple, size>1, Klasse .no-liquid.
//   • Später eingefügte Selects (Modals, JS-Re-Render) erfasst ein
//     MutationObserver auf document.body automatisch.
//   • window.liquidifySelect(el)    → gezielt per JS
//   • el._lqRefresh()               → Button-Text nach programmatischem
//                                     value-Set auffrischen (zusätzlich
//                                     synct ein 400ms-Timer die Labels).
// Options-/Text-Änderungen (z.B. Zähler) werden per MutationObserver
// automatisch übernommen. Panel ist FIXED positioniert — kein Clipping
// in scrollenden Modals/Containern.
// ══════════════════════════════════════════════════════════════════════
(function () {
    'use strict';

    // Ab so vielen Einträgen zeigt das Panel oben ein Suchfeld.
    const SUCH_AB = 8;

    function lqEsc(s) {
        return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
    }

    function liquidifySelect(sel) {
        if (!sel || sel._lq || sel.tagName !== 'SELECT') return;
        if (sel.multiple || (sel.size && sel.size > 1)) return;
        if (sel.classList.contains('no-liquid')) return;
        // Bug-Fix 17.07.2026: disabled Selects (z.B. easy@work-gesperrte
        // Felder mit data-easywork-locked im MA-Edit) NICHT umbauen — sonst
        // wird das gesperrte Feld ueber den Liquid-Button doch aenderbar.
        if (sel.disabled) return;
        // Selects, die SELBST bewusst versteckt sind, sind Datenquellen eines
        // bereits gebauten Custom-Controls (z.B. #branchSelect hinter dem
        // Sidebar-Filial-Selektor, #liquidBranchSelect auf dem Dashboard) —
        // NICHT umbauen, sonst erscheint ein zweites Auswahlfeld
        // (Walter-Bug 16.07.2026: «ploetzlich 2 filial auswahl felder»).
        // Wichtig: NUR das Inline-Style/hidden-Attribut pruefen — Selects in
        // (noch) versteckten Seiten/Modals sollen normal umgebaut werden.
        if (sel.style.display === 'none' || sel.hasAttribute('hidden')) return;
        if (sel.classList.contains('liquid-branch-select')) return;
        // Schon von lightSelect (.ls2) umgebaut → nicht doppelt wrappen.
        if (sel.dataset.ls2 || sel.closest('.ls2')) return;
        sel._lq = true;

        const wrap = document.createElement('div');
        wrap.className = 'lqsel-wrap';
        // Breite des Original-Selects uebernehmen (width:100% / flex bleiben erhalten)
        if (sel.style.width)    wrap.style.width    = sel.style.width;
        if (sel.style.minWidth) wrap.style.minWidth = sel.style.minWidth;
        if (sel.style.flex)     wrap.style.flex     = sel.style.flex;
        sel.parentNode.insertBefore(wrap, sel);
        wrap.appendChild(sel);
        sel.style.display = 'none';

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'lqsel-btn';
        const panel = document.createElement('div');
        panel.className = 'lqsel-panel';
        panel.style.display = 'none';
        // Tipp-Suche (Walter 04.10.2026): «mcb» oder «205» → McBonus.
        const search = document.createElement('input');
        search.type = 'text';
        search.className = 'lqsel-search';
        search.placeholder = 'Suchen … (Text oder Nummer)';
        search.autocomplete = 'off';
        search.spellcheck = false;
        const list = document.createElement('div');
        list.className = 'lqsel-list';
        panel.appendChild(search);
        panel.appendChild(list);
        wrap.appendChild(btn);
        wrap.appendChild(panel);

        const curLabel = () => {
            const o = sel.selectedIndex >= 0 ? sel.options[sel.selectedIndex] : null;
            return o ? o.textContent.trim() : '';
        };
        // Zähler-Spalte (Walter 08.09.2026): <option data-badge="N"> → Name links,
        // Anzahl rechts in einer fixen Spalte als Pille (Postfächer).
        const badgeHtml = (o) => {
            if (!o || o.dataset.badge === undefined) return '';
            const n = parseInt(o.dataset.badge, 10) || 0;
            return `<span class="lqsel-badge${n > 0 ? ' on' : ''}">${n > 0 ? n : '–'}</span>`;
        };
        function renderBtn() {
            const o = sel.selectedIndex >= 0 ? sel.options[sel.selectedIndex] : null;
            btn.innerHTML = `<span class="lqsel-label">${lqEsc(curLabel() || '– wählen –')}</span>${badgeHtml(o)}<span class="lqsel-chev">▾</span>`;
            // disabled-State des Original-Selects auf den Button spiegeln
            // (Bug-Fix 17.07.2026 — Sperre darf nicht umgehbar sein).
            btn.disabled = sel.disabled;
            btn.style.cursor = sel.disabled ? 'not-allowed' : '';
            btn.style.opacity = sel.disabled ? '0.55' : '';
        }
        new MutationObserver(renderBtn).observe(sel, { attributes: true, attributeFilter: ['disabled'] });
        const optHtml = (o) =>
            o.hidden ? '' :
            o.dataset.badge !== undefined
                ? `<div class="lqsel-opt lqsel-opt-badge${o.value === sel.value ? ' sel' : ''}${o.disabled ? ' dis' : ''}" data-v="${lqEsc(o.value)}"><span class="lqsel-opt-label">${lqEsc(o.textContent.trim())}</span>${badgeHtml(o)}</div>`
                : `<div class="lqsel-opt${o.value === sel.value ? ' sel' : ''}${o.disabled ? ' dis' : ''}" data-v="${lqEsc(o.value)}">${lqEsc(o.textContent.trim())}</div>`;
        function renderPanel() {
            let html = '';
            Array.from(sel.children).forEach(node => {
                if (node.tagName === 'OPTGROUP') {
                    html += `<div class="lqsel-group">${lqEsc(node.label)}</div>`;
                    Array.from(node.children).forEach(o => { if (o.tagName === 'OPTION') html += optHtml(o); });
                } else if (node.tagName === 'OPTION') {
                    html += optHtml(node);
                }
            });
            list.innerHTML = html || '<div class="lqsel-group">— leer —</div>';
            const mitSuche = sel.options.length >= SUCH_AB;
            panel.classList.toggle('lqsel-mit-suche', mitSuche);
            search.style.display = mitSuche ? '' : 'none';
            if (!mitSuche) search.value = '';
            applyFilter();
        }

        const norm = (s) => String(s || '').toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '');
        function sichtbareOpts() {
            return Array.from(list.querySelectorAll('.lqsel-opt')).filter(el => el.style.display !== 'none' && !el.classList.contains('dis'));
        }
        function setActive(el) {
            list.querySelectorAll('.lqsel-opt.active').forEach(x => x.classList.remove('active'));
            if (el) { el.classList.add('active'); el.scrollIntoView && el.scrollIntoView({ block: 'nearest' }); }
        }
        function applyFilter() {
            const q = norm(search.value.trim());
            list.querySelectorAll('.lqsel-opt').forEach(el => {
                el.style.display = !q || norm(el.textContent).includes(q) ? '' : 'none';
            });
            let group = null, groupHatTreffer = false;
            const schliesseGruppe = () => { if (group) group.style.display = groupHatTreffer ? '' : 'none'; };
            Array.from(list.children).forEach(el => {
                if (el.classList.contains('lqsel-group')) { schliesseGruppe(); group = el; groupHatTreffer = false; }
                else if (el.style.display !== 'none') groupHatTreffer = true;
            });
            schliesseGruppe();
            let leer = list.querySelector('.lqsel-leer');
            const keine = q && !sichtbareOpts().length;
            if (keine && !leer) {
                leer = document.createElement('div');
                leer.className = 'lqsel-group lqsel-leer';
                leer.textContent = 'Kein Treffer';
                list.appendChild(leer);
            } else if (!keine && leer) leer.remove();
            setActive(q ? sichtbareOpts()[0] : null);
        }
        function choose(el) {
            if (!el || el.classList.contains('dis')) return;
            sel.value = el.getAttribute('data-v');
            sel.dispatchEvent(new Event('change'));
            renderBtn();
            close();
            btn.focus({ preventScroll: true });
        }

        // Panel am body → nicht von Modal-transform als Containing-Block
        // eingefangen (Walter-Bug 25.07.2026: Dropdown «zu weit unten»).
        function onDocDown(e) {
            if (!wrap.contains(e.target) && !panel.contains(e.target)) close();
        }
        function onScroll(e) { if (!panel.contains(e.target)) close(); }
        // Bildschirmtastatur (iPad) löst resize aus — dann nicht schliessen.
        function onResize() { if (document.activeElement !== search) close(); }
        function open(startText)  {
            search.value = startText || '';
            renderPanel();
            if (panel.parentNode !== document.body) document.body.appendChild(panel);
            panel.style.display = 'block';
            panel.style.zIndex = '10050'; // über Posteingang-Modals (z-index 300)
            // FIXED unter/ueber dem Button positionieren — so wird das Panel
            // in scrollenden Modals/Containern nie abgeschnitten (16.07.2026).
            const r = btn.getBoundingClientRect();
            const w = Math.max(r.width, 180);
            panel.style.position = 'fixed';
            panel.style.minWidth = w + 'px';
            panel.style.left = Math.max(8, Math.min(r.left, window.innerWidth - w - 8)) + 'px';
            const below = window.innerHeight - r.bottom;
            const panelH = Math.min(panel.scrollHeight, 340);
            if (below < panelH + 12 && r.top > panelH + 12) {
                // Unten verankert, damit das Panel beim Filtern am Feld bleibt.
                panel.style.top = 'auto';
                panel.style.bottom = (window.innerHeight - r.top + 4) + 'px';
            } else {
                panel.style.top = (r.bottom + 4) + 'px';
                panel.style.bottom = 'auto';
            }
            document.addEventListener('mousedown', onDocDown, true);
            document.addEventListener('scroll', onScroll, true);
            window.addEventListener('resize', onResize);
            if (!search.value) {
                const cur = list.querySelector('.lqsel-opt.sel');
                if (cur && cur.scrollIntoView) cur.scrollIntoView({ block: 'nearest' });
            }
            if (search.style.display !== 'none') search.focus({ preventScroll: true });
        }
        function close() {
            panel.style.display = 'none';
            document.removeEventListener('mousedown', onDocDown, true);
            document.removeEventListener('scroll', onScroll, true);
            window.removeEventListener('resize', onResize);
        }

        btn.addEventListener('click', () => (panel.style.display === 'none' ? open() : close()));
        btn.addEventListener('keydown', (e) => {
            if (e.key === 'Escape') { close(); return; }
            // Tippen auf dem fokussierten Feld öffnet direkt mit Suche.
            if (e.key.length === 1 && e.key !== ' ' && !e.ctrlKey && !e.metaKey && !e.altKey
                && sel.options.length >= SUCH_AB && panel.style.display === 'none') {
                e.preventDefault();
                open(e.key);
            }
        });
        search.addEventListener('input', applyFilter);
        search.addEventListener('keydown', (e) => {
            const opts = sichtbareOpts();
            const i = opts.indexOf(list.querySelector('.lqsel-opt.active'));
            if (e.key === 'ArrowDown') { e.preventDefault(); setActive(opts[Math.min(i + 1, opts.length - 1)] || opts[0]); }
            else if (e.key === 'ArrowUp') { e.preventDefault(); setActive(opts[Math.max(i - 1, 0)]); }
            else if (e.key === 'Enter') { e.preventDefault(); choose(opts[i] || opts[0]); }
            else if (e.key === 'Escape') { e.preventDefault(); close(); btn.focus({ preventScroll: true }); }
            else if (e.key === 'Tab') close();
        });
        panel.addEventListener('mousedown', (e) => {
            const t = e.target.closest('.lqsel-opt');
            if (!t || t.classList.contains('dis')) return;
            e.preventDefault();
            choose(t);
        });

        // Optionstexte/-listen ändern sich (Zähler, Nachladen) → mitziehen.
        new MutationObserver(() => {
            renderBtn();
            if (panel.style.display !== 'none') renderPanel();
        }).observe(sel, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['data-badge'] });

        // Nach programmatischem value-Set (ohne change-Event) aufrufbar.
        sel._lqRefresh = renderBtn;
        sel.addEventListener('change', renderBtn);

        renderBtn();
    }

    function liquidifyScan(root) {
        // Walter-Vorgabe 16.07.2026: ALLE Selects, nicht mehr nur data-liquid-select.
        (root || document).querySelectorAll('select').forEach(liquidifySelect);
    }

    // Spaeter eingefuegte Selects (Modals, JS-Re-Render) automatisch umbauen.
    const bodyObserver = new MutationObserver(muts => {
        for (const m of muts) {
            m.addedNodes.forEach(n => {
                if (n.nodeType !== 1) return;
                if (n.tagName === 'SELECT') liquidifySelect(n);
                else if (n.querySelectorAll) n.querySelectorAll('select').forEach(liquidifySelect);
            });
        }
    });

    // Programmatisches .value-Setzen ohne change-Event (sel.value = '' beim
    // Zuruecksetzen) — Labels alle 400ms nachziehen.
    setInterval(() => {
        document.querySelectorAll('select').forEach(sel => {
            if (sel._lq && sel._lqRefresh) sel._lqRefresh();
        });
    }, 400);

    window.liquidifySelect = liquidifySelect;
    window.liquidifyScan = liquidifyScan;
    function lqInit() {
        liquidifyScan();
        bodyObserver.observe(document.body, { childList: true, subtree: true });
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', lqInit);
    } else {
        lqInit();
    }
})();
