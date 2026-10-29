using ID_Indetails.api.DTO.Request.Auth;
using ID_Indetails.api.DTO.Request.Billing;
using ID_Indetails.api.DTO.Request.Company;
using ID_Indetails.api.DTO.Request.Employees;
using ID_Indetails.api.DTO.Request.Invitations;
using ID_Indetails.api.DTO.Request.Payroll;
using ID_Indetails.api.DTO.Request.Permissions;
using ID_Indetails.api.DTO.Request.Settings;
using ID_Indetails.api.DTO.Request.Setup;
using ID_Indetails.api.DTO.Request.Teams;
using ID_Indetails.api.Enums;

namespace ID_Indetails.LoadTests.Tool.Seeding;

/// <summary>
/// The company and its people: the first day's setup (the first super admin, settings, teams, overhead and
/// everyone's invitation), then joiners, leavers, pay reviews and excused absences as the months go by.
/// </summary>
public sealed class Company(World world, ApiClient api, SeedingApi host, SeedMailbox mailbox, SimulatedClock clock, string password)
{
    private Random Random => world.Random;

    private string Admin => world.FirstSuperAdmin.Token;

    public async Task SetUpAsync(DateOnly day)
    {
        clock.MoveTo(day, 8);
        AddPeople();
        await CreateFirstSuperAdminAsync();
        await ConfigureAsync(day);
        await CreateTeamsAsync();
        await CreateOverheadAsync(day);

        foreach (var person in world.People.Skip(1).Where(p => p.JoinsOn == day))
        {
            await InviteAsync(person);
        }

        await PayFirstSuperAdminAsync(day);
        await SetPartTimeSchedulesAsync(day);
        await MakeJokersAsync();
    }

    /// <summary>Joiners, leavers, pay reviews and yesterday's absences, first thing in the morning.</summary>
    public async Task MorningAsync(DateOnly day)
    {
        clock.MoveTo(day, 8, 15);
        foreach (var person in world.People.Where(p => !p.Joined && p.JoinsOn == day))
        {
            await InviteAsync(person);
        }

        foreach (var person in world.People.Where(p => p.LeavesOn == day.AddDays(-1)))
        {
            await api.PostAsync(Admin, $"/api/employees/{person.Id}/deactivate", new DeactivateEmployeeRequest { LastWorkingDay = person.LeavesOn });
            world.Tally.Add("users.deactivated");
        }

        if (day.Day == 1 && day > world.Plan.FirstDay)
        {
            await ReviewPayAsync(day);
        }

        foreach (var (person, absent) in world.Absences.Where(a => a.Day < day).ToList())
        {
            world.Absences.Remove((person, absent));
            if (person.WorksOn(day) && Random.Chance(0.6))
            {
                await api.PostAsync(Admin, $"/api/employees/{person.Id}/hour-excuses", new AddHourExcuseRequest
                {
                    Month = $"{absent:yyyy-MM}",
                    Kind = ExcuseKind.Days,
                    Days = [absent],
                    Note = "Sick leave",
                });
                world.Tally.Add("hour_excuses");
            }
        }
    }

    /// <summary>Everyone the plan asks for. Late joiners and leavers are employees, picked at random.</summary>
    private void AddPeople()
    {
        var plan = world.Plan;
        var groups = new (Persona Persona, int Count)[]
        {
            (Persona.SuperAdmin, plan.SuperAdmins), (Persona.DesignAdmin, plan.DesignAdmins), (Persona.ExecutionAdmin, plan.ExecutionAdmins),
            (Persona.DesignEmployee, plan.DesignEmployees), (Persona.ExecutionEmployee, plan.ExecutionEmployees),
        };
        foreach (var (persona, count) in groups)
        {
            for (var number = 1; number <= count; number++)
            {
                world.People.Add(new Person(persona, number) { JoinsOn = plan.FirstDay });
            }
        }

        var employees = world.People.Where(p => p.IsEmployee).ToList();
        var historyDays = plan.LastDay.DayNumber - plan.FirstDay.DayNumber;
        foreach (var joiner in Shuffled(employees).Take(Math.Max(1, employees.Count * 3 / 100)))
        {
            joiner.JoinsOn = plan.FirstDay.AddDays(historyDays / 3 + Random.Next(historyDays / 2));
        }

        foreach (var leaver in Shuffled(employees.Where(p => p.JoinsOn == plan.FirstDay)).Take(Math.Max(1, employees.Count * 2 / 100)))
        {
            leaver.LeavesOn = plan.FirstDay.AddDays(historyDays / 2 + Random.Next(historyDays / 3));
        }

        foreach (var designer in Shuffled(employees.Where(p => p.Persona == Persona.DesignEmployee && p.LeavesOn is null))
                     .Take(Math.Max(1, plan.DesignEmployees * 5 / 100)))
        {
            designer.PartTime = true;
        }
    }

