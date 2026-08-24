namespace AI.Document.Converter.Wpf.ViewModels;

// FR-030/AC-020: which single-file operation Retry should re-run for a given
// row - a Wpf/presentation-layer concept (which Dashboard action produced
// this row's current Status), distinct from Domain's PipelineStage (which
// stages of the pipeline actually completed for a given ConversionResult).
public enum BatchOperationKind
{
    Convert,
    Chunk
}
