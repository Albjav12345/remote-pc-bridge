using System;
using System.Windows;
using System.Windows.Media;

namespace TorreRemota {
internal static class ThemePalette {
    static readonly string[] Dark = {
        "#050C19", "#075BD3", "#111A27", "#172130", "#1762BB",
        "#1979E5", "#1B2533", "#244464", "#245184", "#2D496B",
        "#3046A2FA", "#3299F5", "#35A8FF", "#405269", "#405B7C",
        "#45162743", "#455C7C", "#456484", "#46523A43", "#46586D",
        "#49617F", "#4974AC", "#4B6280", "#4B688A", "#536980",
        "#5A53325A", "#5A7391", "#5A789B", "#60B2F8", "#66334C68",
        "#8DCCFF", "#93C9FF", "#99111E30", "#99304965", "#9FB7D2",
        "#A31C3654", "#A5B4C4", "#A9C6E6", "#AFC1D7", "#AFD6FF",
        "#B2213854", "#B3C8E1", "#B8132439", "#B8D2EE", "#B9DAFF",
        "#BDD0E5", "#C0D2EA", "#C7D9EE", "#C8DEFF", "#CC1B2C43",
        "#D0142943", "#D4E1EF", "#D91B3D68", "#E519304E", "#E7F1FC",
        "#E9152F53", "#E9F0F8", "#F00C1A2B", "#F00D1827", "#F2F7FE",
        "#F5F9FF", "#FFBB59", "#FFC45E",
    };
    static readonly string[] Light = {
        "#506174", "#075BD3", "#FFFFFF", "#E9EFF7", "#1762BB",
        "#1979E5", "#FFFFFF", "#E3F0FD", "#DDEBFA", "#DDEBFA",
        "#1475C5E8", "#3299F5", "#35A8FF", "#C4D1E0", "#B8CAE0",
        "#DFFFFFFF", "#C5D6E9", "#D9E8FA", "#FFF2E7D6", "#C7D2DF",
        "#C3D3E5", "#6CA6E9", "#CCD8E6", "#C3D5E8", "#A9BDD3",
        "#FFF2E2C9", "#BCCDE0", "#BDD1E8", "#60B2F8", "#EAF2FB",
        "#1769BC", "#236CB8", "#FFF9FCFF", "#E8F1FB", "#5C728C",
        "#E8F1FB", "#72849A", "#466B93", "#445E79", "#236CB8",
        "#ECF4FC", "#526D89", "#F9FCFF", "#496984", "#286FB5",
        "#45617D", "#536D8B", "#405B76", "#286FB5", "#EBFFFFFF",
        "#F4F8FE", "#284057", "#F0F7FF", "#F6FAFF", "#244362",
        "#EEF5FD", "#243C55", "#F1F6FC", "#ECF3FA", "#1B344F",
        "#142B44", "#B46E00", "#CF7A12",
    };
    public static void Apply(ResourceDictionary resources, bool useLight) {
        var values = useLight ? Light : Dark;
        for (int i=0; i<values.Length; i++) {
            var color = (Color)ColorConverter.ConvertFromString(values[i]);
            resources[$"ThemeColor{i:00}"] = color;
            resources[$"ThemeBrush{i:00}"] = new SolidColorBrush(color);
        }
        foreach (var pair in new[] {
            ("Ink", "#F3F7FD", "#172D45"), ("Muted", "#B0C3DA", "#52687F"),
            ("Accent", "#74BDFF", "#2277C9"), ("Line", "#435D7D", "#C5D2E0") }) {
            resources[pair.Item1] = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(useLight ? pair.Item3 : pair.Item2));
        }
    }
}
}
