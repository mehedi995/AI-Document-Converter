namespace AI.Document.Converter.Application.Interfaces;

// Isolates the one piece of real file-system I/O the import validation needs, so
// ImportService can be unit-tested against synthetic file lists
// (docs/15-IMPLEMENTATION-PLAN.md Section 2 testing approach) without touching disk.
public interface IFileSizeReader
{
    long GetFileSizeBytes(string filePath);
}
