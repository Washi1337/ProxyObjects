using System.IO.Compression;
using System.Reflection;

namespace ProxyObjects.Runtime;

#nullable disable

public static class EmbeddedAssemblyResolver
{
    public static void Initialize()
    {
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;
    }

    private static Assembly CurrentDomainOnAssemblyResolve(object sender, ResolveEventArgs args)
    {
        if (new AssemblyName(args.Name).Name != "RUNTIME")
            return null;

        var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("RUNTIME");
        if (resource is null)
            return null;

        using var decompressed = new MemoryStream();
        using (var inflate = new DeflateStream(resource, CompressionMode.Decompress)) 
            inflate.CopyTo(decompressed);

        return Assembly.Load(decompressed.ToArray());
    }
}