using System.Globalization;
using System.Text;
using HrSystem.Tests.Swissdec;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Nachrechnung aller Muster-AG-Testfälle gegen die Swissdec-RefXML — ohne Testinstanz,
/// ohne bestätigte Lohnläufe (Walter 26.09.2026). Läuft die echte Abzugs-Engine
/// (<c>PayrollCalculations.BuildResult</c>) über die Lohnarten aus der CSV und vergleicht
/// SV-Basen und Abzüge mit dem Referenz-XML.
///
/// Der Bericht landet in <c>SWISSCEC/Abgleich/nachrechnung.md</c>. Der Test hält die Zahl
/// der Abweichungen fest: wird sie grösser, ist eine Änderung an der Lohnrechnung schuld.
/// </summary>
public class TestmandantNachrechnungTests
{
    private const int Jahr = 2025;
    /// <summary>Rappen-Toleranz: SV-Abzüge sind bei uns rappengenau, Swissdec rundet auf 5 Rp.</summary>
    private const decimal Toleranz = 0.06m;

    private sealed record Befund(string Tf, string Name, string Monat, string Was,
                                 decimal Wir, decimal Swissdec, string Hinweis)
    {
        public decimal Diff => Wir - Swissdec;
        /// <summary>BEWUSST (Abweichungsprotokoll) · WERKZEUG (Grenze des Nachrechners) · OFFEN.</summary>
        public string Art { get; init; } = "OFFEN";
        public string Grund { get; init; } = "";
    }

    /// <summary>
    /// Bekannte Fälle einordnen, damit die offene Liste nur enthält, was wirklich
    /// noch zu klären ist (Walter 26.09.2026).
    /// </summary>
    private static Befund Einordnen(Befund b) => b switch
    {
        // Abweichungsprotokoll A7: Teilmonat — Swissdec zahlt den vollen Monatslohn und
        // kürzt mit Lohnart 1001, OneCrew rechnet die Tage selbst (Eintritt 10.02.).
        { Tf: "TF25" or "TF26", Monat: "02" }
            => b with { Art = "BEWUSST", Grund = "A7 Teilmonat: Eintrittstag zählt (8'400 statt 8'000)" },

        // Nachzahlung nach Austritt: die Engine rechnet sie über die Anstellungsmonate des
        // AUSTRITTSJAHRES ab (Alter, Sätze, Höchstlöhne per Austrittsmonat, CalculateCorrectionAsync).
        // Der Nachrechner kennt nur das laufende Jahr — er kann den Fall nicht abbilden.
        { Tf: "TF07", Monat: "01" or "02" }
            => b with { Art = "WERKZEUG", Grund = "Nachzahlung nach Austritt (Austrittsjahr 2024) — Korrekturlauf, nicht im Nachrechner" },

        _ => b,
    };

