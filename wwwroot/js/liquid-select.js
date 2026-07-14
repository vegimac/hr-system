// ══════════════════════════════════════════════════════════════════════
// liquid-select.js — generisches Liquid-Glass-Dropdown (Walter 13.07.2026)
//
// Ersetzt native <select>-Menüs durch ein Liquid-Control im App-Look.
// Das Original-Select bleibt VERSTECKT im DOM und trägt weiterhin den
// Wert — alle bestehenden IDs, onchange-Handler und Lese-Pfade
// funktionieren unverändert (Liquid-Glass-Konvention, CLAUDE.md).
//
// Verwendung:
//   • <select data-liquid-select …> → wird beim Laden automatisch umgebaut
//   • window.liquidifySelect(el)    → gezielt per JS
//   • el._lqRefresh()               → Button-Text nach programmatischem
//                                     value-Set auffrischen
// Options-/Text-Änderungen (z.B. Zähler) werden per MutationObserver
// automatisch übernommen.
// ══════════════════════════════════════════════════════════════════════
(function () {
    'use strict';

    function lqEsc(s) {
        return String(s ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/"/g, '&quot;');
    }

    function liquidifySelect(sel) {
        if (!sel || sel._lq || sel.tagName !== 'SELECT') return;
        sel._lq = true;

        const wrap = document.createElement('div');
        wrap.className = 'lqsel-wrap';
        sel.parentNode.insertBefore(wrap, sel);
        wrap.appendChild(sel);
        sel.style.display = 'none';

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'lqsel-btn';
        const panel = document.createElement('div');
        panel.className = 'lqsel-panel';
        panel.style.display = 'none';
        wrap.appendChild(btn);
        wrap.appendChild(panel);

        const curLabel = () => {
            const o = sel.selectedIndex >= 0 ? sel.options[sel.selectedIndex] : null;
            return o ? o.textContent.trim() : '';
        };
        function renderBtn() {
            btn.innerHTML = `<span class="lqsel-label">${lqEsc(curLabel() || '– wählen –')}</span><span class="lqsel-chev">▾</span>`;
        }
        const optHtml = (o) =>
            `<div class="lqsel-opt${o.value === sel.value ? ' sel' : ''}${o.disabled ? ' dis' : ''}" data-v="${lqEsc(o.value)}">${lqEsc(o.textContent.trim())}</div>`;
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
            panel.innerHTML = html || '<div class="lqsel-group">— leer —</div>';
        }

        function onDocDown(e) { if (!wrap.contains(e.target)) close(); }
        function open()  {
            renderPanel();
            panel.style.display = 'block';
            document.addEventListener('mousedown', onDocDown, true);
            const cur = panel.querySelector('.lqsel-opt.sel');
            if (cur && cur.scrollIntoView) cur.scrollIntoView({ block: 'nearest' });
        }
        function close() {
            panel.style.display = 'none';
            document.removeEventListener('mousedown', onDocDown, true);
        }

        btn.addEventListener('click', () => (panel.style.display === 'none' ? open() : close()));
        btn.addEventListener('keydown', (e) => { if (e.key === 'Escape') close(); });
        panel.addEventListener('mousedown', (e) => {
            const t = e.target.closest('.lqsel-opt');
            if (!t || t.classList.contains('dis')) return;
            e.preventDefault();
            sel.value = t.getAttribute('data-v');
            sel.dispatchEvent(new Event('change'));
            renderBtn();
            close();
        });

        // Optionstexte/-listen ändern sich (Zähler, Nachladen) → mitziehen.
        new MutationObserver(() => {
            renderBtn();
            if (panel.style.display !== 'none') renderPanel();
        }).observe(sel, { childList: true, subtree: true, characterData: true });

        // Nach programmatischem value-Set (ohne change-Event) aufrufbar.
        sel._lqRefresh = renderBtn;
        sel.addEventListener('change', renderBtn);

        renderBtn();
    }

    function liquidifyScan(root) {
        (root || document).querySelectorAll('select[data-liquid-select]').forEach(liquidifySelect);
    }

    window.liquidifySelect = liquidifySelect;
    window.liquidifyScan = liquidifyScan;
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => liquidifyScan());
    } else {
        liquidifyScan();
    }
})();
