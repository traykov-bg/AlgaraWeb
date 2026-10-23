namespace Algara.Web.ViewModels.Admin;

public static class AccountEmailLabels
{
    public static string Delivery(string? state) => state switch
    {
        "Pending" => "Чака изпращане", "Processing" => "Изпраща се", "Accepted" => "Прието от SMTP",
        "Failed" => "Неуспешно изпращане", "Cancelled" => "Отменено", _ => "—"
    };

    public static string Action(string action) => action switch
    {
        "Registered" or "RegistrationCreated" => "Регистрация и заявка за потвърждение",
        "PasswordResetRequested" or "ResetRequested" => "Заявка за възстановяване на парола",
        "EmailConfirmationRequested" or "ConfirmationRequested" => "Заявка за потвърждение на имейл",
        "PasswordReset" => "Паролата е възстановена",
        "EmailConfirmed" => "Имейлът е потвърден",
        "InvitationCreated" or "InvitationSent" or "InvitationRequested" => "Създадена покана",
        "InvitationResent" => "Подновена покана",
        "InvitationCancelled" => "Отменена покана",
        "InvitationAccepted" => "Приета покана",
        _ => "Действие по имейла на акаунта"
    };
}
