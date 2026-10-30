#!/usr/bin/env python3
"""Erzeugt docs/swissdec-vorfuehrung/systemtest-handbuch.html aus dem Katalog.

Aufruf (Repo-Root):
  python3 Scripts/swissdec-systemtest-handbuch.py

Stil wie Foundation-Handbuch (itserv-Termin): Wo in OneCrew · So handhabt OneCrew ·
Du prüfst — punkt für punkt.
"""
from __future__ import annotations

import html
import json
from collections import defaultdict
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
KATALOG = ROOT / "docs" / "swissdec-systemtest-katalog.json"
OUT = ROOT / "docs" / "swissdec-vorfuehrung" / "systemtest-handbuch.html"

# seite-Code → Menüpfad (verständlich für Walter / itserv)
WO = {
    "hauptsitze": "System → Filialen & Benutzer → Hauptsitze",
    "filialen": "System → Filialen → Stammdaten der Filiale (Arbeitsort)",
    "lohndaten-empfaenger": "System → Verzeichnisse & Vorgaben → Lohndaten-Empfänger",
    "sv-saetze": "System → Lohn-Stammdaten → SV-Sätze",
    "lohnpositionen": "System → Lohnarten (Lohnpositionen)",
    "mitarbeiter": "Personal → Mitarbeiter (Detail je nach Prüfpunkt)",
    "vertraege": "Personal → Verträge",
    "swissdec": "Entwicklung → Swissdec (Admin-Bereich)",
    "elm-meldungen": "Lohn → ELM-Meldungen (Cockpit)",
    "swissdec-kommunikation": "Entwicklung → Swissdec → Kommunikations-Test (Foundation)",
    "swissdec-systemtest": "Entwicklung → Swissdec → Systemtest",
}

GRUPPE_WORUM = {
    "01": "Firmenstammdaten: Hauptsitz, Arbeitsorte (Filialen), Arbeitszeit, Stellvertreter — und welche Empfänger (z.B. FAK) überhaupt in die Meldung dürfen.",
    "02": "Datenempfänger: Ausgleichskasse, FAK, UVG/UVGZ, KTG, BVG, Statistik, Quellensteuer — Nummern und Zuordnung pro Filiale.",
    "03": "Personendaten: Stammdaten, Verträge, Versicherungen, Lohnausweis, Statistik, Quellensteuer, Grenzgänger.",
    "04": "Abläufe: Ein-/Austrittsmeldungen (EMA), Korrekturen, Historisierung, QST Ein-/Austritt.",
    "05": "Cockpit: Jahres- und Monatsmeldung vorbereiten und senden, SUA/Konfiguration.",
}

TECH_WORUM = {
    "01": "Tech-Gates zur Lohnmeldung (Senden, Faults, Adressierung).",
    "02": "Synchronisieren: Antworten abholen und quittieren.",
    "03": "Completion: Freigabe-Link beim Empfänger.",
    "04": "Lohnausweis / Barcode.",
    "05": "DialogMessage (Rückfragen).",
    "06": "Warnungen und Informationen.",
}


def esc(s: str | None) -> str:
    return html.escape(s or "", quote=True)


def kurz_id(full: str) -> str:
    """S_ELM_V6_01_01_05 → S01_01_05 · ST_ELM_V6_01_01 → T01_01"""
    if full.startswith("S_ELM_V6_"):
        return "S" + full[len("S_ELM_V6_") :]
    if full.startswith("ST_ELM_V6_"):
        return "T" + full[len("ST_ELM_V6_") :]
    return full


def badge(stand: str, gebaut: bool) -> str:
    s = (stand or "").lower()
    if s == "gebaut" or (gebaut and s not in ("offen",)):
        if s == "gebaut":
            return '<span class="ok">gebaut</span>'
        return '<span class="ok">vorhanden</span>'
    if s == "offen" or not gebaut:
        return '<span class="offen">offen</span>'
    return f'<span class="offen">{esc(stand)}</span>'


