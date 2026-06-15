using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ShowCast.Core;

namespace ShowCast.Views;

public sealed class AltCodeReferenceDialog : Window
{
    sealed record AltEntry(string Code, string Char, string Description);

    static readonly IBrush _bg     = SolidColorBrush.Parse("#2d2d2d");
    static readonly IBrush _panel  = SolidColorBrush.Parse("#1e1e1e");
    static readonly IBrush _border = SolidColorBrush.Parse("#555555");
    static readonly IBrush _accent = SolidColorBrush.Parse("#e07050");
    static readonly IBrush _white  = Brushes.White;
    static readonly IBrush _dim    = SolidColorBrush.Parse("#aaaaaa");
    static readonly IBrush _rowA   = SolidColorBrush.Parse("#282828");
    static readonly IBrush _rowB   = SolidColorBrush.Parse("#323232");

    public AltCodeReferenceDialog()
    {
        Title    = "Alt Code Reference";
        Width    = 610;
        Height   = 660;
        MinWidth  = 440;
        MinHeight = 400;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = _bg;

        var tabs = new TabControl
        {
            Items =
            {
                BuildTab("Standard  (Alt + digits)",
                    "Hold Alt  ·  type 1–4 digits on numpad  ·  release Alt",
                    BuildStandard()),
                BuildTab("Extended  (Alt + 0 + digits)",
                    "Hold Alt  ·  type 0 then digits on numpad  ·  release Alt",
                    BuildExtended()),
            }
        };

        var close = MakeButton("Close", 90, _border);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Margin = new Thickness(0, 6, 14, 10);
        close.Click += (_, _) => Close();

        var root = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(tabs,  0); root.Children.Add(tabs);
        Grid.SetRow(close, 1); root.Children.Add(close);
        Content = root;
    }

    // ── Tab builder ────────────────────────────────────────────────────────────

    static TabItem BuildTab(string header, string hint, AltEntry[] all)
    {
        var search = new TextBox
        {
            Watermark = "Filter by code, character, or description…",
            Height    = 30,
            Margin    = new Thickness(8, 8, 8, 4),
            Background  = SolidColorBrush.Parse("#3a3a3a"),
            Foreground  = Brushes.White,
            BorderBrush = SolidColorBrush.Parse("#555555"),
            FontSize    = 12,
            VerticalContentAlignment = VerticalAlignment.Center,
        };

        var hintBlock = new TextBlock
        {
            Text       = hint,
            Foreground = _dim,
            FontSize   = 11,
            Margin     = new Thickness(10, 0, 8, 6),
        };

        var scroll  = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var listHost = new StackPanel();
        scroll.Content = listHost;

        void Populate(AltEntry[] entries)
        {
            listHost.Children.Clear();
            // Column header row
            listHost.Children.Add(MakeHeaderRow());
            for (int i = 0; i < entries.Length; i++)
                listHost.Children.Add(MakeDataRow(entries[i], i % 2 == 0));
        }

        Populate(all);

        search.TextChanged += (_, _) =>
        {
            var q = (search.Text ?? "").Trim();
            var filtered = string.IsNullOrEmpty(q)
                ? all
                : all.Where(e =>
                    e.Code.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.Char.Equals(q, StringComparison.Ordinal)              ||
                    e.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
                  .ToArray();
            Populate(filtered);
        };

        var dock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(search,    Dock.Top);
        DockPanel.SetDock(hintBlock, Dock.Bottom);
        dock.Children.Add(search);
        dock.Children.Add(hintBlock);
        dock.Children.Add(scroll);

        return new TabItem { Header = header, Content = dock };
    }

    // ── Row builders ───────────────────────────────────────────────────────────

    static Grid MakeHeaderRow()
    {
        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("100,52,*"),
            Background = _panel,
            Height = 26,
        };
        AddCell(g, "CODE",        0, 11, FontWeight.Bold, _accent);
        AddCell(g, "CHAR",        1, 11, FontWeight.Bold, _accent);
        AddCell(g, "DESCRIPTION", 2, 11, FontWeight.Bold, _accent);
        return g;
    }

