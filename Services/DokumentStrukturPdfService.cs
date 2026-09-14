using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Druckbare Übersicht der Dokument-Struktur (Kategorien + Typen).
/// Walter 14.09.2026: Personaldossier-Ablage als A4-Liste.
/// </summary>
public class DokumentStrukturPdfService
{
    private static readonly string Ink   = "#1a1a1a";
    private static readonly string Body  = "#3f3f3f";
    private static readonly string Muted = "#8b8b8b";
    private static readonly string Line  = "#c8c0b2";
    private static readonly string Soft  = "#f1efe9";
    private static readonly string KatBg = "#e7e4db";

    public sealed class TypZeile
    {
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
        public bool Aktiv { get; set; } = true;
        public string? LinkedFieldCode { get; set; }
        public int AnzahlDokumente { get; set; }
    }

    public sealed class KategorieBlock
    {
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
        public bool Aktiv { get; set; } = true;
        public int AnzahlDokumente { get; set; }
        public List<TypZeile> Typen { get; set; } = new();
    }

    public byte[] Generate(IReadOnlyList<KategorieBlock> kategorien)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var stand = DateTime.Now;
        int nKat  = kategorien.Count;
        int nTyp  = kategorien.Sum(k => k.Typen.Count);
        int nDok  = kategorien.Sum(k => k.AnzahlDokumente);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.4f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9f).FontColor(Body));

                page.Header().Column(col =>
                {
                    col.Item().Text("Dokument-Struktur").Bold().FontSize(16f).FontColor(Ink);
                    col.Item().PaddingTop(2).Text(
                        $"Personaldossier-Ablage · Stand {stand:dd.MM.yyyy}")
                        .FontSize(9f).FontColor(Muted);
                    col.Item().PaddingTop(1).Text(
                        $"{nKat} Kategorien · {nTyp} Typen · {nDok} Dokumente")
                        .FontSize(8.5f).FontColor(Muted);
                    col.Item().PaddingTop(8);
                });

                page.Content().Column(col =>
                {
                    foreach (var k in kategorien)
                    {
                        col.Item().PaddingBottom(10).Element(c => KategorieTabelle(c, k));
                    }
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span($"OneCrew · Dokument-Struktur · {stand:dd.MM.yyyy HH:mm} · Seite ")
                        .FontSize(7.5f).FontColor(Muted);
                    t.CurrentPageNumber().FontSize(7.5f).FontColor(Muted);
                    t.Span(" / ").FontSize(7.5f).FontColor(Muted);
                    t.TotalPages().FontSize(7.5f).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }

    private static void KategorieTabelle(IContainer container, KategorieBlock k)
    {
        var katTitel = k.Name + (k.Aktiv ? "" : " (inaktiv)");
        var katMeta  = $"{k.Typen.Count} Typen · {k.AnzahlDokumente} Dokumente";

        container.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4.2f); // Typ
                c.ConstantColumn(36);   // Sort
                c.RelativeColumn(2.4f); // Verknüpfung
                c.ConstantColumn(62);   // Dokumente
                c.ConstantColumn(52);   // Status
            });

            t.Header(h =>
            {
                h.Cell().ColumnSpan(5).Background(KatBg)
                    .BorderBottom(0.8f).BorderColor(Ink)
                    .PaddingVertical(5).PaddingHorizontal(6)
                    .Row(r =>
                    {
                        r.RelativeItem().Text(katTitel).Bold().FontSize(10.5f).FontColor(Ink);
                        r.AutoItem().AlignRight().AlignMiddle()
                            .Text(katMeta).FontSize(8f).FontColor(Muted);
                    });

                void Th(string s, bool right = false)
                {
                    var cell = h.Cell().Background(Soft)
                        .BorderBottom(0.7f).BorderColor(Line)
                        .PaddingVertical(3).PaddingHorizontal(5);
                    (right ? cell.AlignRight() : cell)
                        .Text(s).Bold().FontSize(7.5f).FontColor(Ink);
                }
                Th("Typ");
                Th("Sort", true);
                Th("Verknüpfung");
                Th("Dokumente", true);
                Th("Status");
            });

            if (k.Typen.Count == 0)
            {
                t.Cell().ColumnSpan(5)
                    .BorderBottom(0.4f).BorderColor(Line)
                    .PaddingVertical(5).PaddingHorizontal(6)
                    .Text("Keine Typen").FontSize(8.5f).FontColor(Muted).Italic();
                return;
            }

            foreach (var typ in k.Typen)
            {
                Td(t, typ.Name + (typ.Aktiv ? "" : " (inaktiv)"));
                Td(t, typ.SortOrder.ToString(), right: true);
                Td(t, LinkedLabel(typ.LinkedFieldCode));
                Td(t, typ.AnzahlDokumente.ToString("N0"), right: true);
                Td(t, typ.Aktiv ? "aktiv" : "inaktiv");
            }
        });
    }

    private static void Td(TableDescriptor t, string text, bool right = false)
    {
        var cell = t.Cell().BorderBottom(0.4f).BorderColor(Line)
            .PaddingVertical(3).PaddingHorizontal(5);
        var box = right ? cell.AlignRight() : cell;
        box.Text(string.IsNullOrWhiteSpace(text) ? "—" : text)
            .FontSize(8.5f).FontColor(Body);
    }

    private static string LinkedLabel(string? code) => (code ?? "").Trim() switch
    {
        "permit"           => "Bewilligung",
        "passport"         => "Pass",
        "id_card"          => "Identitätskarte",
        "ahv_card"         => "AHV-Karte",
        "bank_card"        => "Bankkarte",
        "contract"         => "Arbeitsvertrag",
        "marriage_cert"    => "Heiratsurkunde",
        "birth_cert"       => "Geburtsurkunde",
        "social_decision"  => "Bescheid Sozialamt",
        "spouse"           => "Ehegatte (Familie)",
        "employee_photo"   => "Mitarbeiterfoto",
        "family_allowance" => "FAK-Entscheid (Kinderzulage)",
        ""                 => "—",
        _                  => code!
    };
}
