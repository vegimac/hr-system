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
        // Positive Antwort zuerst (Walter 05.09.2026).
        var skalaMa = new[] { "Ja", "Eher ja", "Eher nein", "Nein" };
        var skalaGf = new[] { "Stark", "Gut", "Auf gutem Weg", "Braucht Hilfe" };

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
                            c.Item().PaddingTop(1).Text("Deine ersten Wochen im Team").FontSize(18f).Bold();
                            c.Item().PaddingTop(1).Text("Zwei Blickwinkel. Ein Gespräch. Gemeinsam weiter.").FontSize(9.5f).FontColor(Soft);
                        });
                        var logo = LogoBytes;
                        if (logo != null)
                            r.ConstantItem(120).AlignRight().AlignTop().Image(logo).FitWidth();
                    });

                    // ── Stammdaten (ohne Linien; leer = fein gestrichelt) ──
                    col.Item().PaddingTop(10).Column(c =>
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
                        c.Item().PaddingTop(5).Row(r =>
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

                    // ── DU · Wie war deine erste Zeit bei uns? ──────────────
                    col.Item().PaddingTop(12).Element(e => Block(e, "DU", "Wie war deine erste Zeit bei uns?", "Kreise ein, was für dich passt.", skalaMa, new[]
                    {
                        "Ich fühle mich wohl im Team.",
                        "Ich weiss, was ich tun soll.",
                        "Wenn ich Hilfe brauche, bekomme ich sie.",
                        "Mir gefällt meine Arbeit.",
                        "Ich möchte weiter hier arbeiten."
                    }));
                    col.Item().PaddingTop(6).Element(e => Bemerkung(e));

                    // ── WIR · So haben wir dich erlebt ──────────────────────
                    col.Item().PaddingTop(12).Element(e => Block(e, "WIR", "So haben wir dich erlebt", "Wir kreisen ein, was passt.", skalaGf, new[]
                    {
                        "Arbeitsleistung", "Zuverlässigkeit", "Lernbereitschaft",
                        "Verhalten gegenüber Gästen", "Zusammenarbeit im Team", "Selbständigkeit"
                    }));
                    col.Item().PaddingTop(6).Element(e => Bemerkung(e));

                    // ── GEMEINSAM · Wie geht es weiter? ─────────────────────
                    col.Item().PaddingTop(12).Column(c =>
                    {
                        c.Item().Text("GEMEINSAM").FontSize(8.5f).Bold().FontColor(Soft).LetterSpacing(0.08f);
                        c.Item().PaddingTop(1).Text("Wie geht es weiter?").FontSize(14f).Bold();
                        c.Item().PaddingTop(1).Text("Kreise ein, was gilt.").FontSize(9f).FontColor(Soft);
                    });
                    col.Item().PaddingTop(6).Row(r =>
                    {
                        r.RelativeItem().Element(e => EntscheidSpalte(e, "Mitarbeiter/in", new[]
                        {
                            ("Ja, ich möchte Teil des Teams bleiben.", false),
                            ("Ich bin mir noch nicht ganz sicher.", false),
                            ("Nein, ich möchte nicht weiterarbeiten.", false),
                        }));
                        r.ConstantItem(20);
                        r.RelativeItem().Element(e => EntscheidSpalte(e, "Manager / GF", new[]
                        {
                            ("Wir möchten mit dir weiterarbeiten — Probezeit bestanden.", d.Entscheid == "weiter"),
                            ("Weiterarbeit mit Entwicklungszielen (siehe Fokus).", false),
                            ("Das Arbeitsverhältnis wird während der Probezeit beendet.", d.Entscheid == "kuendigung"),
                        }));
                    });
                    col.Item().PaddingTop(6).Element(e => Schreibzeile(e, "Unser gemeinsamer Fokus für die nächsten Wochen:"));

                    // ── Unterschriften: Schreibraum, darunter Name + Rolle ──
                    col.Item().PaddingTop(10).Column(c =>
                    {
                        c.Item().Height(34);
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().Column(cc =>
                            {
                                cc.Item().Text(string.IsNullOrWhiteSpace(d.MaName) ? " " : d.MaName).FontSize(10f);
                                cc.Item().Text("Mitarbeiter/in").FontSize(8.5f).FontColor(Soft);
                            });
                            r.ConstantItem(60);
                            r.RelativeItem().Column(cc =>
                            {
                                cc.Item().Text(string.IsNullOrWhiteSpace(d.GefuehrtVon) ? " " : d.GefuehrtVon).FontSize(10f);
                                cc.Item().Text("Manager / GF").FontSize(8.5f).FontColor(Soft);
                            });
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    private const float SpaltenBreite = 72f;

    /// <summary>
    /// Antwort-Block ohne Kästchen (Walter 05.09.2026): Kicker, Titel, Hinweis,
    /// Skala-Kopf, dann pro Aussage die Antwortwörter zum Einkreisen.
    /// </summary>
    private static void Block(IContainer c, string kicker, string titel, string hinweis, string[] skala, string[] aussagen)
    {
        c.Column(col =>
        {
            col.Item().Text(kicker).FontSize(8.5f).Bold().FontColor(Soft).LetterSpacing(0.08f);
            col.Item().PaddingTop(1).Text(titel).FontSize(14f).Bold();
            col.Item().PaddingTop(1).Row(r =>
            {
                r.RelativeItem().AlignBottom().Text(hinweis).FontSize(9f).FontColor(Soft);
                foreach (var w in skala)
                    r.ConstantItem(SpaltenBreite).AlignCenter().AlignBottom().Text(w).FontSize(8.5f).Bold().FontColor(Soft);
            });
            foreach (var a in aussagen)
            {
                col.Item().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Text(a).FontSize(10f);
                    foreach (var w in skala)
                        r.ConstantItem(SpaltenBreite).AlignCenter().Text(w).FontSize(9.5f);
                });
            }
        });
    }

    /// <summary>Zwei gestrichelte Zeilen «Bemerkung».</summary>
    private static void Bemerkung(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().Text("Bemerkung").FontSize(9.5f).Bold();
            col.Item().Height(20);
            col.Item().Element(Gestrichelt);
            col.Item().Height(20);
            col.Item().Element(Gestrichelt);
        });
    }

    /// <summary>Entscheid-Spalte: Optionen als Text zum Einkreisen; vorentschieden = fett mit Haken.</summary>
    private static void EntscheidSpalte(IContainer c, string titel, (string Text, bool Gewaehlt)[] optionen)
    {
        c.Column(col =>
        {
            col.Item().Text(titel).FontSize(8.5f).Bold().FontColor(Soft);
            foreach (var (text, gewaehlt) in optionen)
            {
                var t = col.Item().PaddingTop(5).Text(gewaehlt ? "✓  " + text : text).FontSize(10f);
                if (gewaehlt) t.Bold();
            }
        });
    }

    /// <summary>
    /// Stammdaten-Feld: vorausgefüllt → nur Label + Wert, keine Linie.
    /// Leer (von Hand) → feine gestrichelte Linie (Walter 05.09.2026).
    /// </summary>
    private static void Feld(IContainer c, string label, string? wert)
    {
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(8f).FontColor(Soft);
            if (string.IsNullOrWhiteSpace(wert))
            {
                col.Item().Height(13);
                col.Item().Element(Gestrichelt);
            }
            else
                col.Item().Text(wert).FontSize(10f).Bold();
        });
    }

    /// <summary>
    /// Feine gestrichelte Linie über die volle Breite — QuestPDF kennt keine
    /// gestrichelten Rahmen, darum 60 kurze Striche mit Lücken.
    /// </summary>
    private static void Gestrichelt(IContainer c)
    {
        c.Height(1).Row(r =>
        {
            for (var i = 0; i < 60; i++)
            {
                r.RelativeItem().BorderBottom(0.5f).BorderColor(Line);
                r.RelativeItem();
            }
        });
    }

    private static void Schreibzeile(IContainer c, string? label)
    {
        c.Column(col =>
        {
            // Handschrift-Zeile: grosszügiger Raum, gestrichelt (Walter 05.09.2026).
            if (label != null) col.Item().Text(label).FontSize(9.5f).Bold();
            col.Item().Height(22);
            col.Item().Element(Gestrichelt);
        });
    }


