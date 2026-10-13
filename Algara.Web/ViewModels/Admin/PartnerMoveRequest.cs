using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels.Admin;

public sealed class PartnerMoveRequest
{
    public const int SelectionLimit = 1000;

    [Required, Range(0, int.MaxValue)] public int? DestinationGroupN { get; set; }
    [Required, StringLength(64)] public string? DestinationRowVersion { get; set; }
    [Required, MaxLength(SelectionLimit)] public List<PartnerMoveSelection>? Groups { get; set; } = [];
    [Required, MaxLength(SelectionLimit)] public List<PartnerMoveSelection>? Partners { get; set; } = [];
}

public sealed class PartnerMoveSelection
{
    [Required, Range(0, int.MaxValue)] public int? N { get; set; }
    [Required, StringLength(64)] public string? RowVersion { get; set; }
}
