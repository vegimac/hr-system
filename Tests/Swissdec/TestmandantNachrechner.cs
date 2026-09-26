using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;

namespace HrSystem.Tests.Swissdec;

/// <summary>
/// Rechnet die Muster-AG-Testfälle OFFLINE nach: Lohnarten aus der CSV → SV-Basen →
/// echte <see cref="PayrollCalculations.BuildResult"/> → Vergleich mit der Swissdec-RefXML.
/// Ohne Datenbank, ohne Testinstanz, ohne bestätigte Lohnläufe (Walter 26.09.2026).
///
/// Geprüft wird die ABZUGSSEITE: SV-Basen je Versicherung (inkl. Höchstlohn, Lohnband,
/// Aufrollung über die Beschäftigungsmonate), AHV-Freibetrag ab Referenzalter samt
/// Verzicht, Versicherungs-Codes (UVG A0/A1/A2/A3, UVGZ/KTG 10/11/12) und deren
/// Wechsel unter dem Jahr.
///
/// NICHT geprüft (kommt im Lohnlauf aus der Datenbank): Stunden, Verträge, Saldi,
/// Ferien-/Feiertag-Tage, 13.-ML-Rückstellung, Quellensteuer. Dafür braucht es
/// weiterhin einen echten Lohnlauf.
/// </summary>
public static class TestmandantNachrechner
{
    public sealed record Zeile(string Tf, string Name, string Monat, string Art,
                               decimal Basis, decimal Betrag);

    public sealed record MonatsErgebnis(string Tf, string Name, int Jahr, int Monat,
                                        decimal TotalLohn, List<Zeile> Abzuege,
                                        bool NbuBeimAn, bool AhvPflichtig)
    {
        public decimal Betrag(string art) => Abzuege.Where(a => a.Art == art).Sum(a => a.Betrag);
        public decimal Basis(string art)  => Abzuege.Where(a => a.Art == art).Sum(a => a.Basis);
        /// <summary>AHV + ALV + ALVZ + NBU — das, was Swissdec als «SocialContributions» meldet.</summary>
        public decimal SozialAbzuege => Betrag("AHV") + Betrag("ALV") + Betrag("ALVZ") + Betrag("NBUV");
    }

    private static readonly CompanyProfile Firma = new()
    {
        Id = 1, CompanyName = "Muster AG", BranchName = "Muster AG",
        Street = "Bahnhofstrasse", HouseNumber = "1", ZipCode = "6003", City = "Luzern",
    };

