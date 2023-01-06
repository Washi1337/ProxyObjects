namespace SampleApp;

public class Person
{
    public Person(string firstName, string lastName, float coolnessFactor)
    {
        FirstName = firstName;
        LastName = lastName;
        CoolnessFactor = coolnessFactor;
    }

    public string FirstName
    {
        get;
        set;
    }

    public string LastName
    {
        get;
        set;
    }

    public float CoolnessFactor
    {
        get;
        set;
    }
}