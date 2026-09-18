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
public class Settings {
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
    public string Password { get { return string.IsNullOrEmpty(ProtectedPassword) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedPassword),null,DataProtectionScope.CurrentUser)); } set { ProtectedPassword=string.IsNullOrEmpty(value)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser)); } }
#if NET8_0_OR_GREATER
    [JsonIgnore]
#else
    [ScriptIgnore]
#endif
    public string DevicePassword { get { return string.IsNullOrEmpty(ProtectedDevicePassword) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedDevicePassword),null,DataProtectionScope.CurrentUser)); } set { ProtectedDevicePassword=string.IsNullOrEmpty(value)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser)); } }
#if NET8_0_OR_GREATER
    [JsonIgnore]
#else
    [ScriptIgnore]
#endif
    public string WifiPassword { get { return string.IsNullOrEmpty(ProtectedWifiPassword) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedWifiPassword),null,DataProtectionScope.CurrentUser)); } set { ProtectedWifiPassword=string.IsNullOrEmpty(value)?"":Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value),null,DataProtectionScope.CurrentUser)); } }
    public static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TorreRemota"); } }
    public static string ConfigFolder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"TorreRemota"); } }
    public static string LastLoadSource { get; private set; } = "";
    static bool CredentialsReadable(Settings s) {
        if(string.IsNullOrWhiteSpace(s.ApiKey)||string.IsNullOrWhiteSpace(s.Email)||string.IsNullOrWhiteSpace(s.ProtectedPassword))return false;
        try{return !string.IsNullOrWhiteSpace(s.Password);}catch{return false;}
    }
    public static Settings Load() {
        var profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var primary=Path.Combine(ConfigFolder,"settings.json");
        var recovery=Path.Combine(profile,"Desktop","TorreRemota.recuperacion.json");
        var paths=new[]{primary,primary+".bak",Path.Combine(Folder,"settings.json"),
            Path.Combine(profile,"AppData","Local","TorreRemota","settings.json"),recovery};
        return LoadFromPaths(primary,recovery,paths);
    }
    internal static Settings LoadFromPaths(string primary,string recovery,IEnumerable<string> paths) {
        Exception lastError=null;
        Settings incomplete=null;string incompletePath="";
        Settings Finish(Settings value,string p) {
            LastLoadSource=p;
            if(!p.Equals(primary,StringComparison.OrdinalIgnoreCase)) {
                try {
                    value.SaveTo(primary);
                    if(p.Equals(recovery,StringComparison.OrdinalIgnoreCase))File.Delete(recovery);
                } catch(IOException) {} catch(UnauthorizedAccessException) {}
            }
            return value;
        }
        foreach(var p in paths.Distinct(StringComparer.OrdinalIgnoreCase)) {
            try {
                if(!File.Exists(p))continue;
                var value=Json.ReadSettings(File.ReadAllText(p));
                if(value==null)throw new InvalidDataException("Configuración vacía.");
                if(CredentialsReadable(value))return Finish(value,p);
                if(incomplete==null){incomplete=value;incompletePath=p;}
            } catch(Exception e){lastError=e;}
        }
        if(incomplete!=null)return Finish(incomplete,incompletePath);
        if(lastError!=null)throw new IOException("No se pudo leer la configuración guardada: "+lastError.Message,lastError);
        LastLoadSource="";
        return new Settings();
    }
    public void Save() {SaveTo(Path.Combine(ConfigFolder,"settings.json"));}
    internal void SaveTo(string p) {
        Directory.CreateDirectory(Path.GetDirectoryName(p));
        File.WriteAllText(p+".tmp",Json.Encode(this),Encoding.UTF8);
        if(File.Exists(p))File.Replace(p+".tmp",p,p+".bak");
        else {File.Move(p+".tmp",p);File.Copy(p,p+".bak",true);}
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
