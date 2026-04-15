using Algara.Data.Models;
using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels.Admin
{
    public class AdminPromotionFormViewModel
    {
        public int N { get; set; }

        [Required(ErrorMessage = "Наименованието е задължително.")]
        [MaxLength(300)]
        [Display(Name = "Наименование")]
        public string Name { get; set; } = string.Empty;

        [Required(ErrorMessage = "Началната дата е задължителна.")]
        [Display(Name = "Начало")]
        public DateTime StartDate { get; set; } = DateTime.Today;

        [Required(ErrorMessage = "Крайната дата е задължителна.")]
        [Display(Name = "Край")]
        public DateTime EndDate { get; set; } = DateTime.Today.AddDays(7);

        [Required(ErrorMessage = "Процентът е задължителен.")]
        [Range(0.001, 99.999, ErrorMessage = "Процентът трябва да е между 0.001 и 99.999.")]
        [Display(Name = "Отстъпка (%)")]
        public decimal DiscountPercent { get; set; }

        [Display(Name = "Активна")]
        public bool IsActive { get; set; } = true;

        // IDs на избраните продукти
        public List<int> SelectedProductNs { get; set; } = new();

        // За попълване на списъка с продукти
        public List<Product> AllProducts { get; set; } = new();
    }
}
