using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Clients;
using ID_Indetails.api.DTO.Request.Expenses;
using ID_Indetails.api.DTO.Request.Labour;
using ID_Indetails.api.DTO.Request.Meetings;
using ID_Indetails.api.DTO.Request.Projects;
using ID_Indetails.api.DTO.Request.Requirements;
using ID_Indetails.api.DTO.Request.Statements;
using ID_Indetails.api.Enums;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// Clients and projects through their lives: a first portfolio on day one, new work twice a week, requirements
/// ticked off by the people assigned to them, holds, completions and archiving, meetings and project expenses.
/// </summary>
public sealed class Portfolio(World world, ApiClient api, SimulatedClock clock)
{
    private int _designNumber;
    private int _executionNumber;
    private int _designClientNumber;
    private int _executionClientNumber;

    private Random Random => world.Random;

    /// <summary>Roughly one live Design project for every two designers, one site for every three site staff.</summary>
    public async Task CreateFirstPortfolioAsync(DateOnly day)
    {
        clock.MoveTo(day, 9, 30);
        for (var i = 0; i < Math.Max(2, world.Plan.DesignEmployees / 2); i++)
        {
            await CreateProjectAsync("design", day);
        }

        for (var i = 0; i < Math.Max(1, world.Plan.ExecutionEmployees / 3); i++)
        {
            await CreateProjectAsync("execution", day);
        }
    }

    public async Task DaytimeAsync(DateOnly day)
    {
        clock.MoveTo(day, 10);
        if (day > world.Plan.FirstDay && day.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Thursday)
        {
            await NewWorkAsync("design", day, perDay: world.Plan.DesignEmployees / 80.0);
            await NewWorkAsync("execution", day, perDay: world.Plan.ExecutionEmployees / 90.0);
        }

        await TickRequirementsAsync(day);
        await ChangeStatusesAsync(day);
        if (World.IsWorkingDay(day))
        {
            await PlanMeetingsAsync(day);
            await AddExpensesAsync(day);
        }
    }

    private async Task NewWorkAsync(string domain, DateOnly day, double perDay)
    {
        var count = (int)perDay + (Random.Chance(perDay - (int)perDay) ? 1 : 0);
        for (var i = 0; i < count; i++)
        {
            await CreateProjectAsync(domain, day);
        }
    }

    private async Task CreateProjectAsync(string domain, DateOnly day)
    {
        var design = domain == "design";
        var owner = world.AnyAdmin(domain, day);
        var isInternal = design && Random.Chance(0.1);
        var client = isInternal ? null : await ClientForAsync(domain, owner, day);
        var weeks = design ? Random.Next(6, 21) : Random.Next(8, 25);
        var target = day.AddDays(weeks * 7);
        var number = design ? ++_designNumber : ++_executionNumber;
        var kind = isInternal ? Random.Pick(Vocabulary.InternalKinds) : Random.Pick(design ? Vocabulary.DesignKinds : Vocabulary.ExecutionKinds);
        var place = Random.Pick(Vocabulary.Places);

        var revenue = design && !isInternal ? Random.Amount(8000m, 60000m, 500m) : 0m;
        var fee = design ? 0m : Random.Amount(2000m, 15000m, 250m);
        var markup = design ? 0m : Random.Amount(0.10m, 0.30m, 0.05m);
        var workers = design ? 0 : Random.Next(4, 13);
        var dailyPay = design ? 0m : Random.Amount(25m, 40m, 5m);
        var initialPayment = !design && Random.Chance(0.5) ? decimal.Round(fee * 0.3m, 0) : (decimal?)null;

        var created = await api.PostAsync(owner.Token, "/api/projects", new CreateProjectRequest
        {
            Title = $"LT-{(design ? "D" : "X")}{number:000} {kind} · {place}",
            Description = $"LT load-test project. {kind} in {place}.",
            Location = place,
            DomainCode = domain,
            BillingType = isInternal ? BillingType.Internal : BillingType.Client,
            ClientId = client?.Id,
            OwnerId = owner.Id,
            TargetDate = target,
            Revenue = design && !isInternal ? revenue : null,
            ProjectFee = design ? null : fee,
            MaterialMarkup = design ? null : markup,
            InitialPayment = initialPayment is { } amount ? new InitialPaymentRequest { Amount = amount, ReceivedOn = day, Reference = $"LT-INIT-{number:000}" } : null,
            LabourDefaults = design ? null : new SetLabourDefaultsRequest { Workers = workers, DailyPay = dailyPay },
        });
        world.Tally.Add($"projects.{domain}");
        if (initialPayment is not null)
        {
            world.Tally.Add("payments");
        }

        var project = new SeedProject
        {
            Id = created.Id(), Domain = domain, Client = client, Owner = owner, StartedOn = day, TargetOn = target,
            Revenue = revenue, Fee = fee, Markup = markup, Workers = workers, DailyPay = dailyPay,
            LastMeetingOn = day, LastInvoiceOn = day,
        };
        world.Projects.Add(project);
        project.Team.AddRange(LeastBusy(domain, day, Random.Next(2, 5)));
        PlanLife(project);
        await AddRequirementsAsync(project, day);
    }

    /// <summary>Half the new work comes from an existing client.</summary>
    private async Task<SeedClient> ClientForAsync(string domain, Person admin, DateOnly day)
    {
        var existing = world.Clients.Where(c => c.Domain == domain).ToList();
        if (existing.Count > 3 && Random.Chance(0.5))
        {
            return Random.Pick(existing);
        }

        var design = domain == "design";
        var number = design ? ++_designClientNumber : ++_executionClientNumber;
        var name = $"LT {(design ? "Design" : "Execution")} Client {number:000}";
        var email = $"client.{(design ? "d" : "x")}{number:000}@loadtest.invalid";
        List<ClientContactRequest> contacts = [new() { Kind = ContactKind.Email, Value = email, Label = "Main", IsPrimary = true }];
        if (number <= 100 && Random.Chance(0.6))
        {
            // 555-01xx: numbers reserved for fiction, never a real line.
            contacts.Add(new() { Kind = ContactKind.Phone, Value = $"+1 555 01{number - 1:00}", Label = "Mobile", IsWhatsapp = true });
        }

        var created = await api.PostAsync(admin.Token, "/api/clients", new CreateClientRequest
        {
            Name = name, Organisation = $"{name} SAL", Label = Random.Pick(new[] { "Client", "Key account", "Retainer", "New" }),
            Note = "LT load-test client.", DomainCode = domain, Contacts = contacts,
        });
        world.Tally.Add("clients");
        var client = new SeedClient(created.Id(), domain, name, email);
        world.Clients.Add(client);
        return client;
    }

    private List<Person> LeastBusy(string domain, DateOnly day, int count)
    {
        var persona = domain == "design" ? Persona.DesignEmployee : Persona.ExecutionEmployee;
        return world.Working(day, p => p.Persona == persona)
            .OrderBy(p => world.Live(domain).Count(project => project.Team.Contains(p)))
            .ThenBy(_ => Random.Next())
            .Take(count)
            .ToList();
    }

    /// <summary>When the project will be held, completed and archived, if that happens before the history ends.</summary>
    private void PlanLife(SeedProject project)
    {
        var last = world.Plan.LastDay;
        var duration = project.TargetOn.DayNumber - project.StartedOn.DayNumber;
        if (Random.Chance(0.08))
        {
            project.HoldOn = project.StartedOn.AddDays(duration / 4 + Random.Next(duration / 4));
            project.ResumeOn = project.HoldOn.Value.AddDays(Random.Next(7, 22));
        }

        var complete = project.TargetOn.AddDays(Random.Next(-3, 11) + HeldDays(project, project.TargetOn));
        if (complete < last)
        {
            project.CompleteOn = complete;
            if (Random.Chance(0.4) && complete.AddDays(30) < last)
            {
                project.ArchiveOn = complete.AddDays(Random.Next(7, 31));
            }
        }
    }

    private static int DaysBetween(DateOnly from, DateOnly to) => to.DayNumber - from.DayNumber;

    /// <summary>Spread over the project, deadline by deadline; each is done around its deadline, some a little late.</summary>
    private async Task AddRequirementsAsync(SeedProject project, DateOnly day)
    {
        var design = project.Domain == "design";
        var texts = design ? Random.Shuffled(Vocabulary.DesignRequirements).Take(Random.Next(5, 11)).ToList()
            : Vocabulary.ExecutionRequirements.Take(Random.Next(4, 9)).ToList();
        var span = DaysBetween(day, project.TargetOn);
        for (var i = 0; i < texts.Count; i++)
        {
            var deadline = day.AddDays(span * (i + 1) / texts.Count);
            var assignees = Random.Shuffled(project.Team).Take(Random.Next(1, Math.Min(2, project.Team.Count) + 1)).ToList();
            var created = await api.PostAsync(project.Owner.Token, $"/api/projects/{project.Id}/requirements", new SaveRequirementRequest
            {
                Text = texts[i], Deadline = deadline, AssigneeIds = assignees.Select(a => a.Id).ToList(),
            });
            var doneOn = deadline.AddDays(Random.Next(-6, 9) + HeldDays(project, deadline));
            project.Requirements.Add(new SeedRequirement(created.Id(), doneOn < day.AddDays(1) ? day.AddDays(1) : doneOn, assignees));
            world.Tally.Add("requirements");
        }
    }

    private static int HeldDays(SeedProject project, DateOnly before)
        => project.HoldOn is { } hold && hold < before && project.ResumeOn is { } resume ? DaysBetween(hold, resume) : 0;

    /// <summary>One of the people assigned ticks it off; while the project is live, never on a Sunday.</summary>
    private async Task TickRequirementsAsync(DateOnly day)
    {
        if (day.DayOfWeek == DayOfWeek.Sunday)
        {
            return;
        }

        foreach (var project in world.Projects.Where(p => p.Status == "active"))
        {
            foreach (var requirement in project.Requirements.Where(r => !r.Done && r.DoneOn <= day))
            {
                var who = requirement.Assignees.FirstOrDefault(a => a.WorksOn(day)) ?? project.Owner;
                await api.PostAsync(who.Token, $"/api/projects/{project.Id}/requirements/{requirement.Id}/done");
                requirement.Done = true;
                world.Tally.Add("requirements.ticked");
            }
        }
    }

    private async Task ChangeStatusesAsync(DateOnly day)
    {
        foreach (var project in world.Projects.ToList())
        {
            var action = (project.Status, day) switch
            {
                ("active", _) when project.HoldOn == day => "hold",
                ("hold", _) when project.ResumeOn == day => "resume",
                ("active", _) when project.CompleteOn == day => "complete",
                ("completed", _) when project.ArchiveOn == day => "archive",
                _ => null,
            };
            if (action is null)
            {
                continue;
            }

            if (!project.Owner.WorksOn(day))
            {
                project.Owner = world.AnyAdmin(project.Domain, day);
            }

            // A site is only completed once the client has had every material on a statement: a final one first.
            if (action == "complete" && project.Unstated.Count > 0)
            {
                var statement = await api.PostAsync(project.Owner.Token, $"/api/projects/{project.Id}/statements", new CreateStatementRequest { Mode = StatementMode.Detail });
                await api.PostAsync(project.Owner.Token, $"/api/statements/{statement.Id()}/deliveries", new RecordDeliveryRequest { Channel = DeliveryChannel.Download });
                project.Unstated.Clear();
                project.LastStatementOn = day;
                world.Tally.Add("statements");
            }

            await api.PostAsync(project.Owner.Token, $"/api/projects/{project.Id}/{action}");
            project.Status = action switch { "hold" => "hold", "resume" => "active", "complete" => "completed", _ => "archived" };
            world.Tally.Add($"projects.{action}");
            if (project.Status == "completed")
            {
                project.Requirements.ForEach(r => r.Done = true);
            }
        }
    }

    /// <summary>About one meeting a fortnight per live project, set a few days ahead by its owner or someone on it.</summary>
    private async Task PlanMeetingsAsync(DateOnly day)
    {
        foreach (var project in world.Projects.Where(p => p.Status == "active" && DaysBetween(p.LastMeetingOn, day) >= 10 && Random.Chance(0.25)))
        {
            var on = day.AddDays(Random.Next(2, 6));
            if (!World.IsWorkingDay(on))
            {
                on = on.AddDays(2);
            }

            var startMinute = Random.Pick(new[] { 600, 690, 840, 930 });
            var starts = clock.At(on, 0, startMinute);
            var team = project.Team.Where(p => p.WorksOn(day)).ToList();
            var by = team.Count > 0 && Random.Chance(0.4) ? Random.Pick(team) : project.Owner.WorksOn(day) ? project.Owner : world.AnyAdmin(project.Domain, day);
            await api.PostAsync(by.Token, $"/api/projects/{project.Id}/meetings", new SaveMeetingRequest
            {
                StartsAt = starts, EndsAt = starts.AddMinutes(Random.Pick(new[] { 30, 45, 60, 90 })),
                Topic = Random.Pick(Vocabulary.MeetingTopics), Description = "LT load-test meeting.",
            });
            project.LastMeetingOn = day;
            world.Tally.Add("meetings");
        }
    }

    private async Task AddExpensesAsync(DateOnly day)
    {
        foreach (var project in world.Projects.Where(p => p.Status == "active" && Random.Chance(p.Domain == "design" ? 0.04 : 0.01)))
        {
            await api.PostAsync(world.AnyAdmin(project.Domain, day).Token, $"/api/projects/{project.Id}/expenses", new CreateExpenseRequest
            {
                Label = $"LT {Random.Pick(Vocabulary.ExpenseLabels)}", Amount = Random.Amount(50m, 1500m, 10m),
                Category = Random.Pick(Vocabulary.ExpenseCategories), SpentOn = day,
            });
            world.Tally.Add("expenses");
        }
    }
}
