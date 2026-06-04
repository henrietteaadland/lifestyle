using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

var meals = SeedMeals();
var plan = new ConcurrentDictionary<DayOfWeek, Guid>();

foreach (var (day, mealId) in DefaultPlan(meals))
{
    plan[day] = mealId;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/meals", (string? filter) =>
{
    var normalized = filter?.Trim().ToLowerInvariant();
    var results = meals.AsEnumerable();

    results = normalized switch
    {
        "billig" or "cheap" => results.Where(meal => meal.Tags.Contains("billig")),
        "sunn" or "healthy" => results.Where(meal => meal.Tags.Contains("sunn")),
        "rask" or "quick" => results.Where(meal => meal.Tags.Contains("rask")),
        "favoritter" or "favorites" => results.Where(meal => meal.IsFavorite),
        _ => results
    };

    return Results.Ok(results.OrderByDescending(meal => meal.IsFavorite).ThenBy(meal => meal.Name));
});

app.MapPatch("/api/meals/{id:guid}/favorite", (Guid id) =>
{
    var meal = meals.FirstOrDefault(meal => meal.Id == id);
    if (meal is null)
    {
        return Results.NotFound();
    }

    meal.IsFavorite = !meal.IsFavorite;
    return Results.Ok(meal);
});

app.MapGet("/api/plan", () =>
{
    var response = OrderedDays()
        .Select(day =>
        {
            plan.TryGetValue(day, out var mealId);
            return new PlannedDay(DayKey(day), NorwegianDayName(day), meals.FirstOrDefault(meal => meal.Id == mealId));
        });

    return Results.Ok(response);
});

app.MapPut("/api/plan/{day}", (string day, PlanMealRequest request) =>
{
    if (!TryParseDay(day, out var parsedDay))
    {
        return Results.BadRequest(new { message = "Ugyldig dag. Bruk mandag, tirsdag, onsdag, torsdag, fredag, lørdag eller søndag." });
    }

    if (meals.All(meal => meal.Id != request.MealId))
    {
        return Results.NotFound(new { message = "Fant ikke retten." });
    }

    plan[parsedDay] = request.MealId;
    return Results.NoContent();
});

app.MapPost("/api/plan/auto", (string? filter) =>
{
    var pool = meals.Where(meal => filter?.ToLowerInvariant() switch
    {
        "billig" => meal.Tags.Contains("billig"),
        "sunn" => meal.Tags.Contains("sunn"),
        "rask" => meal.Tags.Contains("rask"),
        _ => true
    }).ToArray();

    if (pool.Length == 0)
    {
        return Results.BadRequest(new { message = "Filteret har ingen retter." });
    }

    var index = 0;
    foreach (var day in OrderedDays())
    {
        plan[day] = pool[index % pool.Length].Id;
        index++;
    }

    return Results.NoContent();
});

app.MapGet("/api/shopping-list", () =>
{
    var selectedMeals = OrderedDays()
        .Select(day => plan.TryGetValue(day, out var mealId) ? meals.FirstOrDefault(meal => meal.Id == mealId) : null)
        .Where(meal => meal is not null)
        .Cast<Meal>();

    var items = selectedMeals
        .SelectMany(meal => meal.Ingredients)
        .GroupBy(ingredient => new { Name = ingredient.Name.ToLowerInvariant(), Unit = ingredient.Unit.ToLowerInvariant(), ingredient.Category })
        .Select(group => new ShoppingListItem(
            ToTitleCase(group.Key.Name),
            Math.Round(group.Sum(ingredient => ingredient.Amount), 2),
            group.Key.Unit,
            group.Key.Category))
        .OrderBy(item => item.Category)
        .ThenBy(item => item.Name);

    return Results.Ok(items);
});

app.MapFallbackToFile("index.html");

app.Run();