def wo_text(seite: str | None, seite_label: str | None, aktionen: list) -> str:
    pfad = WO.get(seite or "", "")
    label = seite_label or ""
    extra = []
    for a in aktionen or []:
        if a.get("art") == "seite" and a.get("seite"):
            p = WO.get(a["seite"], "")
            lab = a.get("seiteLabel") or a["seite"]
            if p and p != pfad:
                extra.append(f"{lab}: {p}")
            elif not pfad:
                extra.append(lab)
    teile = []
    if pfad:
        teile.append(pfad)
    elif label:
        teile.append(label)
    teile.extend(extra)
    if not teile:
        return "Noch nicht verknüpft — in der Systemtest-Seite nachschlagen."
    return " · ".join(teile)


def handhabung(c: dict) -> str:
    qt = (c.get("kommentarQt") or "").strip()
    notiz = (c.get("notiz") or "").strip()
    if qt and notiz and notiz not in qt:
        return f"{qt} ({notiz})"
    return qt or notiz or "Noch nicht dokumentiert — Maske öffnen und mit der Swissdec-Beschreibung abgleichen."


def pruefen(c: dict) -> str:
    """Kurzer Prüfschritt aus erwartet / beschreibung."""
    e = (c.get("erwartet") or "").strip()
    if not e:
        e = (c.get("beschreibung") or "").strip()
    # Erste 2–3 Sätze / bis ca. 320 Zeichen
    e = " ".join(e.split())
    if len(e) > 360:
        cut = e[:360]
        sp = cut.rfind(" ")
        e = (cut[:sp] if sp > 200 else cut) + "…"
    return e


def css() -> str:
    return """
  @page { size: A4; margin: 11mm 13mm 11mm 13mm; }
  * { box-sizing: border-box; }
  body { font-family: -apple-system, "Helvetica Neue", Arial, sans-serif; font-size: 9.7pt; line-height: 1.36; color: #2f2f2f; margin: 0; }
  h1 { font-size: 20pt; margin: 0 0 2mm; color: #1a1a1a; }
  h2 { font-size: 15pt; margin: 0; color: #1a1a1a; }
  h3 { font-size: 11pt; margin: 4mm 0 1.5mm; color: #3f3f3f; }
  p { margin: 1.4mm 0; }
  .kapitel { page-break-before: always; }
  .kopf { display: flex; justify-content: space-between; align-items: baseline; border-bottom: 2px solid #3f3f3f; padding-bottom: 1.5mm; margin-bottom: 3mm; }
  .kopf .rechts { font-size: 9pt; color: #646464; text-align: right; }
  .worum { background: #f3f2ee; border-radius: 6px; padding: 2mm 3.2mm; margin: 0 0 2.5mm; }
  .worum b.t { display: block; font-size: 9pt; color: #646464; text-transform: uppercase; letter-spacing: .04em; margin-bottom: .8mm; }
  .pp { border: 1px solid #d6d0c4; border-radius: 6px; padding: 1.8mm 3mm; margin: 2mm 0; break-inside: avoid; background: #fbfaf7; }
  .pp-kopf { display: flex; align-items: baseline; gap: 2.5mm; margin-bottom: 1.2mm; flex-wrap: wrap; }
  .pp-kopf .id { font-family: Menlo, monospace; font-weight: 700; font-size: 9.6pt; }
  .pp-kopf .titel { font-weight: 700; flex: 1; min-width: 40mm; }
  .box { display: inline-block; width: 3.8mm; height: 3.8mm; border: 1.3px solid #3f3f3f; border-radius: 1px; vertical-align: -0.7mm; margin-right: 1mm; }
  .ok { font-size: 8.4pt; font-weight: 700; padding: .3mm 2mm; border-radius: 3mm; white-space: nowrap; background: #e3eee3; color: #36513a; border: 1px solid #b8ccb8; }
  .offen { font-size: 8.4pt; font-weight: 700; padding: .3mm 2mm; border-radius: 3mm; white-space: nowrap; background: #fdf1dc; color: #7c5a10; border: 1px solid #f3d9a4; }
  .z { display: grid; grid-template-columns: 32mm 1fr; gap: 2mm; margin: .8mm 0; }
  .z > .lab { color: #646464; font-weight: 700; }
  .sag { background: #eef1ea; border-left: 3px solid #6f8f6a; padding: 1mm 2.5mm; border-radius: 0 4px 4px 0; font-style: italic; }
  code, .mono { font-family: Menlo, monospace; font-size: 8.6pt; background: #efece5; padding: 0 1mm; border-radius: 1mm; }
  .knopf { display: inline-block; font-weight: 700; border: 1px solid #3f3f3f; border-radius: 2.4mm; padding: 0 1.9mm; font-size: 9pt; white-space: nowrap; }
  .hinweis { border-left: 3px solid #d9a441; background: #fdf6e7; padding: 1.8mm 3mm; margin: 2.5mm 0; break-inside: avoid; }
  .info { border-left: 3px solid #6b7280; background: #f3f2ee; padding: 1.8mm 3mm; margin: 2.5mm 0; break-inside: avoid; }
  table { border-collapse: collapse; width: 100%; margin: 1.5mm 0; }
  th, td { border: 1px solid #d6d0c4; padding: .9mm 2mm; text-align: left; vertical-align: top; }
  th { background: #efece5; font-size: 9.2pt; }
  .klein { font-size: 8.8pt; color: #646464; }
  .check { padding-left: 0; }
  .check li { list-style: none; margin: 1.4mm 0; }
  .wb td:first-child { font-weight: 700; width: 42mm; }
  .dir { font-size: 8.2pt; color: #8b8b8b; }
"""


