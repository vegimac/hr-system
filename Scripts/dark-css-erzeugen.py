# Aufruf: python3 Scripts/dark-css-erzeugen.py  (im Projektordner) — schreibt wwwroot/css/dark.css neu.
# Erzeugt wwwroot/css/dark.css — das Sicherheitsnetz für den Dunkel-Modus.
# Fängt fest eingetragene helle Flächen und dunkle Schriften in style="…" ab (HTML und JS-Vorlagen).
ausnahme = ':not(img):not(canvas):not(video):not(iframe):not(.hell-bleiben):not(.hell-bleiben *)'
KURZ = {'#fff', '#000', '#111', '#222', '#333', '#555', '#777', '#888', '#999', '#ddd', '#eee'}
def muster(w, p):
    """Teilstring-Muster für style="…": kurze Hex-Werte nur mit Abschluss (; Leerzeichen ! oder Ende),
    sonst würde #fff auch #fffbeb treffen."""
    m = []
    for sp in ('', ' '):
        if w in KURZ:
            for ende in (';', ' ', '!'):
                m.append(f'[style*="{p}:{sp}{w}{ende}" i]')
            m.append(f'[style$="{p}:{sp}{w}" i]')
        else:
            m.append(f'[style*="{p}:{sp}{w}" i]')
    return m
def sel(werte, props):
    return ',\n'.join(f'body.theme-dark {x}{ausnahme}' for w in werte for p in props for x in muster(w, p))

hell_flaeche = ['#fff', '#ffffff', 'white', '#fdfdfd', '#fafafa', '#f9fafb', '#f8fafc', '#f8f9fa', '#f7f7f7', '#f5f5f5', '#f4f4f5', '#f3f4f6', '#f1f5f9', '#faf8f5', '#f6f3ee', '#eef2f7']
hell_flaeche2 = ['#e5e7eb', '#e2e8f0', '#eeece4', '#e7e4db', '#ece9e2', '#f0f0f0', '#eee', '#eeeeee']
toenung = {   # helle Status-Tönungen → dunkle Tönung, Schrift hell
    'gelb':  (['#fffbeb', '#fef3c7', '#fef9c3', '#fefce8', '#fff7ed', '#ffedd5'], 'rgba(245, 158, 11, 0.16)'),
    'rot':   (['#fef2f2', '#fee2e2', '#fff1f2', '#ffe4e6'], 'rgba(239, 68, 68, 0.16)'),
    'gruen': (['#ecfdf5', '#f0fdf4', '#dcfce7', '#d1fae5'], 'rgba(34, 197, 94, 0.16)'),
    'blau':  (['#eff6ff', '#dbeafe', '#e0f2fe', '#f0f9ff', '#eef2ff', '#e0e7ff'], 'rgba(59, 130, 246, 0.18)'),
}
text_stark = ['#000', 'black', '#111', '#111827', '#0f172a', '#1e293b', '#1f2937', '#1a1a1a', '#222', '#333', '#3f3f3f', '#374151', '#334155', '#2d3748', '#27272a']
text_mittel = ['#475569', '#4b5563', '#555', '#646464', '#6b7280', '#52525b', '#57534e']
text_leise = ['#64748b', '#8b8b8b', '#94a3b8', '#9ca3af', '#999', '#888', '#777']
text_farbig = {'#86efac': ['#166534', '#15803d', '#14532d', '#065f46'], '#fca5a5': ['#991b1b', '#b91c1c', '#7f1d1d'],
               '#fcd34d': ['#92400e', '#b45309', '#78350f', '#854d0e', '#a16207'], '#93c5fd': ['#1d4ed8', '#1e40af', '#1e3a8a']}
rand_hell = ['#e2e8f0', '#e5e7eb', '#cbd5e1', '#d1d5db', '#ddd', '#eee', '#e0e0e0', '#dee2e6']

