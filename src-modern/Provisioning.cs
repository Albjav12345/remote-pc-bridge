using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TorreRemota {
public sealed class FirebaseAccount {
    public string Email="",Password="",Uid="";
}
public static class Provisioning {
    static readonly Regex MacPattern=new Regex("^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$",RegexOptions.Compiled);
    static readonly Regex UidPattern=new Regex("^[A-Za-z0-9_-]{10,128}$",RegexOptions.Compiled);

    public static string GeneratePassword() {
        byte[] bytes=new byte[32];RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    }
    public static string GenerateEmail(string role) {
        byte[] bytes=new byte[8];RandomNumberGenerator.Fill(bytes);
        return role+"-"+Convert.ToHexString(bytes).ToLowerInvariant()+"@remote-pc-bridge.invalid";
    }
    public static void ValidateBase(Settings s) {
        var basic=Json.ReadSettings(Json.Encode(s));basic.Legacy=true;basic.Validate();
        IPAddress ip;
        if(!IPAddress.TryParse(s.TowerLanIp,out ip)||ip.AddressFamily!=AddressFamily.InterNetwork||ip.Equals(IPAddress.Any))throw new Exception("Introduce la IP local de la torre (por ejemplo, 192.168.1.50).");
        if(!MacPattern.IsMatch(s.TowerMac??""))throw new Exception("La MAC de la tarjeta Ethernet debe tener formato AA:BB:CC:DD:EE:FF.");
        if(string.IsNullOrWhiteSpace(s.WifiSsid)||Encoding.UTF8.GetByteCount(s.WifiSsid)>32)throw new Exception("El nombre Wi-Fi debe tener entre 1 y 32 bytes.");
        string wifi=s.WifiPassword;
        if(wifi.Length!=0 && (wifi.Length<8||wifi.Length>64))throw new Exception("La contraseña Wi-Fi debe tener de 8 a 63 caracteres (o 64 hexadecimales). Para red abierta, déjala vacía.");
    }
    public static string BuildDeviceJson(Settings s) {
        ValidateBase(s);s.Validate();
        if(string.IsNullOrWhiteSpace(s.DeviceEmail)||string.IsNullOrWhiteSpace(s.DevicePassword)||!UidPattern.IsMatch(s.DeviceUid??""))throw new Exception("Prepara primero la cuenta técnica del ESP32 y sus reglas de Firebase.");
        return Json.Encode(new {wifiSsid=s.WifiSsid,wifiPassword=s.WifiPassword,firebaseHost=new Uri(s.Database).Host,apiKey=s.ApiKey,email=s.DeviceEmail,password=s.DevicePassword,uid=s.DeviceUid,towerIp=s.TowerLanIp,mac=s.TowerMac.ToUpperInvariant(),basePort=s.BasePort});
    }
    public static string Rules(string laptopUid,string deviceUid) {
        if(!UidPattern.IsMatch(laptopUid??"")||!UidPattern.IsMatch(deviceUid??"")||laptopUid==deviceUid)throw new Exception("Faltan dos UID distintos y válidos para generar las reglas.");
        using(var stream=typeof(Provisioning).Assembly.GetManifestResourceStream("TorreRemota.FirebaseRules")) {
            if(stream==null)throw new Exception("No se encontró la plantilla de reglas incluida en la aplicación.");
            using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd().Replace("UID_PORTATIL",laptopUid).Replace("UID_ESP32",deviceUid);
        }
    }
    static async Task<FirebaseAccount> Auth(string apiKey,string email,string password,string method,CancellationToken ct) {
        if(string.IsNullOrWhiteSpace(apiKey)||string.IsNullOrWhiteSpace(email)||string.IsNullOrWhiteSpace(password))throw new Exception("Faltan API key, correo o contraseña.");
        using(var client=new HttpClient{Timeout=TimeSpan.FromSeconds(15)})
        using(var request=new HttpRequestMessage(HttpMethod.Post,"https://identitytoolkit.googleapis.com/v1/accounts:"+method+"?key="+Uri.EscapeDataString(apiKey))) {
            request.Content=new StringContent(Json.Encode(new {email,password,returnSecureToken=true}),Encoding.UTF8,"application/json");
            using(var response=await client.SendAsync(request,ct)) {
                string body=await response.Content.ReadAsStringAsync(ct);
                if(!response.IsSuccessStatusCode) {
                    string reason="HTTP "+(int)response.StatusCode;
                    try {reason=Json.Str(Json.Obj(Json.Read(body),"error"),"message");}catch{}
                    throw new Exception("Firebase Authentication: "+reason+". Comprueba la API key y que Email/Password esté habilitado.");
                }
                string uid=Json.Str(Json.Read(body),"localId");
                if(!UidPattern.IsMatch(uid))throw new Exception("Firebase no devolvió un UID válido.");
                return new FirebaseAccount{Email=email,Password=password,Uid=uid};
            }
        }
    }
    public static Task<FirebaseAccount> SignIn(string apiKey,string email,string password,CancellationToken ct) {return Auth(apiKey,email,password,"signInWithPassword",ct);}
    public static Task<FirebaseAccount> Create(string apiKey,string email,string password,CancellationToken ct) {return Auth(apiKey,email,password,"signUp",ct);}
}
}
