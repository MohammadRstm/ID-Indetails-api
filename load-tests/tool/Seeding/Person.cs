namespace ID_Indetails.LoadTests.Tool.Seeding;

public enum Persona
{
    SuperAdmin,
    DesignAdmin,
    ExecutionAdmin,
    DesignEmployee,
    ExecutionEmployee,
}

/// <summary>
/// One test user. The address and name come from the persona and a number, never from random names, so test people
/// are recognisable everywhere: <c>lt.design.employee.007@loadtest.invalid</c>, "LT Design Employee 007".
/// </summary>
public sealed class Person(Persona persona, int number)
{
    public Persona Persona { get; } = persona;

    public int Number { get; } = number;

    public string Email => $"lt.{Slug}.{Number:000}@loadtest.invalid";

    public string FirstName => "LT";

    public string LastName => $"{Title} {Number:000}";

    public string Role => Persona switch
    {
        Persona.SuperAdmin => "super_admin",
        Persona.DesignAdmin or Persona.ExecutionAdmin => "admin",
        _ => "employee",
    };

    public string? Domain => Persona switch
    {
        Persona.DesignAdmin or Persona.DesignEmployee => "design",
        Persona.ExecutionAdmin or Persona.ExecutionEmployee => "execution",
        _ => null,
    };

    public bool IsAdmin => Persona is Persona.DesignAdmin or Persona.ExecutionAdmin;

    public bool IsEmployee => Persona is Persona.DesignEmployee or Persona.ExecutionEmployee;

    public long Id { get; set; }

    /// <summary>The access token from accepting the invitation; seeding acts as this person with it.</summary>
    public string Token { get; set; } = string.Empty;

    public long? TeamId { get; set; }

    public string? Position { get; set; }

    public DateOnly JoinsOn { get; set; }

    /// <summary>The last working day of someone who leaves during the history.</summary>
    public DateOnly? LeavesOn { get; set; }

    /// <summary>Works Monday, Wednesday and Friday mornings only, on a personal schedule.</summary>
    public bool PartTime { get; set; }

    /// <summary>Why this person's permissions differ from their role's, when they do.</summary>
    public string? Joker { get; set; }

    public bool Joined { get; set; }

    public bool WorksOn(DateOnly day) => Joined && day >= JoinsOn && (LeavesOn is null || day <= LeavesOn);

    private string Slug => Persona switch
    {
        Persona.SuperAdmin => "super",
        Persona.DesignAdmin => "design.admin",
        Persona.ExecutionAdmin => "execution.admin",
        Persona.DesignEmployee => "design.employee",
        _ => "execution.employee",
    };

    private string Title => Persona switch
    {
        Persona.SuperAdmin => "Super Admin",
        Persona.DesignAdmin => "Design Admin",
        Persona.ExecutionAdmin => "Execution Admin",
        Persona.DesignEmployee => "Design Employee",
        _ => "Execution Employee",
    };
}
