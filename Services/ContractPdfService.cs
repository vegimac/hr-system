using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace HrSystem.Services;

public record ContractPdfInput(
    string CompanyName,
    string CompanyAddress,
    string WorkLocation,
    string SignatoryName,
    string SignatoryTitle,
    string SignatureCity,
    DateTime ContractDate,
    float? DefaultVacationWeeks,
    string Salutation,
    string FirstName,
    string LastName,
    DateTime? DateOfBirth,
    string? EmployeeStreet,
    string? EmployeeZipCity,
    string EmploymentModel,
    string SalaryType,
    string JobTitle,
    string? ContractType,
    DateTime ContractStartDate,
    DateTime? ContractEndDate,
    int? ProbationMonths,
    decimal? MonthlySalary,
    decimal? MonthlySalaryFte,
    decimal? HourlyRate,
    decimal? EmploymentPercentage,
    decimal? WeeklyHours,
    decimal? GuaranteedHoursPerWeek,
    decimal? VacationPercent,
    decimal? HolidayPercent,
    decimal? ThirteenthSalaryPercent,
    string? Gender
);

public class ContractPdfService
{
    private const string Yellow = "#FFC72C";
    private const string Dark   = "#27251F";

    private static byte[]? _bannerBytes;

    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    private static string CHF(decimal? v)
    {
        if (v == null) return "0.00";
        long i = (long)Math.Floor(v.Value);
        int  d = (int)Math.Round((v.Value - i) * 100);
        return i.ToString("N0", CultureInfo.InvariantCulture).Replace(",", "'") + "." + d.ToString("00");
    }

