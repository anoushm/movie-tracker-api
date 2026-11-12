using System.Runtime.CompilerServices;

namespace MovieTracker.Evals;

public static class ProjectRoot
{
    public static string Path { get; } = System.IO.Path.GetDirectoryName(SeedPath())!;

    private static string SeedPath([CallerFilePath] string filePath = "")
    {
        return filePath;
    }
}
