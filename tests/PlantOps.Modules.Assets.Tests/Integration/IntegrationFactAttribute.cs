using System.Runtime.CompilerServices;

namespace PlantOps.Modules.Assets.Tests.Integration;

/// <summary>
/// A fact that is reported as skipped unless PLANTOPS_INTEGRATION_TESTS=1. Setting <c>Skip</c> statically means
/// xUnit never instantiates the class or its fixture, so the SQL Server container is not even started.
/// </summary>
public sealed class IntegrationFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "PLANTOPS_INTEGRATION_TESTS";

    public IntegrationFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!Enabled)
        {
            Skip = $"Integration test: needs Docker. Set {EnvironmentVariable}=1 to run.";
        }
    }

    public static bool Enabled => Environment.GetEnvironmentVariable(EnvironmentVariable) == "1";
}
