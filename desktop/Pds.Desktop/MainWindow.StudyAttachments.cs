using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pds.Core;

namespace Pds.Desktop;

public partial class MainWindow
{
    private List<StudyAttachment> studyDraftAttachments=[];
    private Action<string>? attachmentOpenAction;
    private static string AttachmentSize(long bytes) => bytes<1024 ? $"{bytes} B" : bytes<1024*1024 ? $"{bytes/1024d:0.#} KB" : $"{bytes/(1024d*1024):0.#} MB";
    private async void AttachStudyFile_Click(object sender,RoutedEventArgs e)
    {
        if (busy) return;
        var picker=new OpenFileDialog { Title="학습 자료 첨부", Multiselect=true, CheckFileExists=true,
            Filter="학습 자료|*.pdf;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.hwp;*.hwpx;*.doc;*.docx;*.ppt;*.pptx;*.xls;*.xlsx;*.txt;*.md;*.csv;*.zip" };
        if (picker.ShowDialog(this)==true) await ImportStudyAttachmentsAsync(picker.FileNames);
    }
    private async Task<bool> ImportStudyAttachmentsAsync(IReadOnlyList<string> paths)
    {
        if (busy || paths.Count==0) return false;
        if (paths.Count+studyDraftAttachments.Count>StudyAttachment.MaxPerStudy)
        { ShowJournalError(StudyError,"학습 하나에 파일을 최대 10개 첨부할 수 있어요."); return false; }
        busy=true; Body.IsEnabled=false; var imported=new List<StudyAttachment>();
        try
        {
            await Task.Run(()=> { foreach (var path in paths) imported.Add(repository.Attachments.Import(path)); });
            studyDraftAttachments.AddRange(imported); DrawStudyAttachmentDraft(); StudyComposer.IsExpanded=true;
            StudyError.Visibility=Visibility.Collapsed; UpdateDraftStatus(); return true;
        }
        catch (Exception ex)
        {
            foreach (var file in imported) repository.DiscardAttachmentIfUnused(file);
            ShowJournalError(StudyError,ex.Message); return false;
        }
        finally { busy=false; Body.IsEnabled=true; }
    }
    private void ReleaseStudyAttachmentDraft()
    {
        foreach (var file in studyDraftAttachments) repository.DiscardAttachmentIfUnused(file);
        studyDraftAttachments=[]; if (StudyAttachmentDraftList is not null) StudyAttachmentDraftList.Children.Clear();
    }
    private void DrawStudyAttachmentDraft()
    {
        StudyAttachmentDraftList.Children.Clear();
        foreach (var file in studyDraftAttachments)
        {
            var row=new StackPanel { Margin=new Thickness(0,0,0,8) };
            row.Children.Add(StatsText(file.Name)); row.Children.Add(StatsText(AttachmentSize(file.Length),true));
            var actions=new WrapPanel(); actions.Children.Add(JournalAction("열기",async ()=>await OpenStudyAttachmentAsync(file)));
            actions.Children.Add(JournalAction("첨부 제거",()=>RemoveStudyAttachmentDraft(file.Id))); row.Children.Add(actions);
            StudyAttachmentDraftList.Children.Add(row);
        }
    }
    private void RemoveStudyAttachmentDraft(Guid id)
    {
        var file=studyDraftAttachments.Single(f=>f.Id==id); studyDraftAttachments.Remove(file);
        repository.DiscardAttachmentIfUnused(file); DrawStudyAttachmentDraft(); UpdateDraftStatus();
    }
    private StackPanel CreateStudyAttachmentList(IReadOnlyList<StudyAttachment> files)
    {
        var stack=new StackPanel { Margin=new Thickness(0,8,0,4) }; stack.Children.Add(StatsText($"첨부 자료 · {files.Count}개",true));
        foreach (var file in files)
        {
            var button=new Button { Content=new TextBlock { Text=$"{file.Name} · {AttachmentSize(file.Length)}",TextWrapping=TextWrapping.Wrap },
                HorizontalContentAlignment=HorizontalAlignment.Left,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,3,0,3),
                ToolTip="PC의 연결 프로그램으로 보관본을 열어요. 보관본은 읽기 전용이에요." };
            System.Windows.Automation.AutomationProperties.SetName(button,"첨부 파일 열기: "+file.Name);
            button.Click+=async (_,_)=>await OpenStudyAttachmentAsync(file); stack.Children.Add(button);
        }
        return stack;
    }
    private async Task<bool> OpenStudyAttachmentAsync(StudyAttachment file)
    {
        if (busy) return false;
        busy=true; Body.IsEnabled=false;
        try
        {
            var path=await Task.Run(()=>repository.Attachments.Require(file,true));
            if (verify)
            {
                if (attachmentOpenAction is null) throw new InvalidOperationException("No verification attachment opener");
                attachmentOpenAction(path);
            }
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute=true });
            Status.Text=file.Name+" · 연결 프로그램으로 열었어요.";
            return true;
        }
        catch (Exception ex) { HandleError("첨부 파일 열기",ex); Status.Text="첨부를 열지 못했어요. 파일과 연결 프로그램을 확인해 주세요."; return false; }
        finally { busy=false; Body.IsEnabled=true; }
    }
}
