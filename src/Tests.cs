using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace TorreRemota {
public static class Tests {
    public static async Task<int> Diagnose(bool configured=false) {try{var cfg=configured?Settings.Load():new Settings();using(var f=new Firebase(cfg)){var s=await new Diagnostics(cfg,f).Read(CancellationToken.None);File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostico-real.txt"),new[]{DateTime.Now.ToString("O"),s.CloudText,s.EspText,s.LanText,s.TailText,s.PortsText,s.Advice});return 0;}}catch(Exception e){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"diagnostico-real.txt"),e.ToString());return 1;}}
    public static async Task<int> WakeRoundTrip() {
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"prueba-wol.txt");
        try {var cfg=Settings.Load();cfg.Validate();if(cfg.Legacy)throw new Exception("Esta prueba requiere v2.");using(var fb=new Firebase(cfg))using(var timeout=new CancellationTokenSource(80000)){
            string id=Guid.NewGuid().ToString("N");await fb.QueueWake(id,timeout.Token);File.WriteAllText(path,"Orden aceptada: "+id+Environment.NewLine);
            while(true){await Task.Delay(3000,timeout.Token);var ack=Json.Read(await fb.Request("bridge/ack","GET",null,timeout.Token));if(Json.Str(ack,"id")==id){File.AppendAllText(path,"Acuse: "+Json.Str(ack,"state")+Environment.NewLine);return Json.Str(ack,"state")=="sent"?0:1;}}
        }}catch(Exception e){File.AppendAllText(path,e.GetType().Name+": "+e.Message+Environment.NewLine);return 1;}
    }
    static readonly List<string> results=new List<string>();
    static void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);results.Add("PASS: "+name);}
    sealed class FakeHttp : HttpMessageHandler {
        public Func<HttpRequestMessage,HttpResponseMessage> Handler;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct) {ct.ThrowIfCancellationRequested();return Task.FromResult(Handler(request));}
    }
    public static async Task<int> Run() {
        try {
            long now=Json.Now;Check(!Snapshot.IsFresh(new Dictionary<string,object>(),now),"Sin heartbeat nunca se considera activo");
            Check(Snapshot.IsFresh(new Dictionary<string,object>{{"updatedAt",now-12000}},now),"Heartbeat reciente");
            Check(!Snapshot.IsFresh(new Dictionary<string,object>{{"updatedAt",now-46000}},now),"Heartbeat antiguo");
            Check(!Snapshot.IsFresh(new Dictionary<string,object>{{"updatedAt",now+20000}},now),"Reloj futuro no produce falso positivo");
            var s=new Snapshot{Fresh=true,LanKnown=true,LanOpen=true};s.Explain(false);Check(s.Advice.Contains("permisos"),"LAN disponible y remoto fallido orienta a Tailscale/firewall");
            s=new Snapshot{Fresh=true,LanKnown=true,LanOpen=false};s.Explain(false);Check(s.Advice.Contains("no demuestra"),"LAN sin puertos no se presenta como apagado");
            s=new Snapshot{Ready=true,Fresh=true,LanKnown=true,LanOpen=false};s.Explain(false);Check(s.Advice.Contains("IP local")&&s.Advice.Contains("Moonlight"),"Discrepancia entre Tailscale y LAN visible");
            s=new Snapshot{Ready=true,Cloud=false};s.Explain(false);Check(s.Advice.Contains("UDP"),"Firebase caído no invalida puertos y no se garantiza vídeo");
            var cfg=new Settings{Database="https://example-default-rtdb.europe-west1.firebasedatabase.app",Target="100.64.0.1",Legacy=true};cfg.Validate();Check(true,"Configuración de prueba válida");
            Check(Json.ReadSettings("{}").Language=="en"&&Json.ReadSettings(Json.Encode(new Settings{Language="es"})).Language=="es","English default and saved Spanish preference");
            Check(Json.ReadSettings("{}").Theme=="dark"&&Json.ReadSettings(Json.Encode(new Settings{Theme="light"})).Theme=="light","Dark default and saved light preference");
            bool rejected=false;try{new Settings{Database="http://example.com"}.Validate();}catch{rejected=true;}Check(rejected,"Rechaza URL insegura o ajena a Firebase");
            cfg.Password="test-secret";Check(cfg.Password=="test-secret"&&!Json.Encode(cfg).Contains("test-secret"),"DPAPI y ausencia de contraseña en JSON");
#if NET8_0_OR_GREATER
            var onboarding=new Settings{Database="https://demo-default-rtdb.europe-west1.firebasedatabase.app",Target="100.64.0.10",ApiKey="demo-api-key",Email="client@example.invalid",DeviceEmail="device@example.invalid",DeviceUid="device_123456",LaptopUid="client_123456",WifiSsid="TEST-NET",TowerLanIp="192.168.1.25",TowerMac="AA:BB:CC:DD:EE:FF"};
            onboarding.Password="client-secret";onboarding.DevicePassword="device-secret";onboarding.WifiPassword="wifi-secret";
            var payload=Json.Read(Provisioning.BuildDeviceJson(onboarding));
            Check(Json.Str(payload,"towerIp")=="192.168.1.25"&&Json.Str(payload,"mac")=="AA:BB:CC:DD:EE:FF"&&Json.Str(payload,"email")=="device@example.invalid","Provisionamiento USB usa los datos del usuario y de la torre");
            Check(!Json.Encode(onboarding).Contains("wifi-secret")&&!Json.Encode(onboarding).Contains("device-secret"),"Wi-Fi y dispositivo permanecen cifrados en ajustes");
            string rules=Provisioning.Rules(onboarding.LaptopUid,onboarding.DeviceUid);
            Check(rules.Contains("client_123456")&&rules.Contains("device_123456")&&!rules.Contains("UID_PORTATIL"),"Reglas Firebase generadas para usuarios separados");
            onboarding.TowerMac="invalid";rejected=false;try{Provisioning.BuildDeviceJson(onboarding);}catch{rejected=true;}Check(rejected,"Rechaza MAC inválida antes de flashear");
            string image=Esp32Flasher.ExtractFirmware();
            byte[] imageBytes=File.ReadAllBytes(image);
            string imageText=System.Text.Encoding.Latin1.GetString(imageBytes);
            Check(imageBytes.Length>=1000000&&imageBytes.Length<=4194304&&imageText.Contains("TRCFG1:")&&imageText.Contains("TRPONG1:3.0.0:"),
                "El EXE incluye firmware ESP32 v3 y protocolo de configuración USB");
#endif
            string configTestDir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-config-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(configTestDir);
            try {
                string primary=Path.Combine(configTestDir,"settings.json"),backup=primary+".bak",recovery=Path.Combine(configTestDir,"recuperacion.json");
                var valid=new Settings{Legacy=false,ApiKey="test-key",Email="test@example.invalid"};valid.Password="test-password";
                File.WriteAllText(primary,"{datos rotos");File.WriteAllText(backup,Json.Encode(valid));
                var loaded=Settings.LoadFromPaths(primary,recovery,new[]{primary,backup,recovery});
                Check(loaded.Password=="test-password"&&Json.ReadSettings(File.ReadAllText(primary)).ApiKey=="test-key","Recupera credenciales desde la copia si el archivo principal está dañado");
                File.WriteAllText(primary,Json.Encode(new Settings()));File.WriteAllText(backup,Json.Encode(valid));
                loaded=Settings.LoadFromPaths(primary,recovery,new[]{primary,backup,recovery});
                Check(loaded.Password=="test-password"&&Json.ReadSettings(File.ReadAllText(primary)).Email=="test@example.invalid","Prefiere la copia completa si el archivo principal perdió credenciales");
                File.Delete(primary);File.Delete(backup);File.WriteAllText(recovery,Json.Encode(valid));
                loaded=Settings.LoadFromPaths(primary,recovery,new[]{primary,backup,recovery});
                Check(loaded.Password=="test-password"&&File.Exists(primary)&&File.Exists(backup)&&!File.Exists(recovery),"Importa una copia de rescate y conserva respaldo local");
            } finally {foreach(var file in Directory.GetFiles(configTestDir))File.Delete(file);Directory.Delete(configTestDir);}
            var handler=new FakeHttp{Handler=r=>new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("denied")}};
            using(var fb=new Firebase(new Settings{Legacy=true,Database="https://demo-default-rtdb.europe-west1.firebasedatabase.app"},handler)){rejected=false;try{await fb.Request("encender","PUT",true,CancellationToken.None);}catch(Exception e){rejected=e.Message.Contains("403");}Check(rejected,"Escritura Firebase denegada informa HTTP 403");}
            handler=new FakeHttp{Handler=r=>new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("null")}};
            using(var fb=new Firebase(new Settings{Legacy=true,Database="https://demo-default-rtdb.europe-west1.firebasedatabase.app"},handler)){Check(await fb.Request("encender","GET",null,CancellationToken.None)=="null","Nodo vacío no es confirmación del dispositivo");}
            var modern=new Settings{Legacy=false,Database="https://demo-default-rtdb.europe-west1.firebasedatabase.app",Target="100.64.0.10",ApiKey="test",Email="test@example.invalid"};modern.Password="test";
            int writes=0;
            handler=new FakeHttp{Handler=r=>{
                if(r.RequestUri.Host=="identitytoolkit.googleapis.com")return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"idToken\":\"test-token\"}")};
                if(r.Method==HttpMethod.Get){var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("null")};response.Headers.ETag=new System.Net.Http.Headers.EntityTagHeaderValue("\"test\"");return response;}
                writes++;Check(r.Headers.Contains("if-match"),"Orden protegida por ETag");return new HttpResponseMessage(HttpStatusCode.PreconditionFailed){Content=new StringContent("null")};
            }};
            using(var fb=new Firebase(modern,handler)){rejected=false;try{await fb.QueueWake(new string('a',32),CancellationToken.None);}catch(Exception e){rejected=e.Message.Contains("Otra instancia");}Check(rejected&&writes==1,"Colisión no reintenta ni duplica la orden");}
            handler=new FakeHttp{Handler=r=>{
                if(r.RequestUri.Host=="identitytoolkit.googleapis.com")return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"idToken\":\"test-token\"}")};
                if(r.Method==HttpMethod.Get){var response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("null")};response.Headers.TryAddWithoutValidation("ETag","null_etag");return response;}
                System.Collections.Generic.IEnumerable<string> values;Check(r.Headers.TryGetValues("if-match",out values)&&string.Join("",values)=="null_etag","Preserva el ETag real de Firebase sin comillas");return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")};
            }};
            using(var fb=new Firebase(modern,handler)){await fb.QueueWake(new string('b',32),CancellationToken.None);Check(true,"Primera orden sobre nodo vacío aceptada");}
            handler=new FakeHttp{Handler=r=>{
                if(r.RequestUri.Host=="identitytoolkit.googleapis.com")return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"idToken\":\"test-token\"}")};
                Check(r.Method==HttpMethod.Get,"Reutilizar orden reciente no provoca otra escritura");return new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Json.Encode(new{id=new string('c',32),action="wake",createdAt=Json.Now,ttlSec=90}))};
            }};
            using(var fb=new Firebase(modern,handler)){Check(await fb.QueueWake(new string('d',32),CancellationToken.None,true)==new string('c',32),"Reintentar conexión se une a la orden activa sin duplicar WOL");}
            var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            Check((await Probe.Tcp("127.0.0.1",port,CancellationToken.None)).Open,"Detección TCP real en puerto abierto");listener.Stop();
            Check(!(await Probe.Tcp("127.0.0.1",port,CancellationToken.None)).Open,"Puerto cerrado no se presenta disponible");
#if NET8_0_OR_GREATER
            Check(MoonlightSession.Parse("Received first video packet after 400 ms",true).Phase==MoonlightPhase.Video,
                "Moonlight con vídeo y proceso activo confirma la sesión");
            Check(MoonlightSession.Parse("Received first video packet after 400 ms\nStopping video stream...",true).Phase==MoonlightPhase.Finished,
                "Moonlight finalizado no se presenta como conectado");
            Check(MoonlightSession.Parse("Connection terminated: 0",false).Phase==MoonlightPhase.Finished,
                "Moonlight desconectado no se presenta como sesión activa");
#endif
            results.Add("Todas las pruebas han pasado.");File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),results);return 0;
        }catch(Exception e){results.Add(e.ToString());File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-results.txt"),results);return 1;}
    }
}
}