    static Grid MakeDataRow(AltEntry e, bool even)
    {
        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("100,52,*"),
            Background = even ? _rowA : _rowB,
            Height = 24,
        };
        AddCell(g, e.Code,        0, 12, FontWeight.Normal, _white);
        AddCell(g, e.Char,        1, 16, FontWeight.Normal, _white);
        AddCell(g, e.Description, 2, 12, FontWeight.Normal, _dim);
        return g;
    }

    static void AddCell(Grid g, string text, int col,
        double size, FontWeight weight, IBrush fg)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = size, FontWeight = weight, Foreground = fg,
            Padding = new Thickness(8, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(tb, col);
        g.Children.Add(tb);
    }

    // ── Data generation ────────────────────────────────────────────────────────

    static AltEntry[] BuildStandard()
    {
        var list = new System.Collections.Generic.List<AltEntry>(255);
        for (int i = 1; i <= 255; i++)
        {
            char? c = AltCodes.StandardToChar(i);
            string ch = c.HasValue ? c.Value.ToString() : "";
            list.Add(new AltEntry($"Alt+{i}", ch, StandardDesc(i)));
        }
        return list.ToArray();
    }

    static AltEntry[] BuildExtended()
    {
        var list = new System.Collections.Generic.List<AltEntry>(128);
        for (int i = 128; i <= 255; i++)
        {
            char? c = AltCodes.ExtendedToChar(i);
            string ch = c.HasValue ? c.Value.ToString() : "";
            list.Add(new AltEntry($"Alt+0{i}", ch, ExtendedDesc(i)));
        }
        return list.ToArray();
    }

    // ── Description tables ────────────────────────────────────────────────────

    static string StandardDesc(int n) => n switch
    {
        1   => "White smiling face",
        2   => "Black smiling face",
        3   => "Heart suit",
        4   => "Diamond suit",
        5   => "Club suit",
        6   => "Spade suit",
        7   => "Bullet",
        8   => "Inverse bullet",
        9   => "White circle",
        10  => "Inverse white circle",
        11  => "Male sign",
        12  => "Female sign",
        13  => "Eighth note",
        14  => "Beamed eighth notes",
        15  => "Sun / asterism",
        16  => "Right-pointing triangle",
        17  => "Left-pointing triangle",
        18  => "Up-down arrow",
        19  => "Double exclamation mark",
        20  => "Pilcrow (paragraph sign)",
        21  => "Section sign",
        22  => "Black rectangle",
        23  => "Up-down arrow with base",
        24  => "Up arrow",
        25  => "Down arrow",
        26  => "Right arrow",
        27  => "Left arrow",
        28  => "Right angle",
        29  => "Left-right arrow",
        30  => "Up-pointing triangle",
        31  => "Down-pointing triangle",
        32  => "Space",
        33  => "Exclamation mark",
        34  => "Quotation mark",
        35  => "Number / hash sign",
        36  => "Dollar sign",
        37  => "Percent sign",
        38  => "Ampersand",
        39  => "Apostrophe",
        40  => "Left parenthesis",
        41  => "Right parenthesis",
        42  => "Asterisk",
        43  => "Plus sign",
        44  => "Comma",
        45  => "Hyphen / minus sign",
        46  => "Period / full stop",
        47  => "Forward slash",
        >= 48 and <= 57  => $"Digit {(char)n}",
        58  => "Colon",
        59  => "Semicolon",
        60  => "Less-than sign",
        61  => "Equals sign",
        62  => "Greater-than sign",
        63  => "Question mark",
        64  => "At sign",
        >= 65 and <= 90  => $"Capital letter {(char)n}",
        91  => "Left square bracket",
        92  => "Backslash",
        93  => "Right square bracket",
        94  => "Caret",
        95  => "Underscore",
        96  => "Grave accent / backtick",
        >= 97 and <= 122 => $"Small letter {(char)n}",
        123 => "Left curly brace",
        124 => "Vertical bar",
        125 => "Right curly brace",
        126 => "Tilde",
        127 => "House symbol",
        128 => "Ç — C with cedilla",
        129 => "ü — u with umlaut",
        130 => "é — e acute",
        131 => "â — a circumflex",
        132 => "ä — a umlaut",
        133 => "à — a grave",
        134 => "å — a ring above",
        135 => "ç — c cedilla",
        136 => "ê — e circumflex",
        137 => "ë — e umlaut",
        138 => "è — e grave",
        139 => "ï — i umlaut",
        140 => "î — i circumflex",
        141 => "ì — i grave",
        142 => "Ä — A umlaut",
        143 => "Å — A ring above",
        144 => "É — E acute",
        145 => "æ — ae ligature",
        146 => "Æ — AE ligature",
        147 => "ô — o circumflex",
        148 => "ö — o umlaut",
        149 => "ò — o grave",
        150 => "û — u circumflex",
        151 => "ù — u grave",
        152 => "ÿ — y umlaut",
        153 => "Ö — O umlaut",
        154 => "Ü — U umlaut",
        155 => "¢ — Cent sign",
        156 => "£ — Pound sterling sign",
        157 => "¥ — Yen / Yuan sign",
        158 => "₧ — Peseta sign",
        159 => "ƒ — Florin / Guilder sign",
        160 => "á — a acute",
        161 => "í — i acute",
        162 => "ó — o acute",
        163 => "ú — u acute",
        164 => "ñ — n tilde",
        165 => "Ñ — N tilde",
        166 => "ª — Feminine ordinal indicator",
        167 => "º — Masculine ordinal indicator",
        168 => "¿ — Inverted question mark",
        169 => "⌐ — Reversed not sign",
        170 => "¬ — Not sign (logical negation)",
        171 => "½ — Vulgar fraction one half",
        172 => "¼ — Vulgar fraction one quarter",
        173 => "¡ — Inverted exclamation mark",
        174 => "« — Left-pointing double angle quotation",
        175 => "» — Right-pointing double angle quotation",
        176 => "░ — Light shade (box drawing)",
        177 => "▒ — Medium shade (box drawing)",
        178 => "▓ — Dark shade (box drawing)",
        179 => "│ — Box vertical single",
        180 => "┤ — Box right T-junction single",
        181 => "╡ — Box right T (double-left)",
        182 => "╢ — Box right T (double-right)",
        183 => "╖ — Box top-right (single-top, double-side)",
        184 => "╕ — Box top-right (double-top, single-side)",
        185 => "╣ — Box right T double",
        186 => "║ — Box vertical double",
        187 => "╗ — Box top-right corner double",
        188 => "╝ — Box bottom-right corner double",
        189 => "╜ — Box bottom-right (single-bottom, double-side)",
        190 => "╛ — Box bottom-right (double-bottom, single-side)",
        191 => "┐ — Box top-right corner single",
        192 => "└ — Box bottom-left corner single",
        193 => "┴ — Box bottom T-junction single",
        194 => "┬ — Box top T-junction single",
        195 => "├ — Box left T-junction single",
        196 => "─ — Box horizontal single",
        197 => "┼ — Box cross junction single",
        198 => "╞ — Box left T (single-left, double-right)",
        199 => "╟ — Box left T (double-left, single-right)",
        200 => "╚ — Box bottom-left corner double",
        201 => "╔ — Box top-left corner double",
        202 => "╩ — Box bottom T-junction double",
        203 => "╦ — Box top T-junction double",
        204 => "╠ — Box left T-junction double",
        205 => "═ — Box horizontal double",
        206 => "╬ — Box cross junction double",
        207 => "╧ — Box bottom T (double-horiz, single-vert)",
        208 => "╨ — Box bottom T (single-horiz, double-vert)",
        209 => "╤ — Box top T (double-horiz, single-vert)",
        210 => "╥ — Box top T (single-horiz, double-vert)",
        211 => "╙ — Box bottom-left (double-bottom, single-right)",
        212 => "╘ — Box bottom-left (single-bottom, double-right)",
        213 => "╒ — Box top-left (single-top, double-right)",
        214 => "╓ — Box top-left (double-top, single-right)",
        215 => "╫ — Box cross (double-vert, single-horiz)",
        216 => "╪ — Box cross (single-vert, double-horiz)",
        217 => "┘ — Box bottom-right corner single",
        218 => "┌ — Box top-left corner single",
        219 => "█ — Full block",
        220 => "▄ — Lower half block",
        221 => "▌ — Left half block",
        222 => "▐ — Right half block",
        223 => "▀ — Upper half block",
        224 => "α — Alpha (Greek lowercase)",
        225 => "ß — Sharp s / Eszett (German)",
        226 => "Γ — Gamma (Greek uppercase)",
        227 => "π — Pi (3.14159…)",
        228 => "Σ — Sigma / Sum (Greek uppercase)",
        229 => "σ — Sigma (Greek lowercase)",
        230 => "µ — Micro sign / Mu (Greek)",
        231 => "τ — Tau (Greek lowercase)",
        232 => "Φ — Phi (Greek uppercase)",
        233 => "Θ — Theta (Greek uppercase)",
        234 => "Ω — Omega (Greek uppercase)",
        235 => "δ — Delta (Greek lowercase)",
        236 => "∞ — Infinity symbol",
        237 => "φ — Phi (Greek lowercase)",
        238 => "ε — Epsilon (Greek lowercase)",
        239 => "∩ — Intersection",
        240 => "≡ — Identical to / Triple bar",
        241 => "± — Plus-minus sign",
        242 => "≥ — Greater-than or equal to",
        243 => "≤ — Less-than or equal to",
        244 => "⌠ — Top half integral",
        245 => "⌡ — Bottom half integral",
        246 => "÷ — Division sign",
        247 => "≈ — Almost equal to / Approximately",
        248 => "° — Degree sign",
        249 => "∙ — Bullet operator",
        250 => "· — Middle dot",
        251 => "√ — Square root (radical sign)",
        252 => "ⁿ — Superscript lowercase n",
        253 => "² — Superscript 2 (squared)",
        254 => "■ — Black square",
        255 => "Non-breaking space",
        _   => ""
    };

    static string ExtendedDesc(int n) => n switch
    {
        128 => "€ — Euro sign",
        129 => "(undefined in CP1252)",
        130 => "‚ — Single low-9 quotation mark",
        131 => "ƒ — Latin f with hook / Florin sign",
        132 => "„ — Double low-9 quotation mark",
        133 => "… — Horizontal ellipsis",
        134 => "† — Dagger",
        135 => "‡ — Double dagger",
        136 => "ˆ — Modifier letter circumflex accent",
        137 => "‰ — Per mille sign",
        138 => "Š — Latin capital S with caron",
        139 => "‹ — Single left-pointing angle quotation",
        140 => "Œ — Latin capital OE ligature",
        141 => "(undefined in CP1252)",
        142 => "Ž — Latin capital Z with caron",
        143 => "(undefined in CP1252)",
        144 => "(undefined in CP1252)",
        145 => "‘ — Left single quotation mark",
        146 => "’ — Right single quotation mark",
        147 => "“ — Left double quotation mark",
        148 => "” — Right double quotation mark",
        149 => "• — Bullet",
        150 => "– — En dash",
        151 => "— — Em dash",
        152 => "˜ — Small tilde",
        153 => "™ — Trade mark sign",
        154 => "š — Latin small s with caron",
        155 => "› — Single right-pointing angle quotation",
        156 => "œ — Latin small OE ligature",
        157 => "(undefined in CP1252)",
        158 => "ž — Latin small z with caron",
        159 => "Ÿ — Latin capital Y with diaeresis",
        160 => "Non-breaking space",
        161 => "¡ — Inverted exclamation mark",
        162 => "¢ — Cent sign",
        163 => "£ — Pound sterling sign",
        164 => "¤ — Currency sign",
        165 => "¥ — Yen / Yuan sign",
        166 => "¦ — Broken bar",
        167 => "§ — Section sign",
        168 => "¨ — Diaeresis / Umlaut",
        169 => "© — Copyright sign",
        170 => "ª — Feminine ordinal indicator",
        171 => "« — Left-pointing double angle quotation",
        172 => "¬ — Not sign (logical negation)",
        173 => "Soft hyphen",
        174 => "® — Registered trade mark sign",
        175 => "¯ — Macron",
        176 => "° — Degree sign",
        177 => "± — Plus-minus sign",
        178 => "² — Superscript 2 (squared)",
        179 => "³ — Superscript 3 (cubed)",
        180 => "´ — Acute accent",
        181 => "µ — Micro sign / Greek Mu",
        182 => "¶ — Pilcrow / Paragraph sign",
        183 => "· — Middle dot",
        184 => "¸ — Cedilla",
        185 => "¹ — Superscript 1",
        186 => "º — Masculine ordinal indicator",
        187 => "» — Right-pointing double angle quotation",
        188 => "¼ — Vulgar fraction one quarter",
        189 => "½ — Vulgar fraction one half",
        190 => "¾ — Vulgar fraction three quarters",
        191 => "¿ — Inverted question mark",
        192 => "À — Latin capital A with grave",
        193 => "Á — Latin capital A with acute",
        194 => "Â — Latin capital A with circumflex",
        195 => "Ã — Latin capital A with tilde",
        196 => "Ä — Latin capital A with umlaut",
        197 => "Å — Latin capital A with ring above",
        198 => "Æ — Latin capital AE ligature",
        199 => "Ç — Latin capital C with cedilla",
        200 => "È — Latin capital E with grave",
        201 => "É — Latin capital E with acute",
        202 => "Ê — Latin capital E with circumflex",
        203 => "Ë — Latin capital E with umlaut",
        204 => "Ì — Latin capital I with grave",
        205 => "Í — Latin capital I with acute",
        206 => "Î — Latin capital I with circumflex",
        207 => "Ï — Latin capital I with umlaut",
        208 => "Ð — Latin capital letter Eth (Icelandic)",
        209 => "Ñ — Latin capital N with tilde",
        210 => "Ò — Latin capital O with grave",
        211 => "Ó — Latin capital O with acute",
        212 => "Ô — Latin capital O with circumflex",
        213 => "Õ — Latin capital O with tilde",
        214 => "Ö — Latin capital O with umlaut",
        215 => "× — Multiplication sign",
        216 => "Ø — Latin capital O with stroke",
        217 => "Ù — Latin capital U with grave",
        218 => "Ú — Latin capital U with acute",
        219 => "Û — Latin capital U with circumflex",
        220 => "Ü — Latin capital U with umlaut",
        221 => "Ý — Latin capital Y with acute",
        222 => "Þ — Latin capital letter Thorn (Icelandic)",
        223 => "ß — Latin small sharp s / Eszett (German)",
        224 => "à — Latin small a with grave",
        225 => "á — Latin small a with acute",
        226 => "â — Latin small a with circumflex",
        227 => "ã — Latin small a with tilde",
        228 => "ä — Latin small a with umlaut",
        229 => "å — Latin small a with ring above",
        230 => "æ — Latin small ae ligature",
        231 => "ç — Latin small c with cedilla",
        232 => "è — Latin small e with grave",
        233 => "é — Latin small e with acute",
        234 => "ê — Latin small e with circumflex",
        235 => "ë — Latin small e with umlaut",
        236 => "ì — Latin small i with grave",
        237 => "í — Latin small i with acute",
        238 => "î — Latin small i with circumflex",
        239 => "ï — Latin small i with umlaut",
        240 => "ð — Latin small letter eth (Icelandic)",
        241 => "ñ — Latin small n with tilde",
        242 => "ò — Latin small o with grave",
        243 => "ó — Latin small o with acute",
        244 => "ô — Latin small o with circumflex",
        245 => "õ — Latin small o with tilde",
        246 => "ö — Latin small o with umlaut",
        247 => "÷ — Division sign",
        248 => "ø — Latin small o with stroke",
        249 => "ù — Latin small u with grave",
        250 => "ú — Latin small u with acute",
        251 => "û — Latin small u with circumflex",
        252 => "ü — Latin small u with umlaut",
        253 => "ý — Latin small y with acute",
        254 => "þ — Latin small letter thorn (Icelandic)",
        255 => "ÿ — Latin small y with umlaut",
        _   => ""
    };

    // ── Shared helpers ────────────────────────────────────────────────────────

    static Button MakeButton(string label, double width, IBrush bg)
    {
        return new Button
        {
            Content  = label,
            Width    = width,
            Height   = 32,
            Background   = bg,
            Foreground   = Brushes.White,
            CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment   = VerticalAlignment.Center,
        };
    }
}
