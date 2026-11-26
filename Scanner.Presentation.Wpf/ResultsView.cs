using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using FolderScanner;

namespace ScannerPresentation.Wpf;

// Separate integration component; this library does not launch windows or scan on construction.
public sealed class ResultsView:UserControl
{
    readonly TextBlock title=Text("",30),summary=Text("",16),coverage=Text("",15),folder=Text("",14),count=Text("",14),feedback=Text("",14);
    readonly TextBox query=new(){MinWidth=180,FontSize=16,Padding=new(8),VerticalContentAlignment=VerticalAlignment.Center};
    readonly ComboBox filter=new(){FontSize=16,MinWidth=170,Margin=new(12,0,0,0)};
    readonly ListBox items=new(){BorderThickness=new(0),Background=Brushes.Transparent,HorizontalContentAlignment=HorizontalAlignment.Stretch};
    readonly TabControl tabs=new(){Background=Brushes.Transparent};
    readonly ContentControl graphs=new();
    readonly Button primary=Action("פרטי הבדיקה","primaryDetails"),repeat=Action("סריקה חוזרת","repeatScan"),another=Action("בחירת תיקייה אחרת","chooseAnother"),export=Action("ייצוא דוח","exportReport"),video=Action("סרטון","tributeVideo"),previous=Action("הקודמים","previousPage"),next=Action("הבאים","nextPage");
    ScannerViewState current=new();int offset;bool updating;
    Style buttonStyle=ScannerTheme.ButtonStyle;
    readonly Expander filterOptions=new(){Header="סינון וחיפוש",IsExpanded=false,Foreground=ScannerTheme.Ink,Margin=new(0,0,0,4)};
    public event Action? RepeatRequested,ChooseAnotherRequested,ExportRequested,VideoRequested;
    public event Action<ResultEntry>? DetailsRequested;
    public ResultsView()
    {
        FlowDirection=FlowDirection.RightToLeft;Foreground=ScannerTheme.Ink;Background=ScannerTheme.Ground;
        ScannerTheme.ApplyResources(Resources);
        ScrollViewer.SetHorizontalScrollBarVisibility(items,ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(items,ScrollBarVisibility.Auto);
        items.ItemContainerStyle=ScannerTheme.ItemStyle;items.Foreground=ScannerTheme.Ink;items.Padding=new(0);
        query.Style=ScannerTheme.FieldStyle;filter.Style=ScannerTheme.FilterStyle;tabs.Style=ScannerTheme.TabsStyle;tabs.ItemContainerStyle=ScannerTheme.TabStyle;
        var grid=new Grid{Margin=new(16,10,16,10)};for(int i=0;i<6;i++)grid.RowDefinitions.Add(new(){Height=i==4?new(1,GridUnitType.Star):GridLength.Auto});Content=grid;
        title.FontWeight=FontWeights.SemiBold;summary.MaxHeight=46;summary.TextTrimming=TextTrimming.CharacterEllipsis;coverage.TextWrapping=TextWrapping.NoWrap;coverage.TextTrimming=TextTrimming.CharacterEllipsis;folder.TextWrapping=TextWrapping.NoWrap;feedback.Visibility=Visibility.Collapsed;primary.Style=ScannerTheme.PrimaryButtonStyle;
        var head=new StackPanel();head.Children.Add(title);head.Children.Add(summary);head.Children.Add(coverage);head.Children.Add(folder);Add(head,0);
        var commands=new WrapPanel{Margin=new(0,6,0,6)};foreach(var button in new[]{primary,repeat,another,export,video})commands.Children.Add(button);Add(commands,1);
        var search=new Grid{Margin=new(0,6,0,8)};search.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});search.ColumnDefinitions.Add(new(){Width=GridLength.Auto});search.RowDefinitions.Add(new(){Height=GridLength.Auto});search.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var searchLabel=Text("חיפוש לפי שם או נתיב",14);search.Children.Add(searchLabel);Grid.SetRow(query,1);search.Children.Add(query);filter.ItemsSource=new[]{"פריטים הדורשים תשומת לב","כל הקבצים","ממצאים","שגיאות ודילוגים"};filter.SelectedIndex=0;Grid.SetColumn(filter,1);Grid.SetRow(filter,1);search.Children.Add(filter);filterOptions.Content=search;Add(filterOptions,2);Add(feedback,3);
        AutomationProperties.SetAutomationId(filterOptions,"resultFilterOptions");AutomationProperties.SetName(filterOptions,"פתיחת סינון וחיפוש בתוצאות");
        tabs.Items.Add(new TabItem{Header="פרטי הבדיקה",Content=items});var graphScroll=new ScrollViewer{Content=graphs,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=true};AutomationProperties.SetAutomationId(graphScroll,"resultGraphsScroll");AutomationProperties.SetName(graphScroll,"סיכום גרפים — גלילה אנכית");tabs.Items.Add(new TabItem{Header="סיכום גרפים",Content=graphScroll});Add(tabs,4);
        var pages=new Grid{Margin=new(0,4,0,0)};pages.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});pages.ColumnDefinitions.Add(new(){Width=GridLength.Auto});count.VerticalAlignment=VerticalAlignment.Center;pages.Children.Add(count);var pageActions=new WrapPanel();pageActions.Children.Add(previous);pageActions.Children.Add(next);Grid.SetColumn(pageActions,1);pages.Children.Add(pageActions);Add(pages,5);
        AutomationProperties.SetName(query,"חיפוש מקומי בתוצאות");AutomationProperties.SetAutomationId(query,"resultSearch");AutomationProperties.SetName(filter,"סינון תוצאות");AutomationProperties.SetName(items,"פריטי הריצה הנוכחית");
        primary.Click+=(_,_)=>{if(ResultProjection.PrimaryDetails(current) is {} entry)DetailsRequested?.Invoke(entry);};repeat.Click+=(_,_)=>RepeatRequested?.Invoke();another.Click+=(_,_)=>ChooseAnotherRequested?.Invoke();export.Click+=(_,_)=>ExportRequested?.Invoke();video.Click+=(_,_)=>VideoRequested?.Invoke();
        query.TextChanged+=(_,_)=>{if(!updating){offset=0;ShowPage();}};filter.SelectionChanged+=(_,_)=>{if(!updating){offset=0;ShowPage();}};
        previous.Click+=(_,_)=>{offset=Math.Max(0,offset-30);ShowPage();};next.Click+=(_,_)=>{offset+=30;ShowPage();};
        void Add(UIElement child,int row){Grid.SetRow(child,row);grid.Children.Add(child);}
    }
    public void Apply(ScannerViewState value)
    {
        Dispatcher.VerifyAccess();bool replaced=!ReferenceEquals(current.Report,value.Report)||current.Failure!=value.Failure;current=value;
        if(replaced){filterOptions.IsExpanded=false;updating=true;offset=0;query.Text="";filter.SelectedIndex=value.ShowDetailsInitially?0:1;tabs.SelectedIndex=value.ShowDetailsInitially?0:1;updating=false;}
        var report=value.Report;
        title.Text=value.Failure is not null?"לא ניתן היה להשלים את הבדיקה":report?.WindowsHandledFiles>0?(report.Errors>0||report.Findings>report.WindowsHandledFiles?"Windows טיפל באיום; נותרו פריטים לבדיקה":"האיום כבר טופל על ידי Windows"):report?.Findings>0?"נמצאו קבצים עם דיווח איום":report?.State switch{
            RunState.CleanAccordingToEngine=>"הבדיקה הסתיימה",RunState.Cancelled=>"ההמתנה הופסקה",RunState.Failed=>"הבדיקה נכשלה",_=>"הבדיקה הסתיימה עם מגבלות"};
        summary.Text=value.Failure?.Reason??(report is null?"לא התקבלה תוצאת מנוע.":report.WindowsHandledFiles>0?ResultProjection.WindowsSummary(report):report.Findings>0?$"Defender דיווח על איום ב־{report.Findings} קבצים. עיינו בפרטים ובדרך הטיפול.":report.State==RunState.CleanAccordingToEngine?$"Defender לא דיווח על איומים ב־{report.EngineAcknowledgedFiles} קבצים שנבדקו. תוצאה זו אינה הבטחת בטיחות.":report.State==RunState.Cancelled?"התור הופסק לאחר חזרת הקובץ הפעיל. לא נשלחה הוראת עצירה כללית ל־Defender.":"לא ניתן לאשר את מלוא כיסוי הבדיקה. עיינו בפריטים שלא נבדקו.");
        if(report?.Simulated==true)summary.Text="הדגמה בלבד — "+summary.Text;
        coverage.Text=report is null?"כיסוי הבדיקה לא אושר.":report.CoverageComplete?"הכיסוי אומת בתחום הבדיקה בלבד.":"הכיסוי חלקי או לא מאומת. "+(report.Issues.FirstOrDefault()?.Reason??"פרטים מלאים ברשימת הפריטים.");
        folder.Text=report?.Root??value.Target;folder.FlowDirection=FlowDirection.LeftToRight;folder.TextTrimming=TextTrimming.CharacterEllipsis;feedback.Text=value.Feedback;feedback.Visibility=string.IsNullOrWhiteSpace(value.Feedback)?Visibility.Collapsed:Visibility.Visible;summary.ToolTip=summary.Text;coverage.ToolTip=coverage.Text;folder.ToolTip=folder.Text;
        primary.Visibility=ResultProjection.PrimaryDetails(value) is null?Visibility.Collapsed:Visibility.Visible;primary.Content=value.Failure is not null?"פרטי הכשל":report?.Findings>0?"פרטי הממצא":"פרטי הבדיקה";
        repeat.IsEnabled=report is not null&&value.CanStart;another.IsEnabled=value.CanSelect;export.IsEnabled=value.CanExport;video.Visibility=value.CanShowVideo?Visibility.Visible:Visibility.Collapsed;
        ShowPage();
    }
    public void AttachGraphs(FrameworkElement? content){if(content is not null)content.MinHeight=Math.Max(content.MinHeight,480);graphs.Content=content;}
    public void ApplyTheme(Style buttonStyle)
    {
        Dispatcher.VerifyAccess();ArgumentNullException.ThrowIfNull(buttonStyle);this.buttonStyle=buttonStyle;
        foreach(var button in new[]{repeat,another,export,video,previous,next})button.Style=buttonStyle;primary.Style=ScannerTheme.PrimaryButtonStyle;
        ShowPage();
    }
    void ShowPage()
    {
        items.Items.Clear();
        if(current.Failure is {} failure){items.Items.Add(Card(new(failure.Path,"פרטי הכשל",failure.Reason,EntryKind.Issue)));count.Text="פריט אחד";previous.IsEnabled=next.IsEnabled=false;return;}
        if(current.Report is not {} report){count.Text="";previous.IsEnabled=next.IsEnabled=false;return;}
        var mode=filter.SelectedIndex switch{1=>EntryFilter.All,2=>EntryFilter.Threats,3=>EntryFilter.Issues,_=>EntryFilter.Attention};
        var page=ResultProjection.Page(report,mode,query.Text,offset);
        foreach(var entry in page.Items)items.Items.Add(Card(entry));
        count.Text=page.Matching==0?"אין פריטים התואמים לסינון":$"מוצגים {Math.Min(offset+1,page.Matching)}–{Math.Min(offset+30,page.Matching)} מתוך {page.Matching}";
        previous.IsEnabled=offset>0;next.IsEnabled=offset+30<page.Matching;
    }
    FrameworkElement Card(ResultEntry entry)
    {
        var content=new Grid{Margin=new(12)};content.ColumnDefinitions.Add(new(){Width=new(1,GridUnitType.Star)});
        for(int row=0;row<4;row++)content.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var heading=Text(entry.Title,18);heading.TextWrapping=TextWrapping.NoWrap;heading.TextTrimming=TextTrimming.CharacterEllipsis;heading.FontWeight=FontWeights.SemiBold;content.Children.Add(heading);
        var path=Text(entry.Path,14);path.TextWrapping=TextWrapping.NoWrap;path.TextTrimming=TextTrimming.CharacterEllipsis;path.FlowDirection=FlowDirection.LeftToRight;path.ToolTip=entry.Path;Grid.SetRow(path,1);content.Children.Add(path);
        var brief=Text(ResultProjection.Brief(entry),16);brief.TextWrapping=TextWrapping.NoWrap;brief.TextTrimming=TextTrimming.CharacterEllipsis;brief.ToolTip=entry.Detail;Grid.SetRow(brief,2);content.Children.Add(brief);
        var details=Action("פרטים ונתיב מלא","entryDetails");details.Style=buttonStyle;details.Margin=new(0,6,0,0);details.HorizontalAlignment=HorizontalAlignment.Right;details.Click+=(_,_)=>DetailsRequested?.Invoke(entry);Grid.SetRow(details,3);content.Children.Add(details);
        return new Border{Background=ScannerTheme.Surface,BorderBrush=ScannerTheme.Rule,BorderThickness=new(1),CornerRadius=new(5),HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new(0,4,0,4),Child=content};
    }
    static TextBlock Text(string value,double size)=>new(){Text=value,FontSize=size,Foreground=ScannerTheme.Ink,TextWrapping=TextWrapping.Wrap,Margin=new(0,2,0,2)};
    static Button Action(string text,string id){var b=new Button{Content=text,Style=ScannerTheme.ButtonStyle,FontSize=16,Padding=new(14,8,14,8),Margin=new(0,0,8,0),MinHeight=42};AutomationProperties.SetName(b,text);AutomationProperties.SetAutomationId(b,id);return b;}
}

