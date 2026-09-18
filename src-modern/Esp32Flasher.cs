using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TorreRemota {
public static class Esp32Flasher {
    const string ToolUrl="https://github.com/espressif/esptool/releases/download/v5.1.0/esptool-v5.1.0-windows-amd64.zip";
    const string ZipHash="F68A8F7728ADFC59CD60F9424928199E76EAC66372C7BDC23898AA32753A437A";
    const string ExeHash="A5AEEDAC428EEE8B45E55DD5AD8A0F4F62ADF8BEE74F9D02C81E3E088219EEE9";
    public static string[] Ports() {return SerialPort.GetPortNames().OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();}
    public static string WorkFolder {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RemotePcBridge","flash");}}
    static void CheckPort(string port) {if(!Regex.IsMatch(port??"","^COM[1-9][0-9]{0,3}$",RegexOptions.IgnoreCase))throw new Exception("Selecciona un puerto COM válido del ESP32.");}
    static string Hash(string path) {using(var sha=SHA256.Create())using(var file=File.OpenRead(path))return Convert.ToHexString(sha.ComputeHash(file));}
    public static async Task<string> EnsureTool(IProgress<string> progress,CancellationToken ct) {
        Directory.CreateDirectory(WorkFolder);
        string tool=Path.Combine(WorkFolder,"esptool.exe");
        if(File.Exists(tool)&&Hash(tool).Equals(ExeHash,StringComparison.OrdinalIgnoreCase))return tool;
        string zipPath=Path.Combine(WorkFolder,"esptool-v5.1.0.zip");
        if(!File.Exists(zipPath)||!Hash(zipPath).Equals(ZipHash,StringComparison.OrdinalIgnoreCase)) {
            progress?.Report("Descargando esptool 5.1.0 desde Espressif (60 MB)…");
            string temp=zipPath+".part";
            try {
                using(var client=new HttpClient{Timeout=Timeout.InfiniteTimeSpan})
                using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct)) {
                    timeout.CancelAfter(TimeSpan.FromMinutes(8));
                    using(var response=await client.GetAsync(ToolUrl,HttpCompletionOption.ResponseHeadersRead,timeout.Token)) {
                        response.EnsureSuccessStatusCode();
                        using(var input=await response.Content.ReadAsStreamAsync(timeout.Token))
                        using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,81920,true)) {
                            byte[] buffer=new byte[81920];long total=0,lastReport=0;int count;
                            while((count=await input.ReadAsync(buffer.AsMemory(0,buffer.Length),timeout.Token))>0) {
                                total+=count;if(total>70000000)throw new Exception("La descarga superó el tamaño esperado.");
                                await output.WriteAsync(buffer.AsMemory(0,count),timeout.Token);
                                if(total-lastReport>=5000000){lastReport=total;progress?.Report("Descargando esptool · "+(total/1000000)+" MB");}
                            }
                        }
                    }
                }
                if(!Hash(temp).Equals(ZipHash,StringComparison.OrdinalIgnoreCase))throw new Exception("El hash SHA-256 de esptool no coincide; no se ejecutará.");
                File.Move(temp,zipPath,true);
            } finally {if(File.Exists(temp))File.Delete(temp);}
        }
        progress?.Report("Verificando y preparando esptool…");
        using(var zip=ZipFile.OpenRead(zipPath)) {
            var entry=zip.Entries.SingleOrDefault(x=>x.Name=="esptool.exe");
            var license=zip.Entries.SingleOrDefault(x=>x.Name=="LICENSE");
            if(entry==null||license==null)throw new Exception("El paquete de esptool no tiene el contenido esperado.");
            string temp=tool+".part";entry.ExtractToFile(temp,true);
            if(!Hash(temp).Equals(ExeHash,StringComparison.OrdinalIgnoreCase)){File.Delete(temp);throw new Exception("El ejecutable de esptool no coincide con la versión verificada.");}
            File.Move(temp,tool,true);license.ExtractToFile(Path.Combine(WorkFolder,"LICENSE-esptool.txt"),true);
        }
        return tool;
    }
    public static string ExtractFirmware() {
        Directory.CreateDirectory(WorkFolder);
        string path=Path.Combine(WorkFolder,"RemotePcBridge-ESP32-v3.bin");
        using(var source=typeof(Esp32Flasher).Assembly.GetManifestResourceStream("TorreRemota.Firmware.Esp32")) {
            if(source==null)throw new Exception("Esta compilación no incluye el firmware genérico para ESP32-WROOM-32 de 4 MB.");
            using(var output=File.Create(path))source.CopyTo(output);
        }
        if(new FileInfo(path).Length<100000)throw new Exception("La imagen de firmware incluida está incompleta.");
        return path;
    }
    public static async Task Flash(string port,IProgress<string> progress,CancellationToken ct) {
        CheckPort(port);
        string tool=await EnsureTool(progress,ct),firmware=ExtractFirmware();
        var info=new ProcessStartInfo(tool){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string arg in new[]{"--chip","esp32","--port",port,"--baud","460800","--before","default-reset","--after","hard-reset","write-flash","--flash-mode","dio","--flash-size","4MB","0x0",firmware})info.ArgumentList.Add(arg);
        progress?.Report("Conectando con "+port+". Si se queda esperando, mantén BOOT hasta que comience la escritura.");
        using(var process=new Process{StartInfo=info}) {
            if(!process.Start())throw new Exception("No se pudo iniciar esptool.");
            process.OutputDataReceived+=(sender,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))progress?.Report(e.Data);};
            process.ErrorDataReceived+=(sender,e)=>{if(!string.IsNullOrWhiteSpace(e.Data))progress?.Report(e.Data);};
            process.BeginOutputReadLine();process.BeginErrorReadLine();
            try {await process.WaitForExitAsync(ct);process.WaitForExit();}
            catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);throw;}
            if(process.ExitCode!=0)throw new Exception("esptool terminó con código "+process.ExitCode+". Comprueba el puerto COM, el cable de datos y el botón BOOT.");
        }
        progress?.Report("Firmware escrito y verificado.");
    }
    public static Task Configure(string port,string deviceJson,IProgress<string> progress,CancellationToken ct) {
        CheckPort(port);
        if(string.IsNullOrWhiteSpace(deviceJson)||Encoding.UTF8.GetByteCount(deviceJson)>2400)throw new Exception("La configuración del ESP32 es demasiado grande.");
        return Task.Run(()=>{
            progress?.Report("Esperando al firmware por USB…");
            using(var serial=new SerialPort(port,115200){Encoding=Encoding.UTF8,NewLine="\n",ReadTimeout=700,WriteTimeout=3000,DtrEnable=false,RtsEnable=false}) {
                serial.Open();
                var watch=Stopwatch.StartNew();bool ready=false;
                while(watch.Elapsed<TimeSpan.FromSeconds(25)&&!ready){
                    ct.ThrowIfCancellationRequested();serial.WriteLine("TRPING1");
                    var until=DateTime.UtcNow.AddSeconds(2);
                    while(DateTime.UtcNow<until){
                        ct.ThrowIfCancellationRequested();
                        try {if(serial.ReadLine().Trim().StartsWith("TRPONG1:3.0.0:",StringComparison.Ordinal)){ready=true;break;}}
                        catch(TimeoutException){}
                    }
                }
                if(!ready)throw new Exception("El ESP32 no respondió al protocolo v3. Comprueba que se flasheó, reinícialo y prueba de nuevo.");
                progress?.Report("Enviando ajustes al ESP32 por USB…");
                serial.WriteLine("TRCFG1:"+deviceJson);
                watch.Restart();
                while(watch.Elapsed<TimeSpan.FromSeconds(12)){
                    ct.ThrowIfCancellationRequested();
                    try {
                        string line=serial.ReadLine().Trim();
                        if(line=="TRCFG_OK"){progress?.Report("Configuración guardada. El ESP32 se está reiniciando.");return;}
                        if(line.StartsWith("TRCFG_ERROR:",StringComparison.Ordinal))throw new Exception("El ESP32 rechazó la configuración: "+line.Substring(12));
                    }catch(TimeoutException){}
                }
                throw new Exception("El ESP32 no confirmó los ajustes. Repite «Solo configurar» antes de desconectarlo.");
            }
        },ct);
    }
}
}
