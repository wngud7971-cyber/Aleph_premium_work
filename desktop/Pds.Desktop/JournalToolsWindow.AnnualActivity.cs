using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Pds.Core;

namespace Pds.Desktop;

internal sealed partial class JournalToolsView
{
    private AnnualActivityReport? annualReport;
    internal ComboBox AnnualMetricChoice {get;}=new(){MinWidth=180,MaxWidth=280,HorizontalAlignment=HorizontalAlignment.Left};
    private readonly StackPanel annualBody=new();
    private static readonly string[] MetricLabels=["계획 완료 · 건수","계획 완료율 · %","운동 · 건수","운동 · 시간(분)","학습 · 건수","학습 · 시간(분)"];

    private void DrawAnnualActivity()
    {
        if(annualReport is null)return;
        // One selector persists across month changes; rebuilding the chart never queries the database.
        if(annualSection is Border old)old.Child=null;
        if(AnnualMetricChoice.Parent is Panel previousSelector)previousSelector.Children.Remove(AnnualMetricChoice);
        if(annualBody.Parent is Panel previousBody)previousBody.Children.Remove(annualBody);
        if(AnnualMetricChoice.ItemsSource is null)
        {
            AnnualMetricChoice.ItemsSource=MetricLabels;AnnualMetricChoice.SelectedIndex=0;
            UiLayout.Compact(AnnualMetricChoice);
            System.Windows.Automation.AutomationProperties.SetName(AnnualMetricChoice,"연간 활동 그래프 지표");
            AnnualMetricChoice.SelectionChanged+=(_,_)=>DrawAnnualLines();
        }
        var panel=new StackPanel();panel.Children.Add(T($"{annualReport.Year}년 연간 활동",size:15));
        panel.Children.Add(T("1월부터 12월까지 · 한 번에 한 가지 지표로 비교해요.",true));
        panel.Children.Add(AnnualMetricChoice);panel.Children.Add(annualBody);
        annualSection=Card(panel);annualSection.Focusable=true;summaryRows.Children.Add(annualSection);DrawAnnualLines();
    }
    private void DrawAnnualLines()
    {
        if(annualReport is null || AnnualMetricChoice.SelectedIndex<0)return;
        annualBody.Children.Clear();var metric=(ActivityMetric)AnnualMetricChoice.SelectedIndex;
        var today=DateOnly.FromDateTime(DateTime.Today);
        bool Future(ActivityMonth m)=>new DateOnly(annualReport.Year,m.Month,1)>today;
        decimal? Value(ActivityMonth m)=>Future(m) && !m.HasRecords?null:m.Value(metric);
        string Label(ActivityMonth m)
        {
            if(Future(m) && !m.HasRecords)return "미도래";
            if(Value(m) is not decimal value)return metric==ActivityMetric.PlanRate?"계획 없음":"시간 미입력";
            return value.ToString("N2").TrimEnd('0').TrimEnd('.')+(metric==ActivityMetric.PlanRate?"%":metric is ActivityMetric.WorkoutMinutes or ActivityMetric.StudyMinutes?"분":"건");
        }
        string Detail(ActivityMonth m)
        {
            var state=Future(m)?" · 미도래 (저장된 기록)":annualReport.Year==today.Year && m.Month==today.Month?" · 진행 중":"";
            var coverage=metric==ActivityMetric.WorkoutMinutes?$" · 시간 입력 {m.TimedWorkouts}/{m.Workouts}건":metric==ActivityMetric.StudyMinutes?$" · 시간 입력 {m.TimedStudies}/{m.Studies}건":"";
            return $"{m.Month}월 · {Label(m)}{state}{coverage}";
        }
        var maximum=metric==ActivityMetric.PlanRate?100m:Math.Max(1,annualReport.Months.Select(m=>Value(m)??0).Max());
        annualBody.Children.Add(T($"{MetricLabels[(int)metric]} · 세로축 0~{maximum:N1}".TrimEnd('0').TrimEnd('.'),true));
        var plot=new Grid{Height=220,Margin=new Thickness(0,12,0,8)};
        var line=new AnnualLineChart(annualReport.Months.Select(Value).ToArray(),maximum,B("Accent"),B("Paper"),B("Line")){Height=140,Margin=new Thickness(0,30,0,0),VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false};
        plot.Children.Add(line);
        var chart=new UniformGrid{Columns=12};plot.Children.Add(chart);
        foreach(var m in annualReport.Months)
        {
            var column=new Grid{Margin=new Thickness(2,0,2,0),ToolTip=Detail(m),Background=Brushes.Transparent};
            column.RowDefinitions.Add(new(){Height=new GridLength(30)});column.RowDefinitions.Add(new(){Height=new GridLength(140)});column.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
            var value=T(Value(m) is decimal n?n.ToString("0.##"):"—",true);value.TextWrapping=TextWrapping.NoWrap;value.TextTrimming=TextTrimming.CharacterEllipsis;value.HorizontalAlignment=HorizontalAlignment.Stretch;value.TextAlignment=TextAlignment.Center;UiLayout.Numeric(value);value.TextAlignment=TextAlignment.Center;column.Children.Add(value);
            var month=T($"{m.Month}월",true);month.TextWrapping=TextWrapping.NoWrap;month.TextAlignment=TextAlignment.Center;month.Margin=new Thickness(0,8,0,0);UiLayout.Numeric(month);month.TextAlignment=TextAlignment.Center;Grid.SetRow(month,2);column.Children.Add(month);
            System.Windows.Automation.AutomationProperties.SetName(column,Detail(m));chart.Children.Add(column);
        }
        annualBody.Children.Add(plot);
        var table=new StackPanel();
        foreach(var m in annualReport.Months)table.Children.Add(T(Detail(m)));
        annualBody.Children.Add(new Expander{Header="월별 수치와 입력 상태 보기",Content=table,Margin=new Thickness(0,8,0,8)});
        annualBody.Children.Add(T("연간 활동 안내: 완료 건수·완료율은 계획에 배정된 날짜 기준이에요. 운동·학습은 저장한 날짜로 집계하며, 시간은 입력된 기록만 합산해요. 선의 점은 월별 수치이며, 빈 값은 연결하지 않아요. — 는 미도래·미입력·계획 없음으로, 실제 0과 구분해요. 미래 월에 저장된 기록과 진행 중인 달은 월별 수치에서 확인할 수 있어요.",true));
    }
}
