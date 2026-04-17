using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using System.ComponentModel.DataAnnotations;

namespace Algara.Web.ViewModels
{
    /// <summary>
    /// За задължителни bool полета (напр. "приемам условията"). Заменя
    /// неработещия [Range(typeof(bool), "true", "true")] трик — jQuery validate
    /// не разбира булеви стойности в range адаптера.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class MustBeTrueAttribute : ValidationAttribute, IClientModelValidator
    {
        public override bool IsValid(object? value) => value is bool b && b;

        public void AddValidation(ClientModelValidationContext context)
        {
            context.Attributes.TryAdd("data-val", "true");
            context.Attributes.TryAdd("data-val-mustbetrue", ErrorMessage ?? "Трябва да потвърдите.");
        }
    }
}
