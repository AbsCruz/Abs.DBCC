using System;
using System.IO;
using Abs.DBCC.Desktop.Localization;
using Abs.DBCC.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Abs.DBCC.Desktop.Views;

public partial class MigrationResultView : UserControl
{
    private MigrationResultViewModel? _subscribedViewModel;

    public MigrationResultView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
            _subscribedViewModel.ExportRequested -= OnExportRequested;

        _subscribedViewModel = DataContext as MigrationResultViewModel;
        if (_subscribedViewModel is not null)
            _subscribedViewModel.ExportRequested += OnExportRequested;
    }

    private async void OnExportRequested(object? sender, string reportText)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.SaveReportDialogTitle,
            SuggestedFileName = $"migration-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType(Strings.TextFileLabel) { Patterns = ["*.txt"] }]
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(reportText);
    }
}
