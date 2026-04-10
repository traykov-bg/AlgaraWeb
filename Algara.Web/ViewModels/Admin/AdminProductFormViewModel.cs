using Algara.Data.Models;
using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminProductFormViewModel
    {
        public int N { get; set; }

        [Required(ErrorMessage = "Името е задължително")]
        [MaxLength(300)]
        [Display(Name = "Наименование")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Описание")]
        public string? Description { get; set; }

        [Required(ErrorMessage = "Цената е задължителна")]
        [Range(0.01, 9_999_999, ErrorMessage = "Невалидна цена")]
        [Display(Name = "Цена (€)")]
        public decimal Price { get; set; }

        [Display(Name = "URL на снимка")]
        public string? ImageUrl { get; set; }

        [Display(Name = "По поръчка")]
        public bool IsCustomizable { get; set; }

        [Display(Name = "Препоръчан")]
        public bool IsFeatured { get; set; }

        [Display(Name = "Активен")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Категория")]
        public int? CategoryN { get; set; }

        // Попълва се от контролера за dropdown-а
        public IEnumerable<Category> Categories { get; set; } = [];

        // Под-категории
        public List<int> SelectedSubCategoryNs { get; set; } = new();
        public List<SubCategory> AvailableSubCategories { get; set; } = new();
    }
}