    private async Task CreateFirstSuperAdminAsync()
    {
        var first = world.FirstSuperAdmin;
        var created = await api.PostAsync(null, "/api/setup/super-admin", new CreateSuperAdminRequest
        {
            Email = first.Email, FirstName = first.FirstName, LastName = first.LastName, Position = "Managing Director",
        }, host.SetupKey);
        first.Id = created.GetProperty("userId").GetInt64();

        var temporary = await api.PostAsync(null, "/api/auth/login", new LoginRequest { Email = first.Email, Password = mailbox.TemporaryPassword(first.Email) });
        var signedIn = await api.PostAsync(temporary.GetProperty("accessToken").GetString(), "/api/auth/change-password",
            new ChangePasswordRequest { CurrentPassword = mailbox.TemporaryPassword(first.Email), NewPassword = password });
        first.Token = signedIn.GetProperty("accessToken").GetString()!;
        first.Joined = true;
        world.Tally.Add("users.super_admin");
    }

    /// <summary>
    /// The setup endpoint creates the first super admin without pay, and nobody may set their own: another super admin
    /// sets it. A company with only one keeps it unpaid, as the API leaves it.
    /// </summary>
    private async Task PayFirstSuperAdminAsync(DateOnly day)
    {
        if (world.People.FirstOrDefault(p => p.Persona == Persona.SuperAdmin && p.Joined && p != world.FirstSuperAdmin) is not { } payer)
        {
            return;
        }

        await api.PostAsync(payer.Token, $"/api/employees/{world.FirstSuperAdmin.Id}/pay", new ChangePayRequest
        {
            Basis = PayBasis.Monthly, Amount = 6000m, EffectiveFrom = day, Note = "LT founding director",
        });
        world.Tally.Add("pay_records");
    }

    private async Task ConfigureAsync(DateOnly day)
    {
        await api.PutAsync(Admin, "/api/company/settings", new UpdateCompanySettingsRequest
        {
            Name = "LT Load Test Company", Address = "LT Test Street 1, Beirut", Website = "www.loadtest.invalid",
        });

        foreach (var domain in new[] { "design", "execution" })
        {
            // The defaults a new company starts with.
            await api.PutAsync(Admin, $"/api/domain-settings/{domain}", new SaveDomainSettingsRequest
            {
                EditWindow = EditWindow.OneDay, RequireDescription = true, TimerEnabled = true, LogReminder = true,
                NotifyEntry = EntryNotificationMode.All, NotifyProject = true, NotifyIdle = true, WeeklySummary = true,
            });
            await api.PutAsync(Admin, $"/api/billing-settings/{domain}", new SaveBillingSettingsRequest
            {
                InvoicePrefix = domain == "design" ? "HL" : "EX", DefaultTermsDays = 30, BankName = "LT Test Bank",
                BankAccount = "LT-000-000", TermsText = "Payable within 30 days.", Footer = "LT load-test data",
            });
        }

        await api.PostAsync(Admin, "/api/exchange-rates", new AddExchangeRateRequest { Currency = "LBP", RatePerUsd = 89500m, EffectiveFrom = day });
    }

    private async Task CreateTeamsAsync()
    {
        var plan = world.Plan;
        var teams = new (string Domain, string[] Names, int Count)[]
        {
            ("design", Vocabulary.DesignTeams, Math.Clamp((plan.DesignEmployees + 19) / 20, 2, Vocabulary.DesignTeams.Length)),
            ("execution", Vocabulary.ExecutionTeams, Math.Clamp((plan.ExecutionEmployees + 17) / 18, 2, Vocabulary.ExecutionTeams.Length)),
        };
        foreach (var (domain, names, count) in teams)
        {
            foreach (var name in names.Take(count))
            {
                var team = await api.PostAsync(Admin, "/api/teams", new CreateTeamRequest { Name = $"LT {name}", DomainCode = domain });
                world.Teams.Add((team.Id(), domain));
                world.Tally.Add("teams");
            }
        }

        foreach (var person in world.People.Where(p => p.Domain is not null))
        {
            var ofDomain = world.Teams.Where(t => t.Domain == person.Domain).ToList();
            person.TeamId = ofDomain[(person.Number - 1) % ofDomain.Count].Id;
            person.Position = person.IsAdmin
                ? person.Domain == "design" ? "Design Manager" : "Projects Manager"
                : Random.Pick(person.Domain == "design" ? Vocabulary.DesignPositions : Vocabulary.ExecutionPositions);
        }
    }

    private async Task CreateOverheadAsync(DateOnly day)
    {
        var groups = (await api.GetAsync(Admin, "/api/company/overhead/groups")).EnumerateArray()
            .Select(g => (Id: g.Id(), Name: g.GetProperty("name").GetString()!)).ToList();
        long GroupLike(string word) => groups.FirstOrDefault(g => g.Name.Contains(word, StringComparison.OrdinalIgnoreCase), groups[^1]).Id;

        var items = new (string Name, string Group, decimal Amount)[]
        {
            ("LT Office rent", "Rent", 3500m), ("LT Electricity and generator", "Rent", 900m), ("LT Internet", "Software", 180m),
            ("LT Design software licences", "Software", 1200m), ("LT Accounting firm", "Professional", 600m),
            ("LT Insurance", "Insurance", 450m), ("LT Cleaning", "Other", 250m),
        };
        foreach (var (name, group, amount) in items)
        {
            await api.PostAsync(Admin, "/api/company/overhead", new CreateOverheadRequest
            {
                Name = name, GroupId = GroupLike(group), Currency = "USD", Amount = amount, StartMonth = day,
            });
            world.Tally.Add("overhead_items");
        }
    }

