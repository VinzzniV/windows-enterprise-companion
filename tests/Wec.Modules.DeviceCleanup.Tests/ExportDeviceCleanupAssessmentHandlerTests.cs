using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.DeviceCleanup.Handlers;

namespace Wec.Modules.DeviceCleanup.Tests;

public sealed class ExportDeviceCleanupAssessmentHandlerTests
{
    private readonly ISaveFileDialogService _saveFileDialog =
        Substitute.For<ISaveFileDialogService>();
    private readonly IClock _clock = Substitute.For<IClock>();

    [Fact]
    public async Task HandleAsync_RequiresBoundedContentAndRespectsCancellation()
    {
        _clock.UtcNow.Returns(new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.Zero));
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        var handler = new ExportDeviceCleanupAssessmentHandler(
            _saveFileDialog,
            _clock,
            NullLogger<ExportDeviceCleanupAssessmentHandler>.Instance);

        Result<ExportDeviceCleanupAssessmentResult> empty = await handler.HandleAsync(
            new ExportDeviceCleanupAssessmentRequest("  "),
            CancellationToken.None);
        Result<ExportDeviceCleanupAssessmentResult> tooLarge = await handler.HandleAsync(
            new ExportDeviceCleanupAssessmentRequest(
                new string('x', ExportDeviceCleanupAssessmentHandler.MaximumMarkdownLength + 1)),
            CancellationToken.None);
        Result<ExportDeviceCleanupAssessmentResult> cancelled = await handler.HandleAsync(
            new ExportDeviceCleanupAssessmentRequest("# Review"),
            CancellationToken.None);

        Assert.Equal(ErrorCode.InvalidRequest, empty.Error!.Code);
        Assert.Equal(ErrorCode.InvalidRequest, tooLarge.Error!.Code);
        Assert.True(cancelled.Value.Cancelled);
        Assert.Null(cancelled.Value.FilePath);
    }
}
