using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

public class ContractTextService
{
    private readonly AppDbContext _context;

    public ContractTextService(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Lädt einen Text anhand der TextKey-ID aus der Excel Parameters-Tabelle.
    /// Falls kein Eintrag für den Vertragstyp gefunden wird, wird ALL verwendet.
    /// </summary>
    public async Task<string> GetAsync(
        string textKey,
        string contractType,
        string lang = "de",
        Dictionary<string, string>? placeholders = null)
    {
        var now = DateTime.Today;

        // Zuerst: spezifischer Vertragstyp
        var text = await _context.ContractTexts
            .Where(t => t.TextKey == textKey
                     && t.LanguageCode == lang
                     && t.IsActive
                     && t.ValidFrom <= now
                     && (t.ValidTo == null || t.ValidTo >= now)
                     && t.ContractTypes.Contains(contractType))
            .OrderByDescending(t => t.ValidFrom)
            .FirstOrDefaultAsync();

        // Fallback: ALL
        if (text == null)
        {
            text = await _context.ContractTexts
                .Where(t => t.TextKey == textKey
                         && t.LanguageCode == lang
                         && t.IsActive
                         && t.ValidFrom <= now
                         && (t.ValidTo == null || t.ValidTo >= now)
                         && t.ContractTypes == "ALL")
                .OrderByDescending(t => t.ValidFrom)
                .FirstOrDefaultAsync();
        }

        // Fallback Sprache: DE
        if (text == null && lang != "de")
            return await GetAsync(textKey, contractType, "de", placeholders);

        if (text == null)
            return $"[TEXT NOT FOUND: {textKey}]";

        var content = text.Content;

        if (placeholders != null)
            foreach (var kv in placeholders)
                content = content.Replace($"{{{kv.Key}}}", kv.Value);

        return content;
    }

    /// <summary>
    /// Lädt mehrere Texte auf einmal (effizient: ein DB-Query).
    /// </summary>
    public async Task<Dictionary<string, string>> GetManyAsync(
        IEnumerable<string> textKeys,
        string contractType,
        string lang = "de",
        Dictionary<string, string>? placeholders = null)
    {
        var keyList = textKeys.ToList();
        var now = DateTime.Today;

        var allTexts = await _context.ContractTexts
            .Where(t => keyList.Contains(t.TextKey)
                     && t.LanguageCode == lang
                     && t.IsActive
                     && t.ValidFrom <= now
                     && (t.ValidTo == null || t.ValidTo >= now))
            .ToListAsync();

        var result = new Dictionary<string, string>();

        foreach (var key in keyList)
        {
            // Spezifischer Typ bevorzugt
            var text = allTexts
                .Where(t => t.TextKey == key && t.ContractTypes.Contains(contractType))
                .OrderByDescending(t => t.ValidFrom)
                .FirstOrDefault()
                ?? allTexts
                    .Where(t => t.TextKey == key && t.ContractTypes == "ALL")
                    .OrderByDescending(t => t.ValidFrom)
                    .FirstOrDefault();

            var content = text?.Content ?? $"[TEXT NOT FOUND: {key}]";

            if (placeholders != null)
                foreach (var kv in placeholders)
                    content = content.Replace($"{{{kv.Key}}}", kv.Value);

            result[key] = content;
        }

        return result;
    }
}
