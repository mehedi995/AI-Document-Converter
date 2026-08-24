namespace AI.Document.Converter.Domain.Enums;

// FR-045: a batch item is a full "success" only when CompletedStages includes
// every stage the user requested for that run.
[Flags]
public enum PipelineStage
{
    None = 0,
    Converted = 1,
    Chunked = 2,
    Exported = 4
}