static IReadOnlyList<Meal> SeedMeals()
{
    return
    [
        new(
            Guid.Parse("2eb2b707-4f24-4713-9546-c1c355dcab92"),
            "Kyllingwok med ris",
            25,
            42,
            2,
            ["rask", "sunn"],
            true,
            [
                "Kok risen etter anvisning på pakken.",
                "Stek kylling i strimler på høy varme til den er gjennomstekt.",
                "Ha i wokgrønnsaker og stek videre i 4-5 minutter.",
                "Vend inn soyasaus og server med ris."
            ],
            [
                new("Kyllingfilet", 400, "g", "Protein"),
                new("Fullkornsris", 180, "g", "Tørrvarer"),
                new("Wokgrønnsaker", 500, "g", "Grønt"),
                new("Soyasaus", 3, "ss", "Smak")
            ]),
        new(
            Guid.Parse("fdcc8709-4303-4f89-86c6-77f5b402c9ab"),
            "Linsegryte med kokos",
            35,
            58,
            4,
            ["billig", "sunn"],
            true,
            [
                "Skyll linsene godt i kaldt vann.",
                "Kok linser, hakkede tomater, kokosmelk og gulrot i 20-25 minutter.",
                "Rør inn spinat de siste 2 minuttene.",
                "Smak til med salt, pepper og ønsket krydder."
            ],
            [
                new("Røde linser", 300, "g", "Tørrvarer"),
                new("Kokosmelk", 1, "boks", "Tørrvarer"),
                new("Hakkede tomater", 2, "boks", "Tørrvarer"),
                new("Spinat", 200, "g", "Grønt"),
                new("Gulrot", 4, "stk", "Grønt")
            ]),
        new(
            Guid.Parse("365bd981-78ef-43fd-ad15-1d446f27a659"),
            "Havregrøt jars",
            10,
            16,
            3,
            ["billig", "rask"],
            false,
            [
                "Fordel havregryn i glass eller bokser.",
                "Rør sammen melk og yoghurt, og hell over havregrynene.",
                "Topp med frosne bær.",
                "Sett kaldt i minst 4 timer, helst over natten."
            ],
            [
                new("Havregryn", 240, "g", "Tørrvarer"),
                new("Melk", 6, "dl", "Meieri"),
                new("Gresk yoghurt", 300, "g", "Meieri"),
                new("Frosne bær", 300, "g", "Frys")
            ]),
        new(
            Guid.Parse("778cf95e-3128-4e15-aab0-e90cf5cd475d"),
            "Taco-bowl med bønner",
            20,
            36,
            2,
            ["billig", "rask"],
            false,
            [
                "Skyll bønner og mais godt.",
                "Varm bønnene raskt i panne med litt krydder.",
                "Legg salatmix i boller og topp med bønner, mais og avokado.",
                "Knus tortillachips over rett før servering."
            ],
            [
                new("Svarte bønner", 2, "boks", "Tørrvarer"),
                new("Mais", 1, "boks", "Tørrvarer"),
                new("Salatmix", 200, "g", "Grønt"),
                new("Avokado", 1, "stk", "Grønt"),
                new("Tortillachips", 120, "g", "Tørrvarer")
            ]),
        new(
            Guid.Parse("be9df57d-6862-45c9-bd53-a86169a5d1d0"),
            "Lakseform med poteter",
            40,
            84,
            3,
            ["sunn"],
            true,
            [
                "Sett ovnen på 200 grader.",
                "Del poteter og brokkoli, og legg dem i en ildfast form.",
                "Legg laksen på toppen og krydre med salt, pepper og sitron.",
                "Stek i 18-22 minutter og server med lettrømme."
            ],
            [
                new("Laksefilet", 500, "g", "Protein"),
                new("Potet", 700, "g", "Grønt"),
                new("Brokkoli", 1, "stk", "Grønt"),
                new("Lettrømme", 1, "beger", "Meieri"),
                new("Sitron", 1, "stk", "Grønt")
            ]),
        new(
            Guid.Parse("9712e091-cf6c-4fc0-851e-64275fa34738"),
            "Pasta pesto med kikerter",
            15,
            32,
            2,
            ["billig", "rask"],
            false,
            [
                "Kok pasta etter anvisning på pakken.",
                "Skyll kikertene og varm dem raskt i kjelen med ferdig pasta.",
                "Vend inn pesto og cherrytomater.",
                "Topp med parmesan før servering."
            ],
            [
                new("Fullkornspasta", 220, "g", "Tørrvarer"),
                new("Kikerter", 1, "boks", "Tørrvarer"),
                new("Pesto", 4, "ss", "Smak"),
                new("Cherrytomater", 250, "g", "Grønt"),
                new("Parmesan", 40, "g", "Meieri")
            ]),
        new(
            Guid.Parse("1fe8b46d-20ce-42e7-a707-850dbb94668f"),
            "Omelettwraps",
            12,
            24,
            2,
            ["rask", "sunn"],
            false,
            [
                "Visp egg med litt salt og pepper.",
                "Stek tynne omeletter i panne på middels varme.",
                "Fyll tortillalefser med omelett, paprika, cottage cheese og ruccola.",
                "Rull tett sammen og del i to."
            ],
            [
                new("Egg", 6, "stk", "Meieri"),
                new("Tortillalefser", 4, "stk", "Tørrvarer"),
                new("Paprika", 2, "stk", "Grønt"),
                new("Cottage cheese", 200, "g", "Meieri"),
                new("Ruccola", 70, "g", "Grønt")
            ])
    ];
}

