using System.Reflection;
using System.Reflection.Metadata;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace LoadTestingTool.Execution;

internal static class ScriptMetadataReferences
{
    public static ScriptOptions CreateOptions(Assembly runnerAssembly)
    {
        var references = new List<MetadataReference>();
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (File.Exists(path)) references.Add(MetadataReference.CreateFromFile(path));
        }
        references.Add(Create(runnerAssembly));
        return ScriptOptions.Default.WithReferences(references);
    }

    [UnconditionalSuppressMessage("SingleFile", "IL3000", Justification = "Assembly.Location is used only when available; single-file assemblies use TryGetRawMetadata below.")]
    public static MetadataReference Create(Assembly assembly)
    {
        var location = assembly.Location;
        if (!string.IsNullOrWhiteSpace(location) && File.Exists(location))
            return MetadataReference.CreateFromFile(location);

        unsafe
        {
            if (System.Reflection.Metadata.AssemblyExtensions.TryGetRawMetadata(assembly, out byte* blob, out var length) && length > 0)
                return MetadataReference.CreateFromImage(new ReadOnlySpan<byte>(blob, length).ToArray());
        }

        throw new NotSupportedException($"Cannot create a Roslyn metadata reference for assembly '{assembly.FullName}'. The assembly has no file location and raw metadata is unavailable.");
    }
}