    private static SaldoBlock LeerSaldo() => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0, VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: 0, PrevThirteenth: 0, Basis13ml: 0);

    /// <summary>Alle Monate eines Testfalls der Reihe nach — die Aufrollung braucht die Vormonate.</summary>
    public static List<MonatsErgebnis> RechneJahr(
        string tf, int jahr,
        TestmandantDaten.Person stamm,
        SortedDictionary<string, Dictionary<string, string>>? mutationen,
        SortedDictionary<string, Dictionary<string, decimal>> lohnarten,
        Dictionary<string, TestmandantDaten.Lohnart> katalog,
        TestmandantDaten.Firmensaetze saetze)
    {
        var ergebnisse = new List<MonatsErgebnis>();
        // Ungedeckelte Monatsbasen der Vormonate — je Versicherungsart getrennt
        // (genau wie payroll_snapshot.sv_basis_ahv / _nbuv / _ktg).
        var ytdAhv = new List<decimal>();
        var ytdNbuv = new List<decimal>();
        var ytdKtg = new List<decimal>();
        var ytdMonate = new HashSet<int>();

        foreach (var (monatsSchluessel, arten) in lohnarten.Where(x => x.Key.StartsWith(jahr.ToString(), StringComparison.Ordinal)))
        {
            int monat = int.Parse(monatsSchluessel[5..7]);
            var person = TestmandantDaten.StandPer(stamm, mutationen, monatsSchluessel);
            var (periodFrom, periodTo) = PayrollCalculations.CalcPeriod(jahr, monat);

            // ── Basen aus den Lohnarten (Flags des Swissdec-Katalogs) ──────
            decimal Summe(Func<TestmandantDaten.Lohnart, bool> flag)
                => arten.Where(a => katalog.ContainsKey(a.Key) && flag(katalog[a.Key])).Sum(a => a.Value);

            decimal brutto = Summe(l => l.Brutto);
            var basen = new SvBases(Summe(l => l.Ahv), Summe(l => l.Uvg), Summe(l => l.Ktg),
                                    Summe(l => l.Bvg), Summe(l => l.Qst));
            decimal uvgzBasis = Summe(l => l.Uvgz);

            var eintritt = person.Datum("PersonEntryDate");
            var austritt = person.Datum("PersonWithdrawalDate");
            var vertrag = new Employment
            {
                Id = 1, EmployeeId = 1, EmploymentModel = "FIX", IsActive = true,
                ContractStartDate = (eintritt ?? new DateOnly(jahr, 1, 1)).ToDateTime(TimeOnly.MinValue),
                ContractEndDate = austritt?.ToDateTime(TimeOnly.MinValue),
            };
            var mitarbeiter = new Employee
            {
                Id = 1, FirstName = person.W("PersonFirstname") ?? "", LastName = person.W("PersonLastname") ?? person.Tf,
                Street = "Test", ZipCode = "6003", City = "Luzern",
                Gender = (person.W("PersonSex") ?? "M").StartsWith("F", StringComparison.OrdinalIgnoreCase) ? "F" : "M",
                DateOfBirth = person.Datum("PersonDateOfBirth")?.ToDateTime(TimeOnly.MinValue),
            };
            mitarbeiter.Employments.Add(vertrag);

            decimal monateBisher = PayrollCalculations.BeschaeftigungsMonate(mitarbeiter.Employments, jahr, 1, monat - 1, ytdMonate);
            decimal monateTotal  = monateBisher + PayrollCalculations.BeschaeftigungsMonate(mitarbeiter.Employments, jahr, monat, monat);

            bool ueberReferenzalter = mitarbeiter.DateOfBirth.HasValue
                && PayrollCalculations.HatReferenzalterErreicht(mitarbeiter.Gender, mitarbeiter.DateOfBirth.Value, jahr, monat);
            bool verzichtFreibetrag = !string.IsNullOrWhiteSpace(person.W("PersonWaiveOfPensionDeduct"));

            // AHV/ALV entfallen ganz: unter 18 (beitragspflichtig erst ab dem 1.1. nach
            // dem 17. Geburtstag, AHVG Art. 3 — TF13 Combertaldi) und bei Swissdecs
            // Sonderfall-Flag (TF14 Egli, TF39 Hasler: Lohn wird als «AHV-AVS-Open»
            // gemeldet, kein Beitrag).
            bool sonderfallAhv = !string.IsNullOrWhiteSpace(person.W("PersonAHVALVSpecialCase"));
            bool unter18 = mitarbeiter.DateOfBirth is { } geb && jahr <= geb.Year + 17;
            bool ahvPflichtig = !sonderfallAhv && !unter18;

            var regeln = BaueRegeln(person, saetze, ueberReferenzalter, verzichtFreibetrag,
                                    ahvPflichtig, ytdNbuv, ytdKtg, periodFrom);
            bool nbuBeimAn = regeln.Any(r => r.CategoryCode == "NBUV");

            // AHV-Freibetrag kumuliert: Basen der Vormonate ab Referenzalter.
            decimal? freibetragYtd = null; int freibetragMonate = 0;
            if (ueberReferenzalter && mitarbeiter.DateOfBirth.HasValue)
            {
                var monateMitFreibetrag = ergebnisse
                    .Where(e => PayrollCalculations.HatReferenzalterErreicht(mitarbeiter.Gender, mitarbeiter.DateOfBirth.Value, jahr, e.Monat))
                    .ToList();
                freibetragYtd = monateMitFreibetrag.Sum(e => e.Basis("AHV_ROH"));
                freibetragMonate = monateMitFreibetrag.Count;
            }

            var roh = PayrollCalculations.BuildResult(
                mitarbeiter, vertrag, Firma, jahr, monat, periodFrom, periodTo,
                new List<object>(), new List<object>(), regeln, brutto, basen,
                new List<object>(), 0, new List<object>(), 0, new List<object>(), 0, LeerSaldo(),
                new List<EmployeeLohnAssignment>(), new List<EmployeeBankAccount>(),
                ytdSvBasesDezember: ytdAhv.Count > 0 ? new List<decimal>(ytdAhv) : new List<decimal>(),
                ausgleichMonate: monateTotal,
                ausgleichMonateBisher: monateBisher,
                ahvFreibetragYtdBasen: freibetragYtd,
                ahvFreibetragMonateBisher: freibetragMonate);

            var zeilen = LiesAbzuege(roh, tf, person.Name, monatsSchluessel);
            // Rohbasis merken (für den kumulierten AHV-Freibetrag der Folgemonate).
            zeilen.Add(new Zeile(tf, person.Name, monatsSchluessel, "AHV_ROH", basen.Ahv, 0m));

            ergebnisse.Add(new MonatsErgebnis(tf, person.Name, jahr, monat, brutto, zeilen, nbuBeimAn, ahvPflichtig));

            ytdAhv.Add(basen.Ahv);
            ytdNbuv.Add(basen.Nbuv);
            ytdKtg.Add(basen.Ktg);
            ytdMonate.Add(monat);
        }
        return ergebnisse;
    }

    /// <summary>Abzugsregeln aus Firmensätzen + Versicherungs-Codes der Person.</summary>
    private static List<DeductionRule> BaueRegeln(
        TestmandantDaten.Person person, TestmandantDaten.Firmensaetze s,
        bool ueberReferenzalter, bool verzichtFreibetrag, bool ahvPflichtig,
        List<decimal> ytdNbuv, List<decimal> ytdKtg, DateOnly ab)
    {
        var regeln = new List<DeductionRule>();
        DeductionRule Neu(string code, string name, decimal satz, decimal? cap = null, decimal? von = null,
                          List<decimal>? ytd = null)
            => new()
            {
                Id = -regeln.Count - 1, CompanyProfileId = 1, CategoryCode = code, CategoryName = code, Name = name,
                Type = "percent", Rate = satz, BasisType = "gross", IsActive = true, ValidFrom = ab,
                MaxBaseMonthly = cap, BandVonMonthly = von,
                YtdBasenEigen = ytd is { Count: > 0 } ? new List<decimal>(ytd) : null,
            };

        // AHV: ab Referenzalter mit Freibetrag (ausser bei Verzicht des MA).
        if (ahvPflichtig)
        {
            var ahv = Neu("AHV", "AHV / IV / EO", s.AhvSatz);
            if (ueberReferenzalter)
            {
                ahv.Name = "AHV / IV / EO (65+)";
                if (verzichtFreibetrag) ahv.AhvFreibetragVerzicht = true;
                else ahv.FreibetragMonthly = Math.Round(s.AhvFreibetragJahr / 12m, 2);
            }
            regeln.Add(ahv);
        }

        // ALV/ALVZ entfallen ab Referenzalter.
        if (ahvPflichtig && !ueberReferenzalter)
        {
            regeln.Add(Neu("ALV", "Arbeitslosenversicherung", s.AlvSatz, cap: s.AlvLimit / 12m));
            regeln.Add(Neu("ALVZ", "ALV Solidaritätsprozent", s.AlvzSatz, cap: s.AlvzLimit / 12m, von: s.AlvLimit / 12m));
        }

        // UVG: zweite Stelle des Codes = NBU-Abzug beim AN (A1 ja, A0/A2/A3 nein).
        var uvgCode = person.W("PersonUVGLAACode") ?? "";
        if (uvgCode.Length >= 2 && uvgCode[1] == '1'
            && s.NbuSatz.TryGetValue(uvgCode[..1], out var nbuSatz))
        {
            regeln.Add(Neu("NBUV", $"UVG Betriebsteil {uvgCode[..1]} — BU+NBU, NBU-Abzug AN",
                           nbuSatz, cap: s.UvgLimit / 12m, ytd: ytdNbuv));
        }

        // UVGZ und KTG: je Code ein Lohnband (10 = nicht versichert).
        foreach (var (feld, loesungen, kategorie, ytd) in new[]
                 {
                     ("PersonUVGZLAACCode", s.Uvgz, "UVGZ", ytdNbuv),
                     ("PersonKTGAMCCode",   s.Ktg,  "KTG",  ytdKtg),
                 })
        {
            foreach (var nr in new[] { "1", "2" })
            {
                var code = person.W(feld + nr);
                if (code == null) continue;
                code = code.Split('.')[0].Trim();
                if (code == "10" || !loesungen.TryGetValue(code, out var l) || l.SatzMann + l.SatzFrau == 0m) continue;
                var name = $"{kategorie} {code}";
                regeln.Add(Neu(kategorie, name, l.Satz(person.W("PersonSex")),
                               cap: l.BisMonat > 0 ? l.BisMonat : null,
                               von: l.VonMonat > 0 ? l.VonMonat : null,
                               ytd: ytd));
            }
        }
        return regeln;
    }

    private static List<Zeile> LiesAbzuege(object roh, string tf, string name, string monat)
    {
        var zeilen = new List<Zeile>();
        var felder = roh.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(roh), StringComparer.OrdinalIgnoreCase);
        if (felder.GetValueOrDefault("abzugLines") is not System.Collections.IEnumerable liste) return zeilen;
        foreach (var l in liste)
        {
            var f = l.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(l), StringComparer.OrdinalIgnoreCase);
            var art = f.GetValueOrDefault("categoryCode") as string ?? "";
            decimal basis  = f.GetValueOrDefault("basis")  is decimal b ? b : 0m;
            decimal betrag = f.GetValueOrDefault("betrag") is decimal x ? x : 0m;
            // Abzüge stehen negativ im Slip; hier als positive Belastung führen.
            zeilen.Add(new Zeile(tf, name, monat, art, basis, -betrag));
        }
        return zeilen;
    }
}
