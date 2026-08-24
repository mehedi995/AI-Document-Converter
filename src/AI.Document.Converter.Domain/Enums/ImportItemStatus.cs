namespace AI.Document.Converter.Domain.Enums;

// Phase 2 only ever produces Ready. Phase 8 (Batch Processing) extends this with
// live conversion states (Converting/Converted/Failed) once BatchService exists -
// at that point FileImportItem's immutable Status is expected to be wrapped in an
// observable ViewModel so the UI can reflect live updates.
public enum ImportItemStatus
{
    Ready
}
