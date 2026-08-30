using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HrSystem.Services;

/// <summary>
/// Langsame SQL-Statements protokollieren (Walter-Vorgabe 30.08.2026).
///
/// Ausgangslage: EF hat in Produktiv JEDES Statement mit Level Information ins
/// Journal geschrieben — bei einem Dashboard-Aufruf sind das hunderte Zeilen,
/// von denen 99 % «1ms» sagen. Viel Schreiblast, wenig Erkenntnis, und im
/// Ernstfall scrollt man ewig, bis man die eine langsame Abfrage findet.
///
/// Neu: EF schweigt im Normalbetrieb (Log-Level Warning in appsettings.json),
/// und dieser Interceptor meldet dafür GENAU die Statements, die länger als
/// SchwelleMs dauern — mit Dauer und vollem SQL. Damit gehen keine Details
/// verloren, die je jemand gebraucht hätte: was schnell ist, interessiert
/// niemanden; was langsam ist, steht mit Volltext da.
///
/// Schwelle über appsettings.json anpassbar ("Diagnostics:SlowQueryMs"),
/// 0 oder negativ schaltet die Meldung ab.
/// </summary>
public class SlowQueryInterceptor : DbCommandInterceptor
{
    private readonly ILogger<SlowQueryInterceptor> _log;
    private readonly int _schwelleMs;

    public SlowQueryInterceptor(ILogger<SlowQueryInterceptor> log, IConfiguration cfg)
    {
        _log = log;
        _schwelleMs = cfg.GetValue<int?>("Diagnostics:SlowQueryMs") ?? 200;
    }

    private void Melde(DbCommand command, CommandExecutedEventData data)
    {
        if (_schwelleMs <= 0) return;
        var ms = (int)data.Duration.TotalMilliseconds;
        if (ms < _schwelleMs) return;
        // Eine Zeile pro Fund: Dauer zuerst, damit man im Journal danach
        // greppen kann ("SLOW SQL"), dann das Statement einzeilig.
        _log.LogWarning("SLOW SQL {Dauer}ms: {Sql}", ms,
            command.CommandText.Replace('\n', ' ').Replace('\r', ' '));
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        Melde(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        Melde(command, eventData);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        Melde(command, eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        Melde(command, eventData);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        Melde(command, eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Melde(command, eventData);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }
}
