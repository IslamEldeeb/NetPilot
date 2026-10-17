using Microsoft.AspNetCore.Components;
using MudBlazor;
using NetPilot.Core.Automations;
using NetPilot.Web.Components.DesignSystem;

namespace NetPilot.Web;

public sealed class AutomationFeedback(IAutomationCommands commands, ISnackbar snackbar, NavigationManager navigation, ILogger<AutomationFeedback> logger)
{
    public void Saved(AutomationChange change, string message, string returnPath) =>
        snackbar.AddUndo(message + " Awaiting Agent.", () => RestoreAsync(change.Id, returnPath));

    private async Task RestoreAsync(string changeId, string returnPath)
    {
        try
        {
            var outcome = await commands.UndoAsync(changeId, default);
            snackbar.Add(outcome == UndoOutcome.Restored ? "Configuration restored. Awaiting Agent."
                : $"Undo: {outcome}. Refresh before editing.");
            navigation.NavigateTo(returnPath + "?change=" + changeId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Configuration Undo failed");
            snackbar.Add("Undo could not complete. Check saved configuration before retrying.", Severity.Error);
        }
    }
}
