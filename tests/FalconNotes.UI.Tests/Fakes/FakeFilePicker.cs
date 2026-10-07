using FalconNotes.Core.Platform;

namespace FalconNotes.UI.Tests.Fakes;

/// <summary>Answers with whatever files the test queued, instead of opening a real system picker.</summary>
public sealed class FakeFilePicker : IFilePicker
{
    private readonly Queue<IReadOnlyList<PickedFile>> _answers = new();

    /// <summary>Queues the next call's answer; an empty list reads as the user cancelling the picker.</summary>
    /// <param name="files">The files to hand back.</param>
    public void Enqueue(params PickedFile[] files) => _answers.Enqueue(files);

    /// <inheritdoc />
    public Task<IReadOnlyList<PickedFile>> PickFilesAsync(IReadOnlyList<string>? extensions = null) =>
        Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : (IReadOnlyList<PickedFile>)[]);
}
