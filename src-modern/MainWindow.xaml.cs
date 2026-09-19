using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Ellipse=System.Windows.Shapes.Ellipse;
using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace TorreRemota {
public partial class ModernWindow : Window {
    public bool IsBusy { get { return running!=null; } }
    Settings cfg; Firebase firebase; Diagnostics diagnostics; CancellationTokenSource running;
    Localization localization; bool updatingLanguage;
    readonly Brush ok=new SolidColorBrush(Color.FromRgb(77,213,149)), warn=new SolidColorBrush(Color.FromRgb(255,187,91)), bad=new SolidColorBrush(Color.FromRgb(244,111,123)), neutral=new SolidColorBrush(Color.FromRgb(165,180,196));
    readonly List<Button> actions=new List<Button>();
    string pendingId="",lastDigest="",logPath=""; bool closing;
    readonly DispatcherTimer moonlightTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
    readonly DispatcherTimer statusTimer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};
    readonly CancellationTokenSource windowLifetime=new CancellationTokenSource();
    bool statusRefreshing;
    string lastBackgroundError="";
    readonly bool demoMode;
    readonly bool setupDemoMode;
    readonly bool flashDemoMode;
    readonly bool settingsDemoMode;
    bool setupBusy;
    CancellationTokenSource setupCancellation;
    Process moonlightProcess;
    DateTime moonlightStartedAt=DateTime.MinValue;
    MoonlightPhase moonlightPhase=MoonlightPhase.None;
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr hwnd,int attribute,ref int value,int size);
    public ModernWindow(bool demo=false,bool setupDemo=false,bool flashDemo=false,bool settingsDemo=false) {
        InitializeComponent();
        demoMode=demo;setupDemoMode=setupDemo;flashDemoMode=flashDemo;settingsDemoMode=settingsDemo;
        try{var helper=new System.Windows.Interop.WindowInteropHelper(this);SourceInitialized+=delegate{int dark=1,mica=2,round=2;DwmSetWindowAttribute(helper.Handle,20,ref dark,sizeof(int));DwmSetWindowAttribute(helper.Handle,38,ref mica,sizeof(int));DwmSetWindowAttribute(helper.Handle,33,ref round,sizeof(int));};}catch{}
        cfg=demo?new Settings{Database="https://demo-default-rtdb.europe-west1.firebasedatabase.app",Target="100.64.0.10",Language=Environment.GetCommandLineArgs().Contains("--demo-language-es")?"es":"en"}:Settings.Load();cfg.Legacy=false;
        localization=new Localization(this,cfg.Language);
        UpdateLanguageCombo();
        firebase=new Firebase(cfg);diagnostics=new Diagnostics(cfg,firebase);
        actions.AddRange(new[]{ConnectButton,WakeButton,DiagnoseButton});
        FillSettings();FillSetup();RefreshPorts();RefreshMode();
        if(!demo&&!HasUsableCredentials(cfg))SelectPage(SetupView,NavSetup);
        var previousSession=demo?null:MoonlightSession.Recent(TimeSpan.FromHours(24));
        if(previousSession!=null && previousSession.Phase==MoonlightPhase.Finished){
            MoonlightStatusText.Text=previousSession.VideoReceived?
                "Moonlight · Última sesión con vídeo, finalizada "+previousSession.LastWrite.ToString("HH:mm"):
                "Moonlight · Última sesión finalizada sin vídeo confirmado";
        }
        if(!demo)try {Directory.CreateDirectory(Settings.Folder);string folder=Path.Combine(Settings.Folder,"logs");Directory.CreateDirectory(folder);foreach(var file in new DirectoryInfo(folder).GetFiles("*.log").OrderByDescending(x=>x.LastWriteTimeUtc).Skip(19))file.Delete();logPath=Path.Combine(folder,DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log");}catch{}
        Log("Aplicación iniciada. Cargando diagnóstico sin enviar WOL.");
        Log("Configuración v2: "+(Settings.LastLoadSource==""?"no encontrada":Settings.LastLoadSource)+
            " · credenciales "+(HasUsableCredentials(cfg)?"listas":"incompletas o ilegibles")+".");
        moonlightTimer.Tick+=delegate{try{PollMoonlight();}catch(Exception ex){if(!closing&&moonlightPhase!=MoonlightPhase.NoVideo){moonlightPhase=MoonlightPhase.NoVideo;MoonlightStatusText.Text="Moonlight · Estado local no disponible";MoonlightStatusText.Foreground=warn;Log("No se pudo consultar el estado local de Moonlight: "+ex.Message);}}};
        if(!demo)moonlightTimer.Start();
        statusTimer.Tick+=async (sender,ev)=>await RefreshBackground();
        if(!demo)statusTimer.Start();
        Loaded+=async (sender,ev)=>{if(demo){ShowDemoSnapshot();if(setupDemoMode)ShowSetupDemo();if(flashDemoMode)SetupView.ScrollToBottom();if(settingsDemoMode)SelectPage(SettingsView,NavSettings);}else if(HasUsableCredentials(cfg))await RunAction("diagnose",true);else SetupAccountStatus.Text="Empieza por tu proyecto Firebase y los datos de la torre; todavía no se enviará ninguna orden.";};
        Closing+=delegate {closing=true;windowLifetime.Cancel();if(running!=null)running.Cancel();if(setupCancellation!=null)setupCancellation.Cancel();};
        Closed+=delegate {moonlightTimer.Stop();statusTimer.Stop();windowLifetime.Dispose();if(moonlightProcess!=null)moonlightProcess.Dispose();firebase.Dispose();};
    }
    static bool HasUsableCredentials(Settings s){try{return !string.IsNullOrWhiteSpace(s.ApiKey)&&!string.IsNullOrWhiteSpace(s.Email)&&!string.IsNullOrWhiteSpace(s.Password);}catch{return false;}}
    void RefreshMode(){bool complete=HasUsableCredentials(cfg);SideMode.Text=complete?"Puente · conexión protegida":"Configuración pendiente";TopMode.Text=complete?"Sistema configurado":"Preparar sistema";SideAddress.Text=string.IsNullOrWhiteSpace(cfg.Target)?"Sin torre configurada":cfg.Target+"  ·  "+cfg.App;SideStatusDot.Fill=complete?neutral:warn;HeaderWifiIcon.Foreground=complete?neutral:warn;}
    void ApplySettings(Settings next){firebase.Dispose();cfg=next;localization.SetLanguage(cfg.Language);UpdateLanguageCombo();firebase=new Firebase(cfg);diagnostics=new Diagnostics(cfg,firebase);pendingId="";FillSettings();FillSetup();RefreshMode();}
    void UpdateLanguageCombo(){updatingLanguage=true;LanguageCombo.SelectedIndex=cfg.Language=="es"?1:0;updatingLanguage=false;}
    void LanguageChanged(object sender,SelectionChangedEventArgs e){
        if(updatingLanguage||cfg==null||localization==null)return;
        string language=LanguageCombo.SelectedIndex==1?"es":"en";
        if(cfg.Language==language)return;
        try{cfg.Language=language;if(!demoMode)cfg.Save();localization.SetLanguage(language);FillSettings();FillSetup();RefreshMode();Log(language=="es"?"Idioma cambiado a español.":"Language changed to English.");}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Could not save language",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    void ReloadSettings(){var latest=Settings.Load();latest.Legacy=false;if(Json.Encode(latest)==Json.Encode(cfg))return;ApplySettings(latest);Log("Se han recargado los ajustes guardados.");}
    void FillSettings(){DatabaseBox.Text=cfg.Database;TargetBox.Text=cfg.Target;AppBox.Text=cfg.App;MoonlightBox.Text=cfg.Moonlight;TailscaleBox.Text=cfg.Tailscale;PortBox.Text=cfg.BasePort.ToString();WaitBox.Text=cfg.WaitSeconds.ToString();ApiKeyBox.Text=cfg.ApiKey;EmailBox.Text=cfg.Email;PasswordBox.Password="";CredentialStatus.Text=HasUsableCredentials(cfg)?"Credenciales guardadas. Deja la contraseña vacía para conservarla.":"Faltan credenciales o la contraseña guardada no se puede descifrar.";CredentialStatus.Foreground=HasUsableCredentials(cfg)?ok:warn;}
    void SelectPage(FrameworkElement page,Button nav){DashboardView.Visibility=page==DashboardView?Visibility.Visible:Visibility.Collapsed;SetupView.Visibility=page==SetupView?Visibility.Visible:Visibility.Collapsed;SettingsView.Visibility=page==SettingsView?Visibility.Visible:Visibility.Collapsed;GuideView.Visibility=page==GuideView?Visibility.Visible:Visibility.Collapsed;foreach(var b in new[]{NavDashboard,NavSetup,NavSettings,NavGuide}){b.Background=(Brush)new BrushConverter().ConvertFromString(b==nav?"#183650":"#00000000");b.Foreground=(Brush)new BrushConverter().ConvertFromString(b==nav?"#77C4FF":"#AFC1D7");}}
    void ShowDashboard(object sender,RoutedEventArgs e){SelectPage(DashboardView,NavDashboard);}
    void ShowSetup(object sender,RoutedEventArgs e){FillSetup();RefreshPorts();SelectPage(SetupView,NavSetup);}
    void ShowSettings(object sender,RoutedEventArgs e){FillSettings();SelectPage(SettingsView,NavSettings);}
    void ShowGuide(object sender,RoutedEventArgs e){SelectPage(GuideView,NavGuide);}
    async void SaveSettingsClick(object sender,RoutedEventArgs e){try{if(running!=null||setupBusy)throw new Exception("Espera a que termine la comprobación actual.");while(statusRefreshing&&!closing)await Task.Delay(100);if(closing)return;var next=Json.ReadSettings(Json.Encode(cfg));next.Database=DatabaseBox.Text.Trim().TrimEnd('/');next.Target=TargetBox.Text.Trim();next.App=AppBox.Text.Trim();next.Moonlight=MoonlightBox.Text.Trim();next.Tailscale=TailscaleBox.Text.Trim();next.BasePort=int.Parse(PortBox.Text);next.WaitSeconds=int.Parse(WaitBox.Text);next.Legacy=false;next.ApiKey=string.IsNullOrWhiteSpace(ApiKeyBox.Text)?cfg.ApiKey:ApiKeyBox.Text.Trim();next.Email=string.IsNullOrWhiteSpace(EmailBox.Text)?cfg.Email:EmailBox.Text.Trim();if(PasswordBox.Password!="")next.Password=PasswordBox.Password;next.Validate();next.Save();ApplySettings(next);Log("Ajustes guardados con copia de seguridad cifrada.");SelectPage(DashboardView,NavDashboard);_=RunAction("diagnose",true);}catch(Exception ex){MessageBox.Show(this,localization.Convert(ex.Message),localization.Convert("Revisa los ajustes"),MessageBoxButton.OK,MessageBoxImage.Warning);}}
    void FillSetup(){
        SetupDatabaseBox.Text=cfg.Database;SetupApiKeyBox.Text=cfg.ApiKey;SetupTargetBox.Text=cfg.Target;
        SetupLanBox.Text=cfg.TowerLanIp;SetupMacBox.Text=cfg.TowerMac;SetupWifiBox.Text=cfg.WifiSsid;SetupWifiPasswordBox.Password="";
        SetupAccountStatus.Text=string.IsNullOrWhiteSpace(cfg.LaptopUid)||string.IsNullOrWhiteSpace(cfg.DeviceUid)?
            "Faltan usuarios técnicos o sus UID. El asistente puede crearlos en tu proyecto Firebase.":
            "Usuarios preparados · portátil y ESP32 separados. La contraseña Wi-Fi guardada se conserva si dejas su campo vacío.";
        try{SetupRulesBox.Text=Provisioning.Rules(cfg.LaptopUid,cfg.DeviceUid);}catch{SetupRulesBox.Text=localization.Convert("Las reglas aparecerán al preparar los dos usuarios.");}
    }
    void RefreshPorts(){
        string selected=SetupPortCombo.SelectedItem as string;
        SetupPortCombo.Items.Clear();
        try {foreach(string port in Esp32Flasher.Ports())SetupPortCombo.Items.Add(port);}catch(Exception ex){AppendSetup("No se pudieron listar los puertos COM: "+ex.Message);}
        if(selected!=null&&SetupPortCombo.Items.Contains(selected))SetupPortCombo.SelectedItem=selected;
        else if(SetupPortCombo.Items.Count>0)SetupPortCombo.SelectedIndex=0;
        if(SetupPortCombo.Items.Count==0){SetupPortCombo.Items.Add("Sin puerto COM");SetupPortCombo.SelectedIndex=0;AppendSetup("Ningún puerto COM detectado. Comprueba cable USB y controlador de la placa.");}
    }
    void SetupDetectPortsClick(object sender,RoutedEventArgs e){RefreshPorts();}
    void AppendSetup(string message){
        if(closing)return;
        SetupProgressBox.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+localization.Convert(message)+Environment.NewLine);
        if(SetupProgressBox.Text.Length>30000)SetupProgressBox.Text=SetupProgressBox.Text.Substring(SetupProgressBox.Text.Length-20000);
        SetupProgressBox.ScrollToEnd();
    }
    async Task<Settings> SaveSetupForm(){
        if(running!=null)throw new Exception("Espera a que termine la comprobación actual.");
        while(statusRefreshing&&!closing)await Task.Delay(100);
        if(closing)throw new OperationCanceledException();
        var next=Json.ReadSettings(Json.Encode(cfg));
        next.Database=SetupDatabaseBox.Text.Trim().TrimEnd('/');next.ApiKey=SetupApiKeyBox.Text.Trim();
        next.Target=SetupTargetBox.Text.Trim();next.TowerLanIp=SetupLanBox.Text.Trim();next.TowerMac=SetupMacBox.Text.Trim();
        next.WifiSsid=SetupWifiBox.Text.Trim();if(SetupWifiPasswordBox.Password!="")next.WifiPassword=SetupWifiPasswordBox.Password;
        Provisioning.ValidateBase(next);if(string.IsNullOrWhiteSpace(next.ApiKey))throw new Exception("Introduce la Firebase Web API key.");
        next.Save();ApplySettings(next);return next;
    }
    async void SetupSaveClick(object sender,RoutedEventArgs e){
        try {if(setupBusy)return;await SaveSetupForm();AppendSetup("Datos guardados en tu usuario de Windows.");}
        catch(Exception ex){MessageBox.Show(this,localization.Convert(ex.Message),localization.Convert("Revisa los datos"),MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    void SetSetupBusy(bool busy){setupBusy=busy;CreateAccountsButton.IsEnabled=!busy;SetupFlashButton.IsEnabled=!busy;SetupConfigureButton.IsEnabled=!busy;SetupCancelButton.IsEnabled=busy;}
    async Task<FirebaseAccount> ResolveAccount(Settings s,bool device,CancellationToken ct){
        string email=device?s.DeviceEmail:s.Email,password=device?s.DevicePassword:s.Password;
        bool generated=email.EndsWith("@remote-pc-bridge.invalid",StringComparison.OrdinalIgnoreCase);
        if(generated){
            try{return await Provisioning.SignIn(s.ApiKey,email,password,ct);}
            catch(OperationCanceledException){throw;}
            catch {return await Provisioning.Create(s.ApiKey,email,password,ct);}
        }
        return await Provisioning.SignIn(s.ApiKey,email,password,ct);
    }
    async void SetupAccountsClick(object sender,RoutedEventArgs e){
        if(setupBusy)return;
        SetSetupBusy(true);setupCancellation=new CancellationTokenSource();
        try {
            var next=await SaveSetupForm();var ct=setupCancellation.Token;
            if(string.IsNullOrWhiteSpace(next.Email)||string.IsNullOrWhiteSpace(next.Password)){
                next.Email=Provisioning.GenerateEmail("client");next.Password=Provisioning.GeneratePassword();next.Save();ApplySettings(next);
            }
            AppendSetup("Comprobando el usuario del portátil en Firebase…");
            var laptop=await ResolveAccount(next,false,ct);next.LaptopUid=laptop.Uid;next.Save();ApplySettings(next);
            if(string.IsNullOrWhiteSpace(next.DeviceEmail)||string.IsNullOrWhiteSpace(next.DevicePassword)){
                next.DeviceEmail=Provisioning.GenerateEmail("device");next.DevicePassword=Provisioning.GeneratePassword();next.Save();ApplySettings(next);
            }
            AppendSetup("Comprobando el usuario del ESP32 en Firebase…");
            var device=await ResolveAccount(next,true,ct);next.DeviceUid=device.Uid;next.Save();ApplySettings(next);
            SetupRulesBox.Text=Provisioning.Rules(next.LaptopUid,next.DeviceUid);
            SetupAccountStatus.Text="Usuarios listos. Copia y publica las reglas antes de probar el puente.";
            AppendSetup("Usuarios autenticados. Reglas generadas; falta publicarlas en Firebase.");
            Log("Onboarding: dos usuarios técnicos preparados y reglas generadas.");
        }catch(OperationCanceledException){AppendSetup("Preparación cancelada. Los datos ya guardados se conservarán.");}
        catch(Exception ex){AppendSetup("Error al preparar usuarios: "+ex.Message);SetupAccountStatus.Text=ex.Message;}
        finally {setupCancellation.Dispose();setupCancellation=null;SetSetupBusy(false);}
    }
    void SetupCopyRulesClick(object sender,RoutedEventArgs e){try{if(string.IsNullOrWhiteSpace(cfg.LaptopUid)||string.IsNullOrWhiteSpace(cfg.DeviceUid))throw new Exception("Primero prepara los usuarios.");Clipboard.SetText(SetupRulesBox.Text);AppendSetup("Reglas copiadas. Pégalas en la pestaña Reglas de Realtime Database.");}catch(Exception ex){AppendSetup(ex.Message);}}
    void SetupOpenFirebaseClick(object sender,RoutedEventArgs e){try{Process.Start(new ProcessStartInfo("https://console.firebase.google.com/"){UseShellExecute=true});}catch(Exception ex){AppendSetup("No se pudo abrir Firebase: "+ex.Message);}}
    void SetupFlashClick(object sender,RoutedEventArgs e){_=RunProvision(true);}
    void SetupConfigureClick(object sender,RoutedEventArgs e){_=RunProvision(false);}
    void SetupCancelClick(object sender,RoutedEventArgs e){if(setupCancellation!=null)setupCancellation.Cancel();}
    async Task RunProvision(bool flash){
        if(setupBusy)return;
        string port=SetupPortCombo.SelectedItem as string;
        SetSetupBusy(true);setupCancellation=new CancellationTokenSource();
        try {
            var next=await SaveSetupForm();string payload=Provisioning.BuildDeviceJson(next);
            var progress=new Progress<string>(AppendSetup);
            if(flash)await Esp32Flasher.Flash(port,progress,setupCancellation.Token);
            await Esp32Flasher.Configure(port,payload,progress,setupCancellation.Token);
            SetupAccountStatus.Text="ESP32 configurado. Espera hasta 45 segundos y pulsa Diagnosticar para comprobar su señal.";
            Log("ESP32 configurado por USB en "+port+". No se registraron secretos.");
        }catch(OperationCanceledException){AppendSetup("Operación cancelada. Si se interrumpió el flasheo, repítelo antes de configurar.");}
        catch(Exception ex){AppendSetup("Error: "+ex.Message);SetupAccountStatus.Text="Revisa el detalle del flasheo y vuelve a intentarlo.";}
        finally {setupCancellation.Dispose();setupCancellation=null;SetSetupBusy(false);}
    }
    void ShowDemoSnapshot(){
        SideMode.Text="ESP32 · demostración";TopMode.Text="Vista de ejemplo";SideAddress.Text="100.64.0.10 · Desktop";
        var sample=new Snapshot{Cloud=true,Fresh=true,LanKnown=true,LanOpen=true,Ready=true,
            CloudText="Lectura autenticada correcta",EspText="Activo · Wi-Fi -58 dBm · firmware 3.0.0 · uptime 6 h",
            LanText="192.168.1.25 · HTTP abierto · HTTPS abierto · RTSP abierto",
            TailText="Portátil: Running · torre anunciada en línea (no prueba conexión directa)",
            PortsText="47984: abierto   |   47989: abierto   |   48010: abierto"};
        sample.Explain(false);ShowSnapshot(sample);TopMode.Text="Vista de ejemplo";StatusFreshnessText.Text="Vista de ejemplo · sin conexiones reales";
    }
    void ShowSetupDemo(){
        SetupDatabaseBox.Text="https://sample-project-default-rtdb.firebaseio.com";
        SetupApiKeyBox.Text=localization.Convert("Pega aquí tu Web API key");SetupTargetBox.Text="100.64.0.10";
        SetupLanBox.Text="192.168.1.25";SetupMacBox.Text="AA:BB:CC:DD:EE:FF";SetupWifiBox.Text=localization.Convert("Mi Wi-Fi");
        SetupPortCombo.Items.Clear();SetupPortCombo.Items.Add(localization.Convert("COM7 (ejemplo)"));SetupPortCombo.SelectedIndex=0;
        if(flashDemoMode)SetupProgressBox.Text=localization.Convert("Ejemplo de progreso: puerto COM7 detectado.")+Environment.NewLine+
            localization.Convert("Firmware escrito y verificado.")+Environment.NewLine+
            localization.Convert("Configuración guardada. El ESP32 se está reiniciando.");
        SetupAccountStatus.Text="Vista de ejemplo · los usuarios se crearán en tu proyecto Firebase.";
        SelectPage(SetupView,NavSetup);
    }
    void Log(string message){if(closing)return;string line=DateTime.Now.ToString("HH:mm:ss")+"  "+localization.Convert(message)+Environment.NewLine;ActivityLog.AppendText(line);if(ActivityLog.Text.Length>120000)ActivityLog.Text=ActivityLog.Text.Substring(ActivityLog.Text.Length-90000);ActivityLog.ScrollToEnd();if(logPath!="")try{File.AppendAllText(logPath,line,System.Text.Encoding.UTF8);}catch{OrderDetail.Text="No se pudo guardar el registro; usa Exportar.";}}
    void SetCard(TextBlock value,TextBlock detail,Ellipse dot,string summary,string full,Brush color){value.Text=summary;value.Foreground=color;detail.Text=full;detail.ToolTip=localization.Convert(full);dot.Fill=color;}
    void SetStage(Border border,TextBlock label,string summary,Brush color){label.Text=summary;label.Foreground=color;border.BorderBrush=color;}
    void UpdateVideoStage(){
        if(moonlightPhase==MoonlightPhase.Video)SetStage(StageVideo,StageVideoText,"Vídeo recibido",ok);
        else if(moonlightPhase==MoonlightPhase.Launching)SetStage(StageVideo,StageVideoText,"Conectando…",warn);
        else if(moonlightPhase==MoonlightPhase.NoVideo)SetStage(StageVideo,StageVideoText,"Sin vídeo",bad);
        else if(moonlightPhase==MoonlightPhase.Finished)SetStage(StageVideo,StageVideoText,"Finalizada",neutral);
        else SetStage(StageVideo,StageVideoText,"Sin iniciar",neutral);
    }
    void ShowSnapshot(Snapshot s){
        if(closing)return;
        StatusFreshnessText.Text="Última comprobación: "+DateTime.Now.ToString("HH:mm:ss")+" · actualización automática cada 30 s";
        SetCard(FirebaseValue,FirebaseDetail,FirebaseDot,s.Cloud?"Conectado":"Sin acceso",s.CloudText,s.Cloud?ok:bad);
        SetCard(EspValue,EspDetail,EspDot,s.Fresh?"En línea":"Sin señal reciente",s.EspText,s.Fresh?ok:warn);
        SetCard(LanValue,LanDetail,LanDot,s.LanOpen?"Servicios accesibles":s.LanKnown?"Sin respuesta":"Sin datos",s.LanText,s.LanOpen?ok:s.LanKnown?warn:neutral);
        bool tsRunning=s.TailText.Contains("Portátil: Running");
        bool towerOnline=s.TailText.Contains("torre anunciada en línea");
        SetCard(TailValue,TailDetail,TailDot,!tsRunning?"Revisar Tailscale":towerOnline?"Torre anunciada":"Torre sin anuncio",s.TailText,tsRunning&&towerOnline?ok:warn);
        SetCard(SunValue,SunDetail,SunDot,s.Ready?"Puertos abiertos":"Sin conexión",s.PortsText,s.Ready?ok:warn);
        SetStage(StageCloud,StageCloudText,s.Cloud?"Autenticado":"Sin acceso",s.Cloud?ok:bad);
        SetStage(StageEsp,StageEspText,s.Fresh?"Señal reciente":"Sin señal",s.Fresh?ok:warn);
        SetStage(StageLan,StageLanText,s.LanOpen?"Servicios activos":s.LanKnown?"Sin respuesta":"Sin datos",s.LanOpen?ok:s.LanKnown?warn:neutral);
        SetStage(StageRemote,StageRemoteText,s.Ready?"Puertos abiertos":"Sin respuesta",s.Ready?ok:warn);
        UpdateVideoStage();
        HeroStatusDot.Fill=s.Ready?ok:s.Cloud?warn:bad;
        SideStatusDot.Fill=s.Fresh?ok:s.Cloud?warn:bad;
        HeaderWifiIcon.Foreground=s.Fresh?ok:s.Cloud?warn:bad;
        TopMode.Text=s.Fresh?"ESP32 en línea":s.Cloud?"ESP32 sin señal":"Sin acceso";
        Headline.Text=s.Ready?"Tu torre responde":"Revisando la conexión";
        Advice.Text=s.Advice;
        string digest=string.Join(Environment.NewLine,new[]{s.CloudText,s.EspText,s.LanText,s.TailText,s.PortsText});
        string key=string.Join("|",new[]{s.CloudText,s.Fresh.ToString(),s.EspHealthKey,s.LanText,s.TailText,s.PortsText,s.AckId,s.AckState});
        if(key!=lastDigest){Log(digest);lastDigest=key;}
        if(pendingId!=""&&s.AckId==pendingId){OrderValue.Text=s.AckState=="sent"?"WOL enviado":s.AckState;OrderDetail.Text="Acuse del ESP32 · "+pendingId.Substring(0,8);OrderDot.Fill=s.AckState=="sent"?ok:warn;}
        else if(pendingId==""&&s.AckId!=""&&s.AckTime>0&&s.AckTime<253402300799000){OrderValue.Text=s.AckState=="sent"?"Último WOL enviado":s.AckState;OrderDetail.Text="Acuse "+DateTimeOffset.FromUnixTimeMilliseconds(s.AckTime).ToLocalTime().ToString("dd/MM HH:mm")+" · ID "+s.AckId.Substring(0,Math.Min(8,s.AckId.Length));OrderDot.Fill=s.AckState=="sent"?ok:warn;}
        if(moonlightPhase==MoonlightPhase.Video)ShowVideoBanner();
    }
    void SetBusy(bool busy){foreach(var b in actions)b.IsEnabled=!busy;CancelButton.IsEnabled=busy;}
    async Task RefreshBackground(){
        if(closing||running!=null||statusRefreshing||setupBusy||!HasUsableCredentials(cfg))return;
        statusRefreshing=true;
        try {
            cfg.Validate();
            var snapshot=await diagnostics.Read(windowLifetime.Token);
            if(!closing){ShowSnapshot(snapshot);lastBackgroundError="";}
        } catch(OperationCanceledException) {}
        catch(Exception ex) {if(!closing&&lastBackgroundError!=ex.Message){lastBackgroundError=ex.Message;StatusFreshnessText.Text="No se pudo actualizar: "+ex.Message;Log("Actualización automática: "+ex.Message);}}
        finally {statusRefreshing=false;}
    }
    async Task RunAction(string action,bool automatic=false){
        if(running!=null||setupBusy||demoMode)return;
        running=new CancellationTokenSource();var ct=running.Token;SetBusy(true);
        try{
            while(statusRefreshing)await Task.Delay(100,ct);
            ReloadSettings();cfg.Validate();
            if(!automatic)Log("Acción: "+action+".");
            Headline.Text=action=="diagnose"?"Comprobando cada etapa…":"Preparando la conexión…";
            var snapshot=await diagnostics.Read(ct);ct.ThrowIfCancellationRequested();ShowSnapshot(snapshot);
            if(action=="diagnose")return;
            if(action=="connect"&&snapshot.Ready){OpenMoonlight();return;}
            pendingId=await firebase.QueueWake(Guid.NewGuid().ToString("N"),ct,true);OrderValue.Text="Orden activa";OrderDetail.Text="ID "+pendingId.Substring(0,8)+" · esperando al ESP32";OrderDot.Fill=warn;Log("Orden "+pendingId.Substring(0,8)+" aceptada por Firebase.");
            var watch=Stopwatch.StartNew();
            while(watch.Elapsed.TotalSeconds<cfg.WaitSeconds){
                await Task.Delay(3000,ct);snapshot=await diagnostics.Read(ct);ct.ThrowIfCancellationRequested();ShowSnapshot(snapshot);
                if(snapshot.Ready){if(action=="connect")OpenMoonlight();return;}
                if(action=="wake"&&snapshot.AckId==pendingId){Headline.Text=snapshot.AckState=="sent"?"El ESP32 ha enviado WOL":"Acuse: "+snapshot.AckState;return;}
                Headline.Text="Esperando a la torre · "+(int)watch.Elapsed.TotalSeconds+" / "+cfg.WaitSeconds+" s";
            }
            Headline.Text="Tiempo de espera agotado";Advice.Text=snapshot.Advice;Log("Tiempo de espera agotado. "+snapshot.Advice);
        }catch(OperationCanceledException){if(!closing){Headline.Text="Espera cancelada";Log("Espera cancelada. Una orden ya enviada puede ejecutarse hasta caducar.");}}
        catch(Exception ex){if(!closing){Headline.Text="No se ha completado la acción";Advice.Text=ex.Message;Log(ex.Message);}}
        finally{var old=running;running=null;old.Dispose();if(!closing)SetBusy(false);}
    }
    void ConnectClick(object sender,RoutedEventArgs e){_=RunAction("connect");}
    void WakeClick(object sender,RoutedEventArgs e){_=RunAction("wake");}
    void DiagnoseClick(object sender,RoutedEventArgs e){_=RunAction("diagnose");}
    void CancelClick(object sender,RoutedEventArgs e){if(running!=null)running.Cancel();}
    async void RouteClick(object sender,RoutedEventArgs e){try{ReloadSettings();Log("Ruta Tailscale: "+(await Probe.Run(cfg.Tailscale,"ping --c 3 --timeout 2s "+cfg.Target,CancellationToken.None)).Trim());}catch(Exception ex){Log(ex.Message);}}
    void OpenMoonlightClick(object sender,RoutedEventArgs e){OpenMoonlight();}
    void OpenMoonlight(){try{ReloadSettings();if(!File.Exists(cfg.Moonlight))throw new Exception("No encuentro Moonlight.exe. Revisa la ruta en Configuración.");cfg.Validate();if(moonlightProcess!=null)moonlightProcess.Dispose();moonlightStartedAt=DateTime.Now;moonlightProcess=Process.Start(new ProcessStartInfo(cfg.Moonlight,"stream "+cfg.Target+" \""+cfg.App+"\""){UseShellExecute=false});if(moonlightProcess==null)throw new Exception("Windows no pudo iniciar Moonlight.");moonlightPhase=MoonlightPhase.Launching;UpdateVideoStage();Headline.Text="Conectando con Moonlight…";Advice.Text="Esperando confirmación local de que llega vídeo desde la torre.";MoonlightStatusText.Text="Moonlight · Iniciando sesión";MoonlightStatusText.Foreground=warn;Log("Moonlight lanzado. Esperando el primer paquete de vídeo; este botón no envía WOL a Firebase.");}catch(Exception ex){moonlightStartedAt=DateTime.MinValue;moonlightPhase=MoonlightPhase.NoVideo;UpdateVideoStage();MoonlightStatusText.Text="Moonlight · No se pudo iniciar";MoonlightStatusText.Foreground=bad;Advice.Text=ex.Message;Log(ex.Message);}}
    void ShowVideoBanner(){Headline.Text="Sesión Moonlight conectada";Advice.Text="El registro local de Moonlight confirma que recibió vídeo de la torre. El estado de los servicios se muestra por separado abajo.";}
    void PollMoonlight(){
        if(closing||moonlightStartedAt==DateTime.MinValue)return;
        bool alive=false;
        try{alive=moonlightProcess!=null&&!moonlightProcess.HasExited;}catch{}
        if(!alive)foreach(var p in Process.GetProcessesByName("Moonlight")){try{if(string.Equals(p.MainModule.FileName,cfg.Moonlight,StringComparison.OrdinalIgnoreCase))alive=true;}catch{}finally{p.Dispose();}}
        var state=MoonlightSession.Observe(moonlightStartedAt,alive);
        if(state.Phase==moonlightPhase)return;
        moonlightPhase=state.Phase;
        UpdateVideoStage();
        if(state.Phase==MoonlightPhase.Video){MoonlightStatusText.Text="Moonlight · Vídeo recibido · sesión activa";MoonlightStatusText.Foreground=ok;ShowVideoBanner();}
        else if(state.Phase==MoonlightPhase.Finished){MoonlightStatusText.Text="Moonlight · Sesión finalizada";MoonlightStatusText.Foreground=neutral;Headline.Text="Sesión Moonlight finalizada";Advice.Text=state.Detail;}
        else if(state.Phase==MoonlightPhase.NoVideo){MoonlightStatusText.Text="Moonlight · Sin vídeo confirmado";MoonlightStatusText.Foreground=warn;Headline.Text="Moonlight no confirmó vídeo";Advice.Text="La aplicación se cerró sin recibir vídeo. Revisa Moonlight y exporta el registro si falla otra vez.";}
        else{MoonlightStatusText.Text="Moonlight · Esperando vídeo";MoonlightStatusText.Foreground=warn;}
        Log(state.Detail+(state.LogPath!=""?" Registro local: "+state.LogPath:""));
    }
    void ExportClick(object sender,RoutedEventArgs e){var dialog=new SaveFileDialog{Filter=localization.Convert("Registro de texto")+"|*.txt",FileName="RemotePcBridge-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"};if(dialog.ShowDialog(this)==true)try{File.WriteAllText(dialog.FileName,localization.Convert("Torre Remota · ")+DateTime.Now+Environment.NewLine+SideMode.Text+Environment.NewLine+ActivityLog.Text);Log("Registro exportado.");}catch(Exception ex){MessageBox.Show(this,localization.Convert(ex.Message),localization.Convert("No se pudo exportar"));}}
}
}
