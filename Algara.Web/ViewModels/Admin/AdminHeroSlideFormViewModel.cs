using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels.Admin;

public class AdminHeroSlideFormViewModel
{
    public int N { get; set; }

    [Required(ErrorMessage = "URL на изображението е задължителен")]
    [MaxLength(2048)]
    [Display(Name = "URL на изображение")]
    public string ImageUrl { get; set; } = string.Empty;

    [MaxLength(200)]
    [Display(Name = "Малък текст (eyebrow)")]
    public string? EyebrowText { get; set; }

    [Required(ErrorMessage = "Заглавието е задължително")]
    [MaxLength(300)]
    [Display(Name = "Заглавие")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    [Display(Name = "Подзаглавие")]
    public string? Subtitle { get; set; }

    [Required(ErrorMessage = "Текстът на бутона е задължителен")]
    [MaxLength(100)]
    [Display(Name = "Текст на бутон")]
    public string ButtonText { get; set; } = "Разгледай каталога";

    [Required(ErrorMessage = "URL при кликане е задължителен")]
    [MaxLength(2048)]
    [Display(Name = "URL при кликане на слайда")]
    public string ButtonUrl { get; set; } = "/Product";

    [Display(Name = "Поредност (по-малко = по-напред)")]
    public int DisplayOrder { get; set; } = 0;

    [Display(Name = "Активен")]
    public bool IsActive { get; set; } = true;
}