def punkt_html(c: dict, art: str) -> str:
    kid = kurz_id(c["id"])
    titel = c.get("titel") or ""
    stand = c.get("stand") or ""
    gebaut = bool(c.get("gebaut"))
    wo = wo_text(c.get("seite"), c.get("seiteLabel"), c.get("aktionen") or [])
    hand = handhabung(c)
    chk = pruefen(c)
    dir_ = c.get("direktive") or ""
    dir_html = f' <span class="dir">Direktive {esc(dir_)}</span>' if dir_ else ""

    return f"""
  <div class="pp"><div class="pp-kopf"><span class="box"></span><span class="id">{esc(kid)}</span><span class="titel">{esc(titel)}</span>{badge(stand, gebaut)}{dir_html}</div>
    <div class="z"><span class="lab">Wo in OneCrew</span><span>{esc(wo)}</span></div>
    <div class="z"><span class="lab">So handhabt OneCrew</span><span>{esc(hand)}</span></div>
    <div class="z"><span class="lab">Du prüfst</span><span>{esc(chk)}</span></div>
    <div class="z"><span class="lab">Häkchen</span><span>Stimmt → <span class="box"></span> · Systemtest-Seite: Bewertung setzen. Voll-ID: <span class="mono">{esc(c["id"])}</span></span></div>
  </div>"""


