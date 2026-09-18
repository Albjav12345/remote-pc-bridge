using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TorreRemota {
public static class Program {
    [STAThread]
    public static int Main(string[] args) {
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        if(args.Contains("--self-test"))return Tests.Run().GetAwaiter().GetResult();
        if(args.Contains("--diagnose-config"))return Tests.Diagnose(true).GetAwaiter().GetResult();
        if(args.Contains("--wake-test"))return Tests.WakeRoundTrip().GetAwaiter().GetResult();
        if(args.Contains("--moonlight-report")) {
            var last=MoonlightSession.Recent(TimeSpan.FromHours(24));
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"moonlight-report.txt"),
                last==null?new[]{"Sin registro de Moonlight en las últimas 24 horas."}:
                new[]{last.Phase.ToString(),last.Detail,last.LastWrite.ToString("O"),last.LogPath});
            return 0;
        }
        bool flashDemo=args.Contains("--render-flash-demo");
        bool setupDemo=flashDemo||args.Contains("--render-setup-demo");
        bool demo=setupDemo||args.Contains("--render-demo")||args.Contains("--render-preview");
        bool created;
        using(var mutex=new Mutex(true,demo?"Local\\TorreRemota.Demo.UI":"Local\\TorreRemota.Modern.UI",out created)) {
            if(!created){MessageBox.Show("Torre Remota ya está abierta.","Torre Remota");return 0;}
            try {
                var app=new Application();
                var window=new ModernWindow(demo,setupDemo,flashDemo);
                if(demo) {
                    window.Loaded+=async (sender,ev)=>{
                        var watch=System.Diagnostics.Stopwatch.StartNew();
                        do { await System.Threading.Tasks.Task.Delay(200); } while(window.IsBusy && watch.Elapsed.TotalSeconds<25);
                        Capture(window,Path.Combine(AppContext.BaseDirectory,flashDemo?"preview-flash.png":setupDemo?"preview-setup.png":"preview-modern.png"));
                        window.Close();
                    };
                }
                app.Run(window);
                return 0;
            } catch(Exception e) {MessageBox.Show("No se pudo iniciar: "+e.Message,"Torre Remota");return 1;}
        }
    }
    static void Capture(Window window,string path) {
        var source=PresentationSource.FromVisual(window);
        double sx=source==null?1:source.CompositionTarget.TransformToDevice.M11;
        double sy=source==null?1:source.CompositionTarget.TransformToDevice.M22;
        int width=Math.Max(1,(int)(window.ActualWidth*sx)),height=Math.Max(1,(int)(window.ActualHeight*sy));
        var image=new RenderTargetBitmap(width,height,96*sx,96*sy,PixelFormats.Pbgra32);
        image.Render(window);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
        using(var stream=File.Create(path))encoder.Save(stream);
    }
}
}
