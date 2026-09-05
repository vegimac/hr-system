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
    private const string Line  = "#9a958c";
    private const string Shade = "#f3f1ed";

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
        var skalaMa = new[] { "Nein", "Eher nein", "Eher ja", "Ja" };
        var skalaGf = new[] { "Unterstützung nötig", "Auf gutem Weg", "Gut", "Stark" };

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.0f, Unit.Centimetre);
                page.MarginHorizontal(1.6f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9.5f).LineHeight(1.2f).FontColor(Dark));

                page.Content().Column(col =>
                {
                    // ── Kopf: Kicker + Titel links, Logo rechts ─────────────
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("CHECK-IN · PROBEZEIT").FontSize(8.5f).Bold().FontColor(Soft).LetterSpacing(0.08f);
                            c.Item().PaddingTop(2).Text("Deine ersten Wochen im Team").FontSize(19f).Bold();
                            c.Item().PaddingTop(1).Text("Zwei Blickwinkel. Ein Gespräch. Gemeinsam weiter.").FontSize(9.5f).FontColor(Soft);
                        });
                        var logo = LogoBytes;
                        if (logo != null)
                            // FitWidth statt Höhe+FitHeight: Bei 32 pt Höhe wäre das Logo
                            // 122 pt breit — breiter als die Spalte → QuestPDF-Layoutfehler (500).
                            r.ConstantItem(120).AlignRight().AlignTop().Image(logo).FitWidth();
                    });

                    // ── Stammdaten-Box ──────────────────────────────────────
                    col.Item().PaddingTop(12).Border(0.8f).BorderColor(Line).Padding(9).Column(c =>
                    {
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Element(e => Feld(e, "Mitarbeiter/in", d.MaName));
                            r.ConstantItem(10);
                            r.RelativeItem().Element(e => Feld(e, "Restaurant", d.Restaurant));
                            r.ConstantItem(10);
                            r.RelativeItem().Element(e => Feld(e, "Eintritt", d.Eintritt?.ToString("dd.MM.yyyy")));
                            r.ConstantItem(10);
                            r.RelativeItem().Element(e => Feld(e, "Probezeit bis", d.ProbezeitBis?.ToString("dd.MM.yyyy")));
                        });
                        c.Item().PaddingTop(7).Row(r =>
                        {
                            r.RelativeItem().Element(e => Feld(e, "Gespräch geführt von", d.GefuehrtVon));
                            r.ConstantItem(10);
                            r.RelativeItem().Element(e => Feld(e, "Funktion", d.Funktion));
                            r.ConstantItem(10);
                            r.RelativeItem().Element(e => Feld(e, "Gespräch am", d.GespraechAm?.ToString("dd.MM.yyyy")));
                            r.ConstantItem(10);
                            r.RelativeItem();
                        });
                    });

                    // ── 1 · So erlebe ich meinen Start (MA) ─────────────────
                    col.Item().PaddingTop(10).Element(e => Abschnitt(e, "1", "SO ERLEBE ICH MEINEN START", "Mitarbeiter/in"));
                    col.Item().PaddingTop(4).Element(e => SkalaKopf(e, skalaMa));
                    var fragenMa = new[]
                    {
                        "Ich fühle mich im Team willkommen.",
                        "Ich weiss, was von mir erwartet wird.",
                        "Ich bekomme Unterstützung, wenn ich sie brauche.",
                        "Die Arbeit gefällt mir.",
                        "Ich kann mir vorstellen, weiterhin Teil des Teams zu sein."
                    };
                    for (var i = 0; i < fragenMa.Length; i++)
                        col.Item().Element(e => Zeile(e, fragenMa[i], 4, i % 2 == 0));
                    col.Item().PaddingTop(5).Element(e => Schreibzeile(e, "Das gefällt mir besonders gut:"));
                    col.Item().PaddingTop(3).Element(e => Schreibzeile(e, "Das könnte für mich besser sein:"));

                    // ── 2 · So erleben wir dich (GF) ────────────────────────
                    col.Item().PaddingTop(10).Element(e => Abschnitt(e, "2", "SO ERLEBEN WIR DICH", "Manager / GF"));
                    col.Item().PaddingTop(4).Element(e => SkalaKopf(e, skalaGf));
                    var fragenGf = new[]
                    {
                        "Arbeitsleistung", "Zuverlässigkeit", "Lernbereitschaft",
                        "Verhalten gegenüber Gästen", "Zusammenarbeit im Team", "Selbständigkeit"
                    };
                    for (var i = 0; i < fragenGf.Length; i++)
                        col.Item().Element(e => Zeile(e, fragenGf[i], 4, i % 2 == 0));
                    col.Item().PaddingTop(5).Element(e => Schreibzeile(e, "Das machst du bereits richtig gut:"));
                    col.Item().PaddingTop(3).Element(e => Schreibzeile(e, "Daran möchten wir gemeinsam noch arbeiten:"));

                    // ── 3 · Wie geht es weiter? ─────────────────────────────
                    col.Item().PaddingTop(10).Element(e => Abschnitt(e, "3", "WIE GEHT ES WEITER?", "gemeinsamer Entscheid"));
                    col.Item().PaddingTop(6).Row(r =>
                    {
                        r.RelativeItem().Element(e => EntscheidBox(e, "MITARBEITER/IN", new[]
                        {
                            ("Ja, ich möchte Teil des Teams bleiben.", false),
                            ("Ich bin mir noch nicht ganz sicher.", false),
                            ("Nein, ich möchte nicht weiterarbeiten.", false),
                        }));
                        r.ConstantItem(12);
                        r.RelativeItem().Element(e => EntscheidBox(e, "MANAGER / GF", new[]
                        {
                            ("Wir möchten mit dir weiterarbeiten — Probezeit bestanden.", d.Entscheid == "weiter"),
                            ("Weiterarbeit mit Entwicklungszielen (siehe Fokus).", false),
                            ("Das Arbeitsverhältnis wird während der Probezeit beendet.", d.Entscheid == "kuendigung"),
                        }));
                    });
                    col.Item().PaddingTop(8).Element(e => Schreibzeile(e, "Unser gemeinsamer Fokus für die nächsten Wochen:"));
                    col.Item().Element(e => Schreibzeile(e, null));

                    // ── Unterschriften ──────────────────────────────────────
                    col.Item().PaddingTop(24).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().BorderBottom(0.8f).BorderColor(Dark).Height(1);
                            c.Item().PaddingTop(3).Text("Mitarbeiter/in").FontSize(8.5f).FontColor(Soft);
                        });
                        r.ConstantItem(60);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().BorderBottom(0.8f).BorderColor(Dark).Height(1);
                            c.Item().PaddingTop(3).Text("Manager / GF").FontSize(8.5f).FontColor(Soft);
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    private const float SpaltenBreite = 62f;

    private static void Feld(IContainer c, string label, string? wert)
    {
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(8f).FontColor(Soft);
            col.Item().BorderBottom(0.6f).BorderColor(Line).PaddingBottom(1)
               .Text(string.IsNullOrWhiteSpace(wert) ? " " : wert).FontSize(10f).Bold();
        });
    }

    private static void Abschnitt(IContainer c, string nr, string titel, string wer)
    {
        c.Row(r =>
        {
            r.ConstantItem(24).Element(e =>
                e.Width(22).Height(22).Background(Dark).AlignCenter().AlignMiddle()
                 .Text(nr).FontSize(11f).Bold().FontColor("#ffffff"));
            r.RelativeItem().AlignMiddle().PaddingLeft(8).Text(titel).FontSize(12.5f).Bold().LetterSpacing(0.04f);
            r.AutoItem().AlignMiddle().Text(wer).FontSize(8.5f).FontColor(Soft);
        });
    }

    private static void SkalaKopf(IContainer c, string[] skala)
    {
        c.Row(r =>
        {
            r.RelativeItem();
            foreach (var s in skala)
                r.ConstantItem(SpaltenBreite).AlignCenter().AlignBottom()
                 .Text(s).FontSize(7.5f).FontColor(Soft);
        });
    }

    private static void Zeile(IContainer c, string text, int anzahl, bool schattiert)
    {
        var box = schattiert ? c.Background(Shade) : c;
        box.PaddingVertical(3f).PaddingLeft(4).Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(text).FontSize(9.5f);
            for (var i = 0; i < anzahl; i++)
                r.ConstantItem(SpaltenBreite).AlignCenter().AlignMiddle().Element(e =>
                    e.Width(11).Height(11).Border(0.9f).BorderColor(Dark));
        });
    }

    private static void Schreibzeile(IContainer c, string? label)
    {
        c.Column(col =>
        {
            if (label != null) col.Item().Text(label).FontSize(9.5f).Bold();
            col.Item().Height(15).BorderBottom(0.6f).BorderColor(Line);
        });
    }

    private static void EntscheidBox(IContainer c, string titel, (string Text, bool Kreuz)[] optionen)
    {
        c.Border(0.8f).BorderColor(Line).Padding(9).Column(col =>
        {
            col.Item().Text(titel).FontSize(8.5f).Bold().FontColor(Soft).LetterSpacing(0.06f);
            foreach (var (text, kreuz) in optionen)
            {
                col.Item().PaddingTop(6).Row(r =>
                {
                    r.ConstantItem(17).AlignTop().PaddingTop(1).Element(e =>
                    {
                        var b = e.Width(11).Height(11).Border(0.9f).BorderColor(Dark);
                        if (kreuz) b.AlignCenter().AlignMiddle().Text("X").FontSize(8.5f).Bold();
                    });
                    r.RelativeItem().Text(text).FontSize(9.5f);
                });
            }
        });
    }
}
