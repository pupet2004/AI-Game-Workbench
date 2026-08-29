using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Workbench.App.ViewModels;

namespace Workbench.App;

/// <summary>
/// Given a view model, returns the corresponding view if possible.
/// </summary>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    public Control? Build(object? param)
    {
        if (param is null)
            return null;
        
        var viewModelType = param.GetType();
        var viewName = viewModelType.Name.Replace("ViewModel", "View", StringComparison.Ordinal);
        var names = new[]
        {
            viewModelType.FullName!.Replace("ViewModel", "View", StringComparison.Ordinal),
            $"Workbench.App.Views.{viewName}"
        };
        var type = names
            .Select(name => viewModelType.Assembly.GetType(name) ?? Type.GetType(name))
            .FirstOrDefault(value => value is not null);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }
        
        return new TextBlock { Text = "Not Found: " + names[0] };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
