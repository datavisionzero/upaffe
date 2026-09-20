using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Upaffe.Application.Access;
using Upaffe.Application.Failures;
using Upaffe.Application.Ports;
using Upaffe.Domain.Access;
using Upaffe.Infrastructure;
using Upaffe.Infrastructure.Persistence;

namespace Upaffe.Api.Hosting;

/// <summary>Host/database-authorized access setup; never starts the web host.</summary>
public static class LocalAccessCommand
{
    public static async Task<int> RunAsync(
        string[] args,
        IConfiguration configuration,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var options = Parse(args);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddUpaffeInfrastructure(DeploymentSecrets.Database(configuration));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<ReadBootstrapState>();
            services.AddScoped<EstablishOperator>();
            services.AddScoped<RecoverManagementCredential>();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var scoped = scope.ServiceProvider;

            await scoped.GetRequiredService<SchemaMigrator>().ApplyAsync(cancellationToken);
            IssuedCredential issued;
            if (options.Command == "bootstrap")
            {
                var state = await scoped.GetRequiredService<ReadBootstrapState>()
                    .ExecuteAsync(cancellationToken);
                if (!state.Required) throw Refusal.AlreadyInitialized();

                var password = options.PasswordFile is null
                    ? ReadPassword(input)
                    : ReadProtectedPasswordFile(options.PasswordFile);
                issued = await scoped.GetRequiredService<EstablishOperator>()
                    .ExecuteAsync(options.Email, password, options.CredentialName, cancellationToken);
            }
            else
            {
                issued = await scoped.GetRequiredService<RecoverManagementCredential>()
                    .ExecuteAsync(options.CredentialName, cancellationToken);
            }

            return await TryWriteAsync(output, new
            {
                status = options.Command == "bootstrap" ? "created" : "recovered",
                credential_name = issued.Credential.Name,
                token = issued.Token,
            }) ? 0 : 1;
        }
        catch (Refusal refusal) when (refusal.Code == "already_initialized")
        {
            return await TryWriteAsync(output, new { status = "already_initialized" }) ? 3 : 1;
        }
        catch (Refusal refusal) when (refusal.Code == "validation")
        {
            return await TryWriteAsync(output, new { status = "invalid_input", errors = refusal.Errors }) ? 2 : 1;
        }
        catch (Refusal refusal) when (refusal.Code == "not_found")
        {
            return await TryWriteAsync(output, new { status = "not_found" }) ? 4 : 1;
        }
        catch (ArgumentException)
        {
            return await TryWriteAsync(output, new { status = "invalid_input" }) ? 2 : 1;
        }
        catch (InvalidOperationException)
        {
            await TryWriteAsync(output, new { status = "failed" });
            return 1;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            await TryWriteAsync(output, new { status = "failed" });
            return 1;
        }
    }

    private static string ReadPassword(TextReader input)
    {
        var buffer = new char[Password.MaximumLength + 3];
        var count = 0;
        while (true)
        {
            var next = input.Read();
            if (next < 0) break;
            if (count == buffer.Length) throw new ArgumentException("Password input is too long.");
            buffer[count++] = (char)next;
        }

        var value = new string(buffer, 0, count).TrimEnd('\r', '\n');
        if (count - value.Length > 2 || value.Contains('\r') || value.Contains('\n') || value.Contains('\0'))
            throw new ArgumentException("Password input must be one line.");
        return value;
    }

    private static string ReadProtectedPasswordFile(string path)
    {
        if (OperatingSystem.IsWindows())
            throw new ArgumentException("Use standard input for the operator password on Windows.");
        var exposed = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((File.GetUnixFileMode(path) & exposed) != 0)
            throw new ArgumentException("The password file must be readable only by its owner.");
        return DeploymentSecrets.ReadFile("operator password", path, Password.MaximumLength);
    }

    private static Options Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("bootstrap" or "recover-credential"))
            throw new ArgumentException("Unknown command.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal) || values.ContainsKey(key))
                throw new ArgumentException("Invalid command arguments.");
            if (key == "--password-stdin")
                values.Add(key, "true");
            else if (++index < args.Length && !args[index].StartsWith("--", StringComparison.Ordinal))
                values.Add(key, args[index]);
            else
                throw new ArgumentException("Invalid command arguments.");
        }

        if (args[0] == "recover-credential")
        {
            if (values.Count != 1 || !values.TryGetValue("--name", out var name))
                throw new ArgumentException("Recovery requires --name.");
            return new Options(args[0], null, name, null);
        }

        if (!values.TryGetValue("--email", out var email)
            || !values.TryGetValue("--credential-name", out var credentialName)
            || values.Keys.Any(key => key is not ("--email" or "--credential-name" or "--password-file" or "--password-stdin"))
            || values.ContainsKey("--password-file") == values.ContainsKey("--password-stdin"))
            throw new ArgumentException("Bootstrap requires email, credential name, and one password source.");
        return new Options(args[0], email, credentialName, values.GetValueOrDefault("--password-file"));
    }

    private static async Task<bool> TryWriteAsync(TextWriter output, object value)
    {
        try
        {
            await output.WriteLineAsync(JsonSerializer.Serialize(value));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private sealed record Options(string Command, string? Email, string CredentialName, string? PasswordFile);
}