static IEnumerable<(DayOfWeek Day, Guid MealId)> DefaultPlan(IReadOnlyList<Meal> meals)
{
    foreach (var pair in OrderedDays().Zip(meals.Select(meal => meal.Id)))
    {
        yield return pair;
    }
}

static IEnumerable<DayOfWeek> OrderedDays()
{
    yield return DayOfWeek.Monday;
    yield return DayOfWeek.Tuesday;
    yield return DayOfWeek.Wednesday;
    yield return DayOfWeek.Thursday;
    yield return DayOfWeek.Friday;
    yield return DayOfWeek.Saturday;
    yield return DayOfWeek.Sunday;
}

static bool TryParseDay(string value, out DayOfWeek day)
{
    var normalized = value.Trim().ToLowerInvariant();
    return normalized switch
    {
        "mandag" or "monday" => Set(DayOfWeek.Monday, out day),
        "tirsdag" or "tuesday" => Set(DayOfWeek.Tuesday, out day),
        "onsdag" or "wednesday" => Set(DayOfWeek.Wednesday, out day),
        "torsdag" or "thursday" => Set(DayOfWeek.Thursday, out day),
        "fredag" or "friday" => Set(DayOfWeek.Friday, out day),
        "lordag" or "lørdag" or "saturday" => Set(DayOfWeek.Saturday, out day),
        "sondag" or "søndag" or "sunday" => Set(DayOfWeek.Sunday, out day),
        _ => Set(default, out day, false)
    };
}

static bool Set(DayOfWeek value, out DayOfWeek day, bool result = true)
{
    day = value;
    return result;
}

static string DayKey(DayOfWeek day)
{
    return day switch
    {
        DayOfWeek.Monday => "mandag",
        DayOfWeek.Tuesday => "tirsdag",
        DayOfWeek.Wednesday => "onsdag",
        DayOfWeek.Thursday => "torsdag",
        DayOfWeek.Friday => "fredag",
        DayOfWeek.Saturday => "lordag",
        DayOfWeek.Sunday => "sondag",
        _ => day.ToString().ToLowerInvariant()
    };
}

static string NorwegianDayName(DayOfWeek day)
{
    return day switch
    {
        DayOfWeek.Monday => "Mandag",
        DayOfWeek.Tuesday => "Tirsdag",
        DayOfWeek.Wednesday => "Onsdag",
        DayOfWeek.Thursday => "Torsdag",
        DayOfWeek.Friday => "Fredag",
        DayOfWeek.Saturday => "Lørdag",
        DayOfWeek.Sunday => "Søndag",
        _ => day.ToString()
    };
}

static string ToTitleCase(string value)
{
    return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}

record Ingredient(string Name, decimal Amount, string Unit, string Category);

class Meal(
    Guid id,
    string name,
    int minutes,
    decimal estimatedCost,
    int portions,
    IReadOnlyList<string> tags,
    bool isFavorite,
    IReadOnlyList<string> instructions,
    IReadOnlyList<Ingredient> ingredients)
{
    public Guid Id { get; } = id;
    public string Name { get; } = name;
    public int Minutes { get; } = minutes;
    public decimal EstimatedCost { get; } = estimatedCost;
    public int Portions { get; } = portions;
    public IReadOnlyList<string> Tags { get; } = tags;
    public bool IsFavorite { get; set; } = isFavorite;
    public IReadOnlyList<string> Instructions { get; } = instructions;
    public IReadOnlyList<Ingredient> Ingredients { get; } = ingredients;
}

record PlannedDay(string Day, string Label, Meal? Meal);

record PlanMealRequest(Guid MealId);

record ShoppingListItem(string Name, decimal Amount, string Unit, string Category);
