namespace ProxyObjects.Engine;

/// <summary>
/// Provides a mechanism for obfuscating names of .NET metadata.
/// </summary>
public static class NameObfuscation
{   
    /// <summary>
    /// A mapping of latin characters to their homoglyph counterparts.
    /// </summary>
    private static readonly Dictionary<char, char> Homoglyphs = new()
    {
        ['A'] = 'А',
        ['B'] = 'В',
        ['C'] = 'С',
        ['E'] = 'Е',
        ['I'] = 'І',
        ['K'] = 'К',
        ['M'] = 'М',
        ['H'] = 'Н',
        ['O'] = 'О',
        ['P'] = 'Р',
        ['T'] = 'Т',
        ['a'] = 'а',
        ['e'] = 'е',
        ['i'] = 'і',
        ['o'] = 'о',
        ['c'] = 'с'
    };
    
    /// <summary>
    /// Applies the homoglyph obfuscation to a name.
    /// </summary>
    /// <param name="name">The name to obfuscate.</param>
    /// <returns>The obfuscated name.</returns>
    public static string? ApplyHomoglyphs(string? name)
    {
        if (name is null)
            return null;

        foreach ((char key, char value) in Homoglyphs)
            name = name.Replace(key, value);

        return name;
    }
    
}