namespace Bassign.Application.Common.Security;

/// <summary>The acting user. A real HTTP-context implementation replaces the stub in step 05.</summary>
public interface ICurrentUser
{
    string UserId { get; }
}
