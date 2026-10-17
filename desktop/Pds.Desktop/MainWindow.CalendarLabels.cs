using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private static FormattedText CalendarFormattedText(TextBlock label, string text) => new(text,
        CultureInfo.GetCultureInfo("ko-KR"), FlowDirection.LeftToRight,
        new Typeface(label.FontFamily, label.FontStyle, label.FontWeight, label.FontStretch), label.FontSize,
        Brushes.Black, VisualTreeHelper.GetDpi(label).PixelsPerDip);

    private static double CalendarTextWidth(TextBlock label, string text) => CalendarFormattedText(label, text).WidthIncludingTrailingWhitespace;

    private void AddCalendarLabels(Grid content, AnniversaryOccurrence[]? occasions, KoreanHoliday? holiday, string countsCaption, bool selectedDay)
    {
        var hasOccasions = occasions is { Length: > 0 };
        if (holiday is not null)
        {
            var normal = holiday.Names.Count > 1 ? $"{holiday.Names[0]} 외 {holiday.Names.Count - 1}" : holiday.Label;
            if (normal.StartsWith("대체공휴일")) normal = "대체공휴일";
            normal = normal.Replace("설날 연휴", "설날").Replace("추석 연휴", "추석")
                .Replace("전국동시지방선거", "지방선거").Replace("제21대 대통령선거", "대통령선거");
            var compact = holiday.Names.Count > 1 ? holiday.Names[0] + $"\n외 {holiday.Names.Count - 1}" : normal switch
            {
                "대체공휴일" => "대체\n공휴일", "부처님오신날" => "부처님\n오신날",
                "대통령선거" => "대통령\n선거", "임시공휴일" => "임시\n공휴일", _ => normal
            };
            var holidayLabel = new TextBlock { Text = normal, FontFamily = FontFamily, FontSize = UiTypography.Date, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top, ToolTip = holiday.Label, Tag = "holiday-caption",
                Foreground = ResourceBrush(selectedDay ? "SelectedSunday" : "Sunday") };
            if (hasOccasions)
            {
                // Leave two readable lines for the person's name when both kinds of dates coincide.
                content.RowDefinitions[0].Height = new GridLength(26);
                holidayLabel.TextWrapping = TextWrapping.NoWrap; holidayLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            }
            var dateNumber = content.Children.OfType<TextBlock>().First();
            Rect Ink(TextBlock label, string text) => CalendarFormattedText(label, text).BuildGeometry(new Point()).Bounds;
            var dateInk = Ink(dateNumber, dateNumber.Text);
            var holidayInk = Ink(holidayLabel, normal);
            if (!dateInk.IsEmpty && !holidayInk.IsEmpty && holidayInk.Height > 0)
                holidayLabel.FontSize *= dateInk.Height / holidayInk.Height;
            void AlignInk()
            {
                if (dateNumber.ActualHeight <= 0 || holidayLabel.ActualHeight <= 0) return;
                var firstLine = holidayLabel.Text.Split('\n')[0];
                var nameShape = CalendarFormattedText(holidayLabel, firstLine);
                // Anchor the first line directly; TextBlock baseline offsets can change after wrapping.
                var top = dateInk.Top - nameShape.BuildGeometry(new Point()).Bounds.Top;
                if (double.IsFinite(top) && Math.Abs(holidayLabel.Margin.Top - top) > 0.01)
                    holidayLabel.Margin = new Thickness(0, top, 0, 0);
            }
            holidayLabel.SizeChanged += (_, _) =>
            {
                if (holidayLabel.ActualWidth > 0)
                {
                    var text = hasOccasions || CalendarTextWidth(holidayLabel, normal) <= holidayLabel.ActualWidth ? normal : compact;
                    if (holidayLabel.Text != text) holidayLabel.Text = text;
                }
                AlignInk();
            };
            dateNumber.SizeChanged += (_, _) => AlignInk();
            Grid.SetColumn(holidayLabel, 2); content.Children.Add(holidayLabel);
        }
        if (!hasOccasions && countsCaption.Length == 0) return;
        var body = new StackPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0) };
        TextBlock? birthday = null;
        if (hasOccasions)
        {
            var entry = occasions![0].Entry;
            var full = entry.DisplayLabel + (occasions.Length > 1 ? $" 외 {occasions.Length - 1}" : "");
            birthday = new TextBlock { Text = full, FontFamily = FontFamily, FontSize = UiTypography.Grow(12), TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, Foreground = ResourceBrush(selectedDay ? "AccentInk" : "Ink"),
                ToolTip = string.Join("\n", occasions.Select(o => o.Entry.DisplayLabel)), Tag = "anniversary-caption" };
            birthday.MaxHeight = Math.Ceiling(birthday.FontFamily.LineSpacing * birthday.FontSize * 2) + 2;
            birthday.SizeChanged += (_, _) =>
            {
                if (birthday.ActualWidth <= 0) return;
                var text = CalendarTextWidth(birthday, full) <= birthday.ActualWidth ? full : entry.KindLabel + ":\n" + entry.Name + (occasions.Length > 1 ? $" 외 {occasions.Length - 1}" : "");
                if (birthday.Text != text) birthday.Text = text;
            };
            body.Children.Add(birthday);
        }
        if (countsCaption.Length > 0)
        {
            var count = new TextBlock { Text = hasOccasions ? countsCaption.Split('\n')[0] : countsCaption,
                FontSize = UiTypography.Grow(12), Margin = new Thickness(0, hasOccasions ? 2 : 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = countsCaption,
                Foreground = ResourceBrush(selectedDay ? "AccentInk" : "Muted"), Tag = "record-count-caption" };
            body.Children.Add(count);
            if (birthday is null && countsCaption.Contains('\n'))
            {
                content.SizeChanged += (_, _) =>
                {
                    var required = CalendarFormattedText(count, countsCaption).Height + 2;
                    var text = required <= content.RowDefinitions[1].ActualHeight ? countsCaption : countsCaption.Split('\n')[0];
                    if (count.Text != text) count.Text = text;
                };
            }
            if (birthday is not null)
            {
                void FitCount()
                {
                    var required = birthday.ActualHeight + Math.Ceiling(count.FontFamily.LineSpacing * count.FontSize) + 4;
                    count.Visibility = required <= content.RowDefinitions[1].ActualHeight ? Visibility.Visible : Visibility.Collapsed;
                }
                content.SizeChanged += (_, _) => FitCount(); birthday.SizeChanged += (_, _) => FitCount();
            }
        }
        Grid.SetRow(body, 1); Grid.SetColumnSpan(body, 3); content.Children.Add(body);
    }
    private void DrawSelectedHolidays()
    {
        HolidayDayList.Children.Clear(); var holiday = KoreanHolidays.On(selected);
        HolidayDayList.Visibility = holiday is null ? Visibility.Collapsed : Visibility.Visible;
        if (holiday is not null) HolidayDayList.Children.Add(new TextBlock { Text = "공휴일: " + holiday.Label,
            Foreground = ResourceBrush("Sunday"), TextWrapping = TextWrapping.Wrap });
    }
}