    [Fact]
    public void AlleTestfaelle_GegenRefXml()
    {
        var katalog    = TestmandantDaten.LadeKatalog();
        var saetze     = TestmandantDaten.LadeFirmensaetze(Jahr);
        var personen   = TestmandantDaten.LadePersonen();
        var mutationen = TestmandantDaten.LadeMutationen();
        var lohnarten  = TestmandantDaten.LadeLohnarten();
        TestmandantDaten.ErgaenzeCsvLuecken(lohnarten);

        var xmlYtd   = Enumerable.Range(1, 12).ToDictionary(m => m, m => TestmandantDaten.LadeXmlYtd(Jahr, m));
        var xmlMonat = Enumerable.Range(1, 12).ToDictionary(m => m, m => TestmandantDaten.LadeXmlMonat(Jahr, m));

        var befunde = new List<Befund>();
        int gepruefteWerte = 0, gepruefteMonate = 0;

        foreach (var tf in lohnarten.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (!personen.TryGetValue(tf, out var stamm)) continue;
            var monate = TestmandantNachrechner.RechneJahr(
                tf, Jahr, stamm, mutationen.GetValueOrDefault(tf), lohnarten[tf], katalog, saetze);

            // Verglichen wird der MONATS-Wert: Swissdec meldet YTD, wir bilden die
            // Differenz zum Vormonat. So bleibt jeder Monat für sich prüfbar — eine
            // YTD-Summe würde nach einem Versicherungs-Codewechsel dauerhaft daneben
            // liegen, obwohl jeder einzelne Monat stimmt.
            TestmandantDaten.XmlYtd? vorher = null;
            foreach (var e in monate)
            {
                gepruefteMonate++;

                if (xmlYtd[e.Monat].TryGetValue(tf, out var ytdJetzt))
                {
                    var v = vorher ?? new TestmandantDaten.XmlYtd(0, 0, 0, 0, 0, 0);
                    var soll = new TestmandantDaten.XmlYtd(
                        ytdJetzt.Ahv - v.Ahv, ytdJetzt.Alv - v.Alv, ytdJetzt.Alvz - v.Alvz,
                        ytdJetzt.Uvg - v.Uvg, ytdJetzt.Uvgz - v.Uvgz, ytdJetzt.Ktg - v.Ktg);
                    vorher = ytdJetzt;
                    decimal kumAhv = e.Basis("AHV_ROH"), kumAlv = e.Basis("ALV"), kumAlvz = e.Basis("ALVZ"),
                            kumNbuv = e.Basis("NBUV"), kumUvgz = e.Basis("UVGZ"), kumKtg = e.Basis("KTG");
                    void Pruefe(string was, decimal wir, decimal swissdec, string hinweis = "")
                    {
                        gepruefteWerte++;
                        if (Math.Abs(wir - swissdec) > Toleranz)
                            befunde.Add(new Befund(tf, e.Name, e.Monat.ToString("00"), was, wir, swissdec, hinweis));
                    }
                    Pruefe("AHV-Basis",  kumAhv,  soll.Ahv);
                    if (e.AhvPflichtig)
                    {
                        Pruefe("ALV-Basis",  kumAlv,  soll.Alv);
                        Pruefe("ALVZ-Basis", kumAlvz, soll.Alvz);
                    }
                    // Der UVG-Block der Meldung führt den NBU-pflichtigen Lohn auch dann,
                    // wenn der AN nichts zahlt (Code A0/A2/A3 = Prämie beim AG). Nur bei
                    // A1 ist unsere Abzugsbasis damit vergleichbar.
                    if (e.NbuBeimAn) Pruefe("UVG-Basis", kumNbuv, soll.Uvg);
                    Pruefe("UVGZ-Basis", kumUvgz, soll.Uvgz);
                    Pruefe("KTG-Basis",  kumKtg,  soll.Ktg);
                }
                if (xmlMonat[e.Monat].TryGetValue(tf, out var sollMonat) && sollMonat.SozialAbzuege != 0m)
                {
                    gepruefteWerte++;
                    if (Math.Abs(e.SozialAbzuege - sollMonat.SozialAbzuege) > Toleranz)
                        befunde.Add(new Befund(tf, e.Name, e.Monat.ToString("00"), "SV-Abzug Monat",
                            e.SozialAbzuege, sollMonat.SozialAbzuege, "AHV + ALV + ALVZ + NBU"));
                }
            }
        }

        befunde = befunde.Select(Einordnen).ToList();
        var offen = befunde.Where(b => b.Art == "OFFEN").ToList();
        SchreibeBericht(befunde, offen, gepruefteWerte, gepruefteMonate);

        // Stand 26.09.2026: die verbleibenden Abweichungen sind im Bericht einzeln
        // aufgeführt. Wächst die Zahl, hat eine Code-Änderung die Lohnrechnung bewegt.
        Assert.True(offen.Count <= ErwarteteAbweichungen,
            $"Neue Abweichungen gegenüber der RefXML: {offen.Count} statt höchstens {ErwarteteAbweichungen}. "
            + "Bericht: SWISSCEC/Abgleich/nachrechnung.md\n"
            + string.Join("\n", offen.Take(15).Select(b =>
                $"  {b.Tf} {b.Monat} {b.Was}: wir {b.Wir:N2} / Swissdec {b.Swissdec:N2} ({b.Diff:+0.00;-0.00})")));
    }

    /// <summary>
    /// Offene Abweichungen, die noch niemand erklärt hat — Stand 26.09.2026: **29** in
    /// sieben Testfällen (TF03, TF09, TF12, TF15, TF16, TF40, TF41), aufgelistet in
    /// `SWISSCEC/Abgleich/nachrechnung.md`. Eingeordnete Fälle (BEWUSST / WERKZEUG,
    /// siehe <see cref="Einordnen"/>) zählen hier NICHT mit.
    /// Deckel, nicht Ziel: jede geklärte Abweichung senkt die Zahl, eine neue lässt den
    /// Test rot werden. NICHT anheben, ohne die neue Abweichung verstanden zu haben.
    /// </summary>
    private const int ErwarteteAbweichungen = 29;

