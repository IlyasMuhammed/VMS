using VMS.Shared.Exceptions;

namespace VMS.Modules.Auth.Infrastructure;

internal static class PasswordPolicy
{
    public const string Requirements =
        "Password must be at least 8 characters and contain an uppercase letter, a lowercase letter, a digit and a special character.";

    public static void Validate(string? password)
    {
        if (string.IsNullOrEmpty(password)
            || password.Length < 8
            || password.Length > 128
            || !password.Any(char.IsUpper)
            || !password.Any(char.IsLower)
            || !password.Any(char.IsDigit)
            || password.All(char.IsLetterOrDigit))
            throw new BadRequestException(Requirements);
    }
}
