using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using MudBlazor;

namespace NetPilot.Web.Components.DesignSystem;

public sealed class Confirm(IDialogService dialogs)
{
    public async Task<bool> DangerAsync(string title, string message, string confirmText = "Delete") =>
        await dialogs.ShowMessageBoxAsync(title,
            new MarkupString($"<span class='np-confirm-danger'>{HtmlEncoder.Default.Encode(message)}</span>"),
            yesText: confirmText, cancelText: "Cancel",
            options: new DialogOptions { CloseOnEscapeKey = true, DefaultFocus = DefaultFocus.FirstChild }) == true;
}

public static class SnackbarUndo
{
    public static void AddUndo(this ISnackbar snackbar, string message, Func<Task> undo) =>
        snackbar.Add(message, Severity.Normal, options =>
        {
            options.Action = "Undo";
            options.VisibleStateDuration = 10_000;
            options.RequireInteraction = false;
            options.OnClick = _ => undo();
        });
}
