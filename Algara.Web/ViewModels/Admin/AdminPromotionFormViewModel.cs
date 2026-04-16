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

        [Display(Name = "Режим")]
        public PromotionType Type { get; set; } = PromotionType.Percent;

        [Display(Name = "Активна")]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Всички продукти като редове. Маркираните (Included=true) ще се запишат в ProductPromotions.
        /// </summary>
        public List<AdminPromotionProductRowViewModel> ProductRows { get; set; } = new();
    }
}
