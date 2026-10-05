namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// The words the seeded company uses: plausible work for a design and fit-out company, so pages, searches and PDFs
/// handle text like the real thing. Every name that identifies a record still starts with "LT".
/// </summary>
public static class Vocabulary
{
    public static readonly string[] DesignTeams = ["Architecture", "Interior Design", "Structural", "Visualisation", "BIM & Drawings", "Landscape"];

    public static readonly string[] ExecutionTeams = ["Site Operations", "Procurement", "Finishing", "MEP", "Carpentry"];

    public static readonly string[] DesignPositions = ["Architect", "Interior Designer", "Junior Architect", "3D Visualiser", "Draughtsperson", "Structural Engineer"];

    public static readonly string[] ExecutionPositions = ["Site Engineer", "Site Supervisor", "Foreman", "Procurement Officer", "Quantity Surveyor"];

    public static readonly string[] Places = ["Beirut", "Jounieh", "Byblos", "Batroun", "Zahle", "Broummana", "Saida", "Tripoli", "Baabda", "Dbayeh", "Achrafieh", "Hamra"];

    public static readonly string[] DesignKinds =
    [
        "Villa interior", "Apartment renovation", "Office fit-out", "Restaurant concept", "Retail store design",
        "Residential tower facade", "Boutique hotel lobby", "Clinic layout", "Kitchen and bath redesign", "Rooftop terrace",
        "School extension", "Showroom design",
    ];

    public static readonly string[] ExecutionKinds =
    [
        "Apartment fit-out", "Villa construction works", "Office renovation", "Shop finishing works", "Restaurant build-out",
        "Facade cladding", "Warehouse refurbishment", "Pool and garden works",
    ];

    public static readonly string[] InternalKinds = ["Internal: portfolio update", "Internal: template library", "Internal: training", "Internal: BIM standards"];

    public static readonly string[] DesignWork =
    [
        "Floor plan revisions after client feedback", "Facade detailing", "3D render iteration", "Lighting layout",
        "Furniture selection and schedule", "Material board preparation", "Elevations and sections", "Ceiling plan",
        "Client presentation deck", "Site measurements review", "Joinery details", "BOQ review",
        "Coordination with structural drawings", "Kitchen layout options", "Bathroom detailing", "Door and window schedule",
        "Mood board", "Permit drawings", "Revisions from site visit", "As-built drawings update",
    ];

    public static readonly string[] DesignRequirements =
    [
        "Concept design approved", "Schematic layout", "3D views for client", "Material selection", "Lighting plan",
        "Detailed drawings", "Furniture schedule", "Permit set", "Tender documents", "Final presentation",
        "Kitchen design", "Bathroom design",
    ];

    public static readonly string[] ExecutionRequirements =
    [
        "Site handover", "Demolition complete", "Electrical first fix", "Plumbing first fix", "Plastering",
        "Tiling", "Gypsum ceilings", "Painting", "Joinery installation", "Final cleaning and snag list",
    ];

    public static readonly string[] MeetingTopics =
    [
        "Weekly progress", "Client review", "Design review", "Site coordination", "Budget review", "Material selection",
        "Kick-off", "Handover planning",
    ];

    public static readonly string[] ExpenseLabels =
    [
        "Render farm credits", "Printing of drawings", "Site visit transport", "Specialist consultant", "Sample materials",
        "Software licence", "Model making",
    ];

    public static readonly string[] ExpenseCategories = ["services", "software", "travel", "contractor", "other"];

    /// <summary>Name, unit and the range of its unit cost in USD.</summary>
    public static readonly (string Name, string Unit, decimal Low, decimal High)[] Materials =
    [
        ("Cement bags 50kg", "bag", 6m, 9m), ("Rebar 12mm", "tonne", 650m, 820m), ("Sand", "m³", 18m, 30m),
        ("Ceramic tiles 60x60", "m²", 12m, 35m), ("Paint 18L", "bucket", 45m, 90m), ("Gypsum board", "sheet", 7m, 12m),
        ("PVC pipe 4in", "length", 9m, 16m), ("Electrical cable 2.5mm", "roll", 35m, 60m), ("Plywood 18mm", "sheet", 28m, 45m),
        ("Aluminium profiles", "length", 22m, 40m), ("Waterproofing membrane", "roll", 55m, 95m), ("Granite slab", "m²", 60m, 140m),
    ];

    public static readonly string[] Suppliers = ["LT Supplier 01", "LT Supplier 02", "LT Supplier 03", "LT Supplier 04", "LT Supplier 05"];

    public static readonly string[] ChatLines =
    [
        "Morning all, updated drawings are on the shared drive.", "Can someone check the latest client comments?",
        "Site visit moved to tomorrow 10:00.", "Renders are exported, please review before the meeting.",
        "Supplier confirmed delivery for Thursday.", "Who has the measurements for the second floor?",
        "Client approved the material board.", "Reminder: timesheets before Friday.", "Thanks, looks good.",
        "I'll take the revisions on this one.", "Uploading the BOQ now.", "The ceiling detail needs another look.",
    ];

    public static readonly string[] DirectLines =
    [
        "Do you have five minutes for a quick call?", "Sent you the file.", "Can you cover the client call at 3?",
        "Thanks for the help today.", "Where did we land on the budget?", "I'll be on site this afternoon.",
    ];

    public static readonly string[] Comments =
    [
        "Good progress, keep the client comments in mind.", "Please add more detail to the description next time.",
        "Let's review this together tomorrow.", "Nice work on this.", "Is this billable to the client?",
    ];

    public static T Pick<T>(this Random random, IReadOnlyList<T> items) => items[random.Next(items.Count)];

    public static bool Chance(this Random random, double probability) => random.NextDouble() < probability;

    public static List<T> Shuffled<T>(this Random random, IEnumerable<T> items)
    {
        var array = items.ToArray();
        random.Shuffle(array);
        return [.. array];
    }

    /// <summary>A round amount between the two, in steps of <paramref name="step"/>.</summary>
    public static decimal Amount(this Random random, decimal low, decimal high, decimal step)
        => low + step * random.Next((int)((high - low) / step) + 1);
}