    private static void SchreibeBericht(List<Befund> alle, List<Befund> offen, int werte, int monate)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Nachrechnung Muster AG — echte Abzugs-Engine gegen Swissdec-RefXML");
        sb.AppendLine();
        sb.AppendLine($"Erzeugt von `Tests/TestmandantNachrechnungTests.cs` (`dotnet test --filter TestmandantNachrechnung`).");
        sb.AppendLine("Lohnarten aus `SWISSCEC/Testmandant/wagetypes_export.csv`, Sätze aus `company_export.csv`,");
        sb.AppendLine("Personen + Mutationen aus den beiden anderen CSV — gerechnet mit `PayrollCalculations.BuildResult`,");
        sb.AppendLine("verglichen mit `SWISSCEC/RefXML/*_RETROSPECTIVE.xml` (YTD-Basen) und `*_MONTHLY.xml` (SV-Abzug).");
        sb.AppendLine();
        sb.AppendLine($"**{monate} Monatsabrechnungen, {werte} Vergleiche, {offen.Count} offene Abweichungen** "
                      + $"(dazu {alle.Count(b => b.Art == "BEWUSST")} bewusste und "
                      + $"{alle.Count(b => b.Art == "WERKZEUG")} Werkzeug-Grenzen, unten aufgeführt).");
        sb.AppendLine();
        sb.AppendLine("Nicht Teil dieser Nachrechnung (kommt im Lohnlauf aus der Datenbank): Stunden, Verträge,");
        sb.AppendLine("Saldi, Ferien-/Feiertag-Tage, 13.-ML-Rückstellung, Quellensteuer.");
        sb.AppendLine();
        sb.AppendLine("## Grenzen des Nachrechners");
        sb.AppendLine();
        sb.AppendLine("- **Nachzahlung nach Austritt**: die Engine rechnet sie über die Anstellungsmonate des");
        sb.AppendLine("  Austrittsjahres ab (Alter, Sätze und Höchstlöhne per Austrittsmonat,");
        sb.AppendLine("  `CalculateCorrectionAsync`). Der Nachrechner kennt nur das laufende Jahr.");
        sb.AppendLine("- **Monate ohne Lohnart** werden übersprungen; im echten Lohnlauf zählen sie als");
        sb.AppendLine("  Beschäftigungsmonat für den kumulierten Höchstlohn (Ein-/Austrittsmonate).");
        sb.AppendLine("- **Austritt und Wiedereintritt im selben Jahr** bildet der Nachrechner nur über ein");
        sb.AppendLine("  Vertragsfenster ab.");
        sb.AppendLine("- **Dokumentierte CSV-Lücken** (F5/F5b, TF11 März/Juni) trägt `ErgaenzeCsvLuecken` nach,");
        sb.AppendLine("  genau wie sie in der Testinstanz von Hand erfasst sind.");
        sb.AppendLine();

        if (offen.Count > 0)
        {
            sb.AppendLine("## Offene Abweichungen");
            sb.AppendLine();
            sb.AppendLine("| Testfall | Monat | Prüfung | OneCrew | Swissdec | Differenz | Hinweis |");
            sb.AppendLine("|---|---|---|---:|---:|---:|---|");
            foreach (var b in offen.OrderBy(b => b.Tf, StringComparer.Ordinal).ThenBy(b => b.Monat, StringComparer.Ordinal))
                sb.AppendLine($"| {b.Tf} {b.Name} | {b.Monat} | {b.Was} | {b.Wir.ToString("N2", CultureInfo.InvariantCulture)} "
                            + $"| {b.Swissdec.ToString("N2", CultureInfo.InvariantCulture)} "
                            + $"| {b.Diff.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} | {b.Hinweis} |");
            sb.AppendLine();
        }
        else sb.AppendLine("## Keine offenen Abweichungen\n");

        foreach (var (art, titel) in new[]
                 {
                     ("BEWUSST",  "Bewusste Abweichungen (docs/swissdec-abweichungsprotokoll.md)"),
                     ("WERKZEUG", "Grenzen des Nachrechners — kein Befund gegen die Lohnrechnung"),
                 })
        {
            var gruppe = alle.Where(b => b.Art == art).ToList();
            if (gruppe.Count == 0) continue;
            sb.AppendLine($"## {titel}");
            sb.AppendLine();
            sb.AppendLine("| Testfall | Monat | Prüfung | OneCrew | Swissdec | Grund |");
            sb.AppendLine("|---|---|---|---:|---:|---|");
            foreach (var b in gruppe.OrderBy(b => b.Tf, StringComparer.Ordinal).ThenBy(b => b.Monat, StringComparer.Ordinal))
                sb.AppendLine($"| {b.Tf} {b.Name} | {b.Monat} | {b.Was} | {b.Wir.ToString("N2", CultureInfo.InvariantCulture)} "
                            + $"| {b.Swissdec.ToString("N2", CultureInfo.InvariantCulture)} | {b.Grund} |");
            sb.AppendLine();
        }

        var ziel = Path.Combine(TestmandantDaten.RepoRoot, "SWISSCEC", "Abgleich");
        Directory.CreateDirectory(ziel);
        File.WriteAllText(Path.Combine(ziel, "nachrechnung.md"), sb.ToString());
    }
}
