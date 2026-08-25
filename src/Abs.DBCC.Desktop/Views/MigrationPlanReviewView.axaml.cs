using System;
using System.IO;
using Abs.DBCC.Desktop.Localization;
using Abs.DBCC.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Abs.DBCC.Desktop.Views;

public partial class MigrationPlanReviewView : UserControl
{
    private MigrationPlanReviewViewModel? _subscribedViewModel;

    public MigrationPlanReviewView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_subscribedViewModel is not null)
            _subscribedViewModel.ScriptExportRequested -= OnScriptExportRequested;

        _subscribedViewModel = DataContext as MigrationPlanReviewViewModel;
        if (_subscribedViewModel is not null)
            _subscribedViewModel.ScriptExportRequested += OnScriptExportRequested;
    }

    private async void OnScriptExportRequested(object? sender, string script)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.SaveScriptDialogTitle,
            SuggestedFileName = $"collation-migration-{DateTime.Now:yyyyMMdd-HHmmss}.sql",
            DefaultExtension = "sql",
            FileTypeChoices = [new FilePickerFileType(Strings.SqlScriptFileLabel) { Patterns = ["*.sql"] }]
        });

        if (file is null)
            return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(script);
    }
}
