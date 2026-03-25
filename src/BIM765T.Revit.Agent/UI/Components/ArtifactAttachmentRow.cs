using System;
using System.Windows;
using System.Windows.Controls;
using BIM765T.Revit.Agent.UI.Chat;
using BIM765T.Revit.Agent.UI.Theme;

namespace BIM765T.Revit.Agent.UI.Components;

internal sealed class ArtifactAttachmentRow : Border
{
    internal ArtifactAttachmentRow(ArtifactAttachmentVm vm, Action<ArtifactAttachmentVm, bool> onAction)
    {
        vm ??= new ArtifactAttachmentVm();
        Background = AppTheme.SurfaceMuted;
        BorderBrush = AppTheme.SubtleBorder;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(AppTheme.ButtonRadius);
        Margin = new Thickness(0, 0, 0, AppTheme.SpaceSM);
        Padding = new Thickness(AppTheme.SpaceSM);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textStack = new StackPanel();
        textStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(vm.Label) ? "Artifact" : vm.Label,
            Foreground = AppTheme.TextPrimary,
            FontSize = AppTheme.FontCaption,
            FontWeight = FontWeights.SemiBold
        });
        textStack.Children.Add(new TextBlock
        {
            Text = vm.Path ?? string.Empty,
            Foreground = AppTheme.TextMuted,
            FontSize = AppTheme.FontSmall,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetColumn(textStack, 0);
        grid.Children.Add(textStack);

        var actions = new WrapPanel
        {
            Margin = new Thickness(AppTheme.SpaceSM, 0, 0, 0)
        };
        actions.Children.Add(new SuggestionChip("Open", () => onAction(vm, true), AppTheme.AccentBlue));
        actions.Children.Add(new SuggestionChip("Copy path", () => onAction(vm, false), AppTheme.TextSecondary));
        Grid.SetColumn(actions, 1);
        grid.Children.Add(actions);

        Child = grid;
    }
}
