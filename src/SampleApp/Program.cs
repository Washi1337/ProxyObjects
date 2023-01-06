using System;

namespace SampleApp;

internal static class Program
{
    public static void Main(string[] args)
    {
        var people = GetPeople();

        foreach (var person in people)
        {
            Console.WriteLine(string.Format("{0} {1} (Coolness: {2})",
                person.FirstName,
                person.LastName,
                person.CoolnessFactor));

            if (person.CoolnessFactor > 9000f)
                Console.WriteLine("  Wow this person is really cool!");
        }
    }

    private static Person[] GetPeople() => new Person[]
    {
        new("Alice", "Average", 4.2f),
        new("Bob", "The Builder", -1.7f),
        new("John", "Doe", 1.3f),
        new("Bruce", "Willis", 9001f)
    };
}