    /// <summary>
    /// Invited as the company would: super admins and admins by the first super admin, employees by an admin of their
    /// domain. Accepting sets the name and the password and signs them in, which gives the token seeding uses.
    /// </summary>
    private async Task InviteAsync(Person person)
    {
        var inviter = person.IsEmployee
            ? world.People.First(p => p.IsAdmin && p.Domain == person.Domain && p.Joined)
            : world.FirstSuperAdmin;
        var invited = await api.PostAsync(inviter.Token, "/api/invitations", new CreateInvitationRequest
        {
            Email = person.Email, Role = person.Role, DomainCode = person.Domain, TeamId = person.TeamId,
            Position = person.Position ?? "Director", Pay = PayFor(person),
        });
        person.Id = invited.GetProperty("userId").GetInt64();

        var accepted = await api.PostAsync(null, "/api/invitations/accept", new AcceptInvitationRequest
        {
            Token = mailbox.InvitationToken(person.Email), FirstName = person.FirstName, LastName = person.LastName, Password = password,
        });
        person.Token = accepted.GetProperty("accessToken").GetString()!;
        person.Joined = true;
        world.Tally.Add($"users.{person.Role}");
        world.Tally.Add("pay_records");
    }

    private InvitationPayRequest PayFor(Person person) => person.Persona switch
    {
        Persona.SuperAdmin => new() { Basis = PayBasis.Monthly, Amount = Random.Amount(4000m, 6000m, 250m) },
        Persona.DesignAdmin or Persona.ExecutionAdmin => new() { Basis = PayBasis.Monthly, Amount = Random.Amount(2500m, 3800m, 100m) },
        _ when Random.Chance(0.1) => new() { Basis = PayBasis.Hourly, Amount = Random.Amount(8m, 18m, 0.5m) },
        Persona.DesignEmployee => new() { Basis = PayBasis.Monthly, Amount = Random.Amount(900m, 2200m, 50m) },
        _ => new() { Basis = PayBasis.Monthly, Amount = Random.Amount(700m, 1600m, 50m) },
    };

    private async Task SetPartTimeSchedulesAsync(DateOnly day)
    {
        foreach (var person in world.People.Where(p => p.PartTime && p.Joined))
        {
            // Monday, Wednesday and Friday, 09:00 to 13:00 (weekday 0 is Sunday).
            await api.PutAsync(Admin, $"/api/employees/{person.Id}/schedule", new SetScheduleRequest
            {
                EffectiveFrom = day,
                Days = [new ScheduleDayRequest(1, 540, 780), new ScheduleDayRequest(3, 540, 780), new ScheduleDayRequest(5, 540, 780)],
            });
            world.Tally.Add("schedules");
        }
    }

    /// <summary>People whose permissions differ from their role's, as a real company would have a few of.</summary>
    private async Task MakeJokersAsync()
    {
        var jokers = new (Persona Persona, string Note, PermissionChangeRequest[] Changes)[]
        {
            (Persona.DesignEmployee, "team lead: creates projects and clients",
                [Grant("project:create"), Grant("client:create")]),
            (Persona.ExecutionEmployee, "site lead: manages everyone's site expenses", [Grant("material:manage")]),
            (Persona.DesignAdmin, "may not void invoices", [new PermissionChangeRequest { Code = "invoice:void", Action = PermissionChangeAction.Revoke, Reason = "LT load test" }]),
            (Persona.DesignAdmin, "also prices Execution projects", [Grant("project:pricing_manage")]),
        };
        foreach (var (persona, note, changes) in jokers.Take(Math.Max(1, world.Plan.Users / 50)))
        {
            var person = world.People.Last(p => p.Persona == persona && p.Joined && p.LeavesOn is null && p.Joker is null && !p.PartTime);
            await api.PutAsync(Admin, $"/api/users/{person.Id}/permissions", new UpdateUserPermissionsRequest { Changes = changes });
            person.Joker = note;
            world.Tally.Add("jokers");
        }

        static PermissionChangeRequest Grant(string code) => new() { Code = code, Action = PermissionChangeAction.Grant, Reason = "LT load test" };
    }

    private async Task ReviewPayAsync(DateOnly day)
    {
        foreach (var person in Shuffled(world.Working(day, p => p.IsEmployee)).Take(world.Plan.Users / 10))
        {
            await api.PostAsync(Admin, $"/api/employees/{person.Id}/pay", new ChangePayRequest
            {
                Basis = PayBasis.Monthly, Amount = Random.Amount(1000m, 2400m, 50m), EffectiveFrom = day, Note = "LT pay review",
            });
            world.Tally.Add("pay_records");
        }
    }

    private List<Person> Shuffled(IEnumerable<Person> people) => Random.Shuffled(people);
}
