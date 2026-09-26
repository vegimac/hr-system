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
    }

    /// <summary>
    /// Bekannte, bewusste Abweichungen (docs/swissdec-abweichungsprotokoll.md) — sie
    /// erscheinen im Bericht, zählen aber nicht als Fehler.
    /// </summary>
    private static bool IstBewusst(Befund b) =>
        // A7: Teilmonat — Swissdec zahlt den vollen Monatslohn und kürzt mit Lohnart 1001,
        // OneCrew rechnet die Tage selbst (TF25/26 Februar 8'400 vs 8'000).
        (b.Tf is "TF25" or "TF26" && b.Monat == "2025-02");

    [Fact]
    public void AlleTestfaelle_GegenRefXml()
    {
        var katalog    = TestmandantDaten.LadeKatalog();
        var saetze     = TestmandantDaten.LadeFirmensaetze(Jahr);
        var personen   = TestmandantDaten.LadePersonen();
        var mutationen = TestmandantDaten.LadeMutationen();
        var lohnarten  = TestmandantDaten.LadeLohnarten();

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

        var offen = befunde.Where(b => !IstBewusst(b)).ToList();
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
    /// Stand beim Bau des Nachrechners (26.09.2026): 48 Abweichungen in 9 Testfällen,
    /// aufgelistet in `SWISSCEC/Abgleich/nachrechnung.md`. Bewusst als Deckel, nicht als
    /// Ziel — jede abgearbeitete Abweichung senkt die Zahl, eine neue lässt den Test rot
    /// werden. NICHT anheben, ohne die neue Abweichung verstanden zu haben.
    /// </summary>
    private const int ErwarteteAbweichungen = 48;

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
        sb.AppendLine($"**{monate} Monatsabrechnungen, {werte} Vergleiche, {offen.Count} offene Abweichungen"
                      + $" ({alle.Count - offen.Count} bewusst).**");
        sb.AppendLine();
        sb.AppendLine("Nicht Teil dieser Nachrechnung (kommt im Lohnlauf aus der Datenbank): Stunden, Verträge,");
        sb.AppendLine("Saldi, Ferien-/Feiertag-Tage, 13.-ML-Rückstellung, Quellensteuer.");
        sb.AppendLine();
        sb.AppendLine("## Bekannte Grenzen des Nachrechners");
        sb.AppendLine();
        sb.AppendLine("Diese Punkte kann der Nachrechner nicht abbilden — die Abweichungen unten sind dort");
        sb.AppendLine("**kein** Befund gegen die Lohnrechnung, sondern eine Lücke des Prüfwerkzeugs:");
        sb.AppendLine();
        sb.AppendLine("- **Nachzahlung nach Austritt** (TF07 Burri Jan/Feb): die Engine rechnet sie über die");
        sb.AppendLine("  Anstellungsmonate des Austrittsjahres ab (Alter, Sätze und Höchstlöhne per Austrittsmonat,");
        sb.AppendLine("  `CalculateCorrectionAsync`). Der Nachrechner kennt nur das laufende Jahr.");
        sb.AppendLine("- **Monate ohne Lohnart** werden übersprungen; im echten Lohnlauf zählen sie als");
        sb.AppendLine("  Beschäftigungsmonat für den kumulierten Höchstlohn (betrifft Ein-/Austrittsmonate).");
        sb.AppendLine("- **Austritt und Wiedereintritt im selben Jahr** (TF40, TF41) bildet der Nachrechner nur");
        sb.AppendLine("  über ein Vertragsfenster ab.");
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

        var bewusst = alle.Except(offen).ToList();
        if (bewusst.Count > 0)
        {
            sb.AppendLine("## Bewusste Abweichungen (Abweichungsprotokoll)");
            sb.AppendLine();
            sb.AppendLine("| Testfall | Monat | Prüfung | OneCrew | Swissdec |");
            sb.AppendLine("|---|---|---|---:|---:|");
            foreach (var b in bewusst.OrderBy(b => b.Tf, StringComparer.Ordinal).ThenBy(b => b.Monat, StringComparer.Ordinal))
                sb.AppendLine($"| {b.Tf} {b.Name} | {b.Monat} | {b.Was} | {b.Wir.ToString("N2", CultureInfo.InvariantCulture)} "
                            + $"| {b.Swissdec.ToString("N2", CultureInfo.InvariantCulture)} |");
        }

        var ziel = Path.Combine(TestmandantDaten.RepoRoot, "SWISSCEC", "Abgleich");
        Directory.CreateDirectory(ziel);
        File.WriteAllText(Path.Combine(ziel, "nachrechnung.md"), sb.ToString());
    }
}
