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

        [Display(Name = "Описание")]
        public string? Description { get; set; }

        [Display(Name = "Препоръчана")]
        public bool IsFeatured { get; set; }

        [Display(Name = "Активна")]
        public bool IsActive { get; set; } = true;
    }
}
