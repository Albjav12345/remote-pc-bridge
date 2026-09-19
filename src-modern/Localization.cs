using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace TorreRemota {
// The UI's existing Spanish copy is kept as the fallback; the English column in
// Localization.tsv is applied before the window is first shown. New diagnostic
// values are translated when TextBlock.Text changes, including after refreshes.
public sealed class Localization {
    readonly List<KeyValuePair<string,string>> translations;
    readonly Dictionary<TextBlock,string> rawText=new Dictionary<TextBlock,string>();
    readonly Dictionary<Button,string> rawButtons=new Dictionary<Button,string>();
    readonly Window window;
    readonly string rawTitle;
    bool applying;
    public string Language { get; private set; }

    public Localization(Window root,string language) {
        window=root;rawTitle=root.Title;
        using(var stream=typeof(Localization).Assembly.GetManifestResourceStream("TorreRemota.Localization")) {
            if(stream==null)throw new InvalidDataException("Translation resource is missing.");
            using(var reader=new StreamReader(stream,Encoding.UTF8)) {
                var rows=new List<KeyValuePair<string,string>>();
                string line;
                while((line=reader.ReadLine())!=null) {
                    if(line.Length==0||line[0]=='#')continue;
                    int separator=line.IndexOf('\t');
                    if(separator<1||separator==line.Length-1)throw new InvalidDataException("Invalid translation row: "+line);
                    rows.Add(new KeyValuePair<string,string>(line.Substring(0,separator).Replace(@"\s"," "),line.Substring(separator+1).Replace(@"\s"," ")));
                }
                translations=rows.OrderByDescending(x=>x.Key.Length).ToList();
            }
        }
        Collect(root);
        SetLanguage(language);
    }

    void Collect(DependencyObject node) {
        if(node is TextBlock text) {
            rawText[text]=text.Text;
            DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock)).AddValueChanged(text,(sender,args)=>{
                if(applying)return;
                rawText[text]=text.Text;
                ApplyText(text);
            });
        }
        if(node is Button button && button.Content is string label)rawButtons[button]=label;
        foreach(object child in LogicalTreeHelper.GetChildren(node))if(child is DependencyObject element)Collect(element);
    }

    void ApplyText(TextBlock text) {
        applying=true;
        try{text.Text=Convert(rawText[text]);}
        finally{applying=false;}
    }
    public void SetLanguage(string language) {
        Language=language=="es"?"es":"en";
        applying=true;
        try {
            window.Title=Convert(rawTitle);
            foreach(var pair in rawText)pair.Key.Text=Convert(pair.Value);
            foreach(var pair in rawButtons)pair.Key.Content=Convert(pair.Value);
        } finally {applying=false;}
    }
    public string Convert(string value) {
        if(Language=="es"||string.IsNullOrEmpty(value))return value;
        foreach(var pair in translations)value=value.Replace(pair.Key,pair.Value,StringComparison.Ordinal);
        return value;
    }
}
}
