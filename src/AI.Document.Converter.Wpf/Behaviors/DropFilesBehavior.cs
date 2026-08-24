using System.Windows;
using System.Windows.Input;

namespace AI.Document.Converter.Wpf.Behaviors;

// FR-003: an attached behavior rather than a code-behind event handler, so the
// View stays declarative (docs/50 MVVM rule) - XAML wires
// local:DropFilesBehavior.DropCommand to a ViewModel command that accepts a
// string[] of dropped paths.
public static class DropFilesBehavior
{
    public static readonly DependencyProperty DropCommandProperty =
        DependencyProperty.RegisterAttached(
            "DropCommand",
            typeof(ICommand),
            typeof(DropFilesBehavior),
            new PropertyMetadata(null, OnDropCommandChanged));

    public static void SetDropCommand(UIElement element, ICommand value) =>
        element.SetValue(DropCommandProperty, value);

    public static ICommand GetDropCommand(UIElement element) =>
        (ICommand)element.GetValue(DropCommandProperty);

    private static void OnDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.AllowDrop = true;
        element.Drop -= OnDrop;
        element.Drop += OnDrop;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not UIElement element || !e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        var command = GetDropCommand(element);
        if (command is null)
        {
            return;
        }

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (command.CanExecute(paths))
        {
            command.Execute(paths);
        }
    }
}