def main() -> None:
    data = json.loads(KATALOG.read_text(encoding="utf-8"))
    erp = data["erp"]
    tech = data.get("tech") or []
    heute = date.today().strftime("%d.%m.%Y")

    gebaut_erp = sum(1 for x in erp if x.get("stand") == "gebaut" or (x.get("gebaut") and x.get("stand") != "offen"))
    # genauer: stand gebaut
    n_gebaut = sum(1 for x in erp if (x.get("stand") or "").lower() == "gebaut")
    n_vorhanden = sum(1 for x in erp if (x.get("stand") or "").lower() == "vorhanden")
    n_offen = sum(1 for x in erp if (x.get("stand") or "").lower() == "offen")

    # Gruppierung ERP
    by_g: dict[str, list] = defaultdict(list)
    for c in erp:
        by_g[c.get("gruppe") or "?"].append(c)

    parts: list[str] = []
    parts.append(f"""<!DOCTYPE html>
<html lang="de">
<head>
<meta charset="utf-8">
<title>Swissdec Systemtest — Handbuch (ERP + Tech)</title>
<style>{css()}
</style>
</head>
<body>

<!-- DECKBLATT -->
<section>
  <div class="kopf"><h1>Swissdec Systemtest<br>Handbuch für die Prüfung (ERP + Tech)</h1>
    <div class="rechts">OneCrew · Muster AG · ELMv6.0.1<br>Testinstanz test.onecrew.ch<br>Stand {heute}</div></div>

  <div class="worum"><b class="t">Worum es bei diesem Test geht</b>
    Anders als der <b>Foundation-Test</b> (Verbindung, Siegel, Zertifikate) prüft der <b>Systemtest</b>,
    ob OneCrew Firmendaten, Empfänger und Personen <b>richtig abbildet</b> und in der Lohnmeldung
    korrekt meldet. Die Expertin (itserv / Swissdec) geht die Liste im Quality Tool durch —
    du zeigst jeweils die Maske in OneCrew und ggf. das XML.
    <b>Foundation-Handbuch:</b> <span class="mono">docs/swissdec-vorfuehrung/vorfuehrung.html</span></div>

  <table>
    <tr><th style="width:40mm">ERP (Run 24364)</th><td><b>{len(erp)}</b> Prüfpunkte · gebaut <b>{n_gebaut}</b> · vorhanden <b>{n_vorhanden}</b> · offen <b>{n_offen}</b></td></tr>
    <tr><th>Tech (Run 26667)</th><td><b>{len(tech)}</b> Fälle (Gates im QT teils höher) — oft Überlappung mit Foundation / ELM-Cockpit</td></tr>
    <tr><th>Digital begleiten</th><td>Entwicklung → Swissdec → <b>Systemtest</b> — gleiche Punkte, Sprungknopf in die Maske, Bewertung speichern</td></tr>
    <tr><th>Katalog</th><td><span class="mono">docs/swissdec-systemtest-katalog.json</span> · dieses HTML neu erzeugen: <span class="mono">python3 Scripts/swissdec-systemtest-handbuch.py</span></td></tr>
  </table>

  <h3>Drei Grundregeln</h3>
  <ol>
    <li><b>Immer test.onecrew.ch</b> (Muster AG) — nie Produktivdaten.</li>
    <li><b>Punkt für Punkt:</b> «Wo» öffnen → «So handhabt OneCrew» zeigen → mit «Du prüfst» abhaken.</li>
    <li><b>Grün nur wenn es stimmt.</b> Fehlt die Handhabung noch («offen»), ehrlich sagen und notieren — nichts schönreden.</li>
  </ol>

  <h3>So ist jeder Prüfpunkt aufgebaut</h3>
  <table>
    <tr><th style="width:40mm">Wo in OneCrew</th><td>Menüpfad zur Maske (gleicher Sprung wie in der Systemtest-Seite).</td></tr>
    <tr><th>So handhabt OneCrew</th><td>Wie wir es gebaut haben — Felder, Regeln, XML-Verhalten.</td></tr>
    <tr><th>Du prüfst</th><td>Was Swissdec erwartet (Kurzfassung aus dem Katalog).</td></tr>
    <tr><th>Häkchen</th><td>Stimmt → Checkbox; Bewertung auch in der Systemtest-Seite setzen.</td></tr>
  </table>
</section>

<!-- VORBEREITUNG -->
<section class="kapitel">
  <div class="kopf"><h2>Vorbereitung und Ablauf</h2><div class="rechts">vor der Prüfung · ca. 10 Minuten</div></div>

  <h3>Vorher erledigen</h3>
  <ul class="check">
    <li><span class="box"></span><b>test.onecrew.ch</b> · Cmd+Shift+R · Zoom 100&nbsp;% (Cmd+0).</li>
    <li><span class="box"></span>Entwicklung → Swissdec → <b>Systemtest</b> öffnen (digitale Checkliste parallel zu diesem Handbuch).</li>
    <li><span class="box"></span>Quality Tool offen (Systemtest ERP / Tech).</li>
    <li><span class="box"></span>Bei XML-Punkten: Lohn → <b>ELM-Meldungen</b> bzw. Swissdec → Jahresmeldung AHV bereit.</li>
    <li><span class="box"></span>Dieses Handbuch ausgedruckt oder auf zweitem Bildschirm.</li>
  </ul>

  <h3>Empfohlene Reihenfolge</h3>
  <ol>
    <li><b>S01</b> Unternehmen (Hauptsitz, Arbeitsorte, FAK-Nullmeldung, Arbeitszeit) — oft zuerst.</li>
    <li><b>S02</b> Empfänger (AHV, FAK, Versicherungen, QST).</li>
    <li><b>S03</b> Personendaten (grosser Block).</li>
    <li><b>S04 / S05</b> Prozesse und Cockpit.</li>
    <li><b>Tech</b> nur was nicht schon im Foundation-Test durch ist.</li>
  </ol>

  <div class="hinweis"><b>FAK-Nullmeldung (S01_01_05):</b> Filiale → Tab «Lohndaten Empfänger», Art FAK, von/bis setzen.
    Dann Swissdec → Jahresmeldung AHV → XML: nur gültig zugeordnete FAKs unter <span class="mono">Addressees</span> /
    <span class="mono">FAK-CAF</span> — auch ohne Personen an der Filiale. Abgelaufene fehlen.</div>

  <div class="info"><b>Wenn die Expertin das XML sehen will:</b> Jahres- oder Monatsmeldung erzeugen → herunterladen /
    anzeigen → nach <span class="mono">#FAK-</span>, Kassennummer oder Workplace suchen. Belege auch unter ELM-Meldungen → Protokoll.</div>
</section>

<!-- WÖRTERBUCH -->
<section class="kapitel">
  <div class="kopf"><h2>Kleines Wörterbuch (Systemtest)</h2><div class="rechts">zusätzlich zum Foundation-Wörterbuch</div></div>
  <table class="wb">
    <tr><th>Wort</th><th>Bedeutet einfach</th></tr>
    <tr><td>FAK</td><td>Familienausgleichskasse — rechnet die <b>Familienzulagen</b> ab.</td></tr>
    <tr><td>Nullmeldung FAK</td><td>Kasse steht in der Meldung, auch wenn an der Filiale <b>niemand</b> Lohn/Zulagen hat — solange die Filiale dort angemeldet ist (von/bis).</td></tr>
    <tr><td>Workplace / Arbeitsort</td><td>Bei uns eine <b>Filiale</b>.</td></tr>
    <tr><td>Hauptsitz</td><td>Rechtseinheit (UID, Sitzadresse) — Kopf der Meldung.</td></tr>
    <tr><td>Lohndaten-Empfänger</td><td>Katalog der Kassen/Versicherer/Steuerämter; Filiale bekommt nur die Zuordnung + Mitgliednummer + von/bis.</td></tr>
    <tr><td>von / bis</td><td>Gültigkeit der Zuordnung Empfänger ↔ Filiale. Abgelaufen = nicht mehr in der Meldung.</td></tr>
    <tr><td>Job / Addressees</td><td>Adressliste im XML: wer die Meldung bekommen soll.</td></tr>
    <tr><td>ProcessByDistributor</td><td>true = Poststelle soll an diesen Empfänger verteilen; false = nicht.</td></tr>
    <tr><td>DeclareAnnualSalary</td><td>Jahresmeldung (u.a. AHV + FAK-Adressierung).</td></tr>
    <tr><td>DeclareMonthlySalary</td><td>Monatsmeldung (u.a. QST, Statistik).</td></tr>
    <tr><td>EMA</td><td>Ein-/Austrittsmeldung an Empfänger (z.B. FAK).</td></tr>
    <tr><td>vorhanden / gebaut / offen</td><td>Status bei uns: Maske da · fachlich verdrahtet · noch zu bauen.</td></tr>
  </table>
</section>
""")

    # ERP chapters
    for g in sorted(by_g.keys()):
        cases = by_g[g]
        name = data.get("erpGruppen", {}).get(g) or cases[0].get("gruppeName") or g
        worum = GRUPPE_WORUM.get(g, "")
        n_g = sum(1 for x in cases if (x.get("stand") or "").lower() == "gebaut")
        parts.append(f"""
<section class="kapitel">
  <div class="kopf"><h2>S{g} — {esc(name)}</h2>
    <div class="rechts">{len(cases)} Prüfpunkte · {n_g} gebaut<br>Reiter «S{g}» in Systemtest</div></div>
  <div class="worum"><b class="t">Worum es geht</b> {esc(worum)}</div>
""")
        # Untergruppen
        by_u: dict[str, list] = defaultdict(list)
        for c in cases:
            by_u[c.get("untergruppe") or "?"].append(c)
        for u in sorted(by_u.keys()):
            uname = data.get("erpUntergruppen", {}).get(u) or by_u[u][0].get("untergruppeName") or u
            parts.append(f'  <h3>{esc(u)} · {esc(uname)}</h3>')
            for c in by_u[u]:
                parts.append(punkt_html(c, "erp"))
        parts.append("</section>")

    # Tech
    by_tg: dict[str, list] = defaultdict(list)
    for c in tech:
        by_tg[c.get("gruppe") or "?"].append(c)

    parts.append(f"""
<section class="kapitel">
  <div class="kopf"><h2>Tech-Systemtest</h2>
    <div class="rechts">{len(tech)} Fälle · Reiter T01–T06<br>Überlappung Foundation / ELM-Cockpit</div></div>
  <div class="worum"><b class="t">Worum es geht</b>
    Technische Gates (Senden, Sync, Completion, Dialog). Vieles ist schon im Foundation-Test oder im ELM-Cockpit gezeigt —
    hier nur die Punkte, die itserv noch separat abhaken will.</div>
""")
    for g in sorted(by_tg.keys()):
        cases = by_tg[g]
        name = (data.get("techGruppen") or {}).get(g) or cases[0].get("gruppeName") or g
        worum = TECH_WORUM.get(g, "")
        parts.append(f'  <h3>T{esc(g)} · {esc(name)}</h3>')
        if worum:
            parts.append(f'  <p class="klein">{esc(worum)}</p>')
        for c in cases:
            parts.append(punkt_html(c, "tech"))
    parts.append("</section>")

    parts.append(f"""
<section class="kapitel">
  <div class="kopf"><h2>Notizen vom Termin</h2><div class="rechts">{heute}</div></div>
  <p class="klein">Antworten der Expertin, offene Punkte, nächste Schritte.</p>
  <div style="border-bottom:1px solid #bbb;height:8mm;margin:2mm 0"></div>
  <div style="border-bottom:1px solid #bbb;height:8mm;margin:2mm 0"></div>
  <div style="border-bottom:1px solid #bbb;height:8mm;margin:2mm 0"></div>
  <div style="border-bottom:1px solid #bbb;height:8mm;margin:2mm 0"></div>
  <div style="border-bottom:1px solid #bbb;height:8mm;margin:2mm 0"></div>
  <div style="border-bottom:1px solid #bbb;height:8mm;margin:2mm 0"></div>
  <p class="klein">Quellen: Katalog <span class="mono">docs/swissdec-systemtest-katalog.json</span> ·
    Protokoll <span class="mono">docs/swissdec-systemtest-protokoll.md</span> ·
    Foundation <span class="mono">docs/swissdec-vorfuehrung/vorfuehrung.html</span></p>
</section>

</body>
</html>
""")

    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text("".join(parts), encoding="utf-8")
    print(f"Geschrieben: {OUT} ({OUT.stat().st_size // 1024} KB, {len(erp)} ERP + {len(tech)} Tech)")


if __name__ == "__main__":
    main()
