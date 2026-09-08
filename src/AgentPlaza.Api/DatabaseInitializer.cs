using AgentPlaza.Domain;
using AgentPlaza.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentPlaza.Api;

/// <summary>Creates the development database and initial reporter identities.</summary>
public sealed class DatabaseInitializer(
    PlazaDbContext dbContext,
    IOptions<BootstrapOptions> options,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>Initializes storage and inserts configured people when missing.</summary>
    /// <param name="cancellationToken">A token that cancels initialization.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        foreach (var configuredPerson in options.Value.People)
        {
            if (configuredPerson.Id == Guid.Empty || string.IsNullOrWhiteSpace(configuredPerson.Name))
            {
                continue;
            }

            var person = await dbContext.People
                .Include(value => value.Installations)
                .SingleOrDefaultAsync(value => value.Id == configuredPerson.Id, cancellationToken);
            if (person is null)
            {
                person = new Person
                {
                    Id = configuredPerson.Id,
                    Name = configuredPerson.Name,
                    AvatarSeed = string.IsNullOrWhiteSpace(configuredPerson.AvatarSeed) ? configuredPerson.Name : configuredPerson.AvatarSeed,
                };
                dbContext.People.Add(person);
            }

            foreach (var configuredInstallation in configuredPerson.Installations.Where(value => value.Id != Guid.Empty && !string.IsNullOrWhiteSpace(value.Token)))
            {
                var installation = person.Installations.SingleOrDefault(value => value.Id == configuredInstallation.Id);
                if (installation is null)
                {
                    person.Installations.Add(new Installation
                    {
                        Id = configuredInstallation.Id,
                        Name = configuredInstallation.Name,
                        TokenHash = TokenHash.Create(configuredInstallation.Token),
                    });
                }
                else
                {
                    installation.Name = configuredInstallation.Name;
                    installation.TokenHash = TokenHash.Create(configuredInstallation.Token);
                }
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Agent Plaza database initialized with {PersonCount} configured people", options.Value.People.Count);
    }
}
