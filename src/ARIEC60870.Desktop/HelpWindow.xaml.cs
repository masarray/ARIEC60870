// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ARIEC60870.Desktop;

public partial class HelpWindow : Window
{
    private readonly List<HelpTopic> _topics;
    private string _language = "en";
    private string _selectedTopicKey;

    public HelpWindow(string? topicKey = null)
    {
        InitializeComponent();
        SearchBox.Text = "Search ACD, Class 1, command timeout...";
        SearchBox.GotKeyboardFocus += (_, _) =>
        {
            if (SearchBox.Text == "Search ACD, Class 1, command timeout..." || SearchBox.Text == "Cari ACD, Class 1, command timeout...")
            {
                SearchBox.Text = string.Empty;
            }
        };
        SearchBox.LostKeyboardFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(SearchBox.Text))
            {
                SearchBox.Text = _language == "id" ? "Cari ACD, Class 1, command timeout..." : "Search ACD, Class 1, command timeout...";
            }
        };

        _topics = CreateTopics();
        _selectedTopicKey = string.IsNullOrWhiteSpace(topicKey) ? "overview" : topicKey!;
        RefreshLanguageText();
        RefreshTopicList();
        SelectTopic(_selectedTopicKey);
    }

    public void SelectTopic(string? topicKey)
    {
        if (!string.IsNullOrWhiteSpace(topicKey) && _topics.Any(topic => string.Equals(topic.Key, topicKey, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedTopicKey = topicKey!;
        }

        foreach (var item in TopicListBox.Items.OfType<HelpTopicListItem>())
        {
            if (string.Equals(item.Key, _selectedTopicKey, StringComparison.OrdinalIgnoreCase))
            {
                TopicListBox.SelectedItem = item;
                TopicListBox.ScrollIntoView(item);
                return;
            }
        }

        var topic = _topics.FirstOrDefault(t => string.Equals(t.Key, _selectedTopicKey, StringComparison.OrdinalIgnoreCase)) ?? _topics[0];
        RenderTopic(topic);
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string language)
        {
            _language = language;
            RefreshLanguageText();
            RefreshTopicList();
            SelectTopic(_selectedTopicKey);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshTopicList();
        SelectTopic(_selectedTopicKey);
    }

    private void TopicListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TopicListBox.SelectedItem is not HelpTopicListItem item)
        {
            return;
        }

        _selectedTopicKey = item.Key;
        var topic = _topics.FirstOrDefault(t => string.Equals(t.Key, item.Key, StringComparison.OrdinalIgnoreCase));
        if (topic is not null)
        {
            RenderTopic(topic);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenOnlineButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _language == "id" ? "id/wiki.html" : "wiki.html";
        var url = $"https://masarray.github.io/ARIEC60870/{path}";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to open the online Field Wiki.\n\n{ex.Message}", "Open Field Wiki", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RefreshLanguageText()
    {
        HelpTitleText.Text = _language == "id" ? "Pusat Bantuan ARIEC60870" : "ARIEC60870 Help Center";
        HelpSubtitleText.Text = _language == "id"
            ? "Panduan cepat untuk IEC 101, IEC 103, IEC 104, frame trace, findings, dan report."
            : "Quick field guidance for IEC 101, IEC 103, IEC 104, frame traces, findings, and reports.";
        TopicListTitleText.Text = _language == "id" ? "Topik lapangan" : "Field topics";
        TopicListHintText.Text = _language == "id"
            ? "Tekan F1 dari workspace apa pun untuk membuka topik yang paling relevan."
            : "Press F1 from any workspace to open the most relevant topic.";
        OpenOnlineButton.Content = BuildOnlineButtonContent(_language == "id" ? "Buka Field Wiki" : "Open Field Wiki");
        if (string.IsNullOrWhiteSpace(SearchBox.Text)
            || SearchBox.Text == "Search ACD, Class 1, command timeout..."
            || SearchBox.Text == "Cari ACD, Class 1, command timeout...")
        {
            SearchBox.Text = _language == "id" ? "Cari ACD, Class 1, command timeout..." : "Search ACD, Class 1, command timeout...";
        }
    }

    private object BuildOnlineButtonContent(string label)
    {
        var path = new System.Windows.Shapes.Path
        {
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform,
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = Brushes.Transparent,
            Data = (Geometry)FindResource("LucideCircleChevronRight"),
            Margin = new Thickness(0, 0, 7, 0)
        };
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new System.Windows.Data.Binding("Foreground") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { path, new TextBlock { Text = label } }
        };
    }

    private void RefreshTopicList()
    {
        var rawSearch = SearchBox.Text ?? string.Empty;
        if (rawSearch == "Search ACD, Class 1, command timeout..." || rawSearch == "Cari ACD, Class 1, command timeout...")
        {
            rawSearch = string.Empty;
        }

        var terms = rawSearch.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var items = _topics
            .Where(topic => terms.Length == 0 || terms.All(term => Matches(topic, term)))
            .Select(topic => new HelpTopicListItem(
                topic.Key,
                topic.Icon,
                Pick(topic.CategoryEn, topic.CategoryId),
                Pick(topic.TitleEn, topic.TitleId),
                Pick(topic.SummaryEn, topic.SummaryId)))
            .ToList();

        TopicListBox.ItemsSource = items;
        if (items.Count > 0 && !items.Any(item => string.Equals(item.Key, _selectedTopicKey, StringComparison.OrdinalIgnoreCase)))
        {
            _selectedTopicKey = items[0].Key;
        }
    }

    private bool Matches(HelpTopic topic, string term)
    {
        return topic.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
            || topic.CategoryEn.Contains(term, StringComparison.OrdinalIgnoreCase)
            || topic.CategoryId.Contains(term, StringComparison.OrdinalIgnoreCase)
            || topic.TitleEn.Contains(term, StringComparison.OrdinalIgnoreCase)
            || topic.TitleId.Contains(term, StringComparison.OrdinalIgnoreCase)
            || topic.SummaryEn.Contains(term, StringComparison.OrdinalIgnoreCase)
            || topic.SummaryId.Contains(term, StringComparison.OrdinalIgnoreCase)
            || topic.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private void RenderTopic(HelpTopic topic)
    {
        TopicKickerText.Text = Pick(topic.CategoryEn, topic.CategoryId);
        TopicTitleText.Text = Pick(topic.TitleEn, topic.TitleId);
        TopicSummaryText.Text = Pick(topic.SummaryEn, topic.SummaryId);
        TopicHeroIconText.Text = topic.Icon;
        TopicContentPanel.Children.Clear();

        foreach (var section in topic.Sections)
        {
            TopicContentPanel.Children.Add(BuildSectionCard(section));
        }
    }

    private Border BuildSectionCard(HelpSection section)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = Pick(section.HeadingEn, section.HeadingId),
            FontSize = 14.8,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("Ink900Brush"),
            TextWrapping = TextWrapping.Wrap
        });

        foreach (var paragraph in PickList(section.ParagraphsEn, section.ParagraphsId))
        {
            body.Children.Add(new TextBlock
            {
                Text = paragraph,
                FontSize = 12.4,
                Foreground = (Brush)FindResource("Ink600Brush"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0),
                LineHeight = 19
            });
        }

        var bullets = PickList(section.BulletsEn, section.BulletsId).ToArray();
        if (bullets.Length > 0)
        {
            var bulletPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var bullet in bullets)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
                row.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = (Brush)FindResource("AccentBrush"),
                    Margin = new Thickness(0, 5, 9, 0)
                });
                row.Children.Add(new TextBlock
                {
                    Text = bullet,
                    FontSize = 12.2,
                    Foreground = (Brush)FindResource("Ink700Brush"),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 18,
                    MaxWidth = 660
                });
                bulletPanel.Children.Add(row);
            }
            body.Children.Add(bulletPanel);
        }

        var frames = PickList(section.FrameLinesEn ?? Array.Empty<string>(), section.FrameLinesId ?? Array.Empty<string>()).ToArray();
        if (frames.Length > 0)
        {
            var code = new StackPanel();
            foreach (var line in frames)
            {
                code.Children.Add(new TextBlock
                {
                    Text = line,
                    FontFamily = (FontFamily)FindResource("TraceMonoFont"),
                    FontSize = 12,
                    Foreground = (Brush)FindResource("Ink800Brush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 2)
                });
            }
            body.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(246, 250, 255)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(224, 234, 254)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 11, 0, 0),
                Child = code
            });
        }

        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(230, 239, 251)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(15),
            Margin = new Thickness(0, 0, 0, 13),
            Child = body
        };
    }

    private string Pick(string en, string id) => _language == "id" ? id : en;

    private IReadOnlyList<string> PickList(IReadOnlyList<string> en, IReadOnlyList<string> id) => _language == "id" ? id : en;

    private static List<HelpTopic> CreateTopics()
    {
        return new List<HelpTopic>
        {
            new(
                "overview",
                "◎",
                "Start here", "Mulai dari sini",
                "How to use the analyzer without losing the field workflow.",
                "Cara memakai analyzer tanpa kehilangan alur kerja lapangan.",
                "overview help start workflow setup values events trace report",
                new[]
                {
                    new HelpSection(
                        "The normal workflow",
                        "Alur kerja normal",
                        new[] { "Start with Setup, connect to the device, then read Values, Events, Trace, Smart Findings, and Report. The app is designed as an evidence workspace: each view should answer one practical question." },
                        new[] { "Mulai dari Setup, connect ke device, lalu baca Values, Events, Trace, Smart Findings, dan Report. Aplikasi ini didesain sebagai evidence workspace: setiap view menjawab satu pertanyaan praktis." },
                        new[] { "Values answers: what data points are alive?", "Events answers: what changed and when?", "Trace answers: what bytes actually moved?", "Smart Findings answers: what is likely wrong and what should be checked next?", "Report answers: how do I hand over the evidence?" },
                        new[] { "Values menjawab: data point apa yang hidup?", "Events menjawab: apa yang berubah dan kapan?", "Trace menjawab: byte apa yang benar-benar bergerak?", "Smart Findings menjawab: masalah yang mungkin terjadi dan apa yang harus dicek berikutnya?", "Report menjawab: bagaimana evidence diserahkan?" }),
                    new HelpSection(
                        "Field habit",
                        "Kebiasaan lapangan",
                        new[] { "When something looks wrong, do not jump straight to the mapping table. First verify traffic direction, link control, CA, IOA, COT, and quality flags from the Trace view." },
                        new[] { "Saat sesuatu terlihat salah, jangan langsung lompat ke mapping table. Verifikasi dulu arah traffic, link control, CA, IOA, COT, dan quality flags dari Trace view." },
                        Array.Empty<string>(),
                        Array.Empty<string>())
                }),
            new(
                "setup",
                "⚙",
                "Connection", "Koneksi",
                "Setup ports, protocol mode, addresses, and polling profile.",
                "Atur port, mode protokol, address, dan profil polling.",
                "setup connection serial tcp iec101 iec103 iec104 port link address common address",
                new[]
                {
                    new HelpSection(
                        "Before connecting",
                        "Sebelum connect",
                        new[] { "Confirm the protocol mode first. IEC-101 and IEC-103 usually use serial parameters; IEC-104 uses TCP. A correct serial port with a wrong address still looks like a communication problem, so keep addressing visible in every test note." },
                        new[] { "Pastikan mode protokol lebih dulu. IEC-101 dan IEC-103 biasanya memakai parameter serial; IEC-104 memakai TCP. Port serial yang benar tapi address salah tetap terlihat seperti masalah komunikasi, jadi address harus selalu dicatat." },
                        new[] { "IEC-101: link address and common address are different concepts.", "IEC-103: relay event interpretation depends on function/type mapping.", "IEC-104: verify IP, port, STARTDT state, and sequence health." },
                        new[] { "IEC-101: link address dan common address adalah konsep berbeda.", "IEC-103: interpretasi event relay bergantung pada function/type mapping.", "IEC-104: cek IP, port, status STARTDT, dan kesehatan sequence." }),
                    new HelpSection(
                        "Clean test note",
                        "Catatan test yang bersih",
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        new[] { "Protocol: IEC-101 unbalanced", "Port: COM5, 9600 8E1", "Link address: 1", "Common address: 105", "Expected IOA range: 1..250" },
                        new[] { "Protocol: IEC-101 unbalanced", "Port: COM5, 9600 8E1", "Link address: 1", "Common address: 105", "Expected IOA range: 1..250" })
                }),
            new(
                "frame-trace",
                "◌",
                "Frame Trace", "Frame Trace",
                "Read direction, control field, CA, IOA, COT, ACD, and DFC from the real traffic.",
                "Baca arah, control field, CA, IOA, COT, ACD, dan DFC dari traffic asli.",
                "trace frame acd dfc prm fcb fcv ca ioa cot raw hex",
                new[]
                {
                    new HelpSection(
                        "What to read first",
                        "Yang dibaca pertama",
                        new[] { "A frame trace is not only hex. It tells you who spoke, which address was used, whether the slave has pending Class 1 data, and whether the application response is normal." },
                        new[] { "Frame trace bukan hanya hex. Di sana terlihat siapa yang bicara, address apa yang dipakai, apakah slave punya Class 1 pending, dan apakah response aplikasi normal." },
                        new[] { "TX/RX: traffic direction from the analyzer point of view.", "Control field: link-layer behavior such as PRM, ACD, DFC, FCB, FCV.", "CA: common address of ASDU; wrong CA often looks like silent data.", "IOA: information object address; wrong IOA often looks like bad mapping." },
                        new[] { "TX/RX: arah traffic dari sudut pandang analyzer.", "Control field: perilaku link-layer seperti PRM, ACD, DFC, FCB, FCV.", "CA: common address ASDU; CA salah sering terlihat seperti data diam.", "IOA: information object address; IOA salah sering terlihat seperti mapping salah." }),
                    new HelpSection(
                        "Normal quick reading",
                        "Pembacaan cepat yang normal",
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        new[] { "TX  Request status / Class 1 / Class 2", "RX  ACD=1 means Class 1 data is pending", "TX  Request Class 1", "RX  Event or priority ASDU", "TX  Request Class 2", "RX  Background/process data" },
                        new[] { "TX  Request status / Class 1 / Class 2", "RX  ACD=1 berarti data Class 1 sedang pending", "TX  Request Class 1", "RX  Event atau ASDU prioritas", "TX  Request Class 2", "RX  Background/process data" })
                }),
            new(
                "acd-dfc",
                "A1",
                "IEC-101", "IEC-101",
                "Understand ACD and DFC without guessing from the slave behavior.",
                "Memahami ACD dan DFC tanpa menebak dari perilaku slave.",
                "acd dfc access demand flow control class 1 slave pending busy",
                new[]
                {
                    new HelpSection(
                        "ACD in practical words",
                        "ACD secara praktis",
                        new[] { "ACD=1 means the slave/outstation has Class 1 data waiting. The master should not ignore it forever. In a healthy scan, the master drains Class 1 data and then continues background/Class 2 polling." },
                        new[] { "ACD=1 berarti slave/outstation memiliki data Class 1 yang menunggu. Master tidak boleh mengabaikannya terus-menerus. Pada scan sehat, master mengambil Class 1 lalu lanjut ke polling background/Class 2." },
                        new[] { "If ACD stays high, check whether event traffic is flooding the link.", "If command response is slow while ACD is always high, Class 1 congestion is a strong suspect.", "ACD is returned by the secondary station; it is not a command from the master." },
                        new[] { "Jika ACD terus tinggi, cek apakah event traffic membanjiri link.", "Jika command lambat saat ACD selalu tinggi, Class 1 congestion patut dicurigai.", "ACD dikirim oleh secondary station; bukan command dari master." }),
                    new HelpSection(
                        "DFC in practical words",
                        "DFC secara praktis",
                        new[] { "DFC=1 tells the master that the secondary station cannot accept more data at that moment. Treat it as a traffic/availability warning, not as a mapping problem." },
                        new[] { "DFC=1 memberi tahu master bahwa secondary station belum siap menerima data lanjutan saat itu. Anggap ini sebagai warning traffic/availability, bukan masalah mapping." },
                        new[] { "If DFC appears often, lower polling pressure and check RTU/relay load.", "Do not solve DFC by only changing IOA mapping.", "Record the time pattern: DFC during GI, command, or cyclic scan means different things." },
                        new[] { "Jika DFC sering muncul, turunkan tekanan polling dan cek load RTU/relay.", "Jangan menyelesaikan DFC hanya dengan mengganti IOA mapping.", "Catat polanya: DFC saat GI, command, atau cyclic scan artinya bisa berbeda." })
                }),
            new(
                "class-polling",
                "C1",
                "IEC-101", "IEC-101",
                "How Class 1 and Class 2 polling should behave in a clean master scan.",
                "Bagaimana Class 1 dan Class 2 polling seharusnya berjalan pada scan master yang bersih.",
                "class 1 class 2 polling master slave request scan event background cyclic spontaneous",
                new[]
                {
                    new HelpSection(
                        "Class 1 is priority traffic",
                        "Class 1 adalah traffic prioritas",
                        new[] { "Class 1 normally carries priority data such as events, state changes, and responses that should not wait behind slow background scans." },
                        new[] { "Class 1 biasanya membawa data prioritas seperti event, perubahan status, dan response yang tidak seharusnya tertahan oleh scan background." },
                        new[] { "Poll Class 1 when the slave indicates pending priority data.", "Do not force all cyclic analog data into Class 1.", "Too much Class 1 traffic can make commands feel slow even when the link is alive." },
                        new[] { "Poll Class 1 saat slave menunjukkan data prioritas pending.", "Jangan memaksa semua analog cyclic masuk ke Class 1.", "Class 1 terlalu ramai bisa membuat command terasa lambat walaupun link hidup." }),
                    new HelpSection(
                        "Class 2 is background traffic",
                        "Class 2 adalah traffic background",
                        new[] { "Class 2 is normally used for background/process data. A healthy master balances Class 1 urgency with Class 2 completeness." },
                        new[] { "Class 2 biasanya dipakai untuk data background/process. Master yang sehat menyeimbangkan urgensi Class 1 dan kelengkapan Class 2." },
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        new[] { "Master: request Class 2", "Slave : returns process/background data", "Master: sees ACD=1", "Master: request Class 1", "Slave : returns pending event", "Master: resumes Class 2" },
                        new[] { "Master: request Class 2", "Slave : mengirim data process/background", "Master: melihat ACD=1", "Master: request Class 1", "Slave : mengirim pending event", "Master: lanjut Class 2" })
                }),
            new(
                "values",
                "IOA",
                "Values", "Values",
                "Diagnose live values, quality, common address, and IOA mapping.",
                "Mendiagnosa live values, quality, common address, dan IOA mapping.",
                "values ioa mapping quality invalid substituted non topical common address ca",
                new[]
                {
                    new HelpSection(
                        "Use Values as the map sanity check",
                        "Gunakan Values sebagai sanity check mapping",
                        new[] { "Values should confirm whether your IOA profile matches the device. If frames are present but values stay empty, inspect CA and IOA before blaming the transport." },
                        new[] { "Values memastikan apakah profil IOA cocok dengan device. Jika frame ada tetapi values kosong, cek CA dan IOA sebelum menyalahkan transport." },
                        new[] { "Wrong CA: the ASDU belongs to another common address.", "Wrong IOA: the point exists but is not mapped to the expected signal.", "Bad quality: the value may be present but not trustworthy." },
                        new[] { "CA salah: ASDU milik common address lain.", "IOA salah: point ada tetapi tidak cocok dengan signal yang diharapkan.", "Quality buruk: value mungkin ada tetapi tidak layak dipercaya." })
                }),
            new(
                "events",
                "EV",
                "Events", "Events",
                "Understand event changes, timestamps, causes of transmission, and quality.",
                "Memahami event, timestamp, cause of transmission, dan quality.",
                "events timestamp cot cause of transmission spontaneous activation quality relay event",
                new[]
                {
                    new HelpSection(
                        "Events are not just alarm rows",
                        "Events bukan hanya baris alarm",
                        new[] { "Events tell you whether the device is reporting meaningful changes or only responding to polling. Always compare event time, COT, and the related Trace frame." },
                        new[] { "Events menunjukkan apakah device mengirim perubahan bermakna atau hanya menjawab polling. Selalu bandingkan event time, COT, dan frame Trace terkait." },
                        new[] { "Spontaneous COT usually means the field state changed.", "Activation confirmation belongs to command or interrogation workflow.", "Quality flags can explain why a value is present but should not be trusted." },
                        new[] { "COT spontaneous biasanya berarti status lapangan berubah.", "Activation confirmation terkait command atau interrogation workflow.", "Quality flags menjelaskan mengapa value ada tetapi tidak bisa dipercaya." })
                }),
            new(
                "smart-findings",
                "✓",
                "Smart Findings", "Smart Findings",
                "Use findings as a field checklist, not as blind magic.",
                "Gunakan findings sebagai checklist lapangan, bukan sebagai magic yang dipercaya buta.",
                "smart findings solution ca mismatch unknown ioa command timeout class 1 congestion gi incomplete",
                new[]
                {
                    new HelpSection(
                        "How to read a finding",
                        "Cara membaca finding",
                        new[] { "Each finding should be read as: problem, why it matters, proof, fix, and retest. The proof matters most because it points back to real traffic." },
                        new[] { "Setiap finding dibaca sebagai: problem, mengapa penting, proof, fix, dan retest. Proof paling penting karena menunjuk ke traffic nyata." },
                        new[] { "Do not fix by title only; open the related frame.", "If the finding says CA mismatch, compare configured CA with observed ASDU CA.", "If the finding says command slow, compare command timing against Class 1 pressure." },
                        new[] { "Jangan memperbaiki hanya dari judul; buka frame terkait.", "Jika finding menyebut CA mismatch, bandingkan CA konfigurasi dengan CA ASDU yang terlihat.", "Jika finding menyebut command slow, bandingkan timing command dengan tekanan Class 1." })
                }),
            new(
                "report",
                "PDF",
                "Report", "Report",
                "Export a clean evidence PDF for FAT, SAT, commissioning, and handover.",
                "Export evidence PDF yang rapi untuk FAT, SAT, commissioning, dan handover.",
                "report pdf evidence export fat sat handover findings trace summary",
                new[]
                {
                    new HelpSection(
                        "What a good report should contain",
                        "Isi report yang baik",
                        new[] { "A report is useful when it contains the session summary, evidence coverage, smart findings, key protocol frames, and enough trace appendix to prove what happened." },
                        new[] { "Report berguna jika memuat session summary, evidence coverage, smart findings, frame protokol penting, dan trace appendix yang cukup untuk membuktikan kejadian." },
                        new[] { "Export after the session contains enough representative traffic.", "Include command test frames when reporting command delay.", "Use Smart Findings as the executive explanation, then keep trace rows as proof." },
                        new[] { "Export setelah session memiliki traffic yang cukup representatif.", "Sertakan frame command saat melaporkan command delay.", "Gunakan Smart Findings sebagai penjelasan eksekutif, lalu trace rows sebagai proof." })
                }),
            new(
                "dual-link",
                "DL",
                "IEC-101 Dual Link", "IEC-101 Dual Link",
                "Read primary/backup link behavior without mixing the evidence.",
                "Membaca perilaku primary/backup link tanpa mencampur evidence.",
                "dual link redundancy primary backup iec101 rtu failover timeline link a link b",
                new[]
                {
                    new HelpSection(
                        "What to watch",
                        "Yang perlu dilihat",
                        new[] { "In dual-link tests, the question is not only whether the RTU responds. You need to see which link answered, whether the backup is silent or healthy, and how the timeline behaves during failover." },
                        new[] { "Pada test dual-link, pertanyaannya bukan hanya apakah RTU merespons. Anda perlu melihat link mana yang menjawab, apakah backup diam atau sehat, dan bagaimana timeline saat failover." },
                        new[] { "Keep primary and backup evidence visually separated.", "Check whether ACD/Class 1 behavior appears on one link or both links.", "During failover, compare silence period, first response, and recovered scan pattern." },
                        new[] { "Pisahkan evidence primary dan backup secara visual.", "Cek apakah ACD/Class 1 muncul di salah satu link atau kedua link.", "Saat failover, bandingkan periode silent, response pertama, dan pola scan setelah recovery." })
                })
        };
    }

    public sealed record HelpTopicListItem(string Key, string Icon, string Category, string Title, string Summary);

    private sealed record HelpTopic(
        string Key,
        string Icon,
        string TitleEn,
        string TitleId,
        string SummaryEn,
        string SummaryId,
        string SearchText,
        IReadOnlyList<HelpSection> Sections)
    {
        public string CategoryEn => Key switch
        {
            "overview" => "Quick Help",
            "setup" => "Connection",
            "frame-trace" => "Trace",
            "acd-dfc" => "IEC-101",
            "class-polling" => "IEC-101",
            "values" => "Values",
            "events" => "Events",
            "smart-findings" => "Findings",
            "report" => "Report",
            "dual-link" => "Redundancy",
            _ => "Help"
        };

        public string CategoryId => Key switch
        {
            "overview" => "Bantuan Cepat",
            "setup" => "Koneksi",
            "frame-trace" => "Trace",
            "acd-dfc" => "IEC-101",
            "class-polling" => "IEC-101",
            "values" => "Values",
            "events" => "Events",
            "smart-findings" => "Findings",
            "report" => "Report",
            "dual-link" => "Redundancy",
            _ => "Help"
        };
    }

    private sealed record HelpSection(
        string HeadingEn,
        string HeadingId,
        IReadOnlyList<string> ParagraphsEn,
        IReadOnlyList<string> ParagraphsId,
        IReadOnlyList<string> BulletsEn,
        IReadOnlyList<string> BulletsId,
        IReadOnlyList<string>? FrameLinesEn = null,
        IReadOnlyList<string>? FrameLinesId = null);
}
