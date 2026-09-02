using System.Globalization;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using iText.Kernel.Pdf;
using iText.Kernel.Utils;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Lohnlauf-Orchestrator. Orchestriert Vorab-PDF-Generierung,
/// Vorbedingungen-Checks und (in späteren Phasen) DTA-Erzeugung.
///
/// Vorab-PDF = alle MA-Lohnbelege einer Periode als ein einziges PDF.
/// Quelle: PayrollSnapshot.SlipJson (eingefroren beim provisorischen Abschluss).
/// Per MA wird das gleiche PayrollPdfService-Layout wie für den
/// individuellen Lohnbeleg verwendet — damit HR im Vorab-PDF exakt sieht
/// was später beim MA ankommt.
/// </summary>
public class LohnlaufService
{
    private readonly AppDbContext _db;
    private readonly PayrollPdfService _pdfSvc;
    private readonly StundenkontrollePdfService _stundenkontrollePdf;
    private readonly Iso20022PainService _painSvc;
    private readonly EmailService _emailSvc;
    private readonly IConfiguration _config;
    private readonly string _mailboxPath;

    public LohnlaufService(AppDbContext db,
                           PayrollPdfService pdfSvc,
                           StundenkontrollePdfService stundenkontrollePdf,
                           Iso20022PainService painSvc,
                           EmailService emailSvc,
                           IConfiguration config,
                           IWebHostEnvironment env)
    {
        _db = db;
        _pdfSvc = pdfSvc;
        _stundenkontrollePdf = stundenkontrollePdf;
        _painSvc = painSvc;
        _emailSvc = emailSvc;
        _config = config;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _mailboxPath = Path.Combine(configured, "mailbox");
        Directory.CreateDirectory(_mailboxPath);
    }

    public record ValidationResult(bool Ok, List<string> Issues);

    /// <summary>
    /// Vorbedingungen-Check vor dem Provisorischen Abschluss:
    ///   1. Periode-Status = offen.
    ///   2. Alle Vorperioden derselben Filiale = abgeschlossen.
    ///   3. Alle aktiven, nicht-payroll-excluded MA der Filiale haben einen
    ///      Snapshot mit IsFinal=false (sind im Lohn-Tab bestätigt).
    /// </summary>
    public async Task<ValidationResult> ValidateAsync(int periodeId)
    {
        var issues = new List<string>();
        var periode = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == periodeId);
        if (periode == null) return new ValidationResult(false, new() { "Periode nicht gefunden." });

        if (periode.Status != "offen")
            issues.Add($"Periode-Status ist '{periode.Status}', erwartet 'offen'.");

