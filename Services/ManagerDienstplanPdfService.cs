using HrSystem.Controllers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Manager-Dienstplan als A4-QUER-PDF (Walter-Vorgabe 09.08.2026 — ersetzt
/// den Browser-Druck). Gleiches Bild wie das Grid: Filial-Blöcke (dunkle
/// Trennzeile), GF fett mit ★ zuoberst, Wochenende schattiert, Absenzen
/// farbig (Ferien grün, Krank rot «K», Unfall orange «U», Mutterschaft
/// violett «MS»), Kürzel-Farben aus dem dienstplan_code-Katalog. Legende
/// im Fusszeilenbereich. Daten kommen fertig aufbereitet aus
/// <see cref="ManagerDienstplanController"/> (BuildMonthDataAsync) — das
/// Service selbst ist zustandslos (Singleton).
/// </summary>
public class ManagerDienstplanPdfService
{
    private static readonly string[] MonatsNamen =
        { "Januar", "Februar", "März", "April", "Mai", "Juni",
          "Juli", "August", "September", "Oktober", "November", "Dezember" };

    private static readonly string[] Wochentage = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };

    private sealed record AbsStyle(string Bg, string Fg, string Kuerzel, string Label);
    private static readonly Dictionary<string, AbsStyle> ABS = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FERIEN"]       = new("#bbf7d0", "#166534", "",   "Ferien"),
        ["KRANK"]        = new("#fecaca", "#991b1b", "K",  "Krank"),
        ["UNFALL"]       = new("#fed7aa", "#9a3412", "U",  "Unfall"),
        ["MUTTERSCHAFT"] = new("#e9d5ff", "#6b21a8", "MS", "Mutterschaft"),
    };

    private const string Rand      = "#c9c4bb";
    private const string KopfBg    = "#f6f3ee";
    private const string WochenEnd = "#efece6";
    private const string Dunkel    = "#3f3f3f";

    public byte[] Generate(int year, int month,
        List<DpZeileInfo> zeilen, List<DpFilialeInfo> filialen, List<DpCodeInfo> codes)
    {
        int tage = DateTime.DaysInMonth(year, month);
        bool[] istWe = new bool[tage + 1];
        for (int t = 1; t <= tage; t++)
        {
            var dow = new DateTime(year, month, t).DayOfWeek;
            istWe[t] = dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;
        }

        // Absenz-Lookup MA → Tag → Typ.
        var absMap = new Dictionary<int, Dictionary<int, string>>();
        foreach (var z in zeilen)
        {
            var m = new Dictionary<int, string>();
            foreach (var a in z.Absenzen)
                for (var d = a.Von; d <= a.Bis; d = d.AddDays(1))
                    if (d.Year == year && d.Month == month) m[d.Day] = a.Typ;
            absMap[z.EmployeeId] = m;
        }

        return Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(t => t.FontSize(6.5f).FontColor("#1a1a1a"));

                page.Header().PaddingBottom(6).Row(r =>
                {
                    r.RelativeItem().Text($"Manager-Dienstplan — {MonatsNamen[month - 1]} {year}")
                        .FontSize(13).Bold().FontColor(Dunkel);
                    r.ConstantItem(160).AlignRight().AlignBottom()
                        .Text($"Stand {DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(7).FontColor("#646464");
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(78);
                        for (int t = 1; t <= tage; t++) cols.RelativeColumn();
                    });

                    // Kopf (wiederholt sich auf Folgeseiten): Datum + Wochentag.
                    table.Header(h =>
                    {
                        h.Cell().Element(x => KopfZelle(x, false)).AlignLeft().PaddingLeft(3)
                            .Text("Datum").Bold().FontColor("#646464");
                        for (int t = 1; t <= tage; t++)
                            h.Cell().Element(x => KopfZelle(x, istWe[t]))
                                .Text(t.ToString("00")).Bold().FontColor("#646464");
                        h.Cell().Element(x => KopfZelle(x, false)).AlignLeft().PaddingLeft(3)
                            .Text("Tag").Bold().FontColor("#646464");
                        for (int t = 1; t <= tage; t++)
                            h.Cell().Element(x => KopfZelle(x, istWe[t]))
                                .Text(Wochentage[(int)new DateTime(year, month, t).DayOfWeek])
                                .Bold().FontColor("#646464");
                    });

                    int? lastCp = null;
                    foreach (var z in zeilen)
                    {
                        if (z.CompanyProfileId != lastCp)
                        {
                            lastCp = z.CompanyProfileId;
                            var f = filialen.FirstOrDefault(b => b.Id == z.CompanyProfileId);
                            var label = f == null ? "" : $"{f.Code} {f.Name}".Trim();
                            table.Cell().ColumnSpan((uint)(tage + 1)).Background(Dunkel)
                                .PaddingVertical(2).PaddingLeft(4)
                                .Text(label).FontColor("#ffffff").Bold().FontSize(7.5f);
                        }

                        var nameZelle = table.Cell().Border(0.5f).BorderColor(Rand)
                            .PaddingVertical(1.5f).PaddingLeft(3).AlignMiddle();
                        if (z.IstGf) nameZelle.Text($"★ {z.Vorname}").Bold().FontSize(7);
                        else nameZelle.Text(z.Vorname).FontSize(7);

                        var abs = absMap[z.EmployeeId];
                        for (int t = 1; t <= tage; t++)
                        {
                            var iso = $"{year:D4}-{month:D2}-{t:D2}";
                            if (abs.TryGetValue(t, out var typ))
                            {
                                var st = ABS.TryGetValue(typ, out var a) ? a
                                    : new AbsStyle("#e2e8f0", "#475569", typ.Length > 2 ? typ[..2] : typ, typ);
                                table.Cell().Border(0.5f).BorderColor(Rand).Background(st.Bg)
                                    .AlignCenter().AlignMiddle().PaddingVertical(1.5f)
                                    .Text(st.Kuerzel).Bold().FontColor(st.Fg);
                                continue;
                            }
                            z.Zellen.TryGetValue(iso, out var code);
                            var farbe = codes.FirstOrDefault(x => x.Code == code)?.Farbe;
                            var bg = farbe ?? (istWe[t] ? WochenEnd : null);
                            var cell = table.Cell().Border(0.5f).BorderColor(Rand);
                            if (bg != null) cell = cell.Background(bg);
                            cell.AlignCenter().AlignMiddle().PaddingVertical(1.5f)
                                .Text(code ?? "").Bold();
                        }
                    }
                });

                page.Footer().PaddingTop(5).Row(r =>
                {
                    var teile = codes
                        .Select(x => $"{x.Code} = {x.Bezeichnung}")
                        .Concat(ABS.Values.Select(a => $"{(a.Kuerzel == "" ? "grün" : a.Kuerzel)} = {a.Label}"));
                    r.RelativeItem().Text(string.Join("   ·   ", teile)).FontSize(6.5f).FontColor("#646464");
                    r.ConstantItem(60).AlignRight().Text(x =>
                    {
                        x.DefaultTextStyle(s => s.FontSize(7).FontColor("#646464"));
                        x.Span("Seite ");
                        x.CurrentPageNumber();
                        x.Span(" / ");
                        x.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static IContainer KopfZelle(IContainer x, bool we)
        => x.Border(0.5f).BorderColor(Rand).Background(we ? WochenEnd : KopfBg)
            .PaddingVertical(1.5f).AlignCenter().AlignMiddle();
}