bg = ('background', 'background-color')
out = ["""/* ══ Dunkel-Modus: Sicherheitsnetz (Walter 02.11.2026) ══════════════════════════════════
   Erzeugt — nicht von Hand ergänzen, sondern neue Oberflächen gleich mit den Farben aus
   css/farben.css (--oc-…) bauen. Diese Datei wird ZULETZT geladen und wirkt nur mit
   body.theme-dark: Sie fängt in älterem Code fest eingetragene helle Flächen und dunkle
   Schriften in style="…" ab (rund 1'500 Stellen in HTML und JS, Bestand 02.11.2026) und gibt
   Feldern, Tabellen und Linien ohne eigene Dunkel-Regel die Farben der Mitarbeiter-Maske.
   Soll etwas absichtlich hell bleiben (z.B. eine Unterschrift auf Weiss): Klasse «hell-bleiben». */
"""]
out.append('/* Helle Flächen → dunkle Glasfläche */\n' + sel(hell_flaeche, bg) +
           ' {\n    background-color: var(--dlg-fill) !important;\n    background-image: none !important;\n    color: var(--oc-text);\n}')
out.append('/* Hellgraue Flächen → zweite Ebene */\n' + sel(hell_flaeche2, bg) +
           ' {\n    background-color: var(--dlg-fill-strong) !important;\n    background-image: none !important;\n}')
for name, (werte, farbe) in toenung.items():
    out.append(f'/* Helle Tönung {name} → dunkle Tönung */\n' + sel(werte, bg) +
               f' {{\n    background-color: {farbe} !important;\n    background-image: none !important;\n}}')
out.append('/* Dunkle Schrift → hell */\n' + sel(text_stark, ('color',)) + ' {\n    color: var(--oc-text) !important;\n}')
out.append('/* Mittelgraue Schrift → hellgrau */\n' + sel(text_mittel, ('color',)) + ' {\n    color: var(--oc-text-2) !important;\n}')
out.append('/* Leise Schrift → gedämpft hell */\n' + sel(text_leise, ('color',)) + ' {\n    color: var(--oc-text-3) !important;\n}')
for hell, dunkel in text_farbig.items():
    out.append(f'/* Dunkle Statusschrift → helle Variante */\n' + sel(dunkel, ('color',)) + f' {{\n    color: {hell} !important;\n}}')
rs = ',\n'.join(f'body.theme-dark {x}{ausnahme}' for w in rand_hell
               for x in ([f'[style*="{w};" i]', f'[style*="{w} " i]', f'[style$="{w}" i]'] if w in KURZ else [f'[style*="{w}" i]']))
out.append('/* Helle Linien und Ränder → dezente Linie */\n' + rs + ' {\n    border-color: var(--oc-rand) !important;\n}')
out.append("""/* Grundelemente ohne eigene Dunkel-Regel. :where() = Gewicht 0: jede bestehende Regel gewinnt,
   hier wird nur der helle Browser-Standard ersetzt. */
body.theme-dark :where(input:not([type="checkbox"]):not([type="radio"]):not([type="range"]):not([type="color"]), textarea, select) {
    background-color: var(--oc-feld);
    color: var(--oc-text);
    border-color: var(--oc-feld-rand);
}
body.theme-dark :where(input, textarea)::placeholder { color: var(--oc-text-3); }
body.theme-dark :where(table) :where(th) { color: var(--oc-text-2); border-color: var(--oc-rand); }
body.theme-dark :where(table) :where(td) { border-color: var(--oc-rand-schwach); }
body.theme-dark :where(hr) { border-color: var(--oc-rand); }
body.theme-dark :where(a:not([class])) { color: #93c5fd; }
body.theme-dark :where(code, pre, kbd) { background-color: rgba(210, 235, 255, 0.06); color: var(--oc-text); }
""")
open('wwwroot/css/dark.css', 'w', encoding='utf-8').write('\n\n'.join(out) + '\n')
print('dark.css geschrieben')