    public byte[] Generate(ContractPdfInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        bool isUTP  = d.EmploymentModel == "UTP";
        bool isMTP  = d.EmploymentModel == "MTP";
        bool isFix  = d.EmploymentModel == "FIX";
        bool isFixM = d.EmploymentModel == "FIX-M";
        bool isFixed  = d.ContractEndDate.HasValue;
        bool hasProba = d.ProbationMonths is > 0;
        // Ferienwochen basierend auf Alter am Vertragsbeginn
        int vacWeeks;
        if (d.DateOfBirth.HasValue)
        {
            var age = d.ContractStartDate.Year - d.DateOfBirth.Value.Year;
            if (d.ContractStartDate < d.DateOfBirth.Value.AddYears(age)) age--;
            vacWeeks = age >= 50 ? 6 : (int)(d.DefaultVacationWeeks ?? 5);
        }
        else
        {
            vacWeeks = (int)(d.DefaultVacationWeeks ?? 5);
        }
        int vacDays = vacWeeks * 7;
        string emp    = $"{d.Salutation} {d.FirstName} {d.LastName}".Trim();

        decimal pct        = d.EmploymentPercentage ?? 100m;
        decimal fixWeeklyH = Math.Round(pct / 100m * (decimal)(d.WeeklyHours ?? 42m), 1);
        float sizeTitle = 10f;
        float sizeText  = 9.5f;

        string titleText = isFixM
            ? "Arbeitsvertrag f\u00fcr Mitarbeiter* im Restaurant Management ( Vollzeit )"
            : isFix
                ? "Arbeitsvertrag f\u00fcr Mitarbeiter* im Festpensum ( Vollzeit/Teilzeit )"
                : isMTP
                    ? "Arbeitsvertrag f\u00fcr Mitarbeiter* ( Garantiertes Mindest-Teilzeitpensum )"
                    : "Arbeitsvertrag f\u00fcr Mitarbeiter* im Stundenlohn ( Teilzeit )";

        string footerBase = isFixM ? "Vollzeitvertrag Management"
            : isFix ? "Vollzeitvertrag"
            : "Vertrag im Stundenlohn";

        bool isMinor = d.DateOfBirth.HasValue &&
            DateTime.Today < d.DateOfBirth.Value.AddYears(18);

        return Document.Create(container =>
        {
            // ══════════════════════════════════════════════
            // SEITE 1 – Abschnitte 1–7
            // ══════════════════════════════════════════════
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0.5f, Unit.Centimetre);
                page.MarginBottom(0.5f, Unit.Centimetre);
                page.MarginHorizontal(1.8f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(isMTP ? 8f : 11f).LineHeight(isMTP ? 1.05f : 1.3f).FontColor(Dark));

                // Header: Titel wird ÜBER das Banner-Bild gelegt
                // NACHHER:
                page.Header().Height(38).Layers(layers =>
                {
                    layers.Layer().Image(BannerBytes).FitWidth();
                    layers.PrimaryLayer()
                        .PaddingHorizontal(10)
                        .PaddingTop(10)
                        .Text(titleText).Bold().FontSize(11f).FontColor(Dark);
                });

                page.Content().PaddingTop(isMTP ? 1 : 6).Column(col =>
                {
                    // Parteien
                    col.Item().PaddingBottom(4).Column(p =>
                    {
                        p.Item().Text("zwischen").Italic();
                        p.Item().PaddingTop(2).Row(r =>
                        {
                            r.RelativeItem().Text(t =>
                            {
                                t.Span(d.CompanyName).Bold().FontSize(9.5f);
                                if (!string.IsNullOrWhiteSpace(d.CompanyAddress))
                                    t.Span($"   {d.CompanyAddress}").FontSize(7.5f);
                            });
                            r.AutoItem().PaddingLeft(8).Text("(Arbeitgeber)").Italic().FontSize(8f);
                        });
                        p.Item().PaddingTop(2).Text("und").Italic();
                        p.Item().PaddingTop(2).Row(r =>
                        {
                            r.RelativeItem().Text(t =>
                            {
                                t.Span(emp).Bold().FontSize(9.5f);
                                var addrParts = new[] { d.EmployeeStreet, d.EmployeeZipCity }
                                    .Where(s => !string.IsNullOrWhiteSpace(s));
                                var addr = string.Join(", ", addrParts);
                                if (!string.IsNullOrWhiteSpace(addr))
                                    t.Span($"   {addr}").FontSize(7.5f);
                            });
                            r.AutoItem().PaddingLeft(8).Text("(Mitarbeiter)").Italic().FontSize(8f);
                        });
                    });

                    // 1.
                    col.Item().Element(c => T(c, "1. Arbeitsort/Funktion/Stellenantritt"));
                    if (isFixM)
                    {
                        col.Item().Element(c => P(c,
                            $"Der Mitarbeiter wird an seinem Arbeitsort im Restaurant in {d.WorkLocation}, " +
                            $"eingesetzt als \u2018{d.JobTitle}\u2019. Als Mitglied des McDonald's Managements ist der Mitarbeiter " +
                            "einverstanden, je nach Bedarf, weitere seiner Funktion angemessene Aufgaben zu \u00fcbernehmen " +
                            "und unter Ber\u00fccksichtigung der pers\u00f6nlichen und famili\u00e4ren Verh\u00e4ltnisse " +
                            "an einem anderen Arbeitsort oder an einem anderen Restaurant eingesetzt zu werden."));
                    }
                    else if (isMTP || isUTP)
                    {
                        col.Item().Element(c => P(c,
                            $"Der Mitarbeiter wird an seinem Arbeitsort im Restaurant in {d.WorkLocation}, " +
                            $"eingesetzt als \u2018{d.JobTitle}\u2019. Als Mitglied der Crew k\u00f6nnen " +
                            "ihm alle Arbeitsstationen im Rahmen des Rotationsprinzips zugewiesen werden. " +
                            "Darin inbegriffen sind interne und externe Reinigungs- und Aufr\u00e4umarbeiten. " +
                            "Der Mitarbeiter kann vom Arbeitgeber im zumutbaren Rahmen in einem anderen Restaurant eingesetzt werden."));
                    }
                    else if (isFix)
                    {
                        col.Item().Element(c => P(c,
                            $"Der Mitarbeiter wird an seinem Arbeitsort im Restaurant in {d.WorkLocation}, " +
                            $"eingesetzt als \u2018{d.JobTitle}\u2019. Als Mitglied der Crew k\u00f6nnen " +
                            "ihm alle Arbeitsstationen im Rahmen des Rotationsprinzips zugewiesen werden. " +
                            "Darin inbegriffen sind interne und externe Reinigungs- und Aufr\u00e4umarbeiten. " +
                            "Der Mitarbeiter kann vom Arbeitgeber im zumutbaren Rahmen in einem anderen Restaurant eingesetzt werden."));
                    }
                    else
                    {
                        col.Item().Element(c => P(c,
                            $"Der Mitarbeiter wird an seinem Arbeitsort im Restaurant in {d.WorkLocation}, " +
                            $"eingesetzt als \u2018{d.JobTitle}\u2019. Als Mitglied des McDonald'sManagements ist der Mitarbeiter " +
                            "einverstanden, je nach Bedarf, weitere seiner Funktion angemessene Aufgaben zu \u00fcbernehmen " +
                            "und unter Ber\u00fccksichtigung der pers\u00f6nlichen und famili\u00e4ren Verh\u00e4ltnisse " +
                            "an einem anderen Arbeitsort oder an einem anderen Restaurant eingesetzt zu werden."));
                    }
                    col.Item().Element(c => P(c,
                        $"Der Mitarbeiter tritt seine Stelle am {d.ContractStartDate:dd.MM.yyyy} an."));
                    col.Item().Element(c => P(c,
                        "Falls der Mitarbeiter eine Arbeitsbewilligung ben\u00f6tigt, tritt dieser Vertrag erst in Kraft, " +
                        "wenn diese definitiv erteilt ist."));

                    // 2.
                    col.Item().Element(c => T(c, "2. Vertragsdauer"));
                    col.Item().Element(c => P(c, isFixed
                        ? $"Es wird ein befristeter Arbeitsvertrag abgeschlossen. Er dauert, ohne jegliche schriftliche " +
                          $"Vereinbarung, bis l\u00e4ngstens am {d.ContractEndDate:dd.MM.yyyy}, ist aber w\u00e4hrend " +
                          "der Vertragsdauer gem\u00e4ss Ziff. 3 und 4 k\u00fcndbar."
                        : "Es wird ein unbefristeter Arbeitsvertrag abgeschlossen."));

                    // 3.
                    col.Item().Element(c => T(c, "3. Probezeit"));
                    col.Item().Element(c => P(c, hasProba
                        ? $"Die Probezeit betr\u00e4gt {d.ProbationMonths} Monat" +
                          (d.ProbationMonths == 1 ? "" : "e") +
                          ", die K\u00fcndigungsfrist betr\u00e4gt 3 Kalendertage. " +
                          "Die Probezeit beginnt am tats\u00e4chlich geleisteten ersten Arbeitstag im Restaurant."
                        : "Es gilt keine Probezeit."));

                    // 4.
                    col.Item().Element(c => T(c, "4. K\u00fcndigung"));
                    col.Item().Element(c => P(c,
                        "Nach Ablauf der Probezeit betr\u00e4gt die K\u00fcndigungsfrist 1 Monat auf ein Monatsende " +
                        "im ersten bis f\u00fcnften Arbeitsjahr, und ab dem sechsten Arbeitsjahr mit einer Frist von " +
                        "2 Monaten. Im \u00dcbrigen wird dazu auf den L-GAV sowie auf das entsprechende Kapitel in " +
                        "den \u201eAllgemeinen Arbeitsbedingungen\u201c verwiesen."));

                    // 5.
                    col.Item().Element(c => T(c, isMTP ? "5. Arbeitszeit" : "5. Arbeitszeit/Ferien/Feiertage"));
                    if (isFixM)
                    {
                        col.Item().Element(c => P(c,
                            $"Die durchschnittliche w\u00f6chentliche Arbeitszeit betr\u00e4gt {fixWeeklyH:0} " +
                            $"Stunden ({(pct >= 100m ? "Vollzeitpensum" : $"{pct:0}% Pensum")}) bei {vacWeeks} Wochen Ferien ({vacDays} Kalendertage). " +
                            "Ab dem Tag des 50. Geburtstages, erhalten die Mitarbeiter 6 Wochen Ferien (42 Kalendertage). " +
                            "Es werden j\u00e4hrlich 6 Feiertage gew\u00e4hrt."));
                        col.Item().Element(c => P(c,
                            "Diese Arbeitszeiten sind als Durchschnittswerte in einem Zeitraum von 12 Monaten bzw. der " +
                            "effektiven Dauer des Arbeitsverh\u00e4ltnisses einzuhalten und schliessen Mehr- und " +
                            "Minderstunden pro Woche nicht aus. Im Rahmen des Zumutbaren ist der Mitarbeiter " +
                            "verpflichtet, Mehrstunden zu leisten, beziehungsweise Vorarbeit zu akzeptieren."));
                        col.Item().Element(c => P(c,
                            "Mehrstunden sind w\u00e4hrend des Arbeitsverh\u00e4ltnisses sobald als m\u00f6glich 1:1 zu " +
                            "kompensieren, sp\u00e4testens innert 12 Monaten auf Anordnung des Arbeitgebers; dies gilt " +
                            "auch w\u00e4hrend der K\u00fcndigungsfrist und bei allf\u00e4lliger Freistellung."));
                        col.Item().Element(c => P(c,
                            "Ferien sind effektiv zu beziehen. Nur am Ende des Arbeitsverh\u00e4ltnisses werden noch " +
                            "nicht bezogene Ferientage ausbezahlt."));
                    }
                    else if (isFix)
                    {
                        col.Item().Element(c => P(c,
                            $"Die durchschnittliche w\u00f6chentliche Arbeitszeit betr\u00e4gt {fixWeeklyH:0} " +
                            $"Stunden ({(pct >= 100m ? "Vollzeitpensum" : $"{pct:0}% Pensum")}) bei {vacWeeks} Wochen Ferien ({vacDays} Kalendertage). " +
                            "Ab dem Tag des 50. Geburtstages erhalten die Mitarbeiter 6 Wochen Ferien (42 Kalendertage). " +
                            "Es werden j\u00e4hrlich 6 Feiertage gew\u00e4hrt."));
                        col.Item().Element(c => P(c,
                            "Diese Arbeitszeiten sind als Durchschnittswerte in einem Zeitraum von 12 Monaten bzw. der " +
                            "effektiven Dauer des Arbeitsverh\u00e4ltnisses einzuhalten und schliessen Mehr- und " +
                            "Minderstunden pro Woche nicht aus. Im Rahmen des Zumutbaren ist der Mitarbeiter " +
                            "verpflichtet, Mehrstunden zu leisten, beziehungsweise Vorarbeit zu akzeptieren."));
                        col.Item().Element(c => P(c,
                            "Mehrstunden sind w\u00e4hrend des Arbeitsverh\u00e4ltnisses sobald als m\u00f6glich 1:1 zu " +
                            "kompensieren, sp\u00e4testens innert 12 Monaten auf Anordnung des Arbeitgebers; dies gilt " +
                            "auch w\u00e4hrend der K\u00fcndigungsfrist und bei allf\u00e4lliger Freistellung."));
                        col.Item().Element(c => P(c,
                            "Ferien sind effektiv zu beziehen. Nur am Ende des Arbeitsverh\u00e4ltnisses werden noch " +
                            "nicht bezogene Ferientage ausbezahlt."));
                    }
                    else if (isMTP)
                    {
                        decimal fullWeeklyH = d.WeeklyHours ?? 42m;
                        decimal guarH       = d.GuaranteedHoursPerWeek ?? 0m;
                        decimal guarPct     = fullWeeklyH > 0 ? Math.Round(guarH / fullWeeklyH * 100m, 2) : 0m;
                        col.Item().Element(c => P(c,
                            $"Das garantierte w\u00f6chentliche Mindestteilzeitpensum betr\u00e4gt {guarH:0} Stunden " +
                            $"({guarPct:0.##}% Arbeitspensum), bei einem Vollzeitarbeitspensum von {fullWeeklyH:0} Stunden " +
                            $"bei {vacWeeks} Wochen Ferien ({vacDays} Kalendertage). Ab dem Tag des 50. Geburtstages, " +
                            "erhalten die Mitarbeiter 6 Wochen Ferien (42 Kalendertage). Es werden j\u00e4hrlich 6 Feiertage gew\u00e4hrt."));
                        col.Item().Element(c => P(c,
                            "Ferien sind effektiv zu beziehen. Nur am Ende des Arbeitsverh\u00e4ltnisses werden noch nicht " +
                            "bezogene Ferientage ausbezahlt. F\u00fcr das Mindestteilzeitpensum wie auch f\u00fcr jede \u00fcber " +
                            "das w\u00f6chentliche Mindestteilzeitpensum hinaus geleistete Arbeitsstunde berechnen sich " +
                            "Feriengeldanspr\u00fcche, welche wie die Feiertage durch Lohnzuschl\u00e4ge abgegolten werden."));
                        col.Item().Element(c => P(c,
                            "\u00dcbersteigen die Arbeitszeiten regelm\u00e4ssig innerhalb einer Periode von 12 Monaten das " +
                            "vertraglich festgelegte Mindestteilzeitpensum, ist der Arbeitgeber verpflichtet, in Absprache " +
                            "mit dem Mitarbeiter das Mindestpensum dem Durchschnitt der effektiv geleisteten Arbeitsstunden anzupassen."));
                        col.Item().Element(c => P(c,
                            "Arbeitet der Mitarbeiter ausnahmsweise weniger als das w\u00f6chentliche vertraglich festgelegte " +
                            "Mindestteilzeitpensum, hat er diese Minusstunden so rasch als m\u00f6glich, sp\u00e4testens " +
                            "innerhalb von 6 Monaten mit \u00fcber dem vertraglich vereinbarten w\u00f6chentlichen " +
                            "Mindestteilzeitpensum liegenden Eins\u00e4tzen auszugleichen. Andernfalls ist der Arbeitgeber " +
                            "berechtigt, diese Fehlzeit lohnm\u00e4ssig zu berechnen und vom f\u00e4lligen Lohn " +
                            "verrechnungsweise in Abzug zu bringen."));
                        col.Item().Element(c => P(c,
                            "\u00dcber das w\u00f6chentliche vertraglich festgelegte Mindestteilzeitpensum hinaus geleistete " +
                            "Arbeitsstunden werden in der Regel mit dem Normallohnansatz ausbezahlt. Der Mitarbeiter hat aber " +
                            "die M\u00f6glichkeit, im Einzelfall und auf schriftlich festgehaltene Weise, statt der Auszahlung " +
                            "dieser Mehrstunden die Kompensation dieser Mehrstunden mit Freizeit zu beantragen."));
                    }
                    else
                    {
                        col.Item().Element(c => P(c,
                            $"Das durchschnittliche Pensum soll vom Arbeitsvolumen niedrig und \u00fcberschaubar bleiben, " +
                            $"d.h. durchschnittlich innerhalb eines Kalenderjahres (bzw. der jeweiligen Vertragsdauer pro rata temporis) " +
                            $"max. {d.GuaranteedHoursPerWeek ?? d.WeeklyHours ?? 17m} Stunden pro Woche. Der Mitarbeiter ist damit einverstanden, " +
                            "dass bedingt durch die Betriebskultur und Betriebsnotwendigkeiten, der Arbeitgeber die Einsatzzeiten " +
                            "flexibel einzuteilen hat, ohne dass ein Anspruch auf gleichbleibende oder erh\u00f6hte Arbeitszeiten besteht."));
                    }

                    // 6.
                    col.Item().Element(c => T(c, "6. Arbeitszeitplanung"));
                    col.Item().Element(c => P(c,
                        "Die Einsatzzeiten werden im Arbeitsplan f\u00fcr 2 Wochen mindestens 2 Wochen im Voraus festgelegt."));
                    col.Item().Element(c => P(c,
                        "Die \u201eVerf\u00fcgbaren Arbeitszeiten\u201c des Mitarbeiters, insbesondere wenn das Arbeitspensum " +
                        "w\u00e4hrend der ganzen Vertragsdauer nur an bestimmten Wochentagen oder zu bestimmten Tageszeiten " +
                        "geleistet werden, wird dies gem\u00e4ss der Vereinbarung \u201eVerf\u00fcgbare Arbeitszeiten\u201c " +
                        "festgehalten. Jegliche \u00c4nderungen der vorgenannten Vereinbarung sind im dazu zur Verf\u00fcgung " +
                        "stehenden markt\u00fcblichen HR Reporting System zu beantragen und bei Zustimmung durch beide Parteien " +
                        "zu best\u00e4tigen."));
                    col.Item().Element(c => P(c,
                        "Aufgrund der Art des Betriebes ist der Mitarbeiter damit einverstanden, Abendarbeit, regelm\u00e4ssig " +
                        "wiederkehrende Sonn- und Feiertagsarbeit sowie Nachtarbeit zu leisten. F\u00fcr die geltenden " +
                        "Geld- und Zeitzuschl\u00e4ge wird auf das Reglement \u201eAllgemeine Arbeitsbedingungen\u201c verwiesen."));
                    col.Item().Element(c => P(c,
                        "Durch eine Mitarbeiterversammlung wurde die Nachtarbeit von 24 Uhr bis 7 Uhr festgelegt."));
                    col.Item().Element(c => P(c,
                        "Die vom Arbeitgeber gef\u00fchrte Arbeitszeitkontrolle ist mindestens einmal monatlich vom " +
                        "Mitarbeiter zu unterzeichnen oder sobald verf\u00fcgbar, elektronisch im markt\u00fcblichen " +
                        "HR Reporting System zu validieren."));

                    // 7.
                    if (isFix || isFixM)
                    {
                        col.Item().Element(c => T(c, "7. Lohn"));
                        if (d.MonthlySalary.HasValue)
                        {
                            // Werte direkt aus DB – keine Berechnung hier
                            bool isPartTime = pct < 100m;
                            col.Item().PaddingTop(2).Text(txt =>
                            {
                                txt.Span("Der feste Bruttolohn (ohne 13. Monatslohn) betr\u00e4gt CHF  ").Bold().FontSize(10f);
                                txt.Span(CHF(d.MonthlySalary)).Bold().FontSize(10f);
                                if (isPartTime && d.MonthlySalaryFte.HasValue)
                                    txt.Span($"  ({pct:0}% von {CHF(d.MonthlySalaryFte)})").Bold().FontSize(9f);
                                txt.Span("  pro Monat.").Bold().FontSize(10f);
                            });
                        }
                        col.Item().Element(c => P(c,
                            "Der 13. Monatslohn und die Lohnabz\u00fcge richten sich nach Art. 12 und 13 L-GAV sowie " +
                            "Kapitel 7.4 des Reglements \u201eAllgemeinen Arbeitsbedingungen\u201c. Der Anspruch auf den " +
                            "13. Monatslohn entf\u00e4llt, wenn das Arbeitsverh\u00e4ltnis im Rahmen der Probezeit " +
                            "aufgel\u00f6st wird. Die Lohnauszahlung mit \u00fcbersichtlicher Lohnabrechnung erfolgt " +
                            "monatlich sp\u00e4testens am 6. Tag des folgenden Monats."));
                        col.Item().Element(c => P(c,
                            "Kinderzulagen werden gem\u00e4ss den gesetzlichen Bestimmungen ausgerichtet."));
                    }
                    else // UTP / MTP
                    {
                        col.Item().Element(c => T(c, "7. Stundenlohn/Ferien/Feiertage"));
                        col.Item().Element(c => P(c,
                            "Die Entl\u00f6hnung erfolgt im Stundenlohn ber\u00fccksichtigend die unregelm\u00e4ssige " +
                            "und vom Arbeitszeitvolumen niedrige sowie \u00fcberschaubare Teilzeitarbeit. " +
                            "Die Lohnauszahlung erfolgt anhand der geleisteten Arbeitsstunden."));
                        col.Item().Element(c => P(c,
                            $"Zum Lohnansatz pro Stunde kommt der jeweilige Anteil f\u00fcr Ferien und Feiertage hinzu, " +
                            "was auch auf jeder Lohnabrechnung detailliert aufgelistet wird. " +
                            $"Die Mitarbeiter im Stundenlohn haben {vacWeeks} Wochen Ferien pro Jahr tats\u00e4chlich zu beziehen."));
                        if (d.HourlyRate.HasValue)
                        {
                            decimal base_    = d.HourlyRate.Value;
                            decimal vac_pct  = d.VacationPercent ?? 0m;
                            decimal hol_pct  = d.HolidayPercent ?? 0m;
                            decimal th_pct   = d.ThirteenthSalaryPercent ?? 0m;
                            decimal ferien   = Math.Round(base_ * vac_pct  / 100m, 2);
                            decimal feiertag = Math.Round(base_ * hol_pct  / 100m, 2);
                            decimal dreiz    = Math.Round((base_ + ferien + feiertag) * th_pct / 100m, 2);
                            decimal totMit   = base_ + ferien + feiertag + dreiz;
                            decimal totOhne  = base_ + feiertag + dreiz;
                            col.Item().PaddingTop(3).Table(tbl =>
                            {
                                tbl.ColumnsDefinition(c => { c.ConstantColumn(175); c.ConstantColumn(28); c.ConstantColumn(42); c.RelativeColumn(); });
                                void LohnRow(string desc, decimal val, string note = "", bool bold = false)
                                {
                                    if (bold) { tbl.Cell().Text(desc).Bold().FontSize(9f); tbl.Cell().Text("CHF").Bold().FontSize(9f); tbl.Cell().Text(CHF(val)).Bold().FontSize(9f); tbl.Cell().Text(note).FontSize(9f); }
                                    else      { tbl.Cell().Text(desc).FontSize(9f); tbl.Cell().Text("CHF").FontSize(9f); tbl.Cell().Text(CHF(val)).FontSize(9f); tbl.Cell().Text(note).Italic().FontSize(8f); }
                                }
                                LohnRow("Total brutto inkl. 13. Monatslohn", totMit, bold: true);
                                LohnRow("Total brutto ohne Ferienanteil", totOhne, "(regelm\u00e4ssig ausgezahltes Sal\u00e4r)");
                                LohnRow("Der Stundenlohn betr\u00e4gt :", base_);
                                LohnRow($"+ Ferien ({vac_pct:0.##}% f\u00fcr {vacWeeks} Wochen Ferien)", ferien);
                                LohnRow($"+ Feiertage ({hol_pct:0.##}%)", feiertag);
                                LohnRow($"+ 13. Monatslohn ({th_pct:0.##}%)", dreiz, "(wird erst nach der Probezeit angerechnet und ausbezahlt)");
                            });
                            if (isMTP && d.GuaranteedHoursPerWeek.HasValue)
                            {
                                decimal monthlyAmount = Math.Round(d.GuaranteedHoursPerWeek.Value * (52m / 12m) * totOhne, 2);
                                col.Item().PaddingTop(4).Text(
                                    "Der Bruttolohn f\u00fcr das w\u00f6chentliche Mindestteilzeitpensum (inkl. 13. Monatslohn) jedoch ohne Ferienanteil betr\u00e4gt");
                                col.Item().PaddingTop(1).Text(t =>
                                {
                                    t.Span("CHF   ").Bold().FontSize(10f);
                                    t.Span(CHF(monthlyAmount)).Bold().FontSize(10f);
                                    t.Span("   pro Monat ( umgerechnet in Stundenlohn CHF ").Bold().FontSize(10f);
                                    t.Span(CHF(totOhne)).Bold().FontSize(10f);
                                    t.Span(" )").Bold().FontSize(10f);
                                });
                            }
                            if (isUTP)
                            {
                                col.Item().PaddingTop(4).Element(c => P(c,
                                    $"Ferien auf dem Ferienkonto monatlich gutgeschrieben, {d.VacationPercent:0.##}% ({vacWeeks} Wochen Ferien) " +
                                    "vom Arbeitgeber zur\u00fcckbehalten und erst beim tats\u00e4chlichen Ferienbezug " +
                                    "(im Verh\u00e4ltnis von deren Dauer zum ganzen j\u00e4hrlichen Ferienanspruch) ausbezahlt."));
                            }
                            col.Item().PaddingTop(4).Element(c => P(c,
                                "Der 13. Monatslohn und die Lohnabz\u00fcge richten sich nach Art. 12 und 13 L-GAV sowie " +
                                "Kapitel 7.4 dem Reglement \u201eAllgemeinen Arbeitsbedigungen\u201c. Der Anspruch auf den " +
                                "13. Monatslohn entf\u00e4llt, wenn das Arbeitsverh\u00e4ltnis im Rahmen der Probezeit " +
                                "aufgel\u00f6st wird. Die Lohnauszahlung mit einer \u00fcbersichtlichen Lohnabrechnung erfolgt " +
                                "monatlich sp\u00e4testens am 6. Tag des folgenden Monats."));
                            col.Item().Element(c => P(c,
                                "Kinderzulagen werden gem\u00e4ss den gesetzlichen Bestimmungen ausgerichtet."));
                        }
                    }
                });
                page.Footer().AlignCenter().Text(
                    "*Der Begriff \"Mitarbeiter\" umfasst ebenfalls die Mitarbeiterinnen. " +
                    "Aus Gr\u00fcnden der Einfachheit wird auf eine Differenzierung.")
                    .FontSize(7).Italic();
            });

