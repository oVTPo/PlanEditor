using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using PlanEditor.App.Search;

namespace PlanEditor.App.Controls;

public partial class SearchPanel : UserControl
{
    private VietnamSearchService? _searchService;
    private IDisposable? _pendingSearch;
    private List<VietnamSearchResult> _results = new();

    public event EventHandler<VietnamSearchResult>? ResultSelected;

    public SearchPanel()
    {
        InitializeComponent();

        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    private void OnAttachedToVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        try
        {
            _searchService ??= new VietnamSearchService();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Search initialization failed: {exception}"
            );
        }
    }

    private void OnDetachedFromVisualTree(
        object? sender,
        VisualTreeAttachmentEventArgs e)
    {
        _pendingSearch?.Dispose();
        _pendingSearch = null;

        _searchService?.Dispose();
        _searchService = null;
    }

    private void OnSearchTextChanged(
        object? sender,
        TextChangedEventArgs e)
    {
        _pendingSearch?.Dispose();
        _pendingSearch = null;

        string text = SearchTextBox.Text?.Trim() ?? string.Empty;

        if (text.Length < 2)
        {
            ClearResults();
            return;
        }

        _pendingSearch = DispatcherTimer.RunOnce(
            () => ExecuteSearch(text),
            TimeSpan.FromMilliseconds(180)
        );
    }

    private void ExecuteSearch(string query)
    {
        if (_searchService == null)
            return;

        if (!string.Equals(
                SearchTextBox.Text?.Trim(),
                query,
                StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            _results = _searchService.Search(query, 15);

            ResultsList.ItemsSource = _results;
            ResultsContainer.IsVisible = _results.Count > 0;

            ResultsList.SelectedIndex =
                _results.Count > 0 ? 0 : -1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"Search failed: {exception}"
            );

            ClearResults();
        }
    }

    private void OnSearchKeyDown(
        object? sender,
        KeyEventArgs e)
    {
        if (!ResultsContainer.IsVisible || _results.Count == 0)
        {
            if (e.Key == Key.Escape)
            {
                ClearResults();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Down:
            {
                int index = ResultsList.SelectedIndex;

                if (index < 0)
                    index = 0;
                else
                    index = Math.Min(index + 1, _results.Count - 1);

                ResultsList.SelectedIndex = index;
                ResultsList.ScrollIntoView(_results[index]);

                e.Handled = true;
                break;
            }

            case Key.Up:
            {
                int index = ResultsList.SelectedIndex;

                if (index < 0)
                    index = 0;
                else
                    index = Math.Max(index - 1, 0);

                ResultsList.SelectedIndex = index;
                ResultsList.ScrollIntoView(_results[index]);

                e.Handled = true;
                break;
            }

            case Key.Enter:
            {
                SelectCurrentResult();
                e.Handled = true;
                break;
            }

            case Key.Escape:
            {
                ResultsContainer.IsVisible = false;
                e.Handled = true;
                break;
            }
        }
    }

    private void OnResultPointerReleased(
        object? sender,
        PointerReleasedEventArgs e)
    {
        SelectCurrentResult();
    }

    private void SelectCurrentResult()
    {
        if (ResultsList.SelectedItem is not VietnamSearchResult result)
            return;

        ResultsContainer.IsVisible = false;
        ResultSelected?.Invoke(this, result);
    }

    private void ClearResults()
    {
        _results = new List<VietnamSearchResult>();
        ResultsList.ItemsSource = null;
        ResultsList.SelectedIndex = -1;
        ResultsContainer.IsVisible = false;
    }
}
