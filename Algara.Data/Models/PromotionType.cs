namespace Algara.Data.Models
{
    /// <summary>
    /// Режим на работа на промоцията в UI-а.
    /// Крайната цена (PromoPrice) е единственият източник на истина — типът
    /// определя само коя колона се редактира: процент или сума/крайна цена.
    /// </summary>
    public enum PromotionType
    {
        Percent = 0,
        Amount  = 1,
    }
}
