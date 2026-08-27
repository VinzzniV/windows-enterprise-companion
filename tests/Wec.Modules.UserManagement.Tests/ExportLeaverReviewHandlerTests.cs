using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Wec.Core.Abstractions;
using Wec.Core.Results;
using Wec.Modules.UserManagement.Handlers;

namespace Wec.Modules.UserManagement.Tests;

public sealed class ExportLeaverReviewHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 27, 12, 34, 0, TimeSpan.Zero);
    private readonly ISaveFileDialogService _saveFileDialog = Substitute.For<ISaveFileDialogService>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public ExportLeaverReviewHandlerTests()
    {
        _clock.UtcNow.Returns(Now);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_RejectsEmptyContentWithoutOpeningDialog(string markdown)
    {
        ExportLeaverReviewHandler handler = CreateHandler();

        Result<ExportLeaverReviewResult> result = await handler.HandleAsync(
            new ExportLeaverReviewRequest(markdown), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        _saveFileDialog.DidNotReceive().PromptForSavePath(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleAsync_RejectsOversizedContentWithoutOpeningDialog()
    {
        ExportLeaverReviewHandler handler = CreateHandler();

        Result<ExportLeaverReviewResult> result = await handler.HandleAsync(
            new ExportLeaverReviewRequest(new string('x', ExportLeaverReviewHandler.MaximumMarkdownLength + 1)),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorCode.InvalidRequest, result.Error!.Code);
        _saveFileDialog.DidNotReceive().PromptForSavePath(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleAsync_ReturnsCancelledWhenDialogIsDismissed()
    {
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        ExportLeaverReviewHandler handler = CreateHandler();

        Result<ExportLeaverReviewResult> result = await handler.HandleAsync(
            new ExportLeaverReviewRequest("# Review"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Cancelled);
        Assert.Null(result.Value.FilePath);
        _saveFileDialog.Received(1).PromptForSavePath(
            "wec-leaver-review-20260827-1234.md",
            Arg.Is<string>(filter => filter.Contains("Markdown files", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task HandleAsync_WritesExactMarkdownToSelectedFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"wec-leaver-export-{Guid.NewGuid():N}");
        string targetPath = Path.Combine(directory, "review.md");
        Directory.CreateDirectory(directory);
        _saveFileDialog.PromptForSavePath(Arg.Any<string>(), Arg.Any<string>()).Returns(targetPath);
        ExportLeaverReviewHandler handler = CreateHandler();

        try
        {
            const string markdown = "# Leaver review\n\n- [ ] Confirm access";
            Result<ExportLeaverReviewResult> result = await handler.HandleAsync(
                new ExportLeaverReviewRequest(markdown), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.False(result.Value.Cancelled);
            Assert.Equal(targetPath, result.Value.FilePath);
            Assert.Equal(markdown, await File.ReadAllTextAsync(targetPath));
        }
        finally
        {
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory);
            }
        }
    }

    private ExportLeaverReviewHandler CreateHandler() => new(
        _saveFileDialog,
        _clock,
        NullLogger<ExportLeaverReviewHandler>.Instance);
}
