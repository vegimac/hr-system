using System.Data;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace HrSystem.Services;

/// <summary>
/// Prueft beim Start, ob jede Spalte, die EF Core im Modell erwartet, in der
/// Datenbank auch wirklich existiert (Walter 31.08.2026).
///
/// WARUM: Dieses Projekt benutzt keine EF-Migrationen. Neue Felder brauchen
/// DREI Schritte, die leicht auseinanderlaufen:
///   1. Property am Model
///   2. ALTER TABLE ... ADD COLUMN IF NOT EXISTS in Program.cs
///   3. HasColumnName im AppDbContext
/// Fehlt Schritt 2 oder 3, kompiliert alles und alle Unit-Tests laufen durch —
/// der Fehler zeigt sich erst zur Laufzeit gegen die echte Datenbank, und dann
/// scheitert JEDE Abfrage auf die betroffene Tabelle. Am 31.08.2026 hat genau
/// das die Anmeldung lahmgelegt (fehlendes HasColumnName bei den
/// Oeffnungszeiten → EF suchte «OpeningMonFrom» statt «opening_mon_from»).
///
/// Diese Pruefung macht daraus einen lauten Fehler beim Deploy statt einer
/// kaputten Anmeldung. Sie wirft NIE — ein Problem beim Pruefen selbst darf
/// den Start nicht verhindern.
/// </summary>
public static class SchemaCheckService
{
    public record Ergebnis(
        bool Geprueft,
        List<string> FehlendeTabellen,
        List<string> FehlendeSpalten,
        int GepruefteTabellen,
        int GepruefteSpalten,
        string? Hinweis = null)
    {
        /// <summary>Gemeldet, aber bewusst akzeptiert — blockiert nicht.</summary>
        public List<string> Bekannt { get; init; } = new();

        /// <summary>
        /// DateTime-Felder, bei denen EF-Mapping und Datenbankspalte in der
        /// Zeitzonen-Frage auseinanderlaufen (Walter-Vorgabe 01.09.2026).
        /// Blockiert bewusst NICHT — es gibt Altbestand, und ob es knallt,
        /// haengt davon ab, ob jemand einen Lokalzeit-Wert hineinschreibt.
        /// Aber es steht ab jetzt bei JEDEM Start im Protokoll.
        /// </summary>
        public List<string> Zeitzonen { get; init; } = new();

        public bool Ok => Geprueft && FehlendeTabellen.Count == 0 && FehlendeSpalten.Count == 0;
        public int FehlerAnzahl => FehlendeTabellen.Count + FehlendeSpalten.Count;
    }

    /// <summary>
    /// PostgreSQL-Systemspalten. Sie existieren an JEDER Tabelle, tauchen aber
    /// nie in information_schema.columns auf. «xmin» wird hier als
    /// Nebenlaeufigkeits-Marke benutzt (UseXminAsConcurrencyToken) — ohne diese
    /// Ausnahme meldet die Pruefung es faelschlich als fehlend.
    /// </summary>
    private static readonly HashSet<string> Systemspalten =
        new(StringComparer.OrdinalIgnoreCase) { "xmin", "xmax", "cmin", "cmax", "ctid", "oid", "tableoid" };

