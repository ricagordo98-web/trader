using System.Text.RegularExpressions;

namespace Trader.Core.Alerts;

public static partial class NtfySettingsValidator
{
    public static IReadOnlyList<string> Validate(NtfySettings settings)
    {
        List<string> errors = [];

        if (settings.Topic.Length > 0 && !TopicPattern().IsMatch(settings.Topic))
        {
            errors.Add("O tópico deve ter 8 a 64 caracteres (letras, números, - e _).");
        }

        if (settings.Topic.Length > 0
            && (!Uri.TryCreate(settings.Server, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("O servidor ntfy deve ser um URL https.");
        }

        if (settings.PublicUrl.Length > 0
            && (!Uri.TryCreate(settings.PublicUrl, UriKind.Absolute, out Uri? publicUri) || publicUri.Scheme is not ("http" or "https")))
        {
            errors.Add("O endereço público da app deve ser um URL http(s) completo.");
        }

        return errors;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex TopicPattern();
}
