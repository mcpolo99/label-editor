using Xunit.Abstractions;
using LabelEditor.Tests.Shared;

namespace LabelEditor.App.Tests;

/// <summary>
/// UI tests that need real LEdit files (see <see cref="RealLeditData"/>). Avalonia's test attribute cannot be
/// subclassed to skip, so such tests start with <see cref="Available"/> and end early when the files are missing.
/// </summary>
internal static class RealLedit
{
    public static bool Available(ITestOutputHelper output)
    {
        if (RealLeditData.Root is null)
        {
            output.WriteLine(RealLeditData.SkipReason);
            return false;
        }

        return true;
    }
}