        // Vorperioden prüfen: alle früheren Perioden derselben Filiale müssen
        // abgeschlossen sein (sonst Saldo-Vortrag-Inkonsistenz).
        var prevOpen = await _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == periode.CompanyProfileId
                     && p.PeriodFrom < periode.PeriodFrom
                     && p.Status != "abgeschlossen")
            .OrderBy(p => p.PeriodFrom)
            .ToListAsync();
        foreach (var po in prevOpen)
            issues.Add($"Vorperiode '{po.Label}' ist noch im Status '{po.Status}'.");

        // Aktive, nicht-payroll-excluded MA der Filiale finden.
        // DateOnly.FromDateTime() ist nicht von EF Core / Npgsql übersetzbar —
        // daher Periode-Grenzen vorab in DateTime konvertieren für die Query.
        var periodFromDt = periode.PeriodFrom.ToDateTime(TimeOnly.MinValue);
        var periodToDt   = periode.PeriodTo.ToDateTime(TimeOnly.MaxValue);
        // Lebenszyklus eines Vertrages = ausschliesslich datum-basiert (Walter-Vorgabe 31.05.2026):
        // employment.IsActive ist redundant zu ContractStartDate/ContractEndDate und kann irreführen
        // (Zukunftsverträge laufen parallel zum aktuellen — beide brauchen IsActive=true). Filter
        // hier IGNORIERT IsActive bewusst, damit ein gerade ausgetretener MA noch im Austritts-Monat
        // im Lohnlauf erscheint, und ein neuer Vertrag in der Periode greift, auch wenn der alte
        // noch IsActive=true ist.
        var maMitVertrag = await _db.Employees
            .Where(e => e.IsActive
                     && !e.IsPayrollExcluded
                     && e.Employments.Any(emp => emp.CompanyProfileId == periode.CompanyProfileId
                                              && emp.ContractStartDate <= periodToDt
                                              && (!emp.ContractEndDate.HasValue
                                                  || emp.ContractEndDate.Value >= periodFromDt)))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
            .ToListAsync();

        var bestaetigte = periode.Snapshots.Select(s => s.EmployeeId).ToHashSet();
        var fehlend = maMitVertrag.Where(e => !bestaetigte.Contains(e.Id)).ToList();
        foreach (var f in fehlend)
            issues.Add($"Lohn nicht bestätigt: {f.FirstName} {f.LastName} (Nr. {f.EmployeeNumber}).");

        return new ValidationResult(issues.Count == 0, issues);
    }

    /// <summary>
    /// Generiert das Vorab-Kontroll-PDF: pro MA-Snapshot wird der individuelle
    /// Lohnbeleg gerendert, danach mit iText alle PDFs zu einem zusammen-
    /// gemerged. Reihenfolge: nach Vorname (gleich wie MA-Liste).
    /// </summary>
    public async Task<byte[]> GenerateVorabPdfAsync(int periodeId)
    {
        var snapshots = await _db.PayrollSnapshots
            .Include(s => s.Employee)
            .Where(s => s.PayrollPeriodeId == periodeId)
            .ToListAsync();

        if (snapshots.Count == 0)
            throw new InvalidOperationException("Keine Snapshots in dieser Periode — kein Vorab-PDF generierbar.");

        // Sortierung: Vorname → Nachname (konsistent mit MA-Liste in der UI)
        snapshots = snapshots
            .OrderBy(s => (s.Employee?.FirstName ?? "").ToLowerInvariant())
            .ThenBy(s => (s.Employee?.LastName ?? "").ToLowerInvariant())
            .ToList();

        // Periode laden für gemeinsames printDate
        var periode = await _db.PayrollPerioden.FindAsync(periodeId);
        DateTime? frozenDate = periode?.Status == "abgeschlossen"
            ? periode.AbgeschlossenAm
            : periode?.ProvisorischAbgeschlossenAm;
        string? frozenDateStr = frozenDate?.ToLocalTime().ToString("dd.MM.yyyy");

        // Pro Snapshot ein PDF erzeugen
        var perMaPdfs = new List<byte[]>(snapshots.Count);
        foreach (var snap in snapshots)
        {
            try
            {
                System.Text.Json.JsonElement element;
                if (frozenDateStr != null)
                {
                    // printDate auf Periode-Abschluss-Datum überschreiben damit
                    // alle Lohnbelege im Vorab-PDF dasselbe "Erstellt am"-Datum
                    // tragen — wirkt wie ein konsistenter Druck-Stempel.
                    var node = System.Text.Json.Nodes.JsonNode.Parse(snap.SlipJson)!.AsObject();
                    node["printDate"] = frozenDateStr;
                    element = System.Text.Json.JsonSerializer.SerializeToElement(node);
                }
                else
                {
                    using var doc = JsonDocument.Parse(snap.SlipJson);
                    element = doc.RootElement.Clone();
                }
                var bytes = _pdfSvc.Generate(element);
                perMaPdfs.Add(bytes);
            }
            catch
            {
                // Wenn ein einzelner Snapshot kaputtes JSON hat, übersprungen —
                // sonst würde der gesamte Vorab-Lauf für 60 MA wegen einem
                // einzelnen Datenproblem failen. Der MA fehlt dann im Vorab-
                // PDF; das Problem zeigt sich beim Vorbedingungen-Check der
                // nächsten Phase.
            }
        }

        return MergePdfs(perMaPdfs);
    }

    // ── DTA-Generierung (ISO 20022 pain.001) ─────────────────────────────

    /// <summary>
    /// Generiert das pain.001-XML für die MA-Lohn-Auszahlungen einer Periode.
    /// Aus jedem Snapshot werden die "BANK"-Empfänger des auszahlungEmpfaenger-
    /// Arrays gezogen. Bei abweichendem Empfänger (Kontoinhaber gesetzt, z.B.
    /// Revolut) wird dessen Adresse als Cdtr verwendet, MA-Name in RmtInf.
    /// </summary>
    public async Task<byte[]> GenerateDtaMaAsync(int periodeId)
    {
        var periode = await LoadPeriodeForDtaAsync(periodeId);
        var dbtrBank = await GetMainCompanyBankAsync(periode);

        // EmployeeBankAccounts vorab laden für Cdtr-Adressen-Lookup
        var empIds = periode.Snapshots.Select(s => s.EmployeeId).Distinct().ToList();
        var bankAccountsByEmp = await _db.EmployeeBankAccounts
            .Where(b => empIds.Contains(b.EmployeeId)
                     && b.ValidFrom <= periode.PeriodTo
                     && (b.ValidTo == null || b.ValidTo >= periode.PeriodFrom))
            .ToListAsync();

        var payments = new List<Iso20022PainService.PaymentInstruction>();
        foreach (var snap in periode.Snapshots
                                  .Where(s => s.IsFinal)
                                  .OrderBy(s => s.Employee?.FirstName))
        {
            if (snap.Employee == null) continue;
            using var doc = JsonDocument.Parse(snap.SlipJson);
            if (!doc.RootElement.TryGetProperty("auszahlungEmpfaenger", out var aeArr)) continue;

            foreach (var ae in aeArr.EnumerateArray())
            {
                var typ = TryStr(ae, "typ");
                if (typ != "BANK") continue;
                var iban = TryStr(ae, "iban");
                if (string.IsNullOrWhiteSpace(iban)) continue;
                var betrag = TryDec(ae, "betrag");
                if (betrag <= 0) continue;
                var referenz = TryStr(ae, "referenz");

                // Match EmployeeBankAccount by IBAN für Cdtr-Adresse + ggf. abw. Empfänger
                var bankAcct = bankAccountsByEmp.FirstOrDefault(b =>
                    b.EmployeeId == snap.EmployeeId &&
                    NormalizeIban(b.Iban) == NormalizeIban(iban));

                string  cdtrName;
                string? cdtrStreet, cdtrPlz, cdtrCity, cdtrCountry, cdtrBic;
                string  rmtInf;
                var maName = $"{snap.Employee.FirstName} {snap.Employee.LastName}".Trim();

                if (bankAcct != null && !string.IsNullOrWhiteSpace(bankAcct.Kontoinhaber))
                {
                    // Abweichender Empfänger (Revolut Bank UAB etc.)
                    cdtrName    = bankAcct.Kontoinhaber!;
                    cdtrStreet  = bankAcct.KontoinhaberStrasse;
                    cdtrPlz     = bankAcct.KontoinhaberPlz;
                    cdtrCity    = bankAcct.KontoinhaberOrt;
                    cdtrCountry = string.IsNullOrWhiteSpace(bankAcct.KontoinhaberLand)
                                      ? "CH" : bankAcct.KontoinhaberLand!;
                    cdtrBic     = bankAcct.Bic;
                    rmtInf      = string.IsNullOrWhiteSpace(referenz)
                                      ? $"Lohn {periode.Label} - {maName}"
                                      : $"{referenz} - Lohn {periode.Label}";
                }
                else
                {
                    // MA selbst
                    cdtrName   = maName;
                    cdtrStreet = snap.Employee.Street;
                    cdtrPlz    = snap.Employee.ZipCode;
                    cdtrCity   = snap.Employee.City;
                    cdtrCountry = string.IsNullOrWhiteSpace(snap.Employee.Country)
                                      ? "CH" : snap.Employee.Country!;
                    cdtrBic    = bankAcct?.Bic;
                    rmtInf     = string.IsNullOrWhiteSpace(referenz)
                                      ? $"Lohn {periode.Label}"
                                      : referenz!;
                }

                payments.Add(new Iso20022PainService.PaymentInstruction(
                    EndToEndId:         $"L{periode.Year}{periode.Month:D2}-{snap.EmployeeId}",
                    Amount:             Math.Round(betrag, 2),
                    CreditorName:       cdtrName,
                    CreditorStreet:     cdtrStreet,
                    CreditorPostalCode: cdtrPlz,
                    CreditorCity:       cdtrCity,
                    CreditorCountry:    cdtrCountry,
                    CreditorIban:       iban!,
                    CreditorBic:        cdtrBic,
                    RemittanceInfo:     rmtInf
                ));
            }
        }

        if (payments.Count == 0)
            throw new InvalidOperationException("Keine MA-Auszahlungen in dieser Periode — DTA leer.");

        return _painSvc.Generate(BuildDtaRequest(periode, dbtrBank, payments,
            messagePrefix: "LOHN-MA"));
    }

    /// <summary>
    /// Generiert das pain.001-XML für Lohnabtretungs-Empfänger (Behörden).
    /// Aus jedem Snapshot werden die "BEHOERDE"-Empfänger gezogen und je
    /// Behörde aggregiert (mehrere MA können auf dieselbe Behörde abdrücken).
    /// </summary>
    public async Task<byte[]> GenerateDtaBehoerdenAsync(int periodeId)
    {
        var periode = await LoadPeriodeForDtaAsync(periodeId);
        var dbtrBank = await GetMainCompanyBankAsync(periode);

        // Aggregation: pro IBAN ein Behörden-Empfänger mit Summe der Beträge.
        // referenz wird zum ersten gültigen Wert genommen (üblicherweise gleiche
        // Schuldner-Referenz pro Behörde, falls unterschiedlich → erste).
        var behoerdenAgg = new Dictionary<string, (string Label, string? Referenz, decimal Total, List<string> MaNamen)>(StringComparer.OrdinalIgnoreCase);

        foreach (var snap in periode.Snapshots.Where(s => s.IsFinal))
        {
            if (snap.Employee == null) continue;
            using var doc = JsonDocument.Parse(snap.SlipJson);
            if (!doc.RootElement.TryGetProperty("auszahlungEmpfaenger", out var aeArr)) continue;

            foreach (var ae in aeArr.EnumerateArray())
            {
                var typ = TryStr(ae, "typ");
                if (typ != "BEHOERDE") continue;
                var iban = TryStr(ae, "iban");
                if (string.IsNullOrWhiteSpace(iban)) continue;
                var betrag = TryDec(ae, "betrag");
                if (betrag <= 0) continue;

                var key = NormalizeIban(iban);
                var label = TryStr(ae, "label") ?? "Behörde";
                var referenz = TryStr(ae, "referenz");
                var maName = $"{snap.Employee.FirstName} {snap.Employee.LastName}".Trim();

                if (behoerdenAgg.TryGetValue(key, out var existing))
                {
                    existing.MaNamen.Add(maName);
                    behoerdenAgg[key] = (existing.Label,
                                        existing.Referenz ?? referenz,
                                        existing.Total + betrag,
                                        existing.MaNamen);
                }
                else
                {
                    behoerdenAgg[key] = (label, referenz, betrag, new List<string> { maName });
                }
            }
        }

        if (behoerdenAgg.Count == 0)
            throw new InvalidOperationException("Keine Lohnabtretungen in dieser Periode — Behörden-DTA leer.");

        // Pro IBAN: Behörde-Stammdaten. Bei geteilter IBAN (ORS Burgdorf →
        // Kontoinhaber-Behörde ORS Zürich) Cdtr = verknüpfte Behörde
        // (Name + Adresse/PLZ/Ort).
        var allBehoerden = await _db.Behoerden
            .Where(b => b.IsActive)
            .ToListAsync();

        var payments = new List<Iso20022PainService.PaymentInstruction>();
        int idx = 0;
        foreach (var (key, agg) in behoerdenAgg)
        {
            var matches = allBehoerden
                .Where(b => NormalizeIban(b.QrIban ?? b.Iban ?? "") == key)
                .ToList();
            // Prefer Behörde mit Kontoinhaber-Verknüpfung (ORS-Fall).
            var beh = matches.FirstOrDefault(b => b.KontoinhaberBehoerdeId != null)
                   ?? matches.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.Kontoinhaber))
                   ?? matches.FirstOrDefault();

            Behoerde? cdtr = null;
            if (beh?.KontoinhaberBehoerdeId is int kid)
                cdtr = allBehoerden.FirstOrDefault(b => b.Id == kid);
            if (cdtr == null && !string.IsNullOrWhiteSpace(beh?.Kontoinhaber))
            {
                var ki = beh!.Kontoinhaber!.Trim();
                cdtr = allBehoerden.FirstOrDefault(b =>
                    string.Equals((b.Name ?? "").Trim(), ki, StringComparison.OrdinalIgnoreCase));
            }
            cdtr ??= beh;

            var cdtrName = cdtr?.Name ?? agg.Label;

            // Zusatz-Info im RmtInf: betroffene MA, falls > 1
            string rmtInf;
            if (!string.IsNullOrWhiteSpace(agg.Referenz))
            {
                rmtInf = agg.Referenz!;
            }
            else
            {
                var maList = string.Join(", ", agg.MaNamen.Take(3));
                if (agg.MaNamen.Count > 3) maList += $" + {agg.MaNamen.Count - 3} weitere";
                rmtInf = $"Lohnabtretung {periode.Label} - {maList}";
            }

            payments.Add(new Iso20022PainService.PaymentInstruction(
                EndToEndId:         $"BH{periode.Year}{periode.Month:D2}-{idx++}",
                Amount:             Math.Round(agg.Total, 2),
                CreditorName:       cdtrName,
                CreditorStreet:     cdtr?.Adresse1,
                CreditorPostalCode: cdtr?.Plz,
                CreditorCity:       cdtr?.Ort,
                CreditorCountry:    "CH",
                CreditorIban:       key,
                CreditorBic:        cdtr?.Bic ?? beh?.Bic,
                RemittanceInfo:     rmtInf
            ));
        }

        return _painSvc.Generate(BuildDtaRequest(periode, dbtrBank, payments,
            messagePrefix: "LOHN-BH"));
    }

    // ── DTA-Helpers ──────────────────────────────────────────────────────

    private async Task<PayrollPeriode> LoadPeriodeForDtaAsync(int periodeId)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Company)
            .Include(p => p.Snapshots).ThenInclude(s => s.Employee)
            .FirstOrDefaultAsync(p => p.Id == periodeId);
        if (periode == null) throw new InvalidOperationException("Periode nicht gefunden.");
        if (periode.Company == null) throw new InvalidOperationException("Filiale nicht gefunden.");
        if (periode.Status != "abgeschlossen")
            throw new InvalidOperationException(
                $"Periode ist im Status '{periode.Status}' — DTA nur aus 'abgeschlossen' generierbar.");
        if (periode.Auszahlungsdatum == null)
            throw new InvalidOperationException("Auszahlungsdatum fehlt — beim definitiven Abschluss erfassen.");
        return periode;
    }

    private async Task<CompanyProfileBankAccount> GetMainCompanyBankAsync(PayrollPeriode periode)
    {
        var dbtrBank = await _db.CompanyProfileBankAccounts
            .Where(b => b.CompanyProfileId == periode.CompanyProfileId
                     && b.IsMain
                     && b.ValidFrom <= periode.PeriodTo
                     && (b.ValidTo == null || b.ValidTo >= periode.PeriodFrom))
            .OrderByDescending(b => b.ValidFrom)
            .FirstOrDefaultAsync();
        if (dbtrBank == null)
            throw new InvalidOperationException(
                "Filiale hat keine gültige Hauptbank für diese Periode. " +
                "Bitte unter Filiale → Bankverbindungen erfassen + als Hauptbank markieren.");
        return dbtrBank;
    }

    private static Iso20022PainService.DtaRequest BuildDtaRequest(
        PayrollPeriode periode,
        CompanyProfileBankAccount dbtrBank,
        IReadOnlyList<Iso20022PainService.PaymentInstruction> payments,
        string messagePrefix)
    {
        var initiator = string.IsNullOrWhiteSpace(periode.Company!.BranchName)
            ? periode.Company.CompanyName
            : $"{periode.Company.CompanyName} {periode.Company.BranchName}";
        var initStreet = string.IsNullOrWhiteSpace(periode.Company.HouseNumber)
            ? periode.Company.Street
            : $"{periode.Company.Street} {periode.Company.HouseNumber}".Trim();

        return new Iso20022PainService.DtaRequest(
            MessageId:           $"{messagePrefix}-{periode.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            CreationDateTime:    DateTime.UtcNow,
            InitiatorName:       initiator,
            InitiatorStreet:     initStreet,
            InitiatorPostalCode: periode.Company.ZipCode,
            InitiatorCity:       periode.Company.City,
            InitiatorCountry:    string.IsNullOrWhiteSpace(periode.Company.Country) ? "CH" : periode.Company.Country!,
            ExecutionDate:       periode.Auszahlungsdatum!.Value,
            DebtorName:          initiator,
            DebtorIban:          dbtrBank.Iban,
            DebtorBic:           dbtrBank.Bic,
            Payments:            payments
        );
    }

    private static string TryStr(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static decimal TryDec(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetDecimal(out var d) ? d : 0,
            JsonValueKind.String => decimal.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) ? s : 0,
            _ => 0
        };
    }

    private static string NormalizeIban(string? iban)
        => string.IsNullOrWhiteSpace(iban) ? "" : new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();

    // ── Posteingang-Auto-Versand ──────────────────────────────────────────

    /// <summary>
    /// Schreibt das Vorab-PDF einer Periode ins HR-Postfach. Wird beim
    /// provisorischen Abschluss automatisch aufgerufen — HR sieht die Datei
    /// dann sofort im Posteingang.
    ///
    /// Wirft KEINE Exceptions raus — der Posteingang-Versand soll den
    /// Periode-Abschluss nicht blockieren wenn was schief geht. Schwerwiegende
    /// Fehler werden via stderr geloggt, damit Walter sie im journalctl sieht.
    /// </summary>
    public async Task TrySendVorabPdfToHrAsync(int periodeId, int senderUserId)
    {
        try
        {
            var periode = await _db.PayrollPerioden
                .Include(p => p.Company)
                .FirstOrDefaultAsync(p => p.Id == periodeId);
            if (periode == null) return;

            var pdfBytes = await GenerateVorabPdfAsync(periodeId);
            if (pdfBytes.Length == 0) return;

            // Datei im Mailbox-Storage ablegen — gleiches Schema wie MailboxController
            var branchDir = Path.Combine(_mailboxPath, periode.CompanyProfileId.ToString());
            Directory.CreateDirectory(branchDir);
            var storageName = Guid.NewGuid().ToString("N") + ".pdf";
            var fullPath = Path.Combine(branchDir, storageName);
            await File.WriteAllBytesAsync(fullPath, pdfBytes);

            var sender = await _db.AppUsers.FindAsync(senderUserId);
            var senderName = sender?.Username ?? sender?.Email ?? $"User {senderUserId}";
            var filialName = string.IsNullOrWhiteSpace(periode.Company?.BranchName)
                ? periode.Company?.CompanyName ?? "Filiale"
                : periode.Company.BranchName!;

            var origFileName = $"Lohnlauf_Vorab_{filialName}_{periode.Year}-{periode.Month:D2}.pdf"
                .Replace(" ", "_");

            var bemerkung = $"Provisorischer Lohnabschluss durch {senderName} am {DateTime.Now:dd.MM.yyyy HH:mm}. " +
                            $"Bitte 4-Augen-Kontrolle und definitiv abschliessen, oder bei Korrekturbedarf an GF zurückgeben.";

            _db.MailboxDocuments.Add(new MailboxDocument
            {
                CompanyProfileId = periode.CompanyProfileId,
                UploadedBy       = senderUserId == 0 ? null : senderUserId,
                UploadedAt       = DateTime.Now,
                OriginalFilename = origFileName,
                StorageFilename  = storageName,
                MimeType         = "application/pdf",
                FileSizeBytes    = pdfBytes.Length,
                Bemerkung        = bemerkung,
                EmployeeId       = null,
                NotifyUserId     = null,
                TargetType       = "HR",
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LohnlaufService] Vorab-PDF-Posteingang-Versand fehlgeschlagen für Periode {periodeId}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// Versendet pro Mitarbeiter den Lohnzettel als PDF in dessen
    /// persönliches Postfach (MailboxDocument, TargetType="EMPLOYEE").
    /// Wird beim definitiven Lohnabschluss aufgerufen.
    ///
    /// Idempotent: bei Wieder-Öffnen + erneutem Abschluss werden alte
    /// Lohnzettel der gleichen Periode gelöscht (alte Storage-Datei
    /// auch) und durch neue ersetzt.
    ///
    /// Wirft KEINE Exceptions raus — der Versand soll den Periode-
    /// Abschluss nicht blockieren wenn was schief geht.
    /// </summary>
    public async Task TryDispatchLohnzettelToMaPostfaecherAsync(int periodeId, int senderUserId)
    {
        try
        {
            var periode = await _db.PayrollPerioden
                .Include(p => p.Company)
                .FirstOrDefaultAsync(p => p.Id == periodeId);
            if (periode == null) return;

            var snapshots = await _db.PayrollSnapshots
                .Include(s => s.Employee)
                .Where(s => s.PayrollPeriodeId == periodeId)
                .ToListAsync();
            if (snapshots.Count == 0) return;

            // printDate für alle Lohnzettel auf Abschluss-Datum festschreiben
            DateTime? frozenDate = periode.AbgeschlossenAm ?? periode.ProvisorischAbgeschlossenAm;
            string? frozenDateStr = frozenDate?.ToLocalTime().ToString("dd.MM.yyyy");

            // Periode-Bezeichner für die Bemerkung (z.B. "März 2026")
            var monthNames = new[] {
                "Januar","Februar","März","April","Mai","Juni",
                "Juli","August","September","Oktober","November","Dezember"
            };
            var monatLabel = periode.Month >= 1 && periode.Month <= 12
                ? $"{monthNames[periode.Month - 1]} {periode.Year}"
                : $"{periode.Year}-{periode.Month:D2}";

            // Idempotent: alte Lohnzettel dieser Periode raus (bei Re-Abschluss).
            await DeleteLohnzettelFromMaPostfaecherInternalAsync(periode.CompanyProfileId, periode.Year, periode.Month);

            int erfolgreich = 0;
            int fehler = 0;
            foreach (var snap in snapshots)
            {
                try
                {
                    // PDF aus Snapshot rendern (printDate auf Abschluss-Datum)
                    System.Text.Json.JsonElement element;
                    if (frozenDateStr != null)
                    {
                        var node = System.Text.Json.Nodes.JsonNode.Parse(snap.SlipJson)!.AsObject();
                        node["printDate"] = frozenDateStr;
                        element = System.Text.Json.JsonSerializer.SerializeToElement(node);
                    }
                    else
                    {
                        using var doc = JsonDocument.Parse(snap.SlipJson);
                        element = doc.RootElement.Clone();
                    }
                    var pdfBytes = _pdfSvc.Generate(element);
                    if (pdfBytes.Length == 0) { fehler++; continue; }

                    // Datei im Mailbox-Storage ablegen — gleiches Schema wie HR-Versand
                    var branchDir = Path.Combine(_mailboxPath, periode.CompanyProfileId.ToString());
                    Directory.CreateDirectory(branchDir);
                    var storageName = Guid.NewGuid().ToString("N") + ".pdf";
                    var fullPath = Path.Combine(branchDir, storageName);
                    await File.WriteAllBytesAsync(fullPath, pdfBytes);

                    // Filename: "Lohnzettel_2026-03_750009.pdf" — die Periode-
                    // Komponente ist der Idempotenz-Marker oben.
                    var empNr = snap.Employee?.EmployeeNumber ?? snap.EmployeeId.ToString();
                    var origFileName = $"Lohnzettel_{periode.Year}-{periode.Month:D2}_{empNr}.pdf";

                    _db.MailboxDocuments.Add(new MailboxDocument
                    {
                        CompanyProfileId = periode.CompanyProfileId,
                        UploadedBy       = senderUserId == 0 ? null : senderUserId,
                        UploadedAt       = DateTime.Now,
                        OriginalFilename = origFileName,
                        StorageFilename  = storageName,
                        MimeType         = "application/pdf",
                        FileSizeBytes    = pdfBytes.Length,
                        Bemerkung        = $"Lohnzettel {monatLabel}",
                        EmployeeId       = snap.EmployeeId,
                        NotifyUserId     = null,
                        TargetType       = "EMPLOYEE",
                    });

                    // Monatsblatt Stundenkontrolle (Walter 01.08.2026): mit dem
                    // Lohnzettel, damit der MA seine Stunden kontrolliert und
                    // unterschreibt. Best-effort — Fehler blockieren den Lohnzettel nicht.
                    try
                    {
                        var skBytes = await _stundenkontrollePdf.GenerateAsync(
                            snap.EmployeeId, periode.Year, periode.Month,
                            periode.CompanyProfileId, element);
                        if (skBytes.Length > 0)
                        {
                            var skStorage = Guid.NewGuid().ToString("N") + ".pdf";
                            var skPath = Path.Combine(branchDir, skStorage);
                            await File.WriteAllBytesAsync(skPath, skBytes);
                            _db.MailboxDocuments.Add(new MailboxDocument
                            {
                                CompanyProfileId = periode.CompanyProfileId,
                                UploadedBy       = senderUserId == 0 ? null : senderUserId,
                                UploadedAt       = DateTime.Now,
                                OriginalFilename = $"Stundenkontrolle_{periode.Year}-{periode.Month:D2}_{empNr}.pdf",
                                StorageFilename  = skStorage,
                                MimeType         = "application/pdf",
                                FileSizeBytes    = skBytes.Length,
                                Bemerkung        = $"Stundenkontrolle {monatLabel} — bitte kontrollieren und unterschreiben",
                                EmployeeId       = snap.EmployeeId,
                                NotifyUserId     = null,
                                TargetType       = "EMPLOYEE",
                            });
                        }
                    }
                    catch (Exception exSk)
                    {
                        Console.Error.WriteLine($"[LohnlaufService] Stundenkontrolle für MA {snap.EmployeeId} fehlgeschlagen: {exSk.Message}");
                    }

                    erfolgreich++;
                }
                catch (Exception exMa)
                {
                    fehler++;
                    Console.Error.WriteLine($"[LohnlaufService] Lohnzettel-Versand für MA {snap.EmployeeId} fehlgeschlagen: {exMa.Message}");
                }
            }
            await _db.SaveChangesAsync();
            Console.Error.WriteLine($"[LohnlaufService] Lohnzettel-Versand Periode {periodeId}: {erfolgreich} erfolgreich, {fehler} Fehler.");

            // E-Mail-Versand passiert NICHT mehr hier inline — der Controller
            // ruft TrySendLohnzettelEmailsAsync separat als Fire-and-Forget
            // auf, damit der HTTP-Request für den Definitiv-Abschluss schnell
            // zurückkommt (Modal schliesst sofort, statt 2+ Minuten auf den
            // Mail-Loop zu warten).
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LohnlaufService] Lohnzettel-Postfach-Versand fehlgeschlagen für Periode {periodeId}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// E-Mail-Benachrichtigung an alle MA mit hinterlegter Email-Adresse.
    /// Wird als Fire-and-Forget vom Controller aufgerufen (NACH dem
    /// PDF-Versand und NACHDEM die HTTP-Response schon zurückging).
    ///
    /// Wichtig: dieser Service ist Scoped — wenn er als Hintergrund-Task
    /// läuft, MUSS der Aufrufer (Controller) eine neue DI-Scope erstellen
    /// und den Service da raus auflösen, damit DbContext und EmailService
    /// nicht aus dem mittlerweile beendeten Request-Scope kommen.
    ///
    /// Sequenzieller Versand mit kurzem Delay zwischen Mails, damit
    /// SMTP-Server den Massenversand nicht als Spam-Risiko wertet.
    /// Im Test-Modus gehen alle Mails an die Test-Adresse.
    /// </summary>
    public async Task TrySendLohnzettelEmailsAsync(int periodeId)
    {
        try
        {
            var periode = await _db.PayrollPerioden.FirstOrDefaultAsync(p => p.Id == periodeId);
            if (periode == null) return;

            var snapshots = await _db.PayrollSnapshots
                .Include(s => s.Employee)
                .Where(s => s.PayrollPeriodeId == periodeId)
                .ToListAsync();

            int mailsSent = 0;
            int mailsSkipped = 0;
            foreach (var snap in snapshots)
            {
                var email = snap.Employee?.Email;
                if (string.IsNullOrWhiteSpace(email)) { mailsSkipped++; continue; }
                try
                {
                    await _emailSvc.SendLohnzettelNotificationAsync(
                        email!, snap.Employee?.FirstName ?? "", periode.Year, periode.Month,
                        employeeId: snap.Employee?.Id);
                    mailsSent++;
                    // Kurze Pause zwischen Mails (~0.5s) um SMTP-Throttling zu vermeiden
                    await Task.Delay(500);
                }
                catch (Exception mex)
                {
                    Console.Error.WriteLine($"[LohnlaufService] Mail an {email} fehlgeschlagen: {mex.Message}");
                }
            }
            Console.Error.WriteLine($"[LohnlaufService] Lohnzettel-Mail-Versand Periode {periodeId}: {mailsSent} gesendet, {mailsSkipped} ohne Email-Adresse.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LohnlaufService] Hintergrund-Mail-Versand fehlgeschlagen für Periode {periodeId}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// Beim Definitiv-Abschluss: Download-Link (kein PDF-Anhang) an Behörden,
    /// bei denen die Lohnabtretung «Lohnausweis an Behörde» aktiviert ist
    /// (Walter 30.07.2026). Fire-and-Forget vom Controller — eigene DI-Scope.
    /// </summary>
    public async Task TrySendLohnausweisLinksToBehoerdenAsync(int periodeId)
    {
        const int expiryDays = 90;
        try
        {
            var periode = await _db.PayrollPerioden.FirstOrDefaultAsync(p => p.Id == periodeId);
            if (periode == null) return;

            var year = periode.Year;
            var periodFrom = periode.PeriodFrom;
            var periodTo = periode.PeriodTo;

            var empIds = await _db.PayrollSnapshots
                .Where(s => s.PayrollPeriodeId == periodeId && s.Status != "STORNIERT")
                .Select(s => s.EmployeeId)
                .Distinct()
                .ToListAsync();
            if (empIds.Count == 0) return;

            // Abtretungen mit Flag, die die Periode überlappen.
            // E-Mail: zuerst Sachbearbeiter, sonst Behörden-Zentrale (Walter 02.08.2026).
            var assignments = await _db.EmployeeLohnAssignments
                .Include(a => a.Behoerde)
                .Include(a => a.Sachbearbeiter)
                .Include(a => a.Employee)
                .Where(a => empIds.Contains(a.EmployeeId)
                         && a.LohnausweisAnBehoerde
                         && a.DokumentId != null
                         && a.ValidFrom <= periodTo
                         && (a.ValidTo == null || a.ValidTo >= periodFrom)
                         && a.Behoerde != null)
                .ToListAsync();
            if (assignments.Count == 0) return;

            var cfg = await _emailSvc.GetEffectiveConfigAsync();
            var siteBase = string.IsNullOrWhiteSpace(cfg.SiteUrl)
                ? "https://onecrew.ch"
                : cfg.SiteUrl.TrimEnd('/');

            int sent = 0, skipped = 0;
            foreach (var a in assignments)
            {
                try
                {
                    // Ohne Jahres-Snapshots kein sinnvoller Lohnausweis.
                    var yearStart = new DateOnly(year, 1, 1);
                    var yearEnd = new DateOnly(year, 12, 31);
                    var hasSnapshots = await _db.PayrollSnapshots
                        .AnyAsync(s => s.EmployeeId == a.EmployeeId
                                    && s.Periode != null
                                    && s.Periode.PeriodFrom >= yearStart
                                    && s.Periode.PeriodFrom <= yearEnd
                                    && s.Status != "STORNIERT");
                    if (!hasSnapshots) { skipped++; continue; }

                    var email = (a.Sachbearbeiter?.Email ?? a.Behoerde!.Email ?? "").Trim();
                    if (string.IsNullOrWhiteSpace(email)) { skipped++; continue; }

                    // Vorherige Tokens derselben Abtretung+Jahr widerrufen (Re-Abschluss).
                    var oldTokens = await _db.LohnausweisShareTokens
                        .Where(t => t.EmployeeLohnAssignmentId == a.Id
                                 && t.Year == year
                                 && t.RevokedAt == null)
                        .ToListAsync();
                    foreach (var ot in oldTokens)
                        ot.RevokedAt = DateTime.Now;

                    var (token, hash) = ShareTokenUtil.NewToken();
                    var expiresAt = DateTime.Now.AddDays(expiryDays);
                    _db.LohnausweisShareTokens.Add(new LohnausweisShareToken
                    {
                        EmployeeId = a.EmployeeId,
                        BehoerdeId = a.BehoerdeId,
                        EmployeeLohnAssignmentId = a.Id,
                        PayrollPeriodeId = periodeId,
                        Year = year,
                        TokenHash = hash,
                        ExpiresAt = expiresAt,
                        CreatedAt = DateTime.Now,
                    });
                    await _db.SaveChangesAsync();

                    var url = $"{siteBase}/lohnausweis/{token}";
                    var maName = $"{a.Employee?.FirstName} {a.Employee?.LastName}".Trim();
                    if (string.IsNullOrWhiteSpace(maName)) maName = "Mitarbeiter/in";

                    await _emailSvc.SendLohnausweisBehoerdeNotificationAsync(
                        email,
                        a.Behoerde!.Name,
                        maName,
                        year,
                        url,
                        expiresAt,
                        a.Sachbearbeiter?.Name);
                    sent++;
                    await Task.Delay(500);
                }
                catch (Exception mex)
                {
                    Console.Error.WriteLine(
                        $"[LohnlaufService] Lohnausweis-Link an Behörde (Assignment {a.Id}) fehlgeschlagen: {mex.Message}");
                }
            }
            Console.Error.WriteLine(
                $"[LohnlaufService] Lohnausweis-Behörden-Links Periode {periodeId}: {sent} gesendet, {skipped} übersprungen.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[LohnlaufService] Lohnausweis-Behörden-Versand fehlgeschlagen für Periode {periodeId}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// Entfernt alle Lohnzettel einer Periode aus den MA-Postfächern.
    /// Wird beim Wieder-Öffnen einer definitiv abgeschlossenen Periode
    /// aufgerufen (z.B. Korrekturbedarf entdeckt nachdem MA bereits den
    /// Lohnzettel gesehen hat). Verhindert dass der MA mit einer falschen
    /// Lohn-Version zurückbleibt.
    ///
    /// Wirft KEINE Exceptions raus.
    /// </summary>
    public async Task TryDeleteLohnzettelFromMaPostfaecherAsync(int periodeId)
    {
        try
        {
            var periode = await _db.PayrollPerioden.FindAsync(periodeId);
            if (periode == null) return;
            await DeleteLohnzettelFromMaPostfaecherInternalAsync(periode.CompanyProfileId, periode.Year, periode.Month);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[LohnlaufService] Lohnzettel-Postfach-Löschung fehlgeschlagen für Periode {periodeId}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
    }

    /// <summary>
    /// Interner Helper: löscht alle MailboxDocuments mit TargetType=EMPLOYEE
    /// für die gegebene (Filiale, Periode), und entfernt die zugehörigen
    /// Storage-Dateien. Marker im OriginalFilename:
    /// «Lohnzettel_{Year}-{Month:D2}» und «Stundenkontrolle_{Year}-{Month:D2}».
    /// </summary>
    private async Task DeleteLohnzettelFromMaPostfaecherInternalAsync(int companyProfileId, int year, int month)
    {
        var lohnMarker = $"Lohnzettel_{year}-{month:D2}";
        var skMarker   = $"Stundenkontrolle_{year}-{month:D2}";
        var altePostfachLohnzettel = await _db.MailboxDocuments
            .Where(m => m.TargetType == "EMPLOYEE"
                     && m.CompanyProfileId == companyProfileId
                     && m.OriginalFilename != null
                     && (m.OriginalFilename.Contains(lohnMarker)
                      || m.OriginalFilename.Contains(skMarker)))
            .ToListAsync();
        foreach (var alt in altePostfachLohnzettel)
        {
            // Alte Storage-Datei wegputzen
            if (!string.IsNullOrWhiteSpace(alt.StorageFilename))
            {
                try
                {
                    var altDir = Path.Combine(_mailboxPath, alt.CompanyProfileId.ToString());
                    var altPath = Path.Combine(altDir, alt.StorageFilename);
                    if (File.Exists(altPath)) File.Delete(altPath);
                }
                catch { /* still — DB-Eintrag wird sowieso gelöscht */ }
            }
            _db.MailboxDocuments.Remove(alt);
        }
        if (altePostfachLohnzettel.Count > 0)
            await _db.SaveChangesAsync();
    }

    /// <summary>Mergt mehrere PDF-Bytes zu einem PDF (iText 7).</summary>
    private static byte[] MergePdfs(IEnumerable<byte[]> pdfBytesList)
    {
        var list = pdfBytesList.Where(b => b is { Length: > 0 }).ToList();
        if (list.Count == 0) return Array.Empty<byte>();
        if (list.Count == 1) return list[0];

        using var output = new MemoryStream();
        var writer = new PdfWriter(output);
        using (var target = new PdfDocument(writer))
        {
            var merger = new PdfMerger(target);
            foreach (var bytes in list)
            {
                using var src = new MemoryStream(bytes);
                using var srcDoc = new PdfDocument(new PdfReader(src));
                merger.Merge(srcDoc, 1, srcDoc.GetNumberOfPages());
            }
        }
        return output.ToArray();
    }
}
