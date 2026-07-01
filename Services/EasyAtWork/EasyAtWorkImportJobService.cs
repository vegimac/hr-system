using System.Collections.Concurrent;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// In-Memory-Status-Speicher für den asynchronen Filial-Mitarbeiter-Import
/// (Walter-Vorgabe 29.06.2026). Der Commit läuft als Hintergrund-Job (kann
/// mehrere Minuten dauern, easy@work-API ~1 Aufruf/Sekunde) — der Browser
/// pollt nur den Fortschritt, statt minutenlang auf eine Antwort zu warten
/// und in ein Proxy-Timeout zu laufen. Als Singleton registriert.
/// </summary>
public sealed class EasyAtWorkImportJobService
{
    public sealed class ImportJob
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public string Status { get; set; } = "running";   // running | done | error
        public string Phase { get; set; } = "Starte…";
        public int Done { get; set; }
        public int Total { get; set; }
        public DateTime StartedAt { get; init; } = DateTime.UtcNow;
        public DateTime? FinishedAt { get; set; }
        public object? Result { get; set; }
        public string? Error { get; set; }
    }

    private readonly ConcurrentDictionary<string, ImportJob> _jobs = new();

    public ImportJob Create()
    {
        // Gelegenheits-Aufräumen: fertige Jobs älter als 1 Stunde entfernen,
        // damit der Speicher nicht unbegrenzt wächst.
        var cutoff = DateTime.UtcNow.AddHours(-1);
        foreach (var kv in _jobs)
            if (kv.Value.FinishedAt.HasValue && kv.Value.FinishedAt.Value < cutoff)
                _jobs.TryRemove(kv.Key, out _);

        var job = new ImportJob();
        _jobs[job.Id] = job;
        return job;
    }

    public ImportJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public void Progress(string id, int done, int total, string phase)
    {
        if (_jobs.TryGetValue(id, out var j)) { j.Done = done; j.Total = total; j.Phase = phase; }
    }

    public void Complete(string id, object result)
    {
        if (_jobs.TryGetValue(id, out var j))
        {
            j.Status = "done"; j.Result = result; j.Phase = "Fertig"; j.FinishedAt = DateTime.UtcNow;
        }
    }

    public void Fail(string id, string error)
    {
        if (_jobs.TryGetValue(id, out var j))
        {
            j.Status = "error"; j.Error = error; j.FinishedAt = DateTime.UtcNow;
        }
    }
}
