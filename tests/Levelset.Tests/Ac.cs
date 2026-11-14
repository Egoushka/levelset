namespace Levelset.Tests;

/// <summary>Ties a test to an acceptance check id in docs/specs/agent-output.md.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class AcAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}
