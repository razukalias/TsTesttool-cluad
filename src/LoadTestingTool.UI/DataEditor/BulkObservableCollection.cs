using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace LoadTestingTool.UI.DataEditor;

/// <summary>An observable collection that can be refilled with one notification instead of one per item (thousands of single notifications made the grid and lists crawl).</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
