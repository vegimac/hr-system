// ───────────────────────────────────────────────────────────────────────
// Walter-Vorgabe 31.05.2026: wiederverwendbares ⋯-Dropdown-Menü-Pattern
// für Sekundär-Aktionen in Action-Bars (Lohnlauf, Mitarbeiter, Verträge etc).
//
// Verwendung in HTML:
//   <div class="action-menu" data-action-menu>
//       <button class="action-menu-trigger" onclick="actionMenu.toggle(this)">⋯ Mehr</button>
//       <div class="action-menu-list">
//           <button class="action-menu-item" onclick="..."><span>📋 Bericht</span></button>
//           <div class="action-menu-divider"></div>
//           <button class="action-menu-item danger" onclick="..."><span>🗑 Reset</span></button>
//       </div>
//   </div>
//
// Verhalten:
//   • Klick auf den Trigger öffnet/schließt das Menü
//   • Klick außerhalb des Menüs schließt es
//   • ESC schließt das aktuell offene Menü
//   • Klick auf einen Item schließt automatisch
// ───────────────────────────────────────────────────────────────────────
window.actionMenu = (function() {
    let _open = null;

    function _close() {
        if (_open) {
            _open.classList.remove('open');
            _open = null;
        }
    }

    function toggle(triggerEl) {
        const menu = triggerEl.closest('.action-menu');
        if (!menu) return;
        if (menu === _open) {
            _close();
        } else {
            _close();
            menu.classList.add('open');
            _open = menu;
        }
    }

    function close() { _close(); }

    // Klick außerhalb schließt
    document.addEventListener('click', (ev) => {
        if (!_open) return;
        if (!_open.contains(ev.target)) _close();
    });

    // ESC schließt
    document.addEventListener('keydown', (ev) => {
        if (ev.key === 'Escape') _close();
    });

    // Klick auf einen Menü-Item schließt automatisch (delegated)
    document.addEventListener('click', (ev) => {
        const item = ev.target.closest('.action-menu-item');
        if (item) setTimeout(_close, 0);
    });

    return { toggle, close };
})();
