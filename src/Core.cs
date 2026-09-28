using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
#else
using System.Web.Script.Serialization;
#endif

namespace TorreRemota {
public static class Json {
#if NET8_0_OR_GREATER
    static readonly JsonSerializerOptions Options=new JsonSerializerOptions{IncludeFields=true};
    public static string Encode(object x) { return JsonSerializer.Serialize(x,Options); }
    static object ConvertElement(JsonElement e) {
        switch(e.ValueKind) {
            case JsonValueKind.Object: var d=new Dictionary<string,object>();foreach(var p in e.EnumerateObject())d[p.Name]=ConvertElement(p.Value);return d;
            case JsonValueKind.Array: return e.EnumerateArray().Select(ConvertElement).ToArray();
            case JsonValueKind.String: return e.GetString();
            case JsonValueKind.Number: long n;return e.TryGetInt64(out n)?(object)n:e.GetDouble();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            default: return null;
        }
    }
    public static Dictionary<string,object> Read(string s) {if(string.IsNullOrWhiteSpace(s)||s.Trim()=="null")return new Dictionary<string,object>();using(var d=JsonDocument.Parse(s))return ConvertElement(d.RootElement) as Dictionary<string,object> ?? new Dictionary<string,object>();}
    public static Settings ReadSettings(string s) {return JsonSerializer.Deserialize<Settings>(s,Options);}
#else
    public static string Encode(object x) { return new JavaScriptSerializer().Serialize(x); }
    public static Dictionary<string,object> Read(string s) { return new JavaScriptSerializer().DeserializeObject(s) as Dictionary<string,object> ?? new Dictionary<string,object>(); }
    public static Settings ReadSettings(string s) {return new JavaScriptSerializer().Deserialize<Settings>(s);}
#endif
    public static string Str(Dictionary<string,object> d,string k) { object v; return d.TryGetValue(k,out v) && v!=null ? Convert.ToString(v) : ""; }
    public static double Num(Dictionary<string,object> d,string k) { double n; return double.TryParse(Str(d,k),out n) ? n : 0; }
    public static bool Bool(Dictionary<string,object> d,string k) { return Str(d,k).Equals("true",StringComparison.OrdinalIgnoreCase); }
    public static Dictionary<string,object> Obj(Dictionary<string,object> d,string k) { object v; return d.TryGetValue(k,out v) ? v as Dictionary<string,object> ?? new Dictionary<string,object>() : new Dictionary<string,object>(); }
    public static long Now { get { return (long)(DateTime.UtcNow-new DateTime(1970,1,1)).TotalMilliseconds; } }
    public static object Timestamp { get { return new Dictionary<string,object>{{".sv","timestamp"}}; } }
}
static class CredentialVault {
    const uint Generic=1,PersistOnThisComputer=2;
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    struct NativeCredential {
        public uint Flags,Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist,AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }
    [DllImport("Advapi32.dll",EntryPoint="CredReadW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("Advapi32.dll",EntryPoint="CredWriteW",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool CredWrite(ref NativeCredential credential,uint flags);
    [DllImport("Advapi32.dll",SetLastError=true)]static extern void CredFree(IntPtr buffer);
    public static string Read(string target) {
        if(string.IsNullOrWhiteSpace(target))return "";
        IntPtr pointer;if(!CredRead(target,Generic,0,out pointer))return "";
        try {
            var credential=Marshal.PtrToStructure<NativeCredential>(pointer);
            if(credential.CredentialBlob==IntPtr.Zero||credential.CredentialBlobSize==0)return "";
            var bytes=new byte[credential.CredentialBlobSize];Marshal.Copy(credential.CredentialBlob,bytes,0,bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        } finally {CredFree(pointer);}
    }
    public static void Write(string target,string value) {
        if(string.IsNullOrWhiteSpace(target)||string.IsNullOrEmpty(value))return;
        byte[] bytes=Encoding.UTF8.GetBytes(value);if(bytes.Length>512)throw new InvalidDataException("Credential is too long for Windows Credential Manager.");
        IntPtr blob=Marshal.AllocHGlobal(bytes.Length);
        try {
            Marshal.Copy(bytes,0,blob,bytes.Length);
            var credential=new NativeCredential{Type=Generic,TargetName=target,CredentialBlobSize=(uint)bytes.Length,
                CredentialBlob=blob,Persist=PersistOnThisComputer,UserName=Environment.UserName};
            if(!CredWrite(ref credential,0))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        } finally {Marshal.FreeHGlobal(blob);}
    }
}
static class RecoveryStore {
    const string Key=@"Software\RemotePcBridge";
    public static string Read() {using(var key=Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key,false))return Convert.ToString(key==null?null:key.GetValue("Settings"));}
    public static void Write(string json) {using(var key=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key,true))key.SetValue("Settings",json,Microsoft.Win32.RegistryValueKind.String);}
}
public class Settings {
    public string Language = "en";
    public string Theme = "dark";
    public string Database = "";
    public string Target = "";
    public string App = "Desktop";
    public string Moonlight = @"C:\Program Files\Moonlight Game Streaming\Moonlight.exe";
    public string Tailscale = @"C:\Program Files\Tailscale\tailscale.exe";
    public int BasePort = 47989;
    public int WaitSeconds = 180;
    public bool Legacy = false;
    public string ApiKey = "";
    public string Email = "";
    public string ProtectedPassword = "";
    public string LaptopUid = "";
    public string DeviceEmail = "";
    public string ProtectedDevicePassword = "";
    public string DeviceUid = "";
    public string WifiSsid = "";
    public string ProtectedWifiPassword = "";
    public string TowerLanIp = "";
    public string TowerMac = "";
#if NET8_0_OR_GREATER
    [JsonIgnore]
#else
    [ScriptIgnore]
#endif
    public string Password { get { return ReadSecret(ProtectedPassword,"client",Email); } set {ProtectedPassword=ProtectSecret(value);} }
#if NET8_0_OR_GREATER
    [JsonIgnore]
#else
    [ScriptIgnore]
#endif
    public string DevicePassword { get { return ReadSecret(ProtectedDevicePassword,"device",DeviceEmail); } set {ProtectedDevicePassword=ProtectSecret(value);} }
#if NET8_0_OR_GREATER
    [JsonIgnore]
#else
    [ScriptIgnore]
#endif
    public string WifiPassword { get { return ReadSecret(ProtectedWifiPassword,"wifi",WifiSsid); } set {ProtectedWifiPassword=ProtectSecret(value);} }
    static string ProtectSecret(string value) {return string.IsNullOrEmpty(value)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser));}
    static string ReadSecret(string currentUser,string kind,string identity) {
        Exception error=null;
        if(!string.IsNullOrEmpty(currentUser))try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(currentUser),null,DataProtectionScope.CurrentUser));}catch(Exception e){error=e;}
        var vault=CredentialVault.Read(VaultTarget(kind,identity));
        if(!string.IsNullOrEmpty(vault))return vault;
        if(error!=null)throw error;
        return "";
    }
    static string VaultTarget(string kind,string identity) {
        using(var sha=SHA256.Create()) {
            string hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(identity??""))).Replace("-","").Substring(0,24);
            return "RemotePcBridge/"+kind+"/"+hash;
        }
    }
    public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TorreRemota"); } }
    public static string ConfigFolder {
        get {
            // Some launch contexts resolve ApplicationData differently from the user's
            // actual roaming profile. Prefer the existing profile configuration so a
            // new EXE cannot silently start with empty settings or save over it.
            var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var applicationData=Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return ResolveConfigFolder(profile,applicationData);
        }
    }
    internal static string ResolveConfigFolder(string profile,string applicationData) {
            var profileFolder=Path.Combine(profile,"AppData","Roaming","TorreRemota");
            var shellFolder=Path.Combine(applicationData,"TorreRemota");
            return File.Exists(Path.Combine(profileFolder,"settings.json")) ||
                File.Exists(Path.Combine(profileFolder,"settings.json.bak")) ||
                File.Exists(Path.Combine(profileFolder,"settings.rescue.json")) ||
                string.IsNullOrWhiteSpace(applicationData)
                ? profileFolder : shellFolder;
    }
    public static string LastLoadSource { get; private set; } = "";
    public static string LastRecoveryStatus { get; private set; } = "";
    static bool CredentialsReadable(Settings s) {
        if(string.IsNullOrWhiteSpace(s.ApiKey)||string.IsNullOrWhiteSpace(s.Email)||string.IsNullOrWhiteSpace(s.ProtectedPassword))return false;
        try{return !string.IsNullOrWhiteSpace(s.Password);}catch{return false;}
    }
    static bool UserProtectionReadable(Settings value) {
        try {return !string.IsNullOrWhiteSpace(value.ProtectedPassword)&&
            !string.IsNullOrWhiteSpace(Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value.ProtectedPassword),null,DataProtectionScope.CurrentUser)));}
        catch{return false;}
    }
    static bool TryRefreshProtection(Settings value) {
        try {
            // Re-encrypt readable DPAPI values for the current Windows profile. This
            // also verifies the values before a recovered copy replaces the primary.
            var password=value.Password;
            var devicePassword=value.DevicePassword;
            var wifiPassword=value.WifiPassword;
            if(string.IsNullOrWhiteSpace(password))return false;
            value.Password=password;
            if(!string.IsNullOrEmpty(devicePassword))value.DevicePassword=devicePassword;
            if(!string.IsNullOrEmpty(wifiPassword))value.WifiPassword=wifiPassword;
            return true;
        } catch {return false;}
    }
    static void SaveDurableRecovery(Settings value) {
        RecoveryStore.Write(Json.Encode(value));
        string password=value.Password;if(!string.IsNullOrEmpty(password))CredentialVault.Write(VaultTarget("client",value.Email),password);
        string device=value.DevicePassword;if(!string.IsNullOrEmpty(device))CredentialVault.Write(VaultTarget("device",value.DeviceEmail),device);
        string wifi=value.WifiPassword;if(!string.IsNullOrEmpty(wifi))CredentialVault.Write(VaultTarget("wifi",value.WifiSsid),wifi);
    }
    public static Settings Load() {
        LastRecoveryStatus="";
        var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var primary=Path.Combine(ConfigFolder,"settings.json");
        var rescue=Path.Combine(ConfigFolder,"settings.rescue.json");
        var recovery=Path.Combine(profile,"Desktop","TorreRemota.recuperacion.json");
        var profileRoaming=Path.Combine(profile,"AppData","Roaming","TorreRemota","settings.json");
        var paths=new[]{primary,primary+".bak",rescue,profileRoaming,profileRoaming+".bak",Path.Combine(Folder,"settings.json"),
            Path.Combine(profile,"AppData","Local","TorreRemota","settings.json"),recovery};
        Settings durable=null;try {var saved=RecoveryStore.Read();if(!string.IsNullOrWhiteSpace(saved))durable=Json.ReadSettings(saved);} catch {}
        return LoadFromPaths(primary,recovery,paths,durable,true);
    }
    internal static Settings LoadFromPaths(string primary,string recovery,IEnumerable<string> paths,Settings durable=null,bool persistDurable=false) {
        Exception lastError=null;
        Settings incomplete=null;string incompletePath="";
        Settings Finish(Settings value,string p) {
            LastLoadSource=p;
            bool valid=CredentialsReadable(value);
            bool refreshed=valid&&!UserProtectionReadable(value)&&TryRefreshProtection(value);
            if(!p.Equals(primary,StringComparison.OrdinalIgnoreCase)||refreshed) {
                try {
                    value.SaveTo(primary);
                    if(p.Equals(recovery,StringComparison.OrdinalIgnoreCase))File.Delete(recovery);
                } catch(IOException) {if(!persistDurable)throw;} catch(UnauthorizedAccessException) {if(!persistDurable)throw;}
            }
            if(valid) {
                try {value.SaveRescueTo(Path.Combine(Path.GetDirectoryName(primary),"settings.rescue.json"));} catch(IOException) {} catch(UnauthorizedAccessException) {}
                if(persistDurable)try {SaveDurableRecovery(value);LastRecoveryStatus="ready";} catch(Exception e) {LastRecoveryStatus="error: "+e.Message;}
            }
            return value;
        }
        foreach(var p in paths.Distinct(StringComparer.OrdinalIgnoreCase)) {
            try {
                if(!File.Exists(p))continue;
                var value=Json.ReadSettings(File.ReadAllText(p));
                if(value==null)throw new InvalidDataException("Configuración vacía.");
                if(CredentialsReadable(value)) {
                    return Finish(value,p);
                }
                if(incomplete==null){incomplete=value;incompletePath=p;}
            } catch(Exception e){lastError=e;}
        }
        if(durable!=null&&CredentialsReadable(durable))return Finish(durable,"Windows Credential Manager recovery");
        if(incomplete!=null)return Finish(incomplete,incompletePath);
        if(lastError!=null)throw new IOException("No se pudo leer la configuración guardada: "+lastError.Message,lastError);
        LastLoadSource="";
        return new Settings();
    }
    public void Save() {var primary=Path.Combine(ConfigFolder,"settings.json");SaveTo(primary);if(CredentialsReadable(this)){SaveRescueTo(Path.Combine(ConfigFolder,"settings.rescue.json"));SaveDurableRecovery(this);}}
    internal void SaveTo(string p) {
        // Write two independently replaceable valid copies. File.Replace cannot
        // safely use settings.json.bak as both the recovery source and the backup
        // destination while repairing settings.json.
        SaveRescueTo(p);
        SaveRescueTo(p+".bak");
    }
    internal void SaveRescueTo(string p) {
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        string temporary=p+".tmp";
        File.WriteAllText(temporary,Json.Encode(this),Encoding.UTF8);
        if(File.Exists(p))File.Replace(temporary,p,null);else File.Move(temporary,p);
    }
    public void Validate() {
        Uri u; if(!Uri.TryCreate(Database,UriKind.Absolute,out u) || u.Scheme!="https" || !(u.Host.EndsWith(".firebasedatabase.app")||u.Host.EndsWith(".firebaseio.com")) || u.AbsolutePath!="/" || u.Query!="" || u.UserInfo!="" || !u.IsDefaultPort) throw new Exception("Introduce la raíz HTTPS de Firebase, sin /encender.json ni parámetros.");
        IPAddress ip; if(!IPAddress.TryParse(Target,out ip) || ip.AddressFamily!=AddressFamily.InterNetwork) throw new Exception("La dirección de la torre debe ser una IPv4 de Tailscale.");
        if(BasePort<1024 || BasePort>65514) throw new Exception("Puerto base fuera de rango.");
        if(WaitSeconds<30 || WaitSeconds>600) throw new Exception("La espera debe estar entre 30 y 600 segundos.");
        if(string.IsNullOrWhiteSpace(App)||App.IndexOfAny(new[]{'"','\r','\n'})>=0) throw new Exception("Nombre de aplicación de Moonlight no válido.");
        if(!Legacy && (string.IsNullOrWhiteSpace(ApiKey)||string.IsNullOrWhiteSpace(Email)||string.IsNullOrEmpty(Password))) throw new Exception("El firmware v2 requiere API key, correo y contraseña de Firebase Authentication.");
    }
}
public class Firebase : IDisposable {
    readonly Settings cfg; readonly HttpClient client; string token=""; DateTime validUntil;
    public Firebase(Settings c) : this(c,new HttpClientHandler()) {}
    public Firebase(Settings c,HttpMessageHandler handler) { cfg=c; client=new HttpClient(handler); client.Timeout=TimeSpan.FromSeconds(8); }
    async Task Authenticate(CancellationToken ct) {
        if(cfg.Legacy || (token!="" && DateTime.UtcNow<validUntil)) return;
        try {using(var req=new HttpRequestMessage(HttpMethod.Post,"https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key="+Uri.EscapeDataString(cfg.ApiKey))) {
            req.Content=new StringContent(Json.Encode(new {email=cfg.Email,password=cfg.Password,returnSecureToken=true}),Encoding.UTF8,"application/json");
            using(var r=await client.SendAsync(req,ct)) { if(!r.IsSuccessStatusCode) throw new Exception("Firebase Auth: HTTP "+(int)r.StatusCode+". Revisa credenciales y proveedor Email/Password."); var d=Json.Read(await r.Content.ReadAsStringAsync()); token=Json.Str(d,"idToken"); if(token=="") throw new Exception("Firebase Auth no devolvió un token."); validUntil=DateTime.UtcNow.AddMinutes(50); }
        }}catch(TaskCanceledException){ct.ThrowIfCancellationRequested();throw new Exception("Firebase Auth: tiempo de espera agotado (8 s).");}
    }
    public async Task<string> Request(string path,string method,object body,CancellationToken ct) {
        await Authenticate(ct);
        using(var req=new HttpRequestMessage(new HttpMethod(method),cfg.Database.TrimEnd('/')+"/"+path+".json"+(token==""?"":"?auth="+Uri.EscapeDataString(token)))) {
            if(body!=null) req.Content=new StringContent(Json.Encode(body),Encoding.UTF8,"application/json");
            try { using(var r=await client.SendAsync(req,ct)) { if(!r.IsSuccessStatusCode) { if(r.StatusCode==HttpStatusCode.Unauthorized) token=""; throw new Exception("Firebase "+method+" /"+path+": HTTP "+(int)r.StatusCode+". "+(r.StatusCode==HttpStatusCode.Unauthorized||r.StatusCode==HttpStatusCode.Forbidden?"Revisa autenticación y reglas.":"Reintenta y comprueba la conexión.")); } return await r.Content.ReadAsStringAsync(); } }
            catch(HttpRequestException) { throw new Exception("Firebase inaccesible: comprueba Internet, DNS y TLS."); }
            catch(TaskCanceledException) { ct.ThrowIfCancellationRequested(); throw new Exception("Firebase: tiempo de espera agotado (8 s)."); }
        }
    }
    public async Task<string> QueueWake(string id,CancellationToken ct,bool reusePending=false) {
        await Authenticate(ct);
        string url=cfg.Database.TrimEnd('/')+"/bridge/command.json?auth="+Uri.EscapeDataString(token);
        string etag;
        using(var get=new HttpRequestMessage(HttpMethod.Get,url)) {
            get.Headers.Add("X-Firebase-ETag","true");
            using(var r=await client.SendAsync(get,ct)) {
                if(!r.IsSuccessStatusCode)throw new Exception("No se pudo consultar la orden pendiente: HTTP "+(int)r.StatusCode);
                var old=Json.Read(await r.Content.ReadAsStringAsync());double created=Json.Num(old,"createdAt");
                if(created>0 && Json.Now-created<90000) {
                    Guid parsed;
                    if(reusePending && Json.Str(old,"action")=="wake" && Guid.TryParseExact(Json.Str(old,"id"),"N",out parsed)) return Json.Str(old,"id");
                    throw new Exception("Hay una orden reciente. Espera a que cumpla 90 s antes de enviar otra; usa Diagnosticar para consultar su acuse.");
                }
                // Firebase returns opaque values such as null_etag without HTTP quotes.
                // The typed .NET ETag property discards those; preserve the raw value.
                IEnumerable<string> values;
                etag=r.Headers.TryGetValues("ETag",out values)?values.FirstOrDefault():null;
                if(etag==null)throw new Exception("Firebase no devolvió ETag; no se envía una orden sin control de concurrencia.");
            }
        }
        using(var put=new HttpRequestMessage(HttpMethod.Put,url)) {
            put.Headers.TryAddWithoutValidation("if-match",etag);put.Content=new StringContent(Json.Encode(new{id=id,action="wake",createdAt=Json.Timestamp,ttlSec=90}),Encoding.UTF8,"application/json");
            using(var r=await client.SendAsync(put,ct)) {if(!r.IsSuccessStatusCode)throw new Exception(r.StatusCode==HttpStatusCode.PreconditionFailed?"Otra instancia envió una orden antes. Usa Diagnosticar.":"Firebase rechazó la orden: HTTP "+(int)r.StatusCode);}
        }
        return id;
    }
    public void Dispose() { client.Dispose(); }
}
public class Probe {
    public bool Open; public string Detail;
    public static async Task<Probe> Tcp(string host,int port,CancellationToken ct) {
        using(var c=new TcpClient()) {
            try {
                var task=c.ConnectAsync(host,port);
                if(await Task.WhenAny(task,Task.Delay(1800,ct))!=task) { c.Close(); Observe(task); ct.ThrowIfCancellationRequested(); return new Probe{Detail=port+": sin respuesta (timeout)"}; }
                await task; return new Probe{Open=true,Detail=port+": abierto"};
            } catch(OperationCanceledException) { throw; } catch(SocketException e) { return new Probe{Detail=port+": "+e.SocketErrorCode}; }
        }
    }
    static async void Observe(Task t) { try {await t;} catch {} }
    public static async Task<string> Run(string exe,string args,CancellationToken ct) {
        if(!File.Exists(exe)) return "Ejecutable no encontrado: "+Path.GetFileName(exe);
        using(var p=new Process()) {
            p.StartInfo=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            p.Start(); var stdout=p.StandardOutput.ReadToEndAsync(); var stderr=p.StandardError.ReadToEndAsync();
            var watch=Stopwatch.StartNew();
            try { while(!p.HasExited) { ct.ThrowIfCancellationRequested(); if(watch.Elapsed.TotalSeconds>8) { p.Kill(); return "Tiempo de espera agotado al consultar Tailscale."; } await Task.Delay(100,ct); } }
            finally { if(!p.HasExited) p.Kill(); }
            string output=await stdout, error=await stderr; return p.ExitCode==0?output:"Tailscale: "+error.Trim();
        }
    }
}
public class Snapshot {
    public bool Cloud, Fresh, LanKnown, LanOpen, Ready;
    public string CloudText="Sin comprobar",EspText="Sin comprobar",EspHealthKey="",LanText="Sin comprobar",TailText="Sin comprobar",PortsText="Sin comprobar",Advice="";
    public string AckId="", AckState="";
    public long AckTime;
    public static bool IsFresh(Dictionary<string,object> d,long now) { double stamp=Json.Num(d,"updatedAt"); return stamp>0 && now-stamp>=-10000 && now-stamp<=45000; }
    public void Explain(bool legacy) {
        if(Ready&&Fresh&&LanKnown&&!LanOpen) Advice="Sunshine responde por Tailscale, pero el ESP32 no alcanza sus puertos en casa. Moonlight puede funcionar; revisa la IP local configurada y el firewall si persiste la diferencia.";
        else if(Ready) Advice="Los tres puertos TCP responden. Puedes abrir Moonlight. El vídeo, UDP y el emparejamiento se verifican al iniciar la sesión.";
        else if(Fresh&&LanOpen) Advice="El ESP32 detecta un servicio de la torre en casa, pero el portátil no alcanza todos los puertos. Revisa Tailscale, sus permisos y el firewall de la torre.";
        else if(Fresh&&LanKnown) Advice="El ESP32 está activo, pero los puertos de Sunshine no responden en la LAN. Puede ser arranque, Sunshine detenido, firewall o IP local incorrecta; no demuestra que la torre esté apagada.";
        else if(legacy) Advice="El firmware actual no informa del estado de la torre. El ping por sí solo no permite determinar si está apagada. Revisa el detalle de Tailscale y los puertos.";
        else if(!Cloud) Advice="No se puede leer Firebase. Los puertos remotos se comprueban por separado; un fallo de Firebase no implica un fallo de la torre.";
        else Advice="No hay telemetría reciente del ESP32. Comprueba su alimentación, Wi-Fi, acceso a Firebase y firmware; no es posible localizar el fallo dentro de casa.";
    }
}
public class Diagnostics {
    readonly Settings c; readonly Firebase f;
    public Diagnostics(Settings cfg,Firebase fb) { c=cfg; f=fb; }
    async Task Cloud(Snapshot s,CancellationToken ct) {
        try {
            if(c.Legacy) { string state=await f.Request("encender","GET",null,ct); s.Cloud=true; s.CloudText="Accesible · modo anterior"; s.EspText="Sin telemetría en firmware anterior"; s.LanText="No disponible con firmware anterior"; if(state.Trim()=="true") s.CloudText+=" · orden pendiente"; return; }
            var d=Json.Read(await f.Request("bridge/status","GET",null,ct)); s.Cloud=true; s.CloudText="Lectura autenticada correcta"; s.Fresh=Snapshot.IsFresh(d,Json.Now);
            double age=(Json.Now-Json.Num(d,"updatedAt"))/1000;
            s.EspText=s.Fresh?"Activo · Wi-Fi "+Json.Str(d,"rssi")+" dBm · reinicio "+Json.Str(d,"resetReason")+" · "+Math.Max(0,(int)age)+" s":"Sin señal reciente (más de 45 s o sin datos)";
            var lan=Json.Obj(d,"lan"); s.LanKnown=s.Fresh&&Json.Bool(lan,"configured"); s.LanOpen=s.LanKnown&&(Json.Bool(lan,"http")||Json.Bool(lan,"https")||Json.Bool(lan,"rtsp"));
            s.LanText=!s.Fresh?"Lecturas antiguas: no se usan para diagnosticar":!s.LanKnown?"Falta configurar la IP local en el ESP32":Json.Str(lan,"ip")+" · HTTP "+Mark(Json.Bool(lan,"http"))+" · HTTPS "+Mark(Json.Bool(lan,"https"))+" · RTSP "+Mark(Json.Bool(lan,"rtsp"));
            s.EspText+=" · firmware "+Json.Str(d,"version")+" · uptime "+Json.Str(d,"uptimeSec")+" s";
            if(s.Fresh)s.EspText+=" · memoria "+Json.Str(d,"freeHeap")+" B · fallos HTTP "+Json.Str(d,"httpFailures")+" · último HTTP "+Json.Str(d,"lastHttp")+" · paquetes WOL "+Json.Str(d,"wolPackets");
            if(Json.Str(d,"lastError")!="") s.EspText+=" · último fallo: "+Json.Str(d,"lastError");
            s.EspHealthKey=string.Join("|",new[]{Json.Str(d,"version"),Json.Str(d,"resetReason"),Json.Str(d,"httpFailures"),Json.Str(d,"lastHttp"),Json.Str(d,"wolPackets"),Json.Str(d,"lastError")});
            var ack=Json.Read(await f.Request("bridge/ack","GET",null,ct)); s.AckId=Json.Str(ack,"id"); s.AckState=Json.Str(ack,"state"); s.AckTime=(long)Json.Num(ack,"updatedAt");
        } catch(OperationCanceledException) { throw; } catch(Exception e) { s.CloudText=e.Message; }
    }
    static string Mark(bool b) {return b?"abierto":"sin respuesta";}
    async Task Tail(Snapshot s,CancellationToken ct) {
        try {
            string raw=await Probe.Run(c.Tailscale,"status --json",ct);
            if(!raw.TrimStart().StartsWith("{")) { s.TailText=raw; return; }
            var d=Json.Read(raw); string backend=Json.Str(d,"BackendState"); s.TailText="Portátil: "+backend;
            bool found=false;
            foreach(var entry in Json.Obj(d,"Peer")) {
                var peer=entry.Value as Dictionary<string,object>; if(peer==null)continue;
                object ips; if(!peer.TryGetValue("TailscaleIPs",out ips))continue; var list=ips as IEnumerable; if(list==null)continue;
                foreach(var ip in list) if(Convert.ToString(ip)==c.Target) { found=true; s.TailText+=" · torre "+(Json.Bool(peer,"Online")?"anunciada en línea":"no anunciada en línea")+" (no prueba conexión directa)"; }
            }
            if(!found) s.TailText+=" · torre no encontrada en los peers visibles";
        } catch(OperationCanceledException) {throw;} catch(Exception e) {s.TailText="No se pudo consultar Tailscale: "+e.GetType().Name;}
    }
    async Task Ports(Snapshot s,CancellationToken ct) { var r=await Task.WhenAll(Probe.Tcp(c.Target,c.BasePort-5,ct),Probe.Tcp(c.Target,c.BasePort,ct),Probe.Tcp(c.Target,c.BasePort+21,ct)); s.Ready=r.All(x=>x.Open); s.PortsText=string.Join("   |   ",r.Select(x=>x.Detail)); }
    public async Task<Snapshot> Read(CancellationToken ct) { var s=new Snapshot(); await Task.WhenAll(Cloud(s,ct),Tail(s,ct),Ports(s,ct)); s.Explain(c.Legacy); return s; }
}
}
