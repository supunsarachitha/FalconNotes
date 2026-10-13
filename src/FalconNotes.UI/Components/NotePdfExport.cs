using FalconNotes.Core.Domain;
using FalconNotes.Core.Pdf;
using FalconNotes.UI.State;
using Microsoft.Extensions.DependencyInjection;

namespace FalconNotes.UI.Components;

/// <summary>
/// Export to PDF… in the menu of a note and of a todo list (docs/07, Export to PDF), as a plain helper the two cards
/// share, like <see cref="NoteRemoval"/>. It is offered only with the preference on, and only on a platform that can
/// write a PDF: one that cannot registers no <see cref="PdfExportService"/>.
/// </summary>
/// <param name="services">Finds the export, where the platform has one.</param>
/// <param name="state">Reads the preferences: whether the item is offered, and whether line breaks are kept.</param>
/// <param name="toasts">Shows the outcome.</param>
public sealed class NotePdfExport(IServiceProvider services, AppState state, Toasts toasts)
{
    private readonly PdfExportService? _pdf = services.GetService<PdfExportService>();
    private bool _running;

    /// <summary>Whether the menu has the item.</summary>
    public bool Offered => state.Preferences.PdfExport && _pdf is not null;

    /// <summary>Makes the PDF and opens the save dialog. Cancelling the dialog says nothing.</summary>
    /// <param name="note">The note or list.</param>
    /// <param name="content">Its text as the card shows it now.</param>
    /// <returns>A task that completes when the file is saved or the dialog is closed.</returns>
    public async Task RunAsync(Note note, string content)
    {
        // A second tap while the first PDF is still being drawn would open a second save dialog over the first.
        if (_pdf is null || _running)
        {
            return;
        }

        _running = true;
        try
        {
            if (await _pdf.ExportAsync(note, content, state.Preferences.LineBreaks))
            {
                toasts.Info("Saved as a PDF.");
            }
        }
        catch (Exception)
        {
            toasts.Error("The PDF could not be made. Please try again.");
        }
        finally
        {
            _running = false;
        }
    }
}