            // ══════════════════════════════════════════════
            // SEITE 2 – Abschnitte 8–12 + Unterschriften
            // ══════════════════════════════════════════════
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0.3f, Unit.Centimetre);
                page.MarginBottom(0.8f, Unit.Centimetre);
                page.MarginHorizontal(1.8f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9f).FontColor(Dark));
                page.Header().Image(BannerBytes).FitWidth();
                page.Content().PaddingTop(4).Column(col =>
                {
                    // MTP Mehrstunden
                    if (isMTP)
                    {
                        col.Item().Element(c => P(c,
                            "F\u00fcr jede dar\u00fcber hinaus geleistete Arbeitsstunde (vorbeh\u00e4ltlich Mehrstunden " +
                            "zwecks Kompensation der Minusstunden) werden:"));
                        col.Item().PaddingLeft(6).Element(c => P(c,
                            $"- CHF {CHF(d.HourlyRate)}  " +
                            "pro Stunde (gem\u00e4ss Normalstundenlohnansatz) plus die Zulagen wie oben ausbezahlt."));
                        col.Item().PaddingLeft(6).Element(c => P(c,
                            $"- Ferien auf dem Ferienkonto monatlich gutgeschrieben, {d.VacationPercent:0.##}% " +
                            $"({d.DefaultVacationWeeks ?? 5} Wochen Ferien) vom Arbeitgeber zur\u00fcckbehalten und erst " +
                            "beim tats\u00e4chlichen Ferienbezug (im Verh\u00e4ltnis von deren Dauer zum ganzen " +
                            "j\u00e4hrlichen Ferienanspruch) ausbezahlt."));
                        col.Item().PaddingLeft(6).PaddingBottom(3).Element(c => P(c,
                            $"- Der Anspruch auf Feiertage wird mit einem Lohnzuschlag von {d.HolidayPercent:0.##}% " +
                            "abgegolten und monatlich ausbezahlt (Art. 18 L-GAV)."));
                    }

                    // 8.
                    col.Item().Element(c => T(c, "8. Arbeitgeberweisungen, Hygiene"));
                    col.Item().Element(c => P(c,
                        "Der Mitarbeiter muss w\u00e4hrend der Arbeitszeit die an seinem Arbeitsort vorgeschriebene " +
                        "Kleidung/Uniform tragen. W\u00e4hrend den Arbeitspausen kann sich der Mitarbeiter am " +
                        "Arbeitsort auf seine Kosten verpflegen (verg\u00fcnstigte Mitarbeiterpreise)."));
                    col.Item().Element(c => P(c,
                        "Die allgemeinen McDonald's Richtlinien f\u00fcr Hygiene, Gesundheit und pers\u00f6nliches " +
                        "Verhalten am Arbeitsplatz werden dem Mitarbeiter nach Stellenantritt ausf\u00fchrlich " +
                        "erl\u00e4utert und sind zwingend einzuhalten. Erg\u00e4nzend wird auf das Restaurant Reglement verwiesen."));

                    // 9.
                    col.Item().Element(c => T(c, "9. Hinweise zu den Versicherungen"));
                    col.Item().Element(c => P(c,
                        "Alle gem\u00e4ss L-GAV vorgesehenen Versicherungsleistungen (Krankheit, medizinisch attestierte " +
                        "Schwangerschaftsbeschwerden, Mutterschaftsurlaub, Berufs- und Nichtberufsunfall, Milit\u00e4r-, " +
                        "Schutz- und Zivildienst, etc.) erfolgen gem\u00e4ss den jeweiligen Versicherungspolicen, " +
                        "gesetzlichen Bestimmungen und Merkbl\u00e4ttern."));
                    col.Item().Element(c => P(c,
                        "Die berufliche Vorsorge erfolgt gem\u00e4ss den gesetzlichen Bestimmungen und dem jeweiligen " +
                        "BVG-Reglement. Im \u00dcbrigen wird auf Artikel 22 bis 28 L-GAV verwiesen."));

                    // 10.
                    col.Item().Element(c => T(c, "10. Lohnersatz und Sozialversicherungen bei Arbeitsunf\u00e4higkeit"));
                    col.Item().Element(c => P(c,
                        "Bei Arbeitsunf\u00e4higkeit des Mitarbeiters infolge Krankheit bezahlt der Arbeitgeber den " +
                        "Lohn und die Versicherungsleistungen gem\u00e4ss Art. 23 L-GAV. Wird eine Mitarbeiterin " +
                        "w\u00e4hrend der Schwangerschaft medizinisch als arbeitsunf\u00e4hig erkl\u00e4rt, richten " +
                        "sich die Leistungen ebenfalls nach diesem Artikel."));
                    col.Item().Element(c => P(c,
                        "Bei unverschuldeter Arbeitsunf\u00e4higkeit infolge Unfalls bezahlt der Arbeitgeber den Lohn " +
                        "und die Versicherungsleistungen gem\u00e4ss Art. 25 L-GAV. Bei unterst\u00fctzungspflichtigen " +
                        "Mitarbeitern, welche einen Berufsunfall erleiden und dadurch unverschuldet arbeitsunf\u00e4hig " +
                        "werden, entrichtet der Arbeitgeber 100% des durchschnittlichen Lohnes gem\u00e4ss Art. 324a OR " +
                        "(Berner Skala)."));
                    col.Item().Element(c => P(c,
                        "Bei Milit\u00e4r- und Schutzdienst, Zivilschutz, werden die Leistungen gem\u00e4ss Art. 28 " +
                        "L-GAV gew\u00e4hrt."));

                    // 11.
                    col.Item().Element(c => T(c, "11. Meldepflichten, Arztzeugnis und Vertrauensarzt"));
                    col.Item().Element(c => P(c,
                        "Der Mitarbeiter orientiert die verantwortliche Person des Restaurants sofort bei " +
                        "gesundheitlichen Beeintr\u00e4chtigungen wie Fieber, Durchfall, Erbrechen, Wunden, " +
                        "Ekzemen usw."));
                    col.Item().Element(c => P(c,
                        "Bei Verhinderung an der Arbeitsleistung hat der Mitarbeiter den Arbeitgeber umgehend zu " +
                        "benachrichtigen. Bei einer Arbeitsverhinderung ist der Mitarbeiter verpflichtet, ab dem " +
                        "1. Tag ein \u00e4rztliches Zeugnis vorzulegen."));
                    col.Item().Element(c => P(c,
                        "Der Arbeitgeber oder die Versicherung ist berechtigt, auf eigene Kosten das Zeugnis eines " +
                        "Vertrauensarztes zu verlangen."));

                    // 12.
                    col.Item().Element(c => T(c, "12. Integrierende Bestandteile dieses Vertrages"));
                    col.Item().Element(c => P(c,
                        "Der Mitarbeiter wird ausdr\u00fccklich darauf hingewiesen, dass f\u00fcr diesen Vertrag der " +
                        "L-GAV Landesgesamtarbeitsvertrag des Gastgewerbes gilt, der in jedem Restaurant aufliegt " +
                        "(auch unter www.l-gav.ch). Die jeweiligen detaillierten Versicherungspolicen, arbeitsrechtlich " +
                        "relevante Gesetzesbestimmungen und Merkbl\u00e4tter k\u00f6nnen bei der verantwortlichen " +
                        "Person des Restaurants angefragt werden."));
                    col.Item().PaddingTop(3).Element(c => P(c,
                        "Weiter bilden folgende Bestimmungen einen integrierenden Bestandteil dieses Vertrages, " +
                        "in der jeweils g\u00fcltigen Form bzw."));
                    col.Item().PaddingLeft(6).Text("- die Vereinbarung \u201eVerf\u00fcgbare Arbeitszeiten\u201c");
                    col.Item().PaddingLeft(6).Text("- die Stellenbeschreibung \u201ejob profile\u201c");
                    col.Item().PaddingLeft(6).Text("- das Reglement \u201eallgemeine Arbeitsbedingungen\u201c");
                    col.Item().PaddingTop(4).Element(c => P(c,
                        "Zus\u00e4tzlich werden dem Mitarbeitenden folgende Weisungen, Richtlinien, Merk- oder " +
                        "Informationsbl\u00e4tter ausgeh\u00e4ndigt, in der jeweils g\u00fcltigen Form bzw. Version:"));
                    col.Item().PaddingLeft(6).Text("- die Hygienerichtlinien");
                    col.Item().PaddingLeft(6).Text("- die Richtlinie \u201eZum Schutz der pers\u00f6nlichen Integrit\u00e4t\u201c");
                    col.Item().PaddingLeft(6).Text("- McDonald's Franchise Privacy Notice bzw. die Human Ressources Datenschutzerkl\u00e4rung");
                    col.Item().PaddingLeft(6).Text("- das Versicherungsmerkblatt");
                    col.Item().PaddingLeft(6).Text("- das Informationsblatt \u201eMutterschutz\u201c");
                    col.Item().PaddingTop(4).Element(c => P(c,
                        "Dieser Vertrag ist beiden Parteien in einem gegenseitig unterzeichneten Exemplar " +
                        "ausgeh\u00e4ndigt worden. Der Mitarbeiter best\u00e4tigt ausdr\u00fccklich den Erhalt, " +
                        "die Kenntnisnahme und das Einverst\u00e4ndnis mit den integrierenden " +
                        "Vertragsbestandteilen sowie den ausgeh\u00e4ndigten Weisungen und Richtlinien."));

                    col.Item().PaddingTop(12).ShowEntire().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Der Arbeitgeber:").Bold();
                            c.Item().PaddingTop(2).Text($"{d.SignatureCity}, {d.ContractDate:dd.MM.yyyy}");
                            c.Item().PaddingTop(60).PaddingRight(20).LineHorizontal(0.5f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text(d.SignatoryName);
                            c.Item().Text(d.SignatoryTitle);
                        });
                        if (isMinor)
                        {
                            row.ConstantItem(150).Column(c =>
                            {
                                c.Item().Text("Unterschrift des Erziehungsberechtigten");
                                c.Item().PaddingTop(2).Text("Ort und Datum:");
                                c.Item().PaddingTop(49).PaddingRight(10).LineHorizontal(0.5f).LineColor(Dark);
                            });
                        }
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Der Mitarbeiter:").Bold();
                            c.Item().PaddingTop(2).Text("Ort und Datum:");
                            c.Item().PaddingTop(60).PaddingRight(20).LineHorizontal(0.5f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text($"{d.FirstName} {d.LastName}");
                        });
                    });
                });
                page.Footer().Column(f =>
                {
                    f.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                    f.Item().PaddingTop(2).AlignCenter()
                        .Text("*Der Begriff \"Mitarbeiter\" umfasst ebenfalls die Mitarbeiterinnen. " +
                              "Aus Gr\u00fcnden der Einfachheit wird auf eine Differenzierung.")
                        .FontSize(7).Italic();
                });
            });

            // ══════════════════════════════════════════════
            // SEITE 3 – Verfügbare Arbeitszeiten
            // ══════════════════════════════════════════════
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0.3f, Unit.Centimetre);
                page.MarginBottom(0.5f, Unit.Centimetre);
                page.MarginHorizontal(2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9f).FontColor(Dark));
                page.Header().Image(BannerBytes).FitWidth();
                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Table(tbl =>
                    {
                        tbl.ColumnsDefinition(c => { c.ConstantColumn(110); c.RelativeColumn(); });
                        void Row2(string l, string v)
                        {
                            tbl.Cell().PaddingBottom(3).Text(l);
                            tbl.Cell().PaddingBottom(3).Text(v);
                        }
                        Row2("Beilage zum Arbeitsvertrag vom:", d.ContractStartDate.ToString("dd.MM.yyyy"));
                        Row2("Arbeitgeber:", d.CompanyName);
                        Row2("Mitarbeiter*:", emp);
                        Row2("Restaurant:", d.WorkLocation);
                    });
                    col.Item().PaddingTop(8).Text(
                        "Wenn der Mitarbeiter vor hat, seine Stunden- und/oder Tagesverf\u00fcgbarkeit abzu\u00e4ndern, " +
                        "muss er im dazu zur Verf\u00fcgung stehenden markt\u00fcblichen HR Reporting System einen " +
                        "entsprechenden Antrag stellen, welchem der Arbeitgeber bei Einverst\u00e4ndnis zuzustimmen hat. " +
                        "Dies ersetzt die anf\u00e4nglich festgelegten Verf\u00fcgbarkeiten und bildet einen integrierenden " +
                        "Bestandteil des Vertrages. Jede \u00c4nderung der verf\u00fcgbaren Arbeitszeiten kann eine " +
                        "Ab\u00e4nderung des Durchschnitts, der normalerweise durch den Mitarbeiter absolvierten Stunden, " +
                        "mit sich bringen.");
                    col.Item().PaddingTop(8).Row(r =>
                    {
                        r.ConstantItem(14).Height(14).Border(1).BorderColor(Dark);
                        r.RelativeItem().PaddingLeft(4).AlignMiddle().Text("a) uneingeschr\u00e4nkte Verf\u00fcgbarkeit**");
                    });
                    col.Item().PaddingTop(3).Row(r =>
                    {
                        r.ConstantItem(14).Height(14).Border(1).BorderColor(Dark);
                        r.RelativeItem().PaddingLeft(4).AlignMiddle().Text("b) gem\u00e4ss unten stehender Tabelle:**");
                    });
                    col.Item().PaddingTop(6).Table(tbl =>
                    {
                        tbl.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(65);
                            for (int i = 0; i < 7; i++) c.RelativeColumn();
                        });
                        void H(string txt) =>
                            tbl.Cell().Background(Yellow).Border(0.5f).BorderColor(Dark)
                                .Padding(3).AlignCenter().Text(txt).Bold().FontSize(9.5f);
                        H("Zeit");
                        foreach (var day in new[] { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" })
                            H(day);
                        for (int r2 = 0; r2 < 7; r2++)
                        {
                            tbl.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Height(16);
                            for (int c2 = 0; c2 < 7; c2++)
                                tbl.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Height(16);
                        }
                    });
                    col.Item().PaddingTop(6).Row(r =>
                    {
                        r.ConstantItem(14).Height(14).Border(1).BorderColor(Dark);
                        r.RelativeItem().PaddingLeft(4).AlignMiddle().Text("a) G\u00fcltig f\u00fcr eine unbefristete Dauer**");
                    });
                    col.Item().PaddingTop(3).Row(r =>
                    {
                        r.ConstantItem(14).Height(14).Border(1).BorderColor(Dark);
                        r.RelativeItem().PaddingLeft(4).AlignMiddle().Text("b) G\u00fcltig f\u00fcr die Zeit vom**                    bis");
                    });
                    col.Item().PaddingTop(6).Text(
                        "Wenn der Mitarbeiter nicht in der Lage ist, die Arbeitsstunden w\u00e4hrend den Tagen, " +
                        "welche in diesem Dokument festgelegt sind, zu absolvieren, hat er nicht das Recht darauf, " +
                        "seine Stunden auf einen anderen Tag zu verschieben.");
                    col.Item().PaddingTop(4).Text(
                        "Die, auf der Basis der angegebenen verf\u00fcgbaren Arbeitsstunden geplanten Arbeitszeiten, " +
                        "respektieren, die unter allen Umst\u00e4nden einzuhaltenden, Vorgaben des Arbeitsrechts, " +
                        "Obligationenrechts sowie des L-GAV.");
                    col.Item().PaddingTop(4).Text(
                        "Die Mitarbeiter, die regelm\u00e4ssig nachts arbeiten (mehr als 25 N\u00e4chte pro Jahr \u2013 " +
                        "Arbeit \u00fcberschreitend 22:00, 23:00 Uhr oder 24:00 Uhr, je nachdem was im Restaurant des " +
                        "Mitarbeiters g\u00fcltig ist) haben das Recht, sich einer Arztkontrolle zu unterziehen, um die " +
                        "Gesundheitsst\u00f6rungen bei regelm\u00e4ssiger Nachtarbeit zu verhindern. Diese Kontrolle ist " +
                        "gesetzlich alle 2 Jahre f\u00fcr die Mitarbeiter bis zu 45 Jahren und jedes Jahr f\u00fcr " +
                        "diejenige \u00fcber 45 Jahre vorgesehen. Wenn der Mitarbeiter von diesem Recht profitieren " +
                        "m\u00f6chte, wird er gebeten, sich an die verantwortliche Person des Restaurants zu wenden. " +
                        "Bei regelm\u00e4ssiger Nachtarbeit ohne Wechsel mit Tagesarbeit ist eine medizinische Untersuchung " +
                        "und Beratung obligatorisch. Die Kosten einer medizinischen Untersuchung und Beratung wegen " +
                        "Nachtarbeit gehen zu Lasten des Arbeitgebers, sofern sie nicht von einer Versicherung " +
                        "\u00fcbernommen werden.");
                    col.Item().PaddingTop(6).Text("Bemerkungen").Bold();
                    col.Item().PaddingTop(4).Border(0.5f).BorderColor(Dark).MinHeight(90).Text("");
                    col.Item().PaddingTop(14).ShowEntire().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Der Arbeitgeber:").Bold();
                            c.Item().PaddingTop(2).Text($"{d.SignatureCity}, {d.ContractDate:dd.MM.yyyy}");
                            c.Item().PaddingTop(60).PaddingRight(20).LineHorizontal(0.5f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text(d.SignatoryName);
                            c.Item().Text(d.SignatoryTitle);
                        });
                        if (isMinor)
                        {
                            row.ConstantItem(150).Column(c =>
                            {
                                c.Item().Text("Unterschrift des Erziehungsberechtigten");
                                c.Item().PaddingTop(2).Text("Ort und Datum:");
                                c.Item().PaddingTop(49).PaddingRight(10).LineHorizontal(0.5f).LineColor(Dark);
                            });
                        }
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Der Mitarbeiter:").Bold();
                            c.Item().PaddingTop(2).Text("Ort und Datum:");
                            c.Item().PaddingTop(60).PaddingRight(20).LineHorizontal(0.5f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text($"{d.FirstName} {d.LastName}");
                        });
                    });
                    // NACHHER (im Footer):
                    page.Footer().Column(f =>
                    {
                        f.Item().PaddingTop(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Medium);
                        f.Item().PaddingTop(2)
                            .Text("*Der Begriff \u00abMitarbeiter\u00bb umfasst ebenfalls die Mitarbeiterinnen. " +
                                    "Aus Gr\u00fcnden der Einfachheit wird auf eine Differenzierung im Text verzichtet.")
                            .FontSize(7).Italic();
                    });
                });

            });

            // ══════════════════════════════════════════════
            // SEITE 4 – Informationsblatt Mutterschutz
            // NUR für weibliche Mitarbeitende
            // ══════════════════════════════════════════════
            if (d.Gender == "female")
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginTop(0.3f, Unit.Centimetre);
                    page.MarginBottom(1.5f, Unit.Centimetre);
                    page.MarginHorizontal(2f, Unit.Centimetre);
                    page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(sizeText).FontColor(Dark));
                    page.Header().Image(BannerBytes).FitWidth();
                    page.Content().PaddingTop(6).Column(col =>
                    {
                        col.Item().Element(c => T(c, "1. Information f\u00fcr Frauen \u201eim geb\u00e4rf\u00e4higen\u201c Alter", sizeTitle));
                        col.Item().Element(c => P(c, "Gem\u00e4ss gesetzlicher Regelung muss der Arbeitgeber Frauen im geb\u00e4rf\u00e4higen Alter bei Stellenantritt \u00fcber allf\u00e4llige arbeitsplatzbezogene Gefahren w\u00e4hrend einer Schwangerschaft informieren.", sizeText));
                        col.Item().Element(c => P(c, "Das Risiko einer Sch\u00e4digung des ungeborenen Kindes ist in den ersten drei Schwangerschaftsmonaten am gr\u00f6ssten. Wird eine Schwangerschaft vermutet oder nachgewiesen, sollte die Mitarbeiterin dies deshalb umgehend dem Vorgesetzten mitteilen, damit allf\u00e4llige Risiken bei der weiteren Besch\u00e4ftigung beurteilt und besprochen werden k\u00f6nnen.", sizeText));
                        col.Item().Element(c => P(c, "Kann im Falle einer Schwangerschaft eine gef\u00e4hrliche gesundheitliche Belastung f\u00fcr Mutter und Kind nur durch zus\u00e4tzliche Schutzmassnahmen ausgeschaltet werden, m\u00fcssen diese regelm\u00e4ssig \u00fcberpr\u00fcft werden. Stellt sich dabei heraus, dass das Schutzziel nicht erreicht wird, darf die betroffene Frau in diesem Bereich nicht mehr arbeiten.", sizeText));
                        col.Item().Element(c => P(c, "Zur Sicherung der Wirksamkeit der Schutzmassnahmen soll die behandelnde \u00c4rztin regelm\u00e4ssig den Gesundheitszustand der schwangeren Frau oder der stillenden Mutter \u00fcberpr\u00fcfen. Er teilt der betroffenen Arbeitnehmerin und dem Arbeitgeber das Ergebnis der Beurteilung mit, damit der Arbeitgeber n\u00f6tigenfalls die erforderlichen Massnahmen treffen kann.", sizeText));
                        col.Item().Element(c => P(c, "Bei gef\u00e4hrlichen oder beschwerlichen Arbeiten hat der Arbeitgeber eine schwangere Frau oder eine stillende Mutter an einen f\u00fcr sie ungef\u00e4hrlichen und gleichwertigen Arbeitsplatz zu versetzen.", sizeText));
                        col.Item().Element(c => P(c, "Schwangere Frauen und stillende M\u00fctter m\u00fcssen sich unter geeigneten Bedingungen hinlegen und ausruhen k\u00f6nnen. Hierf\u00fcr sollte mindestens eine Liege, wenn m\u00f6glich in einem ruhigen Raum vorhanden sein.", sizeText));
                        col.Item().Element(c => T(c, "2. Gef\u00e4hrdungen", sizeTitle));
                        col.Item().Element(c => P(c, "Gef\u00e4hrliche und beschwerliche Arbeiten. Als gef\u00e4hrliche und beschwerliche Arbeiten f\u00fcr schwangere Frauen und stillende M\u00fctter gelten alle Arbeiten, die sich erfahrungsgem\u00e4ss nachteilig auf die Gesundheit dieser Frauen und ihrer Kinder auswirken.", sizeText));
                        col.Item().Element(c => P(c, "Folgende Arbeiten gelten als gef\u00e4hrlich oder beschwerlich:", sizeText));
                        foreach (var b in new[] {
                            "Bewegen schwerer Lasten",
                            "Bewegungen und K\u00f6rperhaltungen, die zu vorzeitiger Erm\u00fcdung f\u00fchren",
                            "Arbeiten, die mit \u00e4usseren Krafteinwirkungen wie St\u00f6ssen, Ersch\u00fctterungen oder Vibrationen verbunden sind",
                            "Arbeiten bei K\u00e4lte, Hitze oder N\u00e4sse",
                            "Physikalische Risiken (L\u00e4rm, Strahlung, Druck)",
                            "Chemische Risiken",
                            "Biologische Risiken",
                            "Arbeiten in Arbeitszeitsystemen, die erfahrungsgem\u00e4ss zu einer starken Belastung f\u00fchren"
                        })
                            col.Item().PaddingLeft(8).Text($"\u2022  {b}").FontSize(sizeText);
                        col.Item().Element(c => T(c, "3. Besch\u00e4ftigungserleichterung", sizeTitle));
                        col.Item().Element(c => P(c, "Bei haupts\u00e4chlich stehend zu verrichtender T\u00e4tigkeit sind schwangeren Frauen ab dem vierten Schwangerschaftsmonat eine t\u00e4gliche Ruhezeit von 12 Stunden und nach jeder zweiten Stunde zus\u00e4tzlich zu den Pausen eine Kurzpause von 10 Minuten zu gew\u00e4hren. Ab dem sechsten Schwangerschaftsmonat sind stehende T\u00e4tigkeiten auf insgesamt 4 Stunden pro Tag zu beschr\u00e4nken.", sizeText));
                        col.Item().Element(c => T(c, "4. Zeitliche Regelungen", sizeTitle));
                        col.Item().PaddingTop(2).Text("Absolutes Besch\u00e4ftigungsverbot:").FontSize(sizeText);
                        foreach (var b in new[] {
                            "Eine schwangere Frau darf nicht mehr als 9 Arbeitsstunden pro Tag arbeiten.",
                            "Keine Besch\u00e4ftigung nach der Geburt bis 8 Wochen nach der Entbindung.",
                            "Keine Abend- und Nachtarbeit (20.00 \u2013 6.00 Uhr) 8 Wochen vor dem errechneten Geburtstermin."
                        })
                            col.Item().PaddingLeft(8).Text($"\u2022  {b}").FontSize(sizeText);
                        col.Item().Element(c => T(c, "5. Zus\u00e4tzliche Auflagen:", sizeTitle));
                        col.Item().PaddingLeft(8).Text("\u2022  W\u00e4hrend der Schwangerschaft und der 9.\u201316. Woche nach der Entbindung sowie w\u00e4hrend der Stillzeit ist eine Besch\u00e4ftigung nur mit dem Einverst\u00e4ndnis der werdenden oder stillenden Mutter m\u00f6glich. Auf ihr Verlangen sind diese Frauen von Arbeiten zu befreien, die f\u00fcr sie beschwerlich sind (Art. 64 Abs. 1 ArGV 1).").FontSize(sizeText);
                        col.Item().PaddingLeft(8).PaddingTop(2).Text("\u2022  W\u00e4hrend des ersten Lebensjahres ist das Stillen zu erm\u00f6glichen:").FontSize(sizeText);
                        col.Item().PaddingLeft(20).Text("\u2022  Stillzeit am Arbeitsort ist Arbeitszeit;").FontSize(sizeText);
                        col.Item().PaddingLeft(20).Text("\u2022  Stillzeit ausserhalb des Arbeitsortes ist zur H\u00e4lfte als Arbeitszeit anzurechnen.").FontSize(sizeText);
                        col.Item().Element(c => T(c, "6. Weiterf\u00fchrende Unterlagen", sizeTitle));
                        col.Item().Element(c => P(c, "Zum Thema Mutterschutz und als Eigenstudium finden Sie weitere Informationsmittel auf der Homepage des SECO unter dem Themenblock \u00abSchwangere und Stillende\u00bb.", sizeText));
                        col.Item().PaddingTop(12).ShowEntire().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Mitarbeiterin informiert:").Bold();
                                c.Item().PaddingTop(2).Text($"{d.SignatureCity}, {d.ContractDate:dd.MM.yyyy}");
                                c.Item().PaddingTop(60).PaddingRight(20).LineHorizontal(0.5f).LineColor(Dark);
                                c.Item().PaddingTop(3).Text(d.SignatoryName);
                                c.Item().Text(d.SignatoryTitle);
                            });
                            if (isMinor)
                            {
                                row.ConstantItem(150).Column(c =>
                                {
                                    c.Item().Text("Unterschrift des Erziehungsberechtigten");
                                    c.Item().PaddingTop(2).Text("Ort und Datum:");
                                    c.Item().PaddingTop(49).PaddingRight(10).LineHorizontal(0.5f).LineColor(Dark);
                                });
                            }
                                row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Mitarbeiterin").Bold();
                                c.Item().PaddingTop(2).Text("Ort und Datum:");
                                c.Item().PaddingTop(60).PaddingRight(20).LineHorizontal(0.5f).LineColor(Dark);
                                c.Item().PaddingTop(3).Text($"{d.FirstName} {d.LastName}");
                            });
                        });
                    });
                }); // Ende container.Page Seite 4
            } // Ende if (d.Gender == "female")

        }).GeneratePdf();
    }

   private static void T(IContainer c, string title, float size = 9.5f) =>
    c.PaddingTop(3).Text(title).Bold().FontSize(size);

private static void P(IContainer c, string text, float size = 9f) =>
    c.PaddingTop(1).Text(text).FontSize(size).Justify();
}