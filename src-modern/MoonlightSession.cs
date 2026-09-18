using System;
using System.IO;
using System.Linq;
using System.Text;

namespace TorreRemota {
public enum MoonlightPhase { None, Launching, Video, Finished, NoVideo }

public sealed class MoonlightSession {
    public MoonlightPhase Phase;
    public string Detail="";
    public string LogPath="";
    public bool VideoReceived;
    public DateTime LastWrite;

    public static MoonlightSession Parse(string log, bool processAlive) {
        int videoAt=log.LastIndexOf("Received first video packet",StringComparison.Ordinal);
        int endAt=Math.Max(log.LastIndexOf("Stopping video stream...",StringComparison.Ordinal),
            Math.Max(log.LastIndexOf("Quit event received",StringComparison.Ordinal),
                     log.LastIndexOf("Connection terminated:",StringComparison.Ordinal)));
        bool video=videoAt>=0;
        bool finished=endAt>=0 && endAt>=videoAt;
        if(finished || (video && !processAlive)) {
            string detail=video?"La sesión recibió vídeo y después terminó.":"Moonlight terminó sin confirmar vídeo.";
            const string marker="Server notified termination reason: ";
            int reasonAt=log.LastIndexOf(marker,StringComparison.Ordinal);
            if(reasonAt>=0) {
                int start=reasonAt+marker.Length;
                int end=log.IndexOfAny(new[]{'\r','\n',' '},start);
                string reason=log.Substring(start,(end<0?log.Length:end)-start);
                if(reason.Length>0 && reason.Length<24)detail+=" Código del servidor: "+reason+".";
            }
            return new MoonlightSession{Phase=MoonlightPhase.Finished,Detail=detail,VideoReceived=video};
        }
        if(video && processAlive)
            return new MoonlightSession{Phase=MoonlightPhase.Video,Detail="Moonlight recibe vídeo de la torre.",VideoReceived=true};
        return new MoonlightSession{Phase=processAlive?MoonlightPhase.Launching:MoonlightPhase.NoVideo,
            Detail=processAlive?"Esperando el primer paquete de vídeo.":"Moonlight se cerró sin confirmar vídeo."};
    }

    public static MoonlightSession Observe(DateTime launchedAt, bool processAlive) {
        var waiting=new MoonlightSession{Phase=processAlive||DateTime.Now-launchedAt<TimeSpan.FromSeconds(12)?
            MoonlightPhase.Launching:MoonlightPhase.NoVideo,
            Detail=processAlive?"Esperando el primer paquete de vídeo.":"No se encontró una sesión de Moonlight activa."};
        try {
            string folder=Path.GetTempPath();
            var file=new DirectoryInfo(folder).GetFiles("Moonlight-*.log")
                .Where(x=>x.CreationTime>=launchedAt.AddSeconds(-3))
                .OrderByDescending(x=>x.CreationTime).FirstOrDefault();
            if(file==null)return waiting;
            // Moonlight escribe este archivo mientras reproduce. Leer solo cabecera y cola
            // mantiene constante el coste aun cuando la sesión dura muchas horas.
            string log;
            using(var stream=new FileStream(file.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                int head=(int)Math.Min(stream.Length,128*1024);
                var first=new byte[head];stream.ReadExactly(first);
                log=Encoding.UTF8.GetString(first);
                if(stream.Length>head) {
                    stream.Seek(Math.Max(head,stream.Length-128*1024),SeekOrigin.Begin);
                    var last=new byte[(int)(stream.Length-stream.Position)];
                    stream.ReadExactly(last);
                    log+=Encoding.UTF8.GetString(last);
                }
            }
            var state=Parse(log,processAlive);state.LogPath=file.FullName;state.LastWrite=file.LastWriteTime;
            if(state.Phase==MoonlightPhase.NoVideo && DateTime.Now-launchedAt<TimeSpan.FromSeconds(12))
                state.Phase=MoonlightPhase.Launching;
            return state;
        } catch(IOException){return waiting;} catch(UnauthorizedAccessException){return waiting;}
    }
    public static MoonlightSession Recent(TimeSpan maxAge) {
        try {
            var latest=new DirectoryInfo(Path.GetTempPath()).GetFiles("Moonlight-*.log")
                .OrderByDescending(x=>x.CreationTime).FirstOrDefault();
            if(latest==null || DateTime.Now-latest.CreationTime>maxAge)return null;
            return Observe(latest.CreationTime,false);
        } catch(IOException){return null;} catch(UnauthorizedAccessException){return null;}
    }
}
}
