namespace LabelEditor.Application.Preview;

/// <summary>
/// One set of field values to fill a label with, e.g. one part of a worklist. Keys are field element
/// names from the field tree (<c>CabCode</c>, <c>QMin</c>, <c>Description11</c>), compared case-insensitively.
/// </summary>
public sealed class PreviewRecord
{
    private readonly Dictionary<string, string> _values;

    public PreviewRecord(string name, IEnumerable<KeyValuePair<string, string>> values)
    {
        Name = name;
        _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
        {
            _values.TryAdd(key, value);
        }
    }

    public string Name { get; }

    public IReadOnlyDictionary<string, string> Values => _values;

    public string? Find(string fieldName) => _values.GetValueOrDefault(fieldName);
}

/// <summary>Records loaded from one data file.</summary>
public sealed record PreviewDataSet(string SourcePath, string Description, IReadOnlyList<PreviewRecord> Records);

/// <summary>Reads preview data (worklists, label sheets) for filling labels with real values.</summary>
public interface IPreviewDataSource
{
    /// <summary>True if this source understands the file (cheap check).</summary>
    bool CanRead(string path);

    Task<PreviewDataSet> LoadAsync(string path, CancellationToken cancellationToken = default);
}
