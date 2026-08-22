using System;
using System.IO;
using Abs.DBCC.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Abs.DBCC.Desktop.Views;

public partial class MigrationResultView : UserControl
{
    public MigrationResultView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MigrationResultViewModel viewModel)
            viewModel.ExportRequested += OnExportRequested;
    }

    private async void OnExportRequested(object? sender, string reportText)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Migrationsbericht speichern",
            SuggestedFileName = $"migration-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            DefaultExtension = "txt",
            FileTypeChoices = [new FilePickerFileType("Textdatei") { Patterns = ["*.txt"] }]
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(reportText);
    }
}
