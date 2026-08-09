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
        List<DpZeileInfo> zeilen, List<DpFilialeInfo> filialen, List<DpCodeInfo> codes,
        List<DpFeiertagInfo> feiertage, List<DpSchulferienInfo> schulferien)
    {
        QuestPDF.Settings.License = LicenseType.Community;   // wie alle anderen PDF-Generatoren
        int tage = DateTime.DaysInMonth(year, month);

        // Feiertag-/Schulferien-Lookup pro Filiale/Tag.
        var ftMap = new Dictionary<(int cp, int tag), string>();
        foreach (var f in feiertage)
            if (f.Datum.Year == year && f.Datum.Month == month)
                ftMap[(f.CompanyProfileId, f.Datum.Day)] = f.Bezeichnung;
        var sfMap = new Dictionary<(int cp, int tag), string>();
        foreach (var s in schulferien)
            for (var d = s.Von; d <= s.Bis; d = d.AddDays(1))
                if (d.Year == year && d.Month == month)
                    sfMap[(s.CompanyProfileId, d.Day)] = s.Bezeichnung;
        bool[] istWe = new bool[tage + 1];
        bool[] istMo = new bool[tage + 1];
        for (int t = 1; t <= tage; t++)
        {
            var dow = new DateTime(year, month, t).DayOfWeek;
            istWe[t] = dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;
            istMo[t] = dow == DayOfWeek.Monday;
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

                // Immer EINE A4-quer-Seite (Walter 09.08.2026): ScaleToFit
                // verkleinert das ganze Grid, bis es auf die Seite passt.
                page.Content().ScaleToFit().Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(78);
                        for (int t = 1; t <= tage; t++) cols.RelativeColumn();
                        // Auswertungs-Spalten F | M | S | frei | WE (Walter 09.08.2026).
                        cols.ConstantColumn(16);
                        cols.ConstantColumn(16);
                        cols.ConstantColumn(16);
                        cols.ConstantColumn(18);
                        cols.ConstantColumn(20);
                    });

                    // Kopf: Datum + Wochentag. Wochenende NUR hier schattiert;
                    // vor jedem Montag eine Wochen-Trennlinie.
                    table.Header(h =>
                    {
                        h.Cell().Element(x => KopfZelle(x, false, false)).AlignLeft().PaddingLeft(3)
                            .Text("Datum").Bold().FontColor("#646464");
                        for (int t = 1; t <= tage; t++)
                            h.Cell().Element(x => KopfZelle(x, istWe[t], istMo[t]))
                                .Text(t.ToString("00")).Bold().FontColor("#646464");
                        for (int i = 0; i < 5; i++)
                            h.Cell().Element(x => KopfZelle(x, false, i == 0));
                        h.Cell().Element(x => KopfZelle(x, false, false)).AlignLeft().PaddingLeft(3)
                            .Text("Tag").Bold().FontColor("#646464");
                        for (int t = 1; t <= tage; t++)
                            h.Cell().Element(x => KopfZelle(x, istWe[t], istMo[t]))
                                .Text(Wochentage[(int)new DateTime(year, month, t).DayOfWeek])
                                .Bold().FontColor("#646464");
                        h.Cell().Element(x => KopfZelle(x, false, true)).Text("F").Bold().FontColor("#646464");
                        h.Cell().Element(x => KopfZelle(x, false, false)).Text("M").Bold().FontColor("#646464");
                        h.Cell().Element(x => KopfZelle(x, false, false)).Text("S").Bold().FontColor("#646464");
                        h.Cell().Element(x => KopfZelle(x, false, false)).Text("frei").Bold().FontColor("#646464").FontSize(5.5f);
                        h.Cell().Element(x => KopfZelle(x, false, false)).Text("WE").Bold().FontColor("#646464").FontSize(5.5f);
                    });

                    int? lastCp = null;
                    foreach (var z in zeilen)
                    {
                        if (z.CompanyProfileId != lastCp)
                        {
                            lastCp = z.CompanyProfileId;
                            var f = filialen.FirstOrDefault(b => b.Id == z.CompanyProfileId);
                            var label = f == null ? "" : $"{f.Code} {f.Name}".Trim();
                            // Filial-Zeile: Label + pro Tag Feiertag- (rot ★) /
                            // Schulferien-Marker (blau), wie im Grid.
                            table.Cell().Background(Dunkel).PaddingVertical(2).PaddingLeft(4)
                                .Text(label).FontColor("#ffffff").Bold().FontSize(7.5f);
                            int cp = z.CompanyProfileId ?? -1;
                            for (int t = 1; t <= tage; t++)
                            {
                                bool ft = ftMap.ContainsKey((cp, t));
                                bool sf = sfMap.ContainsKey((cp, t));
                                var bgBr = ft ? "#f87171" : sf ? "#93c5fd" : Dunkel;
                                table.Cell().Background(bgBr).AlignCenter().AlignMiddle()
                                    .Text("").FontSize(6);
                            }
                            for (int i = 0; i < 5; i++) table.Cell().Background(Dunkel).Text("");
                        }

                        // Anzeigename immer «Vorname N.» (Walter 09.08.2026, wie Alters-Report).
                        // KEIN ★ im PDF — das Glyph fehlt im PDF-Font und QuestPDF
                        // wirft dann (HTTP-500-Bug 09.08.2026). GF = fett + «(GF)».
                        var anzName = z.Vorname + (string.IsNullOrEmpty(z.Nachname) ? "" : $" {z.Nachname[0]}.");
                        var nameZelle = table.Cell().Border(0.5f).BorderColor(Rand)
                            .PaddingVertical(1.5f).PaddingLeft(3).AlignMiddle();
                        if (z.IstGf) nameZelle.Text($"{anzName} (GF)").Bold().FontSize(7);
                        else nameZelle.Text(anzName).FontSize(7);

                        var abs = absMap[z.EmployeeId];
                        for (int t = 1; t <= tage; t++)
                        {
                            var iso = $"{year:D4}-{month:D2}-{t:D2}";
                            var basis = table.Cell().Border(0.5f).BorderColor(Rand);
                            if (istMo[t]) basis = basis.BorderLeft(1.6f);   // Wochen-Trennlinie
                            if (abs.TryGetValue(t, out var typ))
                            {
                                var st = ABS.TryGetValue(typ, out var a) ? a
                                    : new AbsStyle("#e2e8f0", "#475569", typ.Length > 2 ? typ[..2] : typ, typ);
                                basis.Background(st.Bg)
                                    .AlignCenter().AlignMiddle().PaddingVertical(1.5f)
                                    .Text(st.Kuerzel).Bold().FontColor(st.Fg);
                                continue;
                            }
                            z.Zellen.TryGetValue(iso, out var code);
                            var farbe = codes.FirstOrDefault(x => x.Code == code)?.Farbe;
                            // Wochenende + Feiertag NICHT im Grid färben (nur Kopf/Filialzeile).
                            if (farbe != null) basis = basis.Background(farbe);
                            basis.AlignCenter().AlignMiddle().PaddingVertical(1.5f)
                                .Text(code ?? "").Bold();
                        }

                        // Auswertung: F/M/S-Dienste, freie Tage («-»), WE-Kontrolle
                        // (OK = mind. ein Sa/So frei oder in den Ferien).
                        int SumOf(string k) => z.Zellen.Values.Count(v => v == k);
                        bool weOk = false;
                        for (int t = 1; t <= tage && !weOk; t++)
                        {
                            if (!istWe[t]) continue;
                            var iso = $"{year:D4}-{month:D2}-{t:D2}";
                            if ((z.Zellen.TryGetValue(iso, out var c2) && c2 == "-")
                                || (abs.TryGetValue(t, out var at) && at == "FERIEN"))
                                weOk = true;
                        }
                        foreach (var (txt, erste, gruen) in new[]
                        {
                            (SumOf("F") > 0 ? SumOf("F").ToString() : "", true, false),
                            (SumOf("M") > 0 ? SumOf("M").ToString() : "", false, false),
                            (SumOf("S") > 0 ? SumOf("S").ToString() : "", false, false),
                            (SumOf("-") > 0 ? SumOf("-").ToString() : "", false, false),
                            (weOk ? "OK" : "", false, true),
                        })
                        {
                            var sc = table.Cell().Border(0.5f).BorderColor(Rand).Background(KopfBg);
                            if (erste) sc = sc.BorderLeft(1.6f);
                            sc.AlignCenter().AlignMiddle().PaddingVertical(1.5f)
                                .Text(txt).Bold().FontColor(gruen ? "#166534" : "#1a1a1a").FontSize(gruen ? 5.5f : 6.5f);
                        }
                    }
                });

                page.Footer().PaddingTop(5).Row(r =>
                {
                    var teile = codes
                        .Select(x => $"{x.Code} = {x.Bezeichnung}")
                        .Concat(ABS.Values.Select(a => $"{(a.Kuerzel == "" ? "grün" : a.Kuerzel)} = {a.Label}"))
                        .Append("rot = Feiertag")
                        .Append("blau = Schulferien");
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

    private static IContainer KopfZelle(IContainer x, bool we, bool mo)
    {
        x = x.Border(0.5f).BorderColor(Rand);
        if (mo) x = x.BorderLeft(1.6f);
        return x.Background(we ? WochenEnd : KopfBg)
            .PaddingVertical(1.5f).AlignCenter().AlignMiddle();
    }
}
