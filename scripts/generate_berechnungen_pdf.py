#!/usr/bin/env python3
"""Erzeugt docs/Berechnungen-Uebersicht.pdf — alle Lohn-/Saldo-Formeln des HR-Systems."""

from pathlib import Path
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.units import mm
from reportlab.lib.colors import HexColor
from reportlab.platypus import (
    SimpleDocTemplate, Paragraph, Spacer, Table, TableStyle,
    PageBreak, KeepTogether, HRFlowable, Preformatted, ListFlowable, ListItem,
)
from reportlab.lib.enums import TA_LEFT, TA_CENTER

OUT = Path(__file__).resolve().parents[1] / "docs" / "Berechnungen-Uebersicht.pdf"
OUT.parent.mkdir(parents=True, exist_ok=True)

C_INK = HexColor("#1f2937")
C_MUTED = HexColor("#4b5563")
C_LINE = HexColor("#d1d5db")
C_HEAD = HexColor("#111827")
C_BOX = HexColor("#f3f4f6")
C_ACCENT = HexColor("#374151")
C_BLUE = HexColor("#1e3a5f")
C_ROW = HexColor("#f9fafb")


def styles():
    base = getSampleStyleSheet()
    return {
        "cover_title": ParagraphStyle(
            "cover_title", parent=base["Title"], fontName="Helvetica-Bold",
            fontSize=22, leading=28, textColor=C_HEAD, alignment=TA_CENTER, spaceAfter=8,
        ),
        "cover_sub": ParagraphStyle(
            "cover_sub", parent=base["Normal"], fontName="Helvetica",
            fontSize=11, leading=15, textColor=C_MUTED, alignment=TA_CENTER, spaceAfter=4,
        ),
        "h1": ParagraphStyle(
            "h1", parent=base["Heading1"], fontName="Helvetica-Bold",
            fontSize=14, leading=18, textColor=C_HEAD, spaceBefore=14, spaceAfter=7,
        ),
        "h2": ParagraphStyle(
            "h2", parent=base["Heading2"], fontName="Helvetica-Bold",
            fontSize=11.5, leading=15, textColor=C_BLUE, spaceBefore=10, spaceAfter=4,
        ),
        "h3": ParagraphStyle(
            "h3", parent=base["Heading3"], fontName="Helvetica-Bold",
            fontSize=10, leading=13, textColor=C_ACCENT, spaceBefore=7, spaceAfter=3,
        ),
        "body": ParagraphStyle(
            "body", parent=base["Normal"], fontName="Helvetica",
            fontSize=9, leading=12.5, textColor=C_INK, spaceAfter=3,
        ),
        "small": ParagraphStyle(
            "small", parent=base["Normal"], fontName="Helvetica",
            fontSize=8, leading=11, textColor=C_MUTED, spaceAfter=2,
        ),
        "formula": ParagraphStyle(
            "formula", parent=base["Code"], fontName="Courier",
            fontSize=7.5, leading=10.5, textColor=C_INK, backColor=C_BOX,
            leftIndent=2, rightIndent=2, spaceBefore=2, spaceAfter=5,
        ),
        "bullet": ParagraphStyle(
            "bullet", parent=base["Normal"], fontName="Helvetica",
            fontSize=9, leading=12, textColor=C_INK, leftIndent=10, spaceAfter=1,
        ),
        "toc": ParagraphStyle(
            "toc", parent=base["Normal"], fontName="Helvetica",
            fontSize=9.5, leading=14, textColor=C_INK, leftIndent=6,
        ),
        "cell": ParagraphStyle(
            "cell", parent=base["Normal"], fontName="Helvetica",
            fontSize=7.5, leading=9.5, textColor=C_INK,
        ),
        "cellb": ParagraphStyle(
            "cellb", parent=base["Normal"], fontName="Helvetica-Bold",
            fontSize=7.5, leading=9.5, textColor=C_INK,
        ),
    }


def footer(canvas, doc):
    canvas.saveState()
    canvas.setStrokeColor(C_LINE)
    canvas.setLineWidth(0.4)
    y = 12 * mm
    canvas.line(16 * mm, y + 5, A4[0] - 16 * mm, y + 5)
    canvas.setFont("Helvetica", 7.5)
    canvas.setFillColor(C_MUTED)
    canvas.drawString(16 * mm, y, "HR-System Schaub Restaurants GmbH — Berechnungsübersicht")
    canvas.drawRightString(A4[0] - 16 * mm, y, f"Seite {doc.page}")
    canvas.restoreState()


def formula_box(text, S):
    lines = []
    for raw in text.strip("\n").split("\n"):
        while len(raw) > 98:
            cut = raw.rfind(" ", 0, 98)
            if cut < 40:
                cut = 98
            lines.append(raw[:cut])
            raw = raw[cut:].lstrip()
        lines.append(raw)
    return Preformatted("\n".join(lines), S["formula"])


def hr():
    return HRFlowable(width="100%", thickness=0.6, color=C_LINE, spaceBefore=4, spaceAfter=6)


