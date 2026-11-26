using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ScannerPresentation;
using ScannerPresentation.Wpf;

namespace ResonancePilot;

internal sealed partial class ScannerWindow:Window
{
    const string MicrosoftSubmissionUrl="https://www.microsoft.com/en-us/wdsi/filesubmission";
    static readonly Brush Ground=ScannerTheme.Ground,Ink=ScannerTheme.Ink,Accent=ScannerTheme.Accent;
    readonly Grid root=new(){FlowDirection=FlowDirection.RightToLeft,Background=Ground};
    readonly Grid body=new(),homePane=new(),scanPane=new(),scenePanel=new(),homeSceneHost=new(),scanDeckHost=new();
    readonly ResonanceScene scene=new();readonly TelemetryDeck deck=new(),resultDeck=new();readonly ResultsView results=new();
    readonly TextBlock engine=Text("",14),selection=Text("בחרו תיקייה לבדיקה",22),homeReason=Text("",16),phase=Text("",16),found=Text("—",28),ack=Text("—",28),elapsed=Text("00:00",14),path=Text("",15),foot=Text("",13.5);
    readonly TextBlock videoNotice=Text("",16);
    readonly Button choose=Action("בחירת תיקייה","chooseFolder"),start=Action("התחלת סריקה","startScan"),cancel=Action("הפסקת המתנה","cancelWait"),engineCheck=Action("בדיקת מנוע","refreshEngine"),fullPath=Action("הנתיב המלא","fullPath");
    readonly CheckBox motion=new(){Content="תנועה דקורטיבית",IsChecked=true,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center};
    readonly ScannerController controller;readonly bool demo;readonly DemoScannerSession? demoSession;
    readonly DispatcherTimer clockTimer=new(){Interval=TimeSpan.FromSeconds(1)};readonly Stopwatch clock=new();
    ScannerPage? shownPage;bool closing;
    internal ScannerWindow(bool demo)
    {
        this.demo=demo;Title="גשר הסריקה — בדיקת תיקיות באמצעות Microsoft Defender"+(demo?" — DEMO":"");
        Width=1280;Height=900;MinWidth=960;MinHeight=760;WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=new("Segoe UI");FontSize=16;Foreground=Ink;Background=Ground;Content=root;
        ScannerTheme.ApplyResources(Resources);Resources[typeof(Button)]=ScannerTheme.ButtonStyle;start.Style=ScannerTheme.PrimaryButtonStyle;
        IScannerSession session=demo?(demoSession=new DemoScannerSession()):new WindowsScannerSession();
        controller=new(new WpfUiScheduler(Dispatcher),session);controller.PropertyChanged+=ControllerStateChanged;
        controller.VideoRequested+=url=>{if(demo){if(demoSession!.Scenario=="videoFailure")throw new InvalidOperationException("DEMO video dispatch failure");return;}Process.Start(new ProcessStartInfo(url){UseShellExecute=true});};
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new Grid{Margin=new(24,10,24,10)};header.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var brand=new StackPanel{Orientation=Orientation.Horizontal};brand.Children.Add(Text("גשר הסריקה",20));engine.Margin=new(18,5,18,0);brand.Children.Add(engine);header.Children.Add(brand);
        var tools=new WrapPanel{VerticalAlignment=VerticalAlignment.Center};tools.Children.Add(motion);engineCheck.Margin=new(16,0,0,0);tools.Children.Add(engineCheck);var help=Action("עזרה","help");help.Margin=new(8,0,0,0);help.Click+=(_,_)=>Info("עזרה ומגבלות","בדיקת תיקיות מקומיות באמצעות Microsoft Defender.\nהיישום אינו מעלה קבצים ביוזמתו; Defender פועל לפי הגדרות Windows.\nאפשר לפתוח את דף הדיווח הידני של Microsoft. פתיחת הדף אינה שולחת קובץ, נתיב או דיווח.\nהפסקת המתנה עוצרת את תור הקבצים אחרי חזרת הקובץ הפעיל, ולא שולחת פקודת עצירה כללית למנוע.\nתוצאה ללא דיווח אינה הבטחת בטיחות.\n"+controller.State.Engine.Description,offerReporting:true);tools.Children.Add(help);Grid.SetColumn(tools,1);header.Children.Add(tools);Add(new Border{Background=ScannerTheme.HeaderSurface,BorderBrush=ScannerTheme.Rule,BorderThickness=new(0,0,0,1),Child=header},0);
        body.Margin=new(20,0,20,2);body.Children.Add(homePane);body.Children.Add(scanPane);body.Children.Add(results);Add(body,1);
        scenePanel.FlowDirection=FlowDirection.LeftToRight;scenePanel.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});scenePanel.RowDefinitions.Add(new(){Height=GridLength.Auto});scenePanel.Children.Add(scene);var label=Text("המחשת פעילות · אמנות דקורטיבית",13.5);label.HorizontalAlignment=HorizontalAlignment.Center;label.Margin=new(0,4,0,8);Grid.SetRow(label,1);scenePanel.Children.Add(label);
        homePane.ColumnDefinitions.Add(new(){Width=new(340)});homePane.ColumnDefinitions.Add(new(){Width=new(24)});homePane.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        var homeActions=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new(8)};homeActions.Children.Add(Text("בחרו יעד לסריקה",32));var purpose=Text("בדיקת תיקייה והבנת ממצאים באמצעות Microsoft Defender",17);purpose.Margin=new(0,12,0,22);homeActions.Children.Add(purpose);selection.TextWrapping=TextWrapping.NoWrap;selection.TextTrimming=TextTrimming.CharacterEllipsis;homeActions.Children.Add(selection);homeActions.Children.Add(start);homeActions.Children.Add(choose);homeActions.Children.Add(homeReason);videoNotice.Margin=new(0,12,0,0);videoNotice.Foreground=Accent;AutomationProperties.SetAutomationId(videoNotice,"videoOpenNotice");AutomationProperties.SetLiveSetting(videoNotice,AutomationLiveSetting.Polite);homeActions.Children.Add(videoNotice);
        if(demo){var scenarios=new ComboBox{Style=ScannerTheme.FilterStyle,ItemsSource=new[]{"תוצאות DEMO חלקיות וממצאים","כשל DEMO יזום","טיפול Windows — תצוגת בדיקה DEMO","הודעת סרטון — הצלחה מדומה בלבד","הודעת סרטון — כשל מדומה בלבד"},SelectedIndex=0,FontSize=16,Margin=new(0,12,0,12)};AutomationProperties.SetName(scenarios,"תרחיש הדגמה ללא מנוע");AutomationProperties.SetAutomationId(scenarios,"demoScenario");scenarios.SelectionChanged+=(_,_)=>demoSession!.Scenario=scenarios.SelectedIndex switch{1=>"failure",2=>"windowsHandled",3=>"videoSuccess",4=>"videoFailure",_=>"partial"};homeActions.Children.Add(scenarios);}
        homePane.Children.Add(homeActions);Grid.SetColumn(homeSceneHost,2);homePane.Children.Add(homeSceneHost);
        scanPane.RowDefinitions.Add(new(){Height=GridLength.Auto});scanPane.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});
        var command=new Grid{Background=ScannerTheme.HeaderSurface,Margin=new(4,0,4,8)};foreach(var width in new[]{new GridLength(1,GridUnitType.Star),new GridLength(105),new GridLength(145),new GridLength(190)})command.ColumnDefinitions.Add(new(){Width=width});
        var stageGroup=new StackPanel{Margin=new(12,8,12,8)};stageGroup.Children.Add(Text(demo?"סריקת DEMO מרחבית":"סריקה פעילה",23));stageGroup.Children.Add(phase);command.Children.Add(stageGroup);
        foreach(var metric in new[]{(found,"נמצאו",1),(ack,"תשובות מוכרות",2)}){var group=new StackPanel{Margin=new(6),VerticalAlignment=VerticalAlignment.Center};group.Children.Add(metric.Item1);group.Children.Add(Text(metric.Item2,13.5));Grid.SetColumn(group,metric.Item3);command.Children.Add(group);}
        cancel.Margin=new(8);cancel.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(cancel,3);command.Children.Add(cancel);scanPane.Children.Add(command);Grid.SetRow(scanDeckHost,1);scanPane.Children.Add(scanDeckHost);scanDeckHost.Children.Add(deck);results.AttachGraphs(resultDeck);
        var rail=new Grid{Margin=new(24,6,24,6)};rail.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});rail.ColumnDefinitions.Add(new(){Width=new(115)});rail.ColumnDefinitions.Add(new(){Width=GridLength.Auto});path.FlowDirection=FlowDirection.LeftToRight;path.TextWrapping=TextWrapping.NoWrap;path.TextTrimming=TextTrimming.CharacterEllipsis;rail.Children.Add(path);elapsed.FlowDirection=FlowDirection.LeftToRight;Grid.SetColumn(elapsed,1);rail.Children.Add(elapsed);fullPath.Padding=new(10,5,10,5);Grid.SetColumn(fullPath,2);rail.Children.Add(fullPath);Add(rail,2);
        foot.Margin=new(24,2,24,8);foot.Text=demo?"DEMO — רצף נתונים מדומה בלבד; לא יופעל מנוע ולא תתאפשר פעולת טיפול.":"הגרפים מבוססים על אירועי הריצה. שליחה ידועה בקבלת תשובה; אין להסיק הבטחת בטיחות מהמחשת הפעילות.";Add(foot,3);
        choose.Click+=(_,_)=>Choose();start.Click+=async(_,_)=>await controller.StartAsync();cancel.Click+=(_,_)=>controller.RequestCancellation();engineCheck.Click+=async(_,_)=>await controller.DiscoverAsync();fullPath.Click+=(_,_)=>Info("הנתיב המלא",controller.State.CurrentPath.Length>0?controller.State.CurrentPath:controller.State.Target);
        motion.Checked+=(_,_)=>scene.MotionEnabled=true;motion.Unchecked+=(_,_)=>scene.MotionEnabled=false;
        results.DetailsRequested+=entry=>{
            Action? treatment=!demo&&entry.Original is {} finding&&controller.CanOfferTreatment(finding)?()=>{
                controller.RequestTreatment(finding,()=>new FolderScanner.WindowsRecycleBackend(new FolderScanner.WindowsScanEvidenceProvider(),new System.Windows.Interop.WindowInteropHelper(this).Handle),
                    request=>MessageBox.Show(this,request.Message,"אישור העברה לסל המחזור",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)==MessageBoxResult.Yes);
            }:null;
            string? unavailable=treatment is not null?null:demo?"זו המחשה בלבד. לא נבדקו קבצים ולא מוצעת פעולת טיפול.":controller.State.Report?.FileActions.Any(x=>string.Equals(x.Path,entry.Path,StringComparison.OrdinalIgnoreCase))==true?"פעולה לממצא זה כבר נוסתה בהרצה הנוכחית. סרקו מחדש לפני בקשה נוספת.":"טיפול מהיישום דורש ממצא מתאים מהריצה הנוכחית וראיות זהות תקפות. ניתן לעיין ב-Windows Security.";
            var dialog=new ReadOnlyDetailsWindow(entry,treatment,isDemo:demo||controller.State.Report?.Simulated==true,treatmentUnavailableReason:unavailable,openWindowsSecurity:demo?null:()=>OpenWindowsSecurity(),openMicrosoftSubmission:demo?null:()=>OpenMicrosoftSubmission()){Owner=this};dialog.ShowDialog();
        };results.RepeatRequested+=async()=>await controller.RepeatAsync();results.ChooseAnotherRequested+=()=>controller.SelectTarget("");results.ExportRequested+=Export;results.VideoRequested+=()=>controller.RequestVideo();
        clockTimer.Tick+=(_,_)=>elapsed.Text=clock.Elapsed.ToString(@"mm\:ss");
        Closing+=OnClosing;Closed+=(_,_)=>{controller.PropertyChanged-=ControllerStateChanged;clockTimer.Stop();scene.DisposeScene();controller.Dispose();};
        Loaded+=async(_,_)=>{RenderState();await controller.DiscoverAsync();};
        SourceInitialized+=(_,_)=>{try{int dark=1;DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle,20,ref dark,sizeof(int));}catch(DllNotFoundException){}};
        AutomationProperties.SetAutomationId(this,demo?"scannerDemoWindow":"scannerWindow");AutomationProperties.SetAutomationId(motion,"animationToggle");AutomationProperties.SetAutomationId(phase,"scanState");AutomationProperties.SetAutomationId(path,"currentPath");AutomationProperties.SetAutomationId(found,"discoveredCount");AutomationProperties.SetAutomationId(ack,"acknowledgedCount");
    }
    void Add(UIElement child,int row){Grid.SetRow(child,row);root.Children.Add(child);}
    void ControllerStateChanged(object? sender,PropertyChangedEventArgs e)=>RenderState();
    void RenderState()
    {
        var state=controller.State;engine.Text=demo?"DEMO · ללא מנוע":state.Discovering?"מנוע · בודק חתימה":state.Engine.Executable is null?"מנוע · לא זמין":"מנוע · חתימת Microsoft אומתה";
        choose.IsEnabled=state.CanSelect;start.IsEnabled=state.CanStart;start.Visibility=state.Target.Length>0?Visibility.Visible:Visibility.Collapsed;engineCheck.IsEnabled=!state.Running&&!state.Recycling&&!state.Discovering;cancel.IsEnabled=state.Running&&!state.CancellationPending;
        selection.Text=state.Target.Length==0?"לא נבחרה תיקייה":System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(state.Target));selection.ToolTip=state.Target;choose.Content=state.Target.Length==0?"בחירת תיקייה":"החלפת תיקייה";choose.Style=state.Target.Length==0?ScannerTheme.PrimaryButtonStyle:ScannerTheme.ButtonStyle;
        homeReason.Text=state.Discovering?"ממתינים לבדיקת המנוע…":state.Engine.Executable is null?state.Engine.Description:state.Target.Length>0?"התיקייה נבחרה. אפשר להתחיל בסריקה.":"";
        videoNotice.Text=state.VideoNotice.Length>0?(demo?"DEMO — לא נפתח דפדפן. ":"")+state.VideoNotice:"";videoNotice.Visibility=state.VideoNotice.Length>0?Visibility.Visible:Visibility.Collapsed;
        path.Text=state.CurrentPath.Length>0?state.CurrentPath:state.Target;fullPath.Visibility=path.Text.Length>0?Visibility.Visible:Visibility.Collapsed;phase.Text=state.Phase;
        found.Text=(state.Progress?.Discovered??state.Report?.DiscoveredFiles)?.ToString("N0")??"—";ack.Text=(state.Progress?.Acknowledged??state.Report?.EngineAcknowledgedFiles)?.ToString("N0")??"—";
        if(shownPage!=state.Page)
        {
            homePane.Visibility=state.Page==ScannerPage.Home?Visibility.Visible:Visibility.Collapsed;scanPane.Visibility=state.Page==ScannerPage.Running?Visibility.Visible:Visibility.Collapsed;results.Visibility=state.Page==ScannerPage.Results?Visibility.Visible:Visibility.Collapsed;
            if(state.Page==ScannerPage.Home){MoveScene(homeSceneHost);clock.Stop();clockTimer.Stop();elapsed.Text="";}
            else if(state.Page==ScannerPage.Running){deck.AttachScene(scenePanel);clock.Restart();clockTimer.Start();}
            else {resultDeck.AttachScene(scenePanel);clock.Stop();clockTimer.Stop();elapsed.Text=state.Elapsed.ToString(@"mm\:ss");}
            shownPage=state.Page;
        }
        if(state.Page==ScannerPage.Running)deck.SetSnapshot(state.Snapshot);
        if(state.Page==ScannerPage.Results)resultDeck.SetSnapshot(state.Snapshot);
        scene.MotionRequested=state.Running&&!state.CancellationPending;
        if(state.Page==ScannerPage.Results)results.Apply(state);
        if(closing&&!state.Running&&!state.Recycling){closing=false;Dispatcher.BeginInvoke(Close);}
    }
    void MoveScene(Panel targetHost){if(scenePanel.Parent is Panel p)p.Children.Remove(scenePanel);targetHost.Children.Add(scenePanel);}
    void Choose()
    {
        if(!controller.State.CanSelect)return;
        var picker=new OpenFolderDialog{Title=demo?"בחירת תיקייה לרצף DEMO בלבד — לא ייקראו קבצים":"בחרו תיקייה מקומית לסריקה",Multiselect=false};
        if(picker.ShowDialog(this)==true)controller.SelectTarget(picker.FolderName,userInitiated:!demo||demoSession?.Scenario is "videoSuccess" or "videoFailure");
    }
    void OnClosing(object? sender,CancelEventArgs e){if(!controller.CanClose()){e.Cancel=true;closing=true;}}
    void Export()
    {
        if(!controller.State.CanExport)return;
        var picker=new SaveFileDialog{Filter="JSON report (*.json)|*.json",FileName=$"scan-{controller.State.Report!.RunId}.json",OverwritePrompt=true};
        if(picker.ShowDialog(this)!=true)return;
        try{controller.ExportToNewFile(picker.FileName);}catch(Exception ex){Info("הדוח לא נשמר","בחרו שם חדש בכונן מקומי. "+ex.Message);}
    }
    void Info(string title,string value,bool offerReporting=false){if(value.Length==0)return;new ReadOnlyDetailsWindow(new(controller.State.Target,title,value,EntryKind.Issue),isDemo:demo,informational:true,openMicrosoftSubmission:offerReporting&&!demo?()=>OpenMicrosoftSubmission():null){Owner=this}.ShowDialog();}
    void OpenMicrosoftSubmission(){try{Process.Start(new ProcessStartInfo(MicrosoftSubmissionUrl){UseShellExecute=true});}catch(Exception ex){Info("לא ניתן לפתוח את דף הדיווח","לא נשלח קובץ או דיווח. אפשר לפתוח ידנית את "+MicrosoftSubmissionUrl+"\n"+ex.Message);}}
    void OpenWindowsSecurity(){try{Process.Start(new ProcessStartInfo("windowsdefender:"){UseShellExecute=true});}catch(Exception ex){Info("לא ניתן לפתוח Windows Security",ex.Message);}}
    static TextBlock Text(string value,double size)=>new(){Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap,Foreground=Ink};
    static Button Action(string text,string id){var b=new Button{Content=text,Style=ScannerTheme.ButtonStyle,FontSize=16,Padding=new(16,10,16,10),Margin=new(0,10,0,0),MinHeight=44};AutomationProperties.SetName(b,text);AutomationProperties.SetAutomationId(b,id);return b;}
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
}




