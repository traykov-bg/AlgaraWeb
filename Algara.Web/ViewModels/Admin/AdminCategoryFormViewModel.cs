using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminCategoryFormViewModel
    {
        public int N { get; set; }

        [Required(ErrorMessage = "Името е задължително")]
        [MaxLength(200)]
        [Display(Name = "Наименование")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Slug-ът е задължителен")]
        [MaxLength(200)]
        [RegularExpression(@"^[a-z0-9]+(-[a-z0-9]+)*$",
            ErrorMessage = "Slug-ът трябва да съдържа само малки букви, цифри и тирета")]
        [Display(Name = "URL Slug")]
        public string Slug { get; set; } = string.Empty;

        [Display(Name = "Описание")]
        public string? Description { get; set; }

        [Display(Name = "Препоръчана")]
        public bool IsFeatured { get; set; }

        [Display(Name = "Активна")]
        public bool IsActive { get; set; } = true;

        // Под-категориите — попълват се само при редактиране
        public List<AdminSubCategoryViewModel> SubCategories { get; set; } = new();
    }

    public class AdminSubCategoryViewModel
    {
        public int N { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
    }
}
