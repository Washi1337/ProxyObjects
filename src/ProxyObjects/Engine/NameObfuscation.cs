namespace ProxyObjects.Engine;

public static class NameObfuscation
{   
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
    
    public static string? ApplyHomoglyphs(string? name)
    {
        if (name is null)
            return null;

        foreach ((char key, char value) in Homoglyphs)
            name = name.Replace(key, value);

        return name;
    }
    
}