using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Druckbare Übersicht der Dokument-Struktur als Baum
/// (Kategorie → Typen). Walter 14.09.2026.
/// </summary>
public class DokumentStrukturPdfService
{
    private static readonly CultureInfo CH = CultureInfo.GetCultureInfo("de-CH");
    private static readonly string Ink   = "#1a1a1a";
    private static readonly string Body  = "#3f3f3f";
    private static readonly string Muted = "#8b8b8b";
    private static readonly string Line  = "#c8c0b2";
    private static readonly string Soft  = "#f1efe9";

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
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9.5f).FontColor(Body));

                page.Header().Column(col =>
                {
                    col.Item().Text("Dokument-Struktur").Bold().FontSize(16f).FontColor(Ink);
                    col.Item().PaddingTop(2).Text(
                        $"Personaldossier-Ablage · Stand {stand:dd.MM.yyyy}")
                        .FontSize(9f).FontColor(Muted);
                    col.Item().PaddingTop(1).Text(
                        $"{nKat} Kategorien · {nTyp} Typen · {nDok.ToString("N0", CH)} Dokumente")
                        .FontSize(8.5f).FontColor(Muted);
                    col.Item().PaddingTop(10).LineHorizontal(0.6f).LineColor(Line);
                    col.Item().PaddingBottom(6);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(2);
                    foreach (var k in kategorien)
                        col.Item().Element(c => KategorieAst(c, k));
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

    private static void KategorieAst(IContainer container, KategorieBlock k)
    {
        var katName = k.Name + (k.Aktiv ? "" : "  · inaktiv");

        container.PaddingBottom(10).Column(col =>
        {
            col.Item().Background(Soft).PaddingVertical(5).PaddingHorizontal(8).Row(r =>
            {
                r.RelativeItem().AlignMiddle().Text(katName).Bold().FontSize(11f).FontColor(Ink);
                r.ConstantItem(72).AlignRight().AlignMiddle()
                    .Text(k.AnzahlDokumente.ToString("N0", CH)).Bold().FontSize(10f).FontColor(Ink);
            });

            if (k.Typen.Count == 0)
            {
                col.Item().PaddingLeft(14).PaddingTop(4)
                    .Text("keine Typen").FontSize(8.5f).Italic().FontColor(Muted);
                return;
            }

            col.Item().PaddingLeft(12).BorderLeft(1.4f).BorderColor(Line)
                .PaddingLeft(12).PaddingTop(2).Column(zweig =>
                {
                    foreach (var typ in k.Typen)
                    {
                        var name = typ.Name + (typ.Aktiv ? "" : "  · inaktiv");
                        var link = LinkedLabel(typ.LinkedFieldCode);
                        zweig.Item().PaddingVertical(2.5f).Row(r =>
                        {
                            r.RelativeItem().AlignMiddle().Text(t =>
                            {
                                t.Span(name).FontSize(9.5f).FontColor(Body);
                                if (link != null)
                                    t.Span("  · " + link).FontSize(8f).FontColor(Muted);
                            });
                            r.ConstantItem(72).AlignRight().AlignMiddle()
                                .Text(typ.AnzahlDokumente.ToString("N0", CH))
                                .FontSize(9f).FontColor(Muted);
                        });
                    }
                });
        });
    }

    private static string? LinkedLabel(string? code) => (code ?? "").Trim() switch
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
        // Ablage nach Angabe (Walter 25.09.2026)
        "night_work_exam"      => "Nachtarbeit: Arztzeugnis",
        "night_work_ausnahme"  => "Nachtarbeit: Ausnahmeregelung",
        "absence"              => "Absenz (z.B. Arztzeugnis)",
        "child_id"             => "Ausweis Kind",
        "andere_korrespondenz" => "Anderes: Korrespondenz",
        "andere_arztzeugnis"   => "Anderes: Arztzeugnis ohne Absenz",
        "andere_lohn"          => "Anderes: Lohn",
        "andere_weiterbildung" => "Anderes: Weiterbildung",
        "andere_sonstiges"     => "Anderes: Sonstiges",
        ""                 => null,
        _                  => code
    };
}
