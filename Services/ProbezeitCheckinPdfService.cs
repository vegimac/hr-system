using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Probezeit-Check-in «Deine ersten Wochen im Team» (Walter 05.09.2026) —
/// die vereinfachte Alternative zum klassischen Probezeit-Gesprächsformular
/// (ProbezeitberichtPdfService bleibt bestehen). Zwei Blickwinkel: MA
/// beurteilt den Start, Manager beurteilt den MA, gemeinsamer Entscheid.
/// OneCrew-Look: schwarz/grau, keine Farbe, Logo oben rechts. Ein A4.
/// </summary>
public record ProbezeitCheckinInput(
    string MaName,
    string? Restaurant,
    string? Funktion,
    DateTime? Eintritt,
    DateTime? ProbezeitBis,
    string? GefuehrtVon,
    DateTime? GespraechAm,
    /// <summary>«weiter» | «kuendigung» | null — Kreuz vorgesetzt, wenn schon entschieden.</summary>
    string? Entscheid = null
);

public class ProbezeitCheckinPdfService
{
    private const string Dark  = "#27251F";
    private const string Soft  = "#6b6152";

    private static byte[]? _logoBytes;
    private static byte[]? LogoBytes
    {
        get
        {
            if (_logoBytes != null) return _logoBytes;
            foreach (var pfad in new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Assets", "onecrew-logo.png"),
                Path.Combine(AppContext.BaseDirectory, "wwwroot", "img", "onecrew-logo.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "img", "onecrew-logo.png"),
            })
            {
                if (File.Exists(pfad)) { _logoBytes = File.ReadAllBytes(pfad); break; }
            }
            return _logoBytes;
        }
    }

