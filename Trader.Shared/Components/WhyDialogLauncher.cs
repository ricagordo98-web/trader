using MudBlazor;

namespace Trader.Shared.Components;

public static class WhyDialogLauncher
{
    public static Task ShowAsync(IDialogService dialogService, string symbol, string name) =>
        dialogService.ShowAsync<WhyDialog>(
            string.Empty,
            new DialogParameters<WhyDialog> { { x => x.Symbol, symbol }, { x => x.Name, name } },
            new DialogOptions { MaxWidth = MaxWidth.Medium, FullWidth = true, CloseButton = true, CloseOnEscapeKey = true });
}
