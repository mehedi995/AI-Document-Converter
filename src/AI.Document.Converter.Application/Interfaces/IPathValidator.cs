namespace AI.Document.Converter.Application.Interfaces;

// SEC-002: every user-supplied path (import files, output directory, export path,
// Python executable path) must be validated before use, rejecting path-traversal
// sequences and other unsafe input.
public interface IPathValidator
{
    bool IsValidDirectoryPath(string path);
}