public sealed class ReadOnlyDetailsWindow:Window
{
    public ReadOnlyDetailsWindow(ResultEntry entry,Action? treatment=null,bool isDemo=false,string? treatmentUnavailableReason=null,bool informational=false,Action? openWindowsSecurity=null,Action? openMicrosoftSubmission=null)
    {
        Title=entry.Title;Width=760;Height=660;MinWidth=440;MinHeight=320;WindowStartupLocation=WindowStartupLocation.CenterOwner;FlowDirection=FlowDirection.RightToLeft;
        Background=ScannerTheme.Ground;Foreground=ScannerTheme.Ink;FontFamily=new("Segoe UI");ScannerTheme.ApplyResources(Resources);Resources[typeof(Button)]=ScannerTheme.ButtonStyle;
        AutomationProperties.SetAutomationId(this,"readOnlyDetailsWindow");
        var grid=new Grid{Margin=new(20)};grid.RowDefinitions.Add(new(){Height=GridLength.Auto});grid.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});grid.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=grid;
        var heading=new StackPanel();if(isDemo)heading.Children.Add(new Border{Background=ScannerTheme.HeaderSurface,BorderBrush=ScannerTheme.Rule,BorderThickness=new(1),Padding=new(12),Margin=new(0,0,0,12),Child=new TextBlock{Text="DEMO — המחשה בלבד; אין טיפול בקבצים",FontSize=20,FontWeight=FontWeights.Bold,Foreground=ScannerTheme.Accent,TextWrapping=TextWrapping.Wrap}});
        heading.Children.Add(new TextBlock{Text=entry.Title,Foreground=ScannerTheme.Ink,FontSize=24,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)});grid.Children.Add(heading);
        string full=entry.Path+"\r\n\r\n"+entry.Detail+(entry.Kind==EntryKind.Threat&&entry.Original?.WindowsTreatment is null?"\r\n\r\n"+ScanPresenter.CareInstructions:"");
        var body=ReadOnly(full,"readOnlyDetailsText");body.FlowDirection=FlowDirection.LeftToRight;AutomationProperties.SetName(body,"הנתיב המלא ופרטי הפריט — טקסט ניתן לבחירה ולהעתקה");
        if(informational){Grid.SetRow(body,1);grid.Children.Add(body);}
        else{
            var overview=new StackPanel();
            overview.Children.Add(Label("מה דווח"));overview.Children.Add(Paragraph((isDemo?"דיווח מדומה — ":"")+entry.Title,"reportedSummary"));
            if(entry.Original?.WindowsTreatment is {} handled){overview.Children.Add(Label("הטיפול שאומת ב־Windows"));overview.Children.Add(Paragraph(handled.Treatment+(string.IsNullOrWhiteSpace(handled.ThreatName)?"":" · "+handled.ThreatName),"windowsTreatmentSummary"));}
            overview.Children.Add(Label("היכן"));var path=ReadOnly(entry.Path,"detailsPath");path.FlowDirection=FlowDirection.LeftToRight;path.Padding=new(8,6,8,6);path.MaxHeight=72;overview.Children.Add(path);
            if(entry.Kind==EntryKind.Issue){string reason=entry.Detail.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()??entry.Detail;overview.Children.Add(Paragraph(reason.Length>220?reason[..220]+"…":reason,"issueSummary"));}
            overview.Children.Add(Label("מה לעשות עכשיו"));
            bool windowsHandled=entry.Original?.WindowsTreatment is not null;
            string next=windowsHandled?"אומת שהאיום כבר טופל על ידי Windows. אין צורך בהעברה נוספת לסל מהאפליקציה. סוג הטיפול, שם האיום ומקור האימות בפרטים הטכניים.":isDemo?"עיינו בהמחשה ובפרטים. לא תבוצע פעולה בקובץ דרך דמו זה.":entry.Kind==EntryKind.Threat?"אל תפתחו ואל תריצו את הקובץ. בדקו שהנתיב תואם ועיינו בהוראות הטיפול שבפרטים הטכניים.":entry.Kind==EntryKind.Issue?"עיינו בסיבת השגיאה ובגישה לנתיב. אין להסיק מכך שהפריט נבדק.":"בדקו שהנתיב תואם לפריט המבוקש. דיווח המנוע אינו הבטחת בטיחות.";
            overview.Children.Add(Paragraph(next,"nextStepSummary"));
            overview.Children.Add(Label("טיפול דרך האפליקציה"));
            string availability=windowsHandled?"לא מוצעת פעולה נוספת: הטיפול של Windows אומת לפריט זה."+(isDemo?" זו תצוגת בדיקה, ולא אירוע אבטחה חי.":""):isDemo?"הטיפול אינו זמין במצב DEMO; הנתונים מוצגים להמחשה בלבד.":treatment is not null?"אפשר לבקש העברה לסל המחזור. הבקשה כפופה לאישור ולבדיקות ההגנה של הפעולה.":treatmentUnavailableReason??"טיפול דרך האפליקציה אינו זמין לפריט זה. אפשר לעיין בהוראות ובדיווח המלא בפרטים הטכניים.";
            overview.Children.Add(Paragraph(availability,"treatmentAvailability"));
            var technical=new Expander{Header="פרטים טכניים והוראות מלאות",IsExpanded=false,Foreground=ScannerTheme.Ink,FontSize=16,Margin=new(0,14,0,0),Content=body};AutomationProperties.SetAutomationId(technical,"technicalDetailsExpander");body.MinHeight=160;body.MaxHeight=280;overview.Children.Add(technical);
            var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Content=overview};Grid.SetRow(scroll,1);grid.Children.Add(scroll);
        }
        var actions=new WrapPanel{Margin=new(0,12,0,0)};var close=new Button{Content="סגירה",Style=ScannerTheme.ButtonStyle,IsCancel=true,MinHeight=44,MinWidth=100,FontSize=16};AutomationProperties.SetAutomationId(close,"closeDetailsWindow");close.Click+=(_,_)=>Close();actions.Children.Add(close);
        if(!informational&&!isDemo&&entry.Original?.WindowsTreatment is null&&treatment is not null){var recycle=new Button{Content="העברה לסל המחזור באישור",Style=ScannerTheme.PrimaryButtonStyle,MinHeight=44,FontSize=16,Padding=new(16,8,16,8),Margin=new(12,0,0,0)};AutomationProperties.SetAutomationId(recycle,"confirmTreatment");recycle.Click+=(_,_)=>{recycle.IsEnabled=false;treatment();};actions.Children.Add(recycle);}
        if(!isDemo&&openWindowsSecurity is not null){var security=new Button{Content="פתיחת Windows Security",Style=ScannerTheme.ButtonStyle,MinHeight=44,FontSize=16,Padding=new(16,8,16,8),Margin=new(12,0,0,0)};AutomationProperties.SetAutomationId(security,"openWindowsSecurity");AutomationProperties.SetName(security,"פתיחת Windows Security — הפניה כללית, ללא אישור טיפול");security.Click+=(_,_)=>openWindowsSecurity();actions.Children.Add(security);}
        if(!isDemo&&openMicrosoftSubmission is not null){var report=new Button{Content="דיווח ידני ל־Microsoft",Style=ScannerTheme.ButtonStyle,MinHeight=44,FontSize=16,Padding=new(16,8,16,8),Margin=new(12,0,0,0),ToolTip="פתיחת הדף הרשמי בלבד; לא נשלחים קובץ, נתיב או דיווח מהיישום"};AutomationProperties.SetAutomationId(report,"openMicrosoftSubmission");AutomationProperties.SetName(report,"פתיחת דף הדיווח הידני של Microsoft — ללא שליחת קובץ או נתיב");report.Click+=(_,_)=>openMicrosoftSubmission();actions.Children.Add(report);}
        Grid.SetRow(actions,2);grid.Children.Add(actions);
    }
    static TextBlock Label(string text)=>new(){Text=text,Foreground=ScannerTheme.Ink,FontSize=16,FontWeight=FontWeights.SemiBold,Margin=new(0,8,0,4)};
    static TextBlock Paragraph(string text,string id){var p=new TextBlock{Text=text,Foreground=ScannerTheme.Ink,FontSize=16,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,4)};AutomationProperties.SetAutomationId(p,id);return p;}
    static TextBox ReadOnly(string text,string id){var field=new TextBox{IsReadOnly=true,Style=ScannerTheme.FieldStyle,Text=text,FontSize=16,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,BorderThickness=new(0),Padding=new(10),MinHeight=34};AutomationProperties.SetAutomationId(field,id);AutomationProperties.SetName(field,"טקסט לקריאה, בחירה והעתקה");return field;}
}
// Local styles are deliberately self-contained: dialogs do not inherit an owner's resources.
public static class ScannerTheme
{
    static Brush B(byte r,byte g,byte b){var value=new SolidColorBrush(Color.FromRgb(r,g,b));value.Freeze();return value;}
    public static readonly Brush Ground=SystemParameters.HighContrast?SystemColors.WindowBrush:B(7,15,24),
        Surface=SystemParameters.HighContrast?SystemColors.WindowBrush:Depth(16,35,48,9,23,34),
        Ink=SystemParameters.HighContrast?SystemColors.WindowTextBrush:B(234,244,249),
        Rule=SystemParameters.HighContrast?SystemColors.WindowTextBrush:B(53,88,107),
        Accent=SystemParameters.HighContrast?SystemColors.HighlightBrush:B(131,232,214),
        HeaderSurface=SystemParameters.HighContrast?SystemColors.WindowBrush:Depth(24,47,61,11,26,38);
    static Brush Depth(byte r,byte g,byte b,byte r2,byte g2,byte b2){var brush=new LinearGradientBrush(Color.FromRgb(r,g,b),Color.FromRgb(r2,g2,b2),100);brush.Freeze();return brush;}
    public static void ApplyResources(ResourceDictionary resources)
    {
        bool hc=SystemParameters.HighContrast;
        resources["ScannerGround"]=Ground;resources["ScannerSurface"]=Surface;resources["ScannerHeaderSurface"]=HeaderSurface;resources["ScannerAccent"]=Accent;
        resources["ResultsGround"]=Ground;resources["ResultsSurface"]=Surface;resources["ResultsInk"]=Ink;resources["ResultsRule"]=Rule;
        resources["ResultsButton"]=hc?SystemColors.ControlBrush:B(22,46,61);resources["ResultsHover"]=hc?SystemColors.HighlightBrush:B(35,71,88);resources["ResultsAccent"]=Accent;resources["ResultsMuted"]=hc?SystemColors.GrayTextBrush:B(155,177,189);
        resources[SystemColors.WindowBrushKey]=Surface;resources[SystemColors.WindowTextBrushKey]=Ink;resources[SystemColors.ControlBrushKey]=Ground;resources[SystemColors.ControlTextBrushKey]=Ink;
        resources[typeof(ScrollBar)]=ScrollStyle;
    }
    static Style Style(string content)=>(Style)System.Windows.Markup.XamlReader.Parse("<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' "+content+"</Style>");
    public static readonly Style ButtonStyle=Style(@"
TargetType='{x:Type Button}'>
 <Setter Property='Foreground' Value='{DynamicResource ResultsInk}'/><Setter Property='Background' Value='{DynamicResource ResultsButton}'/><Setter Property='BorderBrush' Value='{DynamicResource ResultsRule}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='FontSize' Value='16'/><Setter Property='Padding' Value='16,10'/><Setter Property='MinHeight' Value='44'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
  <Border x:Name='surface' CornerRadius='4' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'>
   <ContentPresenter HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' RecognizesAccessKey='True'/>
  </Border>
  <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Background' Value='{DynamicResource ResultsHover}'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='{DynamicResource ResultsAccent}'/><Setter TargetName='surface' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsPressed' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='{DynamicResource ResultsAccent}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='{DynamicResource ResultsMuted}'/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>");
    public static readonly Style PrimaryButtonStyle=MakePrimary();
    static Style MakePrimary(){var style=new Style(typeof(Button),ButtonStyle);style.Setters.Add(new Setter(Control.BackgroundProperty,Accent));style.Setters.Add(new Setter(Control.ForegroundProperty,Ground));style.Setters.Add(new Setter(Control.BorderBrushProperty,Accent));style.Setters.Add(new Setter(Control.FontWeightProperty,FontWeights.SemiBold));var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Control.ForegroundProperty,Ink));style.Triggers.Add(hover);var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(Control.BackgroundProperty,HeaderSurface));disabled.Setters.Add(new Setter(Control.ForegroundProperty,Ink));style.Triggers.Add(disabled);return style;}
    internal static readonly Style FieldStyle=Style(@"
TargetType='{x:Type TextBox}'>
 <Setter Property='Foreground' Value='{DynamicResource ResultsInk}'/><Setter Property='Background' Value='{DynamicResource ResultsSurface}'/><Setter Property='BorderBrush' Value='{DynamicResource ResultsRule}'/><Setter Property='BorderThickness' Value='1'/><Setter Property='CaretBrush' Value='{DynamicResource ResultsInk}'/><Setter Property='SelectionBrush' Value='{DynamicResource ResultsHover}'/><Setter Property='Padding' Value='10'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TextBox}'><Border x:Name='field' CornerRadius='3' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Padding='{TemplateBinding Padding}'><ScrollViewer x:Name='PART_ContentHost'/></Border><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='field' Property='BorderBrush' Value='{DynamicResource ResultsAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>");
    public static readonly Style FilterStyle=Style(@"
TargetType='{x:Type ComboBox}'>
 <Setter Property='Foreground' Value='{DynamicResource ResultsInk}'/><Setter Property='Background' Value='{DynamicResource ResultsButton}'/><Setter Property='BorderBrush' Value='{DynamicResource ResultsRule}'/><Setter Property='MinHeight' Value='44'/><Setter Property='MaxDropDownHeight' Value='280'/>
 <Setter Property='ItemContainerStyle'><Setter.Value><Style TargetType='{x:Type ComboBoxItem}'><Setter Property='Foreground' Value='{DynamicResource ResultsInk}'/><Setter Property='Padding' Value='12,9'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ComboBoxItem}'><Border x:Name='item' Background='{DynamicResource ResultsSurface}' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='item' Property='Background' Value='{DynamicResource ResultsHover}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style></Setter.Value></Setter>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ComboBox}'>
 <Grid><ToggleButton Focusable='False' IsChecked='{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}' ClickMode='Press'><ToggleButton.Template><ControlTemplate TargetType='{x:Type ToggleButton}'><Border CornerRadius='3' Background='{DynamicResource ResultsButton}' BorderBrush='{DynamicResource ResultsRule}' BorderThickness='1'/></ControlTemplate></ToggleButton.Template></ToggleButton>
 <Grid IsHitTestVisible='False' Margin='12,6'><Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='20'/></Grid.ColumnDefinitions><ContentPresenter VerticalAlignment='Center' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}'/><TextBlock Grid.Column='1' Text='⌄' Foreground='{DynamicResource ResultsInk}' VerticalAlignment='Center'/></Grid>
 <Border x:Name='focusOutline' IsHitTestVisible='False' CornerRadius='3' BorderBrush='{DynamicResource ResultsAccent}' BorderThickness='0'/>
 <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'><Border Background='{DynamicResource ResultsSurface}' BorderBrush='{DynamicResource ResultsRule}' BorderThickness='1' MinWidth='{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}'><ScrollViewer HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup></Grid>
 <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='focusOutline' Property='BorderThickness' Value='1'/></Trigger><Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='focusOutline' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='{DynamicResource ResultsMuted}'/><Setter Property='Opacity' Value='0.65'/></Trigger></ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>");
    internal static readonly Style ItemStyle=Style(@"
TargetType='{x:Type ListBoxItem}'>
 <Setter Property='HorizontalContentAlignment' Value='Stretch'/><Setter Property='Padding' Value='0'/><Setter Property='Foreground' Value='{DynamicResource ResultsInk}'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ListBoxItem}'><Border x:Name='selection' BorderThickness='1' BorderBrush='Transparent' Background='Transparent'><ContentPresenter HorizontalAlignment='Stretch'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='selection' Property='BorderBrush' Value='{DynamicResource ResultsRule}'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='selection' Property='BorderBrush' Value='{DynamicResource ResultsAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>");
    internal static readonly Style TabStyle=Style(@"
TargetType='{x:Type TabItem}'>
 <Setter Property='Foreground' Value='{DynamicResource ResultsInk}'/><Setter Property='Padding' Value='18,10'/><Setter Property='FontSize' Value='16'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TabItem}'><Border x:Name='tab' Background='{DynamicResource ResultsButton}' BorderBrush='Transparent' BorderThickness='0,0,0,2' Padding='{TemplateBinding Padding}' Margin='0,0,6,0'><ContentPresenter ContentSource='Header' RecognizesAccessKey='True'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='tab' Property='Background' Value='{DynamicResource ResultsSurface}'/><Setter TargetName='tab' Property='BorderBrush' Value='{DynamicResource ResultsAccent}'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='tab' Property='Background' Value='{DynamicResource ResultsHover}'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='tab' Property='BorderBrush' Value='{DynamicResource ResultsAccent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>");
    internal static readonly Style TabsStyle=Style(@"
TargetType='{x:Type TabControl}'>
 <Setter Property='Foreground' Value='{DynamicResource ResultsInk}'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type TabControl}'><Grid><Grid.RowDefinitions><RowDefinition Height='Auto'/><RowDefinition Height='*'/></Grid.RowDefinitions><TabPanel IsItemsHost='True' Background='{DynamicResource ResultsGround}'/><Border Grid.Row='1' Background='{DynamicResource ResultsGround}' Padding='0,8,0,0'><ContentPresenter x:Name='PART_SelectedContentHost' ContentSource='SelectedContent'/></Border></Grid></ControlTemplate></Setter.Value></Setter>");
    static readonly Style ScrollStyle=Style(@"
TargetType='{x:Type ScrollBar}'>
 <Setter Property='Width' Value='12'/><Setter Property='Background' Value='{DynamicResource ResultsGround}'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type ScrollBar}'><Grid Background='{TemplateBinding Background}'><Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True'><Track.DecreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageUpCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.Thumb><Thumb><Thumb.Template><ControlTemplate><Border Background='{DynamicResource ResultsRule}' CornerRadius='4' Margin='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb><Track.IncreaseRepeatButton><RepeatButton Command='{x:Static ScrollBar.PageDownCommand}' Focusable='False'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton></Track></Grid></ControlTemplate></Setter.Value></Setter>");
}