    public byte[] Generate(ProbezeitCheckinInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // 1:1 nach Walters Vorlage «OneCrew_Probezeit_Gemeinsam_weiter_v25» (05.09.2026).
        var skalaDu  = new[] { "Ja", "Eher ja", "Eher nein", "Nein" };
        var skalaWir = new[] { "Stark", "Gut", "Noch üben", "Noch Hilfe" };

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.4f, Unit.Centimetre);
                page.MarginBottom(1.2f, Unit.Centimetre);
                page.MarginHorizontal(2.1f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9.5f).LineHeight(1.25f).FontColor(Dark));

                page.Content().Column(col =>
                {
                    // ── Logo rechts oben ────────────────────────────────────
                    var logo = LogoBytes;
                    col.Item().Row(r =>
                    {
                        r.RelativeItem();
                        if (logo != null) r.ConstantItem(130).AlignRight().Image(logo).FitWidth();
                        else r.ConstantItem(130).Height(34);
                    });

                    // ── Titel zentriert ─────────────────────────────────────
                    col.Item().PaddingTop(22).AlignCenter().Text("DEINE ERSTEN WOCHEN BEI UNS").FontSize(14f).Bold();
                    col.Item().PaddingTop(2).AlignCenter().Text("Zwei Blickwinkel. Ein Gespräch. Gemeinsam weiter.").FontSize(8.5f).FontColor(Soft);

                    // ── Stammdaten: kleine Labels, Werte darunter ───────────
                    col.Item().PaddingTop(20).Row(r =>
                    {
                        r.RelativeItem(2).Element(e => Feld(e, "MITARBEITER/IN", d.MaName));
                        r.RelativeItem(2).Element(e => Feld(e, "RESTAURANT / BEREICH", d.Restaurant));
                        r.RelativeItem(1.3f).Element(e => Feld(e, "EINTRITT", d.Eintritt?.ToString("dd.MM.yyyy")));
                        r.RelativeItem(1.3f).Element(e => Feld(e, "PROBEZEIT BIS", d.ProbezeitBis?.ToString("dd.MM.yyyy")));
                    });
                    col.Item().PaddingTop(12).Row(r =>
                    {
                        r.RelativeItem(2).Element(e => Feld(e, "GESPRÄCH MIT", d.GefuehrtVon));
                        r.RelativeItem(2).Element(e => Feld(e, "FUNKTION", d.Funktion));
                        r.RelativeItem(1.3f).Element(e => Feld(e, "GESPRÄCH AM", d.GespraechAm?.ToString("dd.MM.yyyy")));
                        r.RelativeItem(1.3f);
                    });

                    // ── DU ──────────────────────────────────────────────────
                    col.Item().PaddingTop(20).Element(e => Block(e, "DU", "Wie war deine erste Zeit bei uns?", skalaDu, new[]
                    {
                        "Ich fühle mich wohl im Team.",
                        "Ich weiss, was ich tun soll.",
                        "Wenn ich Hilfe brauche, bekomme ich sie.",
                        "Mir gefällt meine Arbeit.",
                        "Ich möchte weiter hier arbeiten."
                    }, 12f));
                    col.Item().PaddingTop(8).Text("Bemerkungen").Bold();
                    col.Item().Height(40);
                    col.Item().Element(e => JaNein(e, "Willst du mit uns weitergehen?", null));

                    // ── WIR ─────────────────────────────────────────────────
                    col.Item().PaddingTop(28).Element(e => Block(e, "WIR", "So erleben wir deinen Start.", skalaWir, new[]
                    {
                        "Arbeit / Leistung", "Zuverlässig", "Neues lernen",
                        "Freundlich zu Gästen", "Teamarbeit", "Selbständig arbeiten"
                    }, 9f));
                    col.Item().PaddingTop(8).Text("Bemerkungen").Bold();
                    col.Item().Height(40);
                    col.Item().Element(e => JaNein(e, "Wollen wir mit dir weitergehen?",
                        d.Entscheid == "weiter" ? true : d.Entscheid == "kuendigung" ? false : null));

                    // ── Unterschriften ganz unten ───────────────────────────
                    col.Item().ExtendVertical();
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text(string.IsNullOrWhiteSpace(d.MaName) ? " " : d.MaName).FontSize(9f);
                            c.Item().Text("Mitarbeiter/in").FontSize(7.5f).Bold().FontColor(Soft);
                        });
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text(string.IsNullOrWhiteSpace(d.GefuehrtVon) ? " " : d.GefuehrtVon).FontSize(9f);
                            c.Item().Text("Manager / GF").FontSize(7.5f).Bold().FontColor(Soft);
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    private const float SpaltenBreite = 64f;

    /// <summary>Kleines Label in Grau-Versalien, Wert darunter — keine Linien.</summary>
    private static void Feld(IContainer c, string label, string? wert)
    {
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(6.5f).Bold().FontColor(Soft).LetterSpacing(0.06f);
            col.Item().PaddingTop(1).Text(string.IsNullOrWhiteSpace(wert) ? " " : wert).FontSize(9.5f);
        });
    }

    /// <summary>
    /// Antwort-Block: Kicker, Titel, dann pro Aussage die Antwortwörter rechts
    /// zum Einkreisen — ohne Kopfzeile, ohne Kästchen.
    /// </summary>
    private static void Block(IContainer c, string kicker, string titel, string[] skala, string[] aussagen, float zeilenAbstand)
    {
        c.Column(col =>
        {
            col.Item().Text(kicker).FontSize(8f).Bold().FontColor(Soft).LetterSpacing(0.06f);
            col.Item().PaddingTop(1).Text(titel).FontSize(14f).Bold();
            for (var i = 0; i < aussagen.Length; i++)
            {
                col.Item().PaddingTop(i == 0 ? 8f : zeilenAbstand).Row(r =>
                {
                    r.RelativeItem().Text(aussagen[i]).FontSize(9.5f);
                    foreach (var w in skala)
                        r.ConstantItem(SpaltenBreite).AlignCenter().Text(w).FontSize(9.5f);
                });
            }
        });
    }

    /// <summary>«Frage?   JA   NEIN» — bei bekanntem Entscheid ist die Antwort fett.</summary>
    private static void JaNein(IContainer c, string frage, bool? ja)
    {
        c.Row(r =>
        {
            r.ConstantItem(190).Text(frage).FontSize(9.5f);
            r.ConstantItem(100).AlignCenter().Text(t => { var s = t.Span("JA").FontSize(9.5f);   if (ja == true)  s.Bold(); });
            r.ConstantItem(100).AlignCenter().Text(t => { var s = t.Span("NEIN").FontSize(9.5f); if (ja == false) s.Bold(); });
        });
    }
}
