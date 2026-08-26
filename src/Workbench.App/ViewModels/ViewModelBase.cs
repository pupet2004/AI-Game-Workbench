using CommunityToolkit.Mvvm.ComponentModel;

namespace Workbench.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        Workbench.App.Services.LocalizationService.Current.PropertyChanged += OnLocalizationPropertyChanged;
    }

    public string this[string key] => Workbench.App.Services.LocalizationService.Current[key];

    private void OnLocalizationPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is "Item[]" or nameof(Workbench.App.Services.LocalizationService.Language))
        {
            OnPropertyChanged("Item[]");
            // Dynamic status, summary, and error properties are often computed from
            // localized labels. Refresh them together when the language changes.
            foreach (var property in GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length == 0)
                    OnPropertyChanged(property.Name);
            }
        }
    }
}
