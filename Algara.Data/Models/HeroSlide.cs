using System.ComponentModel.DataAnnotations;

namespace Algara.Data.Models;

public class HeroSlide
{
    public int N { get; set; }

    [MaxLength(2048)]
    public string ImageUrl { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? EyebrowText { get; set; }

    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Subtitle { get; set; }

    [MaxLength(100)]
    public string ButtonText { get; set; } = "Разгледай каталога";

    [MaxLength(2048)]
    public string ButtonUrl { get; set; } = "/Product";

    public int DisplayOrder { get; set; } = 0;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
