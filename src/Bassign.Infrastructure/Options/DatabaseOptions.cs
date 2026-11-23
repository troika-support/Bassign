using System.ComponentModel.DataAnnotations;

namespace Bassign.Infrastructure.Options;

public sealed class DatabaseOptions
{
    public const string Section = "Database";

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = string.Empty;
}
