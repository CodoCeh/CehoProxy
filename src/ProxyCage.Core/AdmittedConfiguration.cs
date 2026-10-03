using System.Text.Json;

namespace ProxyCage.Core;

/// <summary>
/// The configuration whose network effects have been admitted. Saved settings may contain
/// deferred edits, so watchdogs and fail-closed recovery must use this independent snapshot.
/// This is routing input only; subscription caches/status continue to refresh normally.
/// </summary>
public sealed class AdmittedConfiguration
{
    private CehoConfig _snapshot;
    public AdmittedConfiguration(CehoConfig initial) => _snapshot = Copy(initial);
    public CehoConfig Snapshot() => Copy(Volatile.Read(ref _snapshot));
    public void Admit(CehoConfig configuration) => Volatile.Write(ref _snapshot, Copy(configuration));

    public CehoConfig ForStart(bool preserveAdmitted, Func<CehoConfig> readSaved) =>
        preserveAdmitted ? Snapshot() : readSaved();

    private static CehoConfig Copy(CehoConfig cfg) =>
        JsonSerializer.Deserialize<CehoConfig>(JsonSerializer.Serialize(cfg))!;
}