    /// <summary>
    /// Bekannte, bewusst akzeptierte Abweichungen (Walter 31.08.2026).
    /// Sie werden gemeldet, blockieren den Deploy aber nicht — sonst koennte
    /// wegen Altlasten gar nichts mehr ausgeliefert werden. Jede Zeile braucht
    /// eine Begruendung; eine Abweichung OHNE Eintrag hier blockiert weiterhin.
    /// </summary>
    private static readonly Dictionary<string, string> Toleriert =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lohn_zulag_typ"] =
                "Altlast: durch Lohnposition ersetzt (siehe Models/Lohnposition.cs), "
                + "die Tabelle wird nirgends mehr abgefragt. DbSet bleibt vorerst stehen.",
        };

    /// <summary>Ergebnis des letzten Laufs — fuer /api/instance-info.</summary>
    public static Ergebnis? LetztesErgebnis { get; private set; }

    public static Ergebnis Pruefe(AppDbContext db, ILogger logger)
    {
        try
        {
            // Ein einziger Rundgang durch information_schema — deutlich
            // billiger als eine Abfrage pro Tabelle.
            var vorhanden = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var dbTypen   = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var conn = db.Database.GetDbConnection();
            var warGeschlossen = conn.State != ConnectionState.Open;
            if (warGeschlossen) conn.Open();
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT table_name, column_name, data_type FROM information_schema.columns "
                  + "WHERE table_schema = 'public'";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    var tabelle = rd.GetString(0);
                    var spalte  = rd.GetString(1);
                    var typ     = rd.GetString(2);
                    if (!vorhanden.TryGetValue(tabelle, out var set))
                        vorhanden[tabelle] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    set.Add(spalte);
                    dbTypen[tabelle + "." + spalte] = typ;
                }
            }
            finally
            {
                if (warGeschlossen) conn.Close();
            }

            var fehlendeTabellen = new List<string>();
            var fehlendeSpalten  = new List<string>();
            var bekannt          = new List<string>();
            var zeitzonen        = new List<string>();
            var anzTabellen = 0;
            var anzSpalten  = 0;

            foreach (var et in db.Model.GetEntityTypes())
            {
                var tabelle = et.GetTableName();
                // Views und besitzerlose Typen haben keinen Tabellennamen.
                if (string.IsNullOrWhiteSpace(tabelle)) continue;

                anzTabellen++;
                if (!vorhanden.TryGetValue(tabelle, out var spalten))
                {
                    if (Toleriert.TryGetValue(tabelle, out var grundT))
                    {
                        if (!bekannt.Any(b => b.StartsWith(tabelle + " ", StringComparison.Ordinal)))
                            bekannt.Add($"{tabelle} (ganze Tabelle)  —  {grundT}");
                    }
                    else if (!fehlendeTabellen.Contains(tabelle))
                    {
                        fehlendeTabellen.Add(tabelle);
                    }
                    continue;
                }

                var ziel = StoreObjectIdentifier.Table(tabelle, et.GetSchema());
                foreach (var prop in et.GetProperties())
                {
                    var spalte = prop.GetColumnName(ziel);
                    if (string.IsNullOrWhiteSpace(spalte)) continue;
                    // Systemspalten wie xmin gibt es immer, nur nicht im Katalog.
                    if (Systemspalten.Contains(spalte)) continue;
                    anzSpalten++;
                    if (spalten.Contains(spalte))
                    {
                        // ── Zeitzonen-Falle (Walter-Vorgabe 01.09.2026) ───────
                        // Der haeufigste Laufzeitfehler dieses Projekts, und der
                        // hinterhaeltigste: Die Spalte EXISTIERT, nur der Typ
                        // passt nicht. Ohne .HasColumnType("timestamp without
                        // time zone") mappt Npgsql einen DateTime auf
                        // «timestamp with time zone» und verlangt UTC — die App
                        // schreibt aber durchgehend Lokalzeit. Ergebnis:
                        // «Cannot write DateTime with Kind=Local», und zwar erst
                        // beim ersten echten Schreibvorgang im Betrieb.
                        // Die reine Spaltenpruefung sieht davon nichts.
                        var clr = Nullable.GetUnderlyingType(prop.ClrType) ?? prop.ClrType;
                        if (clr == typeof(DateTime)
                            && dbTypen.TryGetValue($"{tabelle}.{spalte}", out var dbTyp))
                        {
                            var efTyp = prop.GetColumnType() ?? "";
                            var dbMitZone = dbTyp.Contains("with time zone", StringComparison.OrdinalIgnoreCase);
                            var efMitZone = string.IsNullOrWhiteSpace(efTyp)
                                ? true   // ohne Angabe ist Npgsqls Vorgabe «with time zone»
                                : efTyp.Contains("with time zone", StringComparison.OrdinalIgnoreCase)
                                  && !efTyp.Contains("without time zone", StringComparison.OrdinalIgnoreCase);

                            if (dbMitZone != efMitZone)
                            {
                                zeitzonen.Add(
                                    $"{tabelle}.{spalte}  ←  {et.ClrType.Name}.{prop.Name}  "
                                  + $"(Datenbank: {dbTyp} / EF: {(string.IsNullOrWhiteSpace(efTyp) ? "ohne Angabe = with time zone" : efTyp)})");
                            }
                        }
                        continue;
                    }

                    var eintrag = $"{tabelle}.{spalte}  ←  {et.ClrType.Name}.{prop.Name}";
                    if (Toleriert.TryGetValue($"{tabelle}.{spalte}", out var grund))
                        bekannt.Add($"{eintrag}  —  {grund}");
                    else
                        fehlendeSpalten.Add(eintrag);
                }
            }

            var erg = new Ergebnis(true, fehlendeTabellen, fehlendeSpalten, anzTabellen, anzSpalten)
            { Bekannt = bekannt, Zeitzonen = zeitzonen };
            LetztesErgebnis = erg;

            foreach (var b in bekannt)
                logger.LogInformation("SCHEMA-PRUEFUNG   bekannt/toleriert: {Eintrag}", b);

            if (zeitzonen.Count > 0)
            {
                logger.LogWarning(
                    "SCHEMA-PRUEFUNG ZEITZONEN — {Anzahl} DateTime-Feld(er) mit unpassendem Spaltentyp. "
                    + "Diese scheitern zur LAUFZEIT beim Schreiben mit «Cannot write DateTime with Kind=Local». "
                    + "Fix: .HasColumnType(\"timestamp without time zone\") im AppDbContext ergaenzen.",
                    zeitzonen.Count);
                foreach (var z in zeitzonen)
                    logger.LogWarning("SCHEMA-PRUEFUNG   Zeitzone:  {Eintrag}", z);
            }

            if (erg.Ok)
            {
                logger.LogInformation(
                    "SCHEMA-PRUEFUNG ok — {Tabellen} Tabellen, {Spalten} Spalten stimmen mit der Datenbank ueberein.",
                    anzTabellen, anzSpalten);
            }
            else
            {
                logger.LogError(
                    "SCHEMA-PRUEFUNG FEHLER — {Anzahl} Abweichung(en) zwischen EF-Modell und Datenbank. "
                    + "Jede Abfrage auf die betroffenen Tabellen wird scheitern.", erg.FehlerAnzahl);
                foreach (var t in fehlendeTabellen)
                    logger.LogError("SCHEMA-PRUEFUNG   fehlende Tabelle: {Tabelle}", t);
                foreach (var sp in fehlendeSpalten)
                    logger.LogError("SCHEMA-PRUEFUNG   fehlende Spalte:  {Spalte}", sp);
                logger.LogError(
                    "SCHEMA-PRUEFUNG   Zu tun: ALTER TABLE ... ADD COLUMN IF NOT EXISTS in Program.cs ergaenzen "
                    + "UND HasColumnName im AppDbContext setzen.");
            }
            return erg;
        }
        catch (Exception ex)
        {
            // Die Pruefung ist eine Hilfe, kein Torwaechter: schlaegt sie
            // selbst fehl, laeuft die Anwendung normal weiter.
            var erg = new Ergebnis(false, new(), new(), 0, 0,
                ex.GetType().Name + ": " + ex.Message);
            LetztesErgebnis = erg;
            logger.LogWarning(ex, "SCHEMA-PRUEFUNG konnte nicht durchgefuehrt werden — uebersprungen.");
            return erg;
        }
    }
}