def bullets(items, S):
    return [Paragraph(f"• {t}", S["bullet"]) for t in items]


def simple_table(headers, rows, S, col_widths=None):
    data = [[Paragraph(h, S["cellb"]) for h in headers]]
    for row in rows:
        data.append([Paragraph(str(c), S["cell"]) for c in row])
    t = Table(data, colWidths=col_widths, hAlign="LEFT", repeatRows=1)
    style = [
        ("BACKGROUND", (0, 0), (-1, 0), C_BOX),
        ("GRID", (0, 0), (-1, -1), 0.3, C_LINE),
        ("VALIGN", (0, 0), (-1, -1), "TOP"),
        ("LEFTPADDING", (0, 0), (-1, -1), 3),
        ("RIGHTPADDING", (0, 0), (-1, -1), 3),
        ("TOPPADDING", (0, 0), (-1, -1), 2),
        ("BOTTOMPADDING", (0, 0), (-1, -1), 2),
    ]
    for i in range(1, len(data)):
        if i % 2 == 0:
            style.append(("BACKGROUND", (0, i), (-1, i), C_ROW))
    t.setStyle(TableStyle(style))
    return t


def build():
    S = styles()
    doc = SimpleDocTemplate(
        str(OUT), pagesize=A4,
        leftMargin=16 * mm, rightMargin=16 * mm,
        topMargin=14 * mm, bottomMargin=18 * mm,
        title="Berechnungsübersicht — HR-System Schaub Restaurants",
        author="HR-System Dokumentation",
    )
    story = []

    # ── Cover ──────────────────────────────────────────────────────────
    story.append(Spacer(1, 28 * mm))
    story.append(Paragraph("Berechnungsübersicht", S["cover_title"]))
    story.append(Paragraph("HR- / Lohnsystem Schaub Restaurants GmbH", S["cover_sub"]))
    story.append(Paragraph("McDonald’s-Franchise · Schweizer L-GAV Gastronomie", S["cover_sub"]))
    story.append(Spacer(1, 8 * mm))
    story.append(hr())
    story.append(Paragraph(
        "Vollständige Zusammenfassung aller Lohn-, Saldo-, SV-, QST-, Akonto- und "
        "Fibu-Berechnungen im Programm. Quelle der Wahrheit: Code in "
        "<b>PayrollCalculationEngine</b>, <b>PayrollCalculations</b> und verwandten Services.",
        S["body"],
    ))
    story.append(Paragraph("Stand: August 2026 · intern · nicht zur Behördenabgabe", S["small"]))
    story.append(Spacer(1, 10 * mm))
    story.append(Paragraph("<b>Inhalt</b>", S["h2"]))
    toc = [
        "1. Architektur der Lohnrechnung",
        "2. Vertragsmodelle & Auszahlungsmatrix",
        "3. Grundlagen: Periode, Rundung, Kurzperiode",
        "4. Stempelzeiten & Nacht",
        "5. Absenzen-Stunden",
        "6. Ferien-Tage, Feiertag-Tage, Art. 329b OR",
        "7. Modell FLEX",
        "8. Ferien-Geld-Pott (FLEX / MTP)",
        "9. Modell MTP",
        "10. Modell FIX / FIX-M",
        "11. 13. Monatslohn",
        "12. KTG / UVG-Tagessatz & Karenz",
        "13. Sozialversicherungen (BuildResult)",
        "14. Quellensteuer (QST)",
        "15. Netto, Abtretung, Bank-Split, Akonto-Verrechnung",
        "16. Akonto-Lauf (6 Regeln)",
        "17. Mindestlohn & Lohnsumme",
        "18. L-GAV-Vollzugsbeitrag",
        "19. Familienzulagen",
        "20. Fibu-Rückstellungen",
        "21. Probezeit, Austritt, Fristen",
        "22. Monatsblatt Stundenkontrolle",
        "23. Querschnitt-Merksätze & Code-Orte",
    ]
    for t in toc:
        story.append(Paragraph(t, S["toc"]))
    story.append(PageBreak())

    # ── 1 Architektur ──────────────────────────────────────────────────
    story.append(Paragraph("1. Architektur der Lohnrechnung", S["h1"]))
    story.append(hr())
    story.append(Paragraph(
        "Die frühere Monolith-Datei wurde entflochten. Bei Änderungen an der "
        "Lohn-Mathematik IMMER hier ansetzen:",
        S["body"],
    ))
    story.append(formula_box("""
Controllers/PayrollController.cs          → nur HTTP (dünn)
Services/PayrollCalculationEngine.cs      → Orchestrierung (FLEX/MTP/FIX-Zweige)
Services/PayrollCalculationService.cs     → reine static Helfer (BuildResult, Round05, …)
Controllers/PayrollModels.cs              → DTOs / Records
""", S))
    story.append(Paragraph(
        "Regel: reine Rechnung → <b>PayrollCalculations</b>; DB-/Service-Orchestrierung → "
        "<b>PayrollCalculationEngine</b>; nur Routing/Auth → Controller. "
        "Geldbeträge schreibt nur <b>/api/payroll/confirm</b> (server-autoritativ via Calculate).",
        S["body"],
    ))

    # ── 2 Modelle ──────────────────────────────────────────────────────
    story.append(Paragraph("2. Vertragsmodelle & Auszahlungsmatrix", S["h1"]))
    story.append(hr())
    story.append(simple_table(
        ["Modell", "Ferien", "Feiertag", "13. ML"],
        [
            ["FLEX", "Saldo CHF («Ferien-Geld») — Auszahlung bei Bezug/Austritt",
             "monatlich ausbezahlt", "monatlich ausbezahlt"],
            ["MTP", "Saldo Tage + CHF-Pott — Auszahlung bei Bezug",
             "monatlich ausbezahlt", "Saldo; Auszahlung nur in Payout-Monaten"],
            ["FIX / FIX-M", "Saldo Tage — keine CHF-Auszahlung",
             "Saldo Tage — keine Auszahlung", "Saldo CHF; Auszahlung nur in Payout-Monaten"],
        ],
        S, col_widths=[22 * mm, 55 * mm, 45 * mm, 50 * mm],
    ))
    story.append(Spacer(1, 3 * mm))
    story.append(Paragraph(
        "Sozialleistungs-Abzug greift erst bei <b>tatsächlicher Auszahlung</b> von Ferien "
        "oder 13. ML — nicht beim monatlichen Akkumulieren in den Saldo.",
        S["body"],
    ))
    story.append(Paragraph("<b>Drei Tagessätze — nie mischen</b>", S["h3"]))
    story.append(formula_box("""
Ferien MTP (Kürzung):     guaranteedH × Stundenlohn / 7
Ferien FIX/FIX-M:         Monatslohn × 12 / 365
Krank/Unfall (alle):      KtgTagessatzService (eigene Formel)
FLEX:                     kein eigener Ferien-Tagessatz — Pott CHF / Pott Tage
""", S))

    # ── 3 Grundlagen ───────────────────────────────────────────────────
    story.append(Paragraph("3. Grundlagen: Periode, Rundung, Kurzperiode", S["h1"]))
    story.append(hr())
    story.append(Paragraph("3.1 Lohnperiode", S["h2"]))
    story.append(Paragraph(
        "Immer Kalendermonat (1. – letzter Tag). Keine Periodenflexibilität mehr.",
        S["body"],
    ))
    story.append(formula_box("CalcPeriod(year, month) → (1., letzter Tag des Monats)", S))

    story.append(Paragraph("3.2 Rundung", S["h2"]))
    story.append(formula_box("""
Round05(x) = Round(x / 0.05) × 0.05   (AwayFromZero)
Zwischenprodukte bleiben exakt; Rundung erst an der Lohnzeile.
Round05 nur auf Nettolohn und Auszahlungsbetrag.
""", S))

    story.append(Paragraph("3.3 Kurzperiode (Eintritt / Austritt)", S["h2"]))
    story.append(formula_box("""
periodEffectiveFrom = max(periodFrom, ContractStart)
shortPeriodDays     = periodTo − periodEffectiveFrom + 1   (bei Austritt ggf. bis Austritt)

FIX/FIX-M Pro-Rata:  Monatslohn × 12/365 × shortPeriodDays
MTP Soll pro-rata:   guaranteedH / 7 × shortPeriodDays
""", S))

    # ── 4 Stempel / Nacht ──────────────────────────────────────────────
    story.append(Paragraph("4. Stempelzeiten & Nacht", S["h1"]))
    story.append(hr())
    story.append(Paragraph("4.1 Dauer pro Stempel (Sekunden-genau)", S["h2"]))
    story.append(Paragraph(
        "Die Anzeige zeigt nur <b>HH:mm</b> (Sekunden ausgeblendet). Gerechnet wird "
        "mit der vollen Uhrzeit inkl. Sekunden aus easy@work — deshalb kann "
        "«11:49–16:22» als 4.56 erscheinen, obwohl 4 h 33 min = 4.55 wären "
        "(z.B. tatsächliches Ende 16:22:36 → 4.56 h).",
        S["body"],
    ))
    story.append(formula_box("""
TotalHours   = Round( (TimeOut − TimeIn).TotalHours , 2 )
               // inkl. Sekunden; Anzeige UI nur HH:mm (stempelFmtTime)
DurationHours = Round( TotalHours − NightHours , 2 )
workedHours   = Σ timeEntries.TotalHours in Periode
""", S))
    story.append(Paragraph("4.2 Nachtzuschlag (Zeit)", S["h2"]))
    story.append(formula_box("""
nightHours    = Schnitt Stempelintervall × Nachtfenster [nightStart, nightEnd)
                (über Mitternacht = 2 Fenster; Round 2 Dezimalen)
nightBonus    = nightHours × 0.10          ← Zeitzuschlag, kein CHF
nachtSaldoNeu = vorMonatNacht + nightBonus − nachtKompStunden

FLEX Nacht-Komp-Auszahlung (AbsenzTyp UtpAuszahlung):
  utpAuszahlungStunden × hourlyRate
""", S))
    story.append(Paragraph(
        "Stempelzeiten sind read-only (easy@work-API). Nacht-Saldo wird für alle Modelle "
        "im Lohnzettel angezeigt. Code: EasyAtWorkTimepunchSyncService.",
        S["body"],
    ))

    # ── 5 Absenz-Stunden ───────────────────────────────────────────────
    story.append(Paragraph("5. Absenzen-Stunden", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
AbsenzStunden = TageInPeriode × weeklyH / divisor × Prozent/100
  divisor = 7 bei Modus «1/7» (Ferien/Feiertag/UU)
  divisor = 5 sonst (Krank/Unfall Werktage)

MTP: weeklyH = GuaranteedHoursPerWeek
FIX Ferien/Feiertag (Basis VERTRAG): NormalWeeklyHours × Pensum%
FLEX/MTP: Ferien+Feiertag-Stunden-Gutschrift oft 0 (werden anders vergütet)
UNBEZ_URLAUB → 0 Stundenlohn-Gutschrift
""", S))
    story.append(Paragraph(
        "Skalierung über Periodengrenze: "
        "<font face='Courier'>(HoursCredited / alleMarkiertenTage) × TageInPeriode</font>",
        S["body"],
    ))

    # ── 6 Ferien/Feiertag Tage ─────────────────────────────────────────
    story.append(Paragraph("6. Ferien-Tage, Feiertag-Tage, Art. 329b OR", S["h1"]))
    story.append(hr())
    story.append(Paragraph("6.1 Ferien-Tage-Saldo (alle Modelle)", S["h2"]))
    story.append(formula_box("""
vacationWeeks     = 6 wenn vacationPct ≥ 12.5 %, sonst 5
                    (Filial-% ; ab VacationSixWeeksFromAge → 6-Wochen-%)
ferienTageAccrual = (vacationWeeks × 7) / 12
UU-Kürzung:       Accrual − (Jahresanspruch/365 × UU-Kalendertage)   ≥ 0
Saldo neu:        Vormonat + Accrual − Genommen
""", S))
    story.append(Paragraph("6.2 Feiertag-Tage (nur FIX / FIX-M)", S["h2"]))
    story.append(formula_box("""
Accrual     = +0.5 Tag / Monat
UU-Kürzung  = (0.5×12)/365 × UU-Tage
Saldo neu   = Vormonat + Accrual − Genommen
Keine CHF-Auszahlung — nur Tage-Saldo.
""", S))
    story.append(Paragraph("6.3 Ferienkürzung Art. 329b OR", S["h2"]))
    story.append(formula_box("""
Zwölftel = ⌊(Tage − Schwelle) / 30⌋
Schwellen: unverschuldet 60 · verschuldet 30 · Schwangerschaft 90
VorschlagTage = TotalKuerzung12tel × (vacationWeeks×7) / 12
Anwendung nur bei Confirm (Bool ApplyFerienKuerzung) — nicht automatisch.
""", S))

    # ── 7 FLEX ─────────────────────────────────────────────────────────
    story.append(Paragraph("7. Modell FLEX", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
Stundenlohn           = workedHours × hourlyRate
Ausbezahlte Feiertage = feiertagStunden × hourlyRate
Feiertagentschädigung = Σ(Flag Feiertag-Basis) × holidayPct/100     → ausbezahlt
Ferienentschädigung   = Σ(Flag Ferien-Basis) × vacationPct/100      → nur Saldo (betrag=0)
Ferien-Geld-Auszahlung = Pott-Formel (§8)
Dezember Auto-Auszahlung Restsaldo (Filial-Flag) → Code 195.3, SV-pflichtig
13. ML                = Basis13 × thirteenthPct/100                 → monatlich ausbezahlt
                         in Probezeit nur Rückstellung, danach Nachzahlung
Krank/Unfall 88%/80%  = Tagessatz100 × %/100 × 0.88 bzw. 0.80
                         kein Festlohn-Abzug; BVG-Aufschlag 12%/20% in Wartefrist
Stunden-Saldo         = keiner (0)
""", S))

    # ── 8 Pott ─────────────────────────────────────────────────────────
    story.append(Paragraph("8. Ferien-Geld-Pott (FLEX / MTP)", S["h1"]))
    story.append(hr())
    story.append(Paragraph(
        "Gemeinsame Formel für Bezug von Ferientagen — gilt für FLEX und MTP "
        "(CalcFerienGeld / FerienAuszahlungService):",
        S["body"],
    ))
    story.append(formula_box("""
Pott CHF   = Vormonats-FerienGeld + AccrualAktMonat
Pott Tage  = Vormonats-FerienTage + ferienTageAccrual
Tagessatz  = Pott CHF / Pott Tage
Auszahlung = Tagessatz × bezogeneTage     Cap = Pott CHF (kein Vorbezug)
Saldo neu  = Pott CHF − Auszahlung        ≥ 0
""", S))
    story.append(Paragraph(
        "Akonto (nur FLEX): nur Ferien mit DateTo ≤ Stichtag und DateFrom ≥ Periodenstart "
        "zählen (ganze Absenz, nicht anteilig). Überhängende Ferien → Definitiv.",
        S["body"],
    ))

    # ── 9 MTP ──────────────────────────────────────────────────────────
    story.append(Paragraph("9. Modell MTP", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
sollStundenVoll = guaranteedH / 7 × Periodentage     (pro-rata bei Kurzperiode)
Festlohn-Soll   = Sollvoll
                  − FerienKalendertage × H/7
                  − KrankWerktage × H/5
                  − UnfallWerktage × H/5
                  − UU × H/7                          Cap ≥ 0
Festlohn CHF    = SollStunden × hourlyRate
Ferien-Tagessatz (Kürzung) = guaranteedH × hourlyRate / 7
  ≠ Pott-Auszahlung, ≠ KTG-Tagessatz

Mehrstunden = max(0, Ist + AbsenzGutschrift − Soll + Vormonat) × Rate
Feiertag-Ent % → ausbezahlt; Ferien-Ent % → Saldo; Ferien-Bezug → Pott (§8)
Krank/Unfall: Festlohn gekürzt (Werktage); KEINE Korrektur-Codes 75/65
              Taggeld 88%/80% vom KTG-Tagessatz (Kalendertage, auch Sa+So)
13. ML: Accrual in Saldo; Auszahlung nur in Payout-Monaten
""", S))
    story.append(Paragraph(
        "Hinweis: Sa+So zählen bei Krank/Unfall <b>nicht</b> für die Festlohn-Kürzung, "
        "wohl aber für das Taggeld (Versicherung).",
        S["body"],
    ))
    story.append(Paragraph(
        "Vertrags-Card «Garantiert / Monat» = guaranteedH × Rate × 52/12 — das ist "
        "<b>nicht</b> der Perioden-Festlohn (der ist /7 × Tage).",
        S["small"],
    ))

    # ── 10 FIX ─────────────────────────────────────────────────────────
    story.append(Paragraph("10. Modell FIX / FIX-M", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
Monatslohn      = MonthlySalary  bzw.  Fte × Pensum%
Kurzperiode     = Monatslohn × 12/365 × shortPeriodDays
Ferien-Tagessatz = MonatslohnVoll × 12 / 365

Festlohn-Split:
  FerienCHF / FeiertagCHF = Tagessatz × bezogene Tage
  Arbeit                  = Monatslohn − FerienCHF − FeiertagCHF

Unbezahlter Urlaub: − Tagessatz × UU-Tage   (reduziert SV-Basis)
Krank/Unfall:       − Tagessatz × Tage (Code 75/65)
                    + 88% Karenz + 80% Taggeld
Sollstunden = (WeeklyHours || NormalWeekly×Pensum) / 7 × Periodentage
Stunden-Saldo = Ist + AbsenzGutschrift − Soll + Vormonat   (kein Payout)
Ferien-/Feiertag-Geld = 0 (im Monatslohn enthalten)
13. ML: wie MTP (Saldo + Payout-Monate)
""", S))

    # ── 11 13. ML ──────────────────────────────────────────────────────
    story.append(Paragraph("11. 13. Monatslohn", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
Monats-Accrual = Basis13ml × thirteenthPct / 100
                 (Basis = SumByFlag ZaehltAlsBasis13ml)

FLEX:   monatlich ausbezahlt (in Probezeit nur Rückstellung → Nachzahlung danach)
MTP/FIX: Saldo = prev + monthly; Auszahlung nur wenn IsThirteenthPayoutMonth
         (CSV ThirteenthMonthPayoutMonths oder Legacy 1/2/4×/Jahr)
Probezeit blockt Auszahlung in allen Modellen.

Zulage mit 13er-Flag: Total → Basis = Total×12/13 , 13.ML = Total−Basis
""", S))

    # ── 12 KTG ─────────────────────────────────────────────────────────
    story.append(Paragraph("12. KTG / UVG-Tagessatz & Karenz", S["h1"]))
    story.append(hr())
    story.append(Paragraph("12.1 Tagessatz 100 %", S["h2"]))
    story.append(formula_box("""
StdLohnBrutto (FLEX/MTP) =
  HourlyRate × (1+Ferien%) × (1+Feiertag%) × (1+8.33%)     // 8.33% = 13. ML

Regel A (< 4 Perioden Historie):
  FIX/FIX-M:  Monatslohn × 12 / 365
  FLEX/MTP:   Wochenstunden × StdLohnBrutto × 52 / 365

Regel B (≥ 4 Perioden):
  FIX/FLEX:   Ø(SvBasisAhv letzte ≤12 Mt) × 12 / 365
  MTP:        Garantie×52/365 + Ø(MTP+Stunden×BruttoAufschlag)×12/365

Karenzentschädigung 88% = Tagessatz100 × 0.88
Taggeld 80%             = Tagessatz100 × 0.80
""", S))
    story.append(Paragraph("12.2 Karenz & BVG-Wartefrist", S["h2"]))
    story.append(formula_box("""
InKarenz: kumulierte Karenztage (×Prozent) ≤ Max
          (Krank/Unfall getrennt; Defaults z.B. 14 / 2)

BVG-Wartefrist:
  AU ab 1.–15. → Monatserster; sonst Folgemonat
  Ende = Start + BvgWartefristMonate − 1
  In Karenz fehlende 12%, nach Karenz 20% → deltaBvg in SV-Basen
""", S))

    # ── 13 SV ──────────────────────────────────────────────────────────
    story.append(Paragraph("13. Sozialversicherungen (BuildResult)", S["h1"]))
    story.append(hr())
    story.append(Paragraph(
        "Zentrale Engine in <font face='Courier'>PayrollCalculations.BuildResult</font>. "
        "Reihenfolge der Caps ist verbindlich.",
        S["body"],
    ))
    story.append(simple_table(
        ["Thema", "Formel / Regel"],
        [
            ["Basiswahl", "AHV/ALV→Ahv; NBUV→Nbuv; KTG→Ktg; BVG→Bvg−Koord; QST→Qst"],
            ["AHV Freibetrag 65+", "max(0, Basis − FreibetragMonthly)"],
            ["BVG Eintritt", "wenn svBases.Bvg×12 < EntryThresholdYearly → Basis 0"],
            ["BVG Min", "versichert und Basis < Min → Min (315)"],
            ["BVG Max flach", "min(Basis, MaxBaseFlatMonthly) = 5'355 — kein Jahresausgleich"],
            ["ALV/NBU Cap Monat", "min(Basis, 12'350) — Jan–Nov"],
            ["ALV/NBU Dezember", "jahresPflichtig = min(ΣYTD+Dez, Cap×12); "
             "Dez = max(0, jahresPflichtig − Σgedeckelt) — YTD über alle Filialen"],
            ["AN-Abzug", "−Round(Basis × Rate/100, 2)"],
            ["AG-Beitrag", "Round(Basis × RateEmployer/100, 2) — nicht im Netto"],
            ["NBU &lt;8h/Woche", "FLEX: Regel entfernt (UVG Art. 1a Abs. 6)"],
            ["FAK AG", "reiner AG-Beitrag auf AHV-Basis (Code FAK)"],
            ["BVG_ZUSATZ", "nur wenn MA in employee_bvg_zusatz_member aktiv"],
        ],
        S, col_widths=[40 * mm, 132 * mm],
    ))
    story.append(Spacer(1, 2 * mm))
    story.append(Paragraph(
        "BVG-Reihenfolge: Eintrittsschwelle → Min → Max. "
        "AHV21-Referenzalter: Männer 65; Frauen gestaffelt Jahrgang ≤1960…1963, ab 1964: 65.",
        S["body"],
    ))

    # ── 14 QST ─────────────────────────────────────────────────────────
    story.append(Paragraph("14. Quellensteuer (QST)", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
Tarif-Lookup (ESTV): Lohnschlüssel = Floor(Brutto/10); Satz in Basispunkten
Steuerbetrag Tabelle = Brutto × bp / 10000

Engine:
  Satz am satzbestimmenden Brutto; Betrag = IST-Brutto × Satz%
  satzBrutto ≥ IST

Priorität satzbestimmend:
  1) manueller MindestlohnSatzbestimmung
  2) Nebenjob-Hochrechnung (Variante B)
  3) IST (Variante A — ein Arbeitgeber)

Variante B1: Brutto × Gesamtpensum / Eigenpensum   (wenn Gesamt ≥100 → null)
Variante B2: IST + GesamteinkommenWeitereAg
Variante B3 Stunden (FLEX/MTP): Brutto × 180 / workedHours
Variante B3 Pensum (FIX):       Brutto × 100 / Pensum%
Pensum-Schätzung: min(100, workedHours/180×100)

Mindestbetrag (z.B. LU 13 CHF): wenn 0 < qst < 13 → 13
Wohnkanton = employee.canton_code (Hauptadresse)
""", S))
    story.append(Paragraph(
        "QST-Pflicht-Check (vor Erfassung): befreit wenn CH-Bürger, C-Ausweis, "
        "Behörden-Befreiung, Ehepartner CH oder Ehepartner C. Sonst Pflicht offen → "
        "Lohnlauf-Block 409 QST_PFLICHT_OFFEN.",
        S["body"],
    ))

    # ── 15 Netto ───────────────────────────────────────────────────────
    story.append(Paragraph("15. Netto, Abtretung, Bank-Split, Akonto-Verrechnung", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
Nettolohn         = Round05(totalLohn + totalAbzuege)     // Abzüge negativ
Lohnabtretung     = min(max(0, Netto − Freigrenze), RestSchuld)  // Kaskade
Akonto verrechnen = Extra-Abzug −akontoBereitsAusbezahlt   // nur wenn AUSBEZAHLT
Auszahlungsbetrag = Round05(Netto + ZulagenExtra − AbzügeExtra)
Bank-Split        = FIXBETRAG / PROZENT / NETTO_ABZUEGLICH / VOLL(Rest)
                    Hauptbank = Rest
""", S))
    story.append(Paragraph(
        "Familienzulagen liegen ausserhalb des Journal-Nettos (kommen erst im "
        "Auszahlungsbetrag dazu). DTA enthält nie Beträge ≤ 0.",
        S["body"],
    ))

    # ── 16 Akonto ──────────────────────────────────────────────────────
    story.append(Paragraph("16. Akonto-Lauf (6 Regeln)", S["h1"]))
    story.append(hr())
    story.append(simple_table(
        ["#", "Regel"],
        [
            ["1", "Kein Akonto wenn Vertragsende ≤ Periodenende"],
            ["2", "Kein Akonto bei Krankheit/Unfall/Mutterschaft am Stichtag"],
            ["3", "FIX: AkontoProzentFix × Definitiv-Auszahlung, abgerundet auf CHF 10"],
            ["4", "FIX-M: wie Regel 3 (eigener %-Default, z.B. 90)"],
            ["5", "MTP: AkontoProzentHourly × (Garantie-Soll × Rate − SV), Floor CHF 10 "
                  "— bewusst ohne Ferien-Pott"],
            ["6", "FLEX: AkontoProzentHourly × (Std bis Stichtag × Rate + Ferien-Pott − SV), "
                  "Floor CHF 10"],
        ],
        S, col_widths=[10 * mm, 162 * mm],
    ))
    story.append(Spacer(1, 2 * mm))
    story.append(formula_box("""
Netto-Vorschlag = Floor( (Brutto − SV − man.Abzüge) × Akonto% / 10 ) × 10
Defaults: FIX 80%, FIX-M 90%, Hourly (FLEX/MTP) 100%
Pfändung: Cap auf Freigrenze (Freigrenze 0 → Akonto 0)

FIX/FIX-M: grobe Brutto-Schätzung zuerst; exakte Korrektur via
  POST …/sync-fix-from-slip/{id}  →  NettoAkonto = % × auszahlungsbetrag
""", S))

    # ── 17 Mindestlohn ─────────────────────────────────────────────────
    story.append(Paragraph("17. Mindestlohn & Lohnsumme", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
FIX/FIX-M:  Monatslohn  vs  rule.Amount × Pensum%     (salaryType=monthly)
FLEX/MTP:   HourlyRate  vs  rule.Amount               (salaryType=hourly)

Kommunaler Floor (Filiale):
  Monat 100% = Jahreslohn / 13
  Stunde     = Jahreslohn / 52 / Filial-Wochenstunden
  wirksam    = max(L-GAV, Floor)     Jugend nur wenn applies_to_youth

Lohnsumme fehlt (eigenständiger Block 409 LOHNSUMME_FEHLT):
  FIX/FIX-M: weder MonthlySalary noch MonthlySalaryFte
  FLEX/MTP:  kein HourlyRate

Stichtag für Checks = älteste offene Lohnperiode (nie DateTime.Now)
Confirm/Freigeben: 409 MINDESTLOHN_UNTERSCHRITTEN bei UNDERPAID
""", S))

    # ── 18 LGAV ────────────────────────────────────────────────────────
    story.append(Paragraph("18. L-GAV-Vollzugsbeitrag", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
FIX/FIX-M: voller Beitrag (Default 99)
FLEX:      reduziert (Default 49.50)
MTP:       voll wenn GuaranteedH > 50% NormalWeekly UND Dauer ≥ 6 Mt., sonst reduziert

1× pro Jahr (Code 600.24), Trigger = LgavTriggerMonat oder unterjähriger Ersteintritt
EnsureAsync ist idempotent — Preview schreibt nicht (persistLgav=false)
""", S))

    # ── 19 FamZ ────────────────────────────────────────────────────────
    story.append(Paragraph("19. Familienzulagen", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
Mindesteinkommen-Check:
  MTP:  max(Garantie-Lohn, Ist-Lohn)
  FLEX: Stunden × Rate
  FIX:  Monatslohn
Unter Schwelle → keine Auszahlung

Kindersatz: Satz1/Satz2 nach Alter aus Kantons-Tarif (FamilienzulagenResolverService)
Im Auszahlungsbetrag, NICHT im Snapshot-Netto / Journal-1920
""", S))

    # ── 20 Fibu ────────────────────────────────────────────────────────
    story.append(Paragraph("20. Fibu-Rückstellungen", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
Tagessatz RST FIX/FIX-M = Monatslohn × 12 / 365     (= Engine-Tagessatz!)

RST Ferien FIX/FIX-M = (ferienTageAccrual − Genommen) × Tagessatz
RST Ferien FLEX/MTP  = ferienGeldAccrual − ferienGeldAuszahlung   (CHF)
RST Feiertag FIX     = (feiertagTageAccrual − Genommen) × Tagessatz
RST 13. ML           = ThirteenthMonthMonthly → Pos 2010

Kostenstelle: FLEX→200 · MTP/FIX→100 · FIX-M→300
Durchlaufkonto 1920 soll Saldo 0 ergeben.
AG-SV / FAK / RST berühren 1920 NICHT.
""", S))

    # ── 21 Fristen ─────────────────────────────────────────────────────
    story.append(Paragraph("21. Probezeit, Austritt, Fristen", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
In Probezeit: today/period ≤ ProbationEndDate  → blockt 13.-ML-Auszahlung
Austritt:     Kurzperioden-Pro-Rata (§3.3)

Sperrfrist AU (OR):
  DJ1: 30 · DJ2–5: 90 · ab DJ6: 180 Tage ab AU-Beginn
  bei DJ-Übergang → längere Frist

Mutterschutz:
  Schwangerschaftsbeginn = ET − 280 Tage
  Schutzende             = Geburt + 16 Wochen   (OR 336c)
""", S))

    # ── 22 Monatsblatt ─────────────────────────────────────────────────
    story.append(Paragraph("22. Monatsblatt Stundenkontrolle", S["h1"]))
    story.append(hr())
    story.append(formula_box("""
Stunden Ist   = Snapshot workedHours / Stempel-Summe
Stunden Bezug = FLEX: = Ist (mit Lohn ausbezahlt), Saldo 0
                MTP/FIX: Bezug typ. 0 (Saldo-geführt)
Nacht Ist     = nightBonus bzw. istNight × 0.10
13./Ferien    = aus Snapshot / SlipJson-Feldern

PDF pro MA; Versand zusammen mit Lohnzettel möglich.
Import Mirus-Monatsblatt: Codes 901 Zeitsaldo, 904 Nacht, 903 Ferien-Tage, 902 Feier-Tage
""", S))

    # ── 23 Merksätze ───────────────────────────────────────────────────
    story.append(Paragraph("23. Querschnitt-Merksätze & Code-Orte", S["h1"]))
    story.append(hr())
    for t in [
        "Drei Tagessätze nie mischen (Ferien MTP / Ferien FIX / KTG).",
        "FLEX: Feiertag + 13. ML monatlich; Ferien nur als CHF-Saldo (Pott bei Bezug).",
        "MTP/FIX: 13. ML und Ferien-Geld erst bei Auszahlung SV-wirksam.",
        "Rundung: exakt rechnen → Round Zeile → Round05 nur Netto/Auszahlung.",
        "Stempel-Total: Sekunden-genau aus TimeOut−TimeIn, Round 2 Dez.; UI zeigt nur HH:mm.",
        "Akonto ≠ Definitiv: MTP-Akonto auf Garantie; FIX grob, dann Slip-Sync.",
        "Stichtag für Compliance = älteste offene Periode, nie «heute».",
        "DTA: nie Beträge ≤ 0; leeres DTA wird abgelehnt.",
        "Timestamps: DateTime.Now (Local), nie UtcNow — Spalten sind timestamp without time zone.",
    ]:
        story.append(Paragraph(f"• {t}", S["bullet"]))

    story.append(Paragraph("<b>Wichtige Dateien</b>", S["h3"]))
    story.append(Paragraph(
        "Services/PayrollCalculationEngine.cs — Orchestrierung<br/>"
        "Services/PayrollCalculationService.cs — BuildResult, CalcFerienGeld, Round05<br/>"
        "Services/FerienAuszahlungService.cs — Pott (auch Akonto)<br/>"
        "Services/AkontoLaufService.cs — 6 Akonto-Regeln<br/>"
        "Services/KtgTagessatzService.cs — Krank/Unfall-Tagessatz<br/>"
        "Services/KarenzService.cs — Karenz / BVG-Wartefrist<br/>"
        "Services/MinimumWageCheckService.cs — Mindestlohn / Lohnsumme<br/>"
        "Services/QuellensteuerTarifService.cs — QST-Tarife<br/>"
        "Services/LgavBeitragService.cs — L-GAV-Beitrag<br/>"
        "Services/FibuJournalService.cs — RST Ferien/Feiertag/13.<br/>"
        "Services/StundenkontrollePdfService.cs — Monatsblatt<br/>"
        "Services/FerienKuerzungService.cs — Art. 329b OR",
        S["small"],
    ))
    story.append(Spacer(1, 4 * mm))
    story.append(Paragraph(
        "Dieses PDF wird erzeugt mit: "
        "<font face='Courier'>python3 scripts/generate_berechnungen_pdf.py</font>",
        S["small"],
    ))

    doc.build(story, onFirstPage=footer, onLaterPages=footer)
    print(f"Wrote {OUT} ({OUT.stat().st_size} bytes)")


if __name__ == "__main__":
    build()
