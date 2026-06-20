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
            ModernMessageBox.Show(this, $"Unable to open the online Field Wiki.\n\n{ex.Message}", "Open Field Wiki", MessageBoxButton.OK, MessageBoxImage.Information);
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
        static string[] A(params string[] values) => values;

        return new List<HelpTopic>
        {
            new(
                "overview",
                "◎",
                "Start here", "Mulai dari sini",
                "The shortest path from connection to evidence-ready conclusions.",
                "Jalur paling singkat dari koneksi sampai kesimpulan evidence-ready.",
                "overview help start workflow evidence values events trace report fat sat commissioning",
                new[]
                {
                    new HelpSection(
                        "The field workflow",
                        "Alur kerja lapangan",
                        A("Use ARIEC60870 as an evidence workspace. Start with a clean connection, let the session collect enough frames, then read Values, Events, Trace, Smart Findings, and Report in that order."),
                        A("Gunakan ARIEC60870 sebagai evidence workspace. Mulai dari koneksi yang bersih, biarkan session mengumpulkan frame yang cukup, lalu baca Values, Events, Trace, Smart Findings, dan Report berurutan."),
                        A("Values answers: which points are alive and trusted?", "Events answers: what changed and when?", "Trace answers: what bytes actually moved?", "Smart Findings answers: what is likely wrong and where is the proof?", "Report answers: how to hand over the evidence cleanly."),
                        A("Values menjawab: point mana yang hidup dan bisa dipercaya?", "Events menjawab: apa yang berubah dan kapan?", "Trace menjawab: byte apa yang benar-benar bergerak?", "Smart Findings menjawab: masalah apa yang mungkin terjadi dan di mana proof-nya?", "Report menjawab: bagaimana evidence diserahkan dengan rapi."),
                        A(),
                        A()),
                    new HelpSection(
                        "Do not diagnose from one screen only",
                        "Jangan diagnosa dari satu layar saja",
                        A("A live value may look wrong because mapping is wrong. A command may look slow because Class 1 is flooded. A silent device may actually be answering with another common address. Always connect the symptom back to the frame evidence."),
                        A("Live value bisa terlihat salah karena mapping salah. Command bisa terasa lambat karena Class 1 terlalu ramai. Device yang tampak diam bisa saja menjawab dengan common address lain. Selalu hubungkan gejala dengan frame evidence."),
                        A("When Values is empty, open Trace and check CA/IOA.", "When Events looks noisy, check COT and quality.", "When command is slow, compare command rows with Class 1 pressure."),
                        A("Saat Values kosong, buka Trace dan cek CA/IOA.", "Saat Events terlalu ramai, cek COT dan quality.", "Saat command lambat, bandingkan row command dengan tekanan Class 1."),
                        A(),
                        A()),
                    new HelpSection(
                        "A good evidence session",
                        "Session evidence yang baik",
                        A("A useful session contains representative traffic: startup, polling, interrogation, at least one event or value change, one command if tested, and enough trace rows before and after the issue."),
                        A("Session yang berguna berisi traffic representatif: startup, polling, interrogation, minimal satu event atau perubahan value, satu command jika diuji, serta trace sebelum dan sesudah masalah."),
                        A("Capture the normal baseline before forcing a fault.", "Keep notes about device address, mapping profile, and test condition.", "Export the PDF only after the trace contains enough proof."),
                        A("Capture baseline normal sebelum memaksa fault.", "Catat address device, mapping profile, dan kondisi test.", "Export PDF setelah trace memiliki proof yang cukup."),
                        A(),
                        A())
                }),
            new(
                "setup",
                "⚙",
                "Connection and setup", "Koneksi dan setup",
                "Ports, protocol mode, link address, common address, and scan profile.",
                "Port, mode protokol, link address, common address, dan profil scan.",
                "setup connection serial tcp port baud parity link address common address protocol mode iec101 iec103 iec104",
                new[]
                {
                    new HelpSection(
                        "Before pressing Connect",
                        "Sebelum menekan Connect",
                        A("Confirm the protocol first. IEC-101 and IEC-103 usually depend on serial timing and address configuration. IEC-104 depends on TCP reachability and data-transfer state. A correct cable with a wrong address still looks like a communication problem."),
                        A("Pastikan protokol lebih dulu. IEC-101 dan IEC-103 biasanya bergantung pada timing serial dan konfigurasi address. IEC-104 bergantung pada TCP reachability dan status data-transfer. Kabel benar tetapi address salah tetap terlihat seperti masalah komunikasi."),
                        A("IEC-101: link address and common address are different.", "IEC-103: relay interpretation depends on FUN/INF or profile mapping.", "IEC-104: verify IP, port, STARTDT state, and sequence health."),
                        A("IEC-101: link address dan common address berbeda.", "IEC-103: interpretasi relay bergantung pada FUN/INF atau profile mapping.", "IEC-104: cek IP, port, STARTDT, dan sequence health."),
                        A(),
                        A()),
                    new HelpSection(
                        "Common setup mistakes",
                        "Kesalahan setup yang sering terjadi",
                        A("Most early failures are not deep protocol problems. They are address, serial, or profile mistakes. Keep the first test simple: connect, observe link behavior, perform one clean polling cycle, then add more complex tests."),
                        A("Sebagian besar kegagalan awal bukan masalah protokol yang dalam. Biasanya address, serial, atau profile. Buat test pertama sederhana: connect, lihat link behavior, jalankan satu siklus polling bersih, baru tambah test kompleks."),
                        A("Wrong baud/parity causes no valid frames.", "Wrong link address causes no useful secondary response.", "Wrong common address can produce frames that do not map to expected values.", "Wrong profile size can shift CA/IOA/COT interpretation."),
                        A("Baud/parity salah membuat frame tidak valid.", "Link address salah membuat secondary response tidak berguna.", "Common address salah bisa menghasilkan frame yang tidak masuk ke values yang diharapkan.", "Ukuran profile salah bisa menggeser interpretasi CA/IOA/COT."),
                        A(),
                        A()),
                    new HelpSection(
                        "Clean first test sequence",
                        "Urutan test awal yang bersih",
                        A("A clean first session should prove that the link is alive, the application address is correct, and the device can provide process data. Do not start with many commands."),
                        A("Session awal yang bersih harus membuktikan link hidup, application address benar, dan device bisa memberi process data. Jangan mulai dengan banyak command."),
                        A("Connect and wait for status/polling frames.", "Run General Interrogation if available.", "Check Values and Events.", "Open Trace and verify CA, IOA, COT, ACD, and DFC."),
                        A("Connect dan tunggu status/polling frame.", "Jalankan General Interrogation jika tersedia.", "Cek Values dan Events.", "Buka Trace dan verifikasi CA, IOA, COT, ACD, dan DFC."),
                        A(),
                        A())
                }),
            new(
                "frame-trace",
                "HEX",
                "Reading the frame trace", "Membaca frame trace",
                "Turn raw bytes into direction, address, COT, ACD/DFC, and field meaning.",
                "Ubah byte mentah menjadi arah, address, COT, ACD/DFC, dan makna lapangan.",
                "frame trace hex raw bytes tx rx control field ca ioa cot acd dfc class polling",
                new[]
                {
                    new HelpSection(
                        "Trace is the ground truth",
                        "Trace adalah ground truth",
                        A("If the UI value looks wrong, the trace tells whether the problem is transport, address, mapping, quality, or workflow. Read the trace before changing configuration randomly."),
                        A("Jika value UI terlihat salah, trace menunjukkan apakah masalahnya transport, address, mapping, quality, atau workflow. Baca trace sebelum mengubah konfigurasi secara acak."),
                        A("TX/RX shows direction from the analyzer point of view.", "Control field shows link behavior: PRM, ACD, DFC, FCB, FCV.", "COT explains why the ASDU was sent.", "CA and IOA tell whether the data belongs to the expected device and point."),
                        A("TX/RX menunjukkan arah dari sudut pandang analyzer.", "Control field menunjukkan perilaku link: PRM, ACD, DFC, FCB, FCV.", "COT menjelaskan mengapa ASDU dikirim.", "CA dan IOA menunjukkan apakah data milik device dan point yang diharapkan."),
                        A(),
                        A()),
                    new HelpSection(
                        "Normal IEC-101 polling pattern",
                        "Pola polling IEC-101 normal",
                        A("A master does not need to request Class 1 forever. In a healthy scan, it checks priority data, drains it when ACD is set, and continues background Class 2 or interrogation tasks."),
                        A("Master tidak perlu request Class 1 terus-menerus. Pada scan sehat, master mengecek data prioritas, mengambilnya saat ACD aktif, lalu lanjut ke Class 2 background atau interrogation."),
                        A("If ACD=0, there may be no pending priority data.", "If ACD=1, the master should drain Class 1 in a bounded way.", "If Class 1 never empties, look for event flood or wrong classification."),
                        A("Jika ACD=0, mungkin tidak ada data prioritas pending.", "Jika ACD=1, master sebaiknya drain Class 1 secara terbatas.", "Jika Class 1 tidak pernah kosong, cari event flood atau klasifikasi data yang salah."),
                        A("TX  Request Class 2 / background poll", "RX  Process data, ACD=0", "RX  Later response shows ACD=1", "TX  Request Class 1", "RX  Event / priority ASDU", "TX  Resume Class 2"),
                        A("TX  Request Class 2 / background poll", "RX  Process data, ACD=0", "RX  Response berikutnya menunjukkan ACD=1", "TX  Request Class 1", "RX  Event / ASDU prioritas", "TX  Lanjut Class 2")),
                    new HelpSection(
                        "What to check when trace is confusing",
                        "Yang dicek saat trace membingungkan",
                        A("Do not read only the hex. Use the decoded columns as a checklist and then open the related frame details when something does not match expectation."),
                        A("Jangan hanya membaca hex. Gunakan kolom decode sebagai checklist lalu buka detail frame terkait saat ada yang tidak sesuai ekspektasi."),
                        A("Direction: who sent the frame?", "Address: is link address or CA wrong?", "COT: is this spontaneous, request, activation, or termination?", "Quality: is the value invalid, old, blocked, substituted, or overflow?"),
                        A("Direction: siapa yang mengirim frame?", "Address: apakah link address atau CA salah?", "COT: apakah spontaneous, request, activation, atau termination?", "Quality: apakah value invalid, old, blocked, substituted, atau overflow?"),
                        A(),
                        A())
                }),
            new(
                "addressing",
                "CA",
                "Link address, CA, and IOA", "Link address, CA, dan IOA",
                "The three address layers that often get mixed during commissioning.",
                "Tiga lapisan address yang sering tertukar saat commissioning.",
                "address link address common address ca ioa information object wrong ca unknown ioa mapping",
                new[]
                {
                    new HelpSection(
                        "Do not mix address layers",
                        "Jangan mencampur lapisan address",
                        A("IEC-101 has more than one address concept. The link address selects the secondary station at the link layer. The common address belongs to the ASDU/application layer. The IOA identifies the point inside that common address."),
                        A("IEC-101 memiliki lebih dari satu konsep address. Link address memilih secondary station di link layer. Common address berada di ASDU/application layer. IOA mengidentifikasi point di dalam common address itu."),
                        A("Link address wrong: the expected station may not answer.", "Common address wrong: frames may exist but belong to another application address.", "IOA wrong: the device answers, but the point maps to the wrong signal or stays empty."),
                        A("Link address salah: station yang diharapkan mungkin tidak menjawab.", "Common address salah: frame bisa ada tetapi milik application address lain.", "IOA salah: device menjawab, tetapi point masuk signal salah atau kosong."),
                        A(),
                        A()),
                    new HelpSection(
                        "Typical symptom patterns",
                        "Pola gejala umum",
                        A("Addressing errors are painful because the link can look alive while the engineering value is still wrong. Use Trace to separate link-level health from application-level mapping."),
                        A("Error addressing menyakitkan karena link bisa terlihat hidup sementara engineering value tetap salah. Gunakan Trace untuk memisahkan kesehatan link-level dari mapping application-level."),
                        A("No secondary response: suspect serial, link address, or physical layer.", "Response with unexpected CA: suspect common address mismatch.", "Response with expected CA but no value: suspect IOA mapping or type mismatch.", "Value present but unreliable: inspect quality flags."),
                        A("Tidak ada secondary response: curigai serial, link address, atau physical layer.", "Response dengan CA tidak sesuai: curigai common address mismatch.", "Response dengan CA sesuai tetapi value kosong: curigai IOA mapping atau type mismatch.", "Value ada tetapi tidak reliable: cek quality flags."),
                        A(),
                        A()),
                    new HelpSection(
                        "Retest checklist",
                        "Checklist retest",
                        A("After changing address or mapping, collect a short fresh session. Do not reuse old evidence because it may still reflect the previous profile."),
                        A("Setelah mengubah address atau mapping, ambil session baru yang singkat. Jangan memakai evidence lama karena bisa masih mencerminkan profile sebelumnya."),
                        A("Clear the session or start a new one.", "Run GI or background scan.", "Check the first valid CA and IOA in Trace.", "Confirm Values and Events match the expected point list."),
                        A("Clear session atau mulai baru.", "Jalankan GI atau background scan.", "Cek CA dan IOA valid pertama di Trace.", "Pastikan Values dan Events cocok dengan daftar point yang diharapkan."),
                        A(),
                        A())
                }),
            new(
                "acd-dfc",
                "A/D",
                "ACD and DFC", "ACD dan DFC",
                "What ACD=1 and DFC=1 mean in real master/slave traffic.",
                "Arti ACD=1 dan DFC=1 pada traffic master/slave yang nyata.",
                "acd dfc access demand data flow control class 1 pending device busy slave secondary station",
                new[]
                {
                    new HelpSection(
                        "ACD in practical words",
                        "ACD secara praktis",
                        A("ACD means Access Demand. When ACD=1 appears in a secondary response, the slave/outstation is telling the master that priority Class 1 data is waiting. The master should not ignore it forever."),
                        A("ACD berarti Access Demand. Saat ACD=1 muncul pada secondary response, slave/outstation memberi tahu master bahwa data prioritas Class 1 sedang menunggu. Master tidak boleh mengabaikannya terus-menerus."),
                        A("ACD is returned by the secondary station.", "ACD=1 does not mean the master sent a command.", "If ACD remains high, inspect Class 1 traffic pressure and event flood."),
                        A("ACD dikirim oleh secondary station.", "ACD=1 bukan berarti master mengirim command.", "Jika ACD terus tinggi, cek tekanan Class 1 dan event flood."),
                        A("RX  Secondary response: ACD=1, DFC=0", "TX  Master requests Class 1", "RX  Event data / priority ASDU", "RX  Later response: ACD=0"),
                        A("RX  Secondary response: ACD=1, DFC=0", "TX  Master request Class 1", "RX  Event data / ASDU prioritas", "RX  Response berikutnya: ACD=0")),
                    new HelpSection(
                        "DFC in practical words",
                        "DFC secara praktis",
                        A("DFC means Data Flow Control. When DFC=1 is returned, the secondary side indicates it is busy or cannot accept more data flow in the normal way. A master should avoid pushing more traffic blindly."),
                        A("DFC berarti Data Flow Control. Saat DFC=1 dikembalikan, sisi secondary menunjukkan sedang sibuk atau tidak siap menerima aliran data normal. Master sebaiknya tidak mendorong traffic secara membabi buta."),
                        A("DFC=1 is a back-pressure signal.", "Check whether the master scan is too aggressive.", "Check serial timing, retry policy, and command burst behavior."),
                        A("DFC=1 adalah sinyal back-pressure.", "Cek apakah scan master terlalu agresif.", "Cek timing serial, retry policy, dan perilaku command burst."),
                        A(),
                        A()),
                    new HelpSection(
                        "When ACD/DFC matters most",
                        "Kapan ACD/DFC paling penting",
                        A("ACD and DFC are small bits, but they explain many field symptoms: command delay, event backlog, intermittent scan, and links that are alive but not responsive enough."),
                        A("ACD dan DFC hanya bit kecil, tetapi menjelaskan banyak gejala lapangan: command delay, event backlog, scan intermittent, dan link yang hidup tetapi kurang responsif."),
                        A("Command slow + ACD often high: Class 1 congestion is possible.", "DFC high during traffic burst: reduce scan pressure and check device capacity.", "ACD never drains: check whether Class 1 is polluted by cyclic analog data."),
                        A("Command lambat + ACD sering tinggi: Class 1 congestion mungkin terjadi.", "DFC tinggi saat traffic burst: kurangi tekanan scan dan cek kapasitas device.", "ACD tidak pernah turun: cek apakah Class 1 tercampur analog cyclic."),
                        A(),
                        A())
                }),
            new(
                "class-polling",
                "C1",
                "Class 1 and Class 2 polling", "Polling Class 1 dan Class 2",
                "How a clean master balances priority events and background data.",
                "Cara master yang bersih menyeimbangkan event prioritas dan data background.",
                "class 1 class 2 polling priority background request master slave cyclic spontaneous congestion",
                new[]
                {
                    new HelpSection(
                        "Class 1 is priority traffic",
                        "Class 1 adalah traffic prioritas",
                        A("Class 1 should carry urgent information: events, state changes, command evidence, and other data that should not wait behind background scans."),
                        A("Class 1 seharusnya membawa informasi urgent: event, perubahan status, evidence command, dan data lain yang tidak boleh tertahan scan background."),
                        A("Poll Class 1 when the slave indicates pending priority data.", "Keep Class 1 bounded so it does not starve the rest of the scan.", "Do not classify every cyclic analog value as Class 1."),
                        A("Poll Class 1 saat slave menunjukkan data prioritas pending.", "Batasi Class 1 agar tidak menghabiskan scan lain.", "Jangan klasifikasikan semua analog cyclic sebagai Class 1."),
                        A(),
                        A()),
                    new HelpSection(
                        "Class 2 is background/process traffic",
                        "Class 2 adalah traffic background/process",
                        A("Class 2 normally carries regular process data. It keeps the picture complete while Class 1 keeps urgent events responsive. The balance matters more than raw polling speed."),
                        A("Class 2 biasanya membawa data process regular. Ia menjaga gambaran sistem tetap lengkap sementara Class 1 menjaga event urgent tetap responsif. Keseimbangan lebih penting daripada polling speed mentah."),
                        A("Use Class 2 for cyclic/background measured values where possible.", "After draining Class 1, resume Class 2.", "If Class 2 is never reached, the master may be trapped in event pressure."),
                        A("Gunakan Class 2 untuk measured value cyclic/background jika memungkinkan.", "Setelah Class 1 diambil, lanjutkan Class 2.", "Jika Class 2 tidak pernah tersentuh, master mungkin terjebak tekanan event."),
                        A(),
                        A()),
                    new HelpSection(
                        "A balanced scan feels responsive",
                        "Scan seimbang terasa responsif",
                        A("A master that polls Class 1 continuously may look active, but it can make engineering work harder because background data, reports, and command response appear delayed."),
                        A("Master yang terus poll Class 1 bisa terlihat aktif, tetapi menyulitkan engineering karena data background, report, dan command response tampak terlambat."),
                        A("Watch the ratio of Class 1/Class 2 in the header.", "Open Trace around command tests.", "If analog rows dominate Class 1, fix the RTU profile."),
                        A("Perhatikan rasio Class 1/Class 2 di header.", "Buka Trace di sekitar test command.", "Jika row analog mendominasi Class 1, perbaiki profile RTU."),
                        A("Healthy: Class 2 → ACD=1 → Class 1 drain → Class 2 resumes", "Risky : Class 1 → Class 1 → Class 1 → command waits behind noise"),
                        A("Sehat : Class 2 → ACD=1 → drain Class 1 → Class 2 lanjut", "Risky : Class 1 → Class 1 → Class 1 → command tertahan noise"))
                }),
            new(
                "general-interrogation",
                "GI",
                "General Interrogation", "General Interrogation",
                "How GI should start, confirm, transfer data, and terminate.",
                "Bagaimana GI dimulai, dikonfirmasi, mengirim data, dan selesai.",
                "general interrogation gi c_ic_na_1 activation actcon actterm interrogation complete timeout",
                new[]
                {
                    new HelpSection(
                        "What GI is for",
                        "Fungsi GI",
                        A("General Interrogation is used to ask the outstation for a full or grouped picture of its data. It is not the same as routine cyclic polling; it is a deliberate application workflow."),
                        A("General Interrogation digunakan untuk meminta gambaran data lengkap atau grup dari outstation. Ini bukan polling cyclic biasa; GI adalah workflow aplikasi yang disengaja."),
                        A("Use GI after connection or after mapping changes.", "Use GI when you need a baseline snapshot.", "Do not confuse GI completion with normal background polling."),
                        A("Gunakan GI setelah connection atau setelah perubahan mapping.", "Gunakan GI saat perlu baseline snapshot.", "Jangan samakan GI completion dengan polling background normal."),
                        A(),
                        A()),
                    new HelpSection(
                        "Expected GI sequence",
                        "Urutan GI yang diharapkan",
                        A("A clean GI has a visible start, confirmation, data transfer, and termination. If ACTTERM never appears, the session may look incomplete even if some values arrived."),
                        A("GI yang bersih memiliki start, confirmation, data transfer, dan termination yang terlihat. Jika ACTTERM tidak pernah muncul, session bisa terlihat incomplete meskipun sebagian value masuk."),
                        A("Activation request starts the GI.", "Activation confirmation tells the master the request was accepted.", "Interrogated data follows.", "Activation termination closes the workflow."),
                        A("Activation request memulai GI.", "Activation confirmation memberi tahu master request diterima.", "Data hasil interrogation menyusul.", "Activation termination menutup workflow."),
                        A("TX  C_IC_NA_1 activation", "RX  ACTCON", "RX  Interrogated data rows", "RX  ACTTERM"),
                        A("TX  C_IC_NA_1 activation", "RX  ACTCON", "RX  Row data hasil interrogation", "RX  ACTTERM")),
                    new HelpSection(
                        "When GI looks incomplete",
                        "Saat GI terlihat incomplete",
                        A("Incomplete GI is often a clue. It may indicate lost frames, wrong CA, device busy state, profile mismatch, or a master workflow that stopped too early."),
                        A("GI incomplete sering menjadi petunjuk. Penyebabnya bisa frame hilang, CA salah, device busy, profile mismatch, atau workflow master berhenti terlalu cepat."),
                        A("Check whether ACTCON arrived.", "Check whether any data rows use unexpected CA.", "Check if DFC or retry pressure appears during GI.", "Retest with smaller traffic pressure if needed."),
                        A("Cek apakah ACTCON masuk.", "Cek apakah ada data row memakai CA tidak sesuai.", "Cek apakah DFC atau retry pressure muncul saat GI.", "Retest dengan tekanan traffic lebih kecil jika perlu."),
                        A(),
                        A())
                }),
            new(
                "command-flow",
                "OP",
                "Command flow", "Alur command",
                "What should happen from select/execute to confirmation and feedback.",
                "Yang seharusnya terjadi dari select/execute sampai confirmation dan feedback.",
                "command flow select execute activation confirmation actcon termination feedback single command double command setpoint timeout",
                new[]
                {
                    new HelpSection(
                        "Command is not just one frame",
                        "Command bukan hanya satu frame",
                        A("A field command is a workflow. The master sends a command, the device confirms or rejects it, the field state may change, and the session should contain enough evidence to prove the result."),
                        A("Command lapangan adalah workflow. Master mengirim command, device mengonfirmasi atau menolak, status lapangan bisa berubah, dan session harus memuat evidence yang cukup untuk membuktikan hasilnya."),
                        A("Select-before-operate requires select then execute.", "Direct operate sends execute directly.", "ACTCON is command acceptance evidence, not always final field feedback.", "Feedback should be checked in Values/Events/Trace."),
                        A("Select-before-operate membutuhkan select lalu execute.", "Direct operate mengirim execute langsung.", "ACTCON adalah evidence command diterima, belum tentu feedback lapangan final.", "Feedback harus dicek di Values/Events/Trace."),
                        A(),
                        A()),
                    new HelpSection(
                        "Expected command evidence",
                        "Evidence command yang diharapkan",
                        A("For FAT/SAT, a command report is weak if it only says the button was clicked. It should show the command frame, confirmation, related feedback, and timing."),
                        A("Untuk FAT/SAT, report command lemah jika hanya menyatakan tombol diklik. Harus terlihat frame command, confirmation, feedback terkait, dan timing."),
                        A("Command TX row exists.", "Device confirmation RX row exists.", "Feedback/event row arrives with expected CA/IOA.", "Quality is valid or intentionally explained.", "Report contains timing and proof rows."),
                        A("Row TX command ada.", "Row RX confirmation dari device ada.", "Row feedback/event datang dengan CA/IOA sesuai.", "Quality valid atau dijelaskan.", "Report memuat timing dan proof rows."),
                        A("TX  Select / execute command", "RX  Activation confirmation", "RX  Feedback event or value change", "RX  Activation termination when applicable"),
                        A("TX  Select / execute command", "RX  Activation confirmation", "RX  Feedback event atau perubahan value", "RX  Activation termination jika berlaku")),
                    new HelpSection(
                        "Common command mistakes",
                        "Kesalahan command umum",
                        A("Command failures are often not caused by the command byte alone. They can be caused by addressing, select/execute mismatch, bad qualifier, device interlock, or traffic congestion."),
                        A("Kegagalan command sering bukan hanya karena byte command. Penyebabnya bisa addressing, mismatch select/execute, qualifier salah, interlock device, atau congestion traffic."),
                        A("Wrong CA/IOA: command targets the wrong application/point.", "Wrong command type: single/double/setpoint mismatch.", "No feedback: command accepted but field state did not change or feedback point is unmapped.", "Slow confirmation: check Class 1 pressure and serial timing."),
                        A("CA/IOA salah: command mengarah ke application/point salah.", "Command type salah: single/double/setpoint mismatch.", "Tidak ada feedback: command diterima tetapi status lapangan tidak berubah atau feedback belum termapping.", "Confirmation lambat: cek tekanan Class 1 dan timing serial."),
                        A(),
                        A())
                }),
            new(
                "command-timeout",
                "ms",
                "Command timeout and slow response", "Command timeout dan response lambat",
                "How to separate protocol delay, device delay, and mapping/feedback mistakes.",
                "Memisahkan delay protokol, delay device, dan kesalahan mapping/feedback.",
                "command timeout slow response latency class 1 congestion feedback missing no actcon no termination",
                new[]
                {
                    new HelpSection(
                        "First separate three different problems",
                        "Pisahkan tiga masalah berbeda",
                        A("A command can fail in different ways: no confirmation, confirmation is slow, or confirmation exists but field feedback is missing. Each case needs a different fix."),
                        A("Command bisa gagal dalam bentuk berbeda: tidak ada confirmation, confirmation lambat, atau confirmation ada tetapi feedback lapangan hilang. Setiap kasus butuh perbaikan berbeda."),
                        A("No ACTCON: check address, command type, qualifier, device readiness.", "ACTCON slow: check Class 1 congestion, serial timing, retry load.", "ACTCON OK but no feedback: check field interlock, feedback IOA, and quality."),
                        A("Tidak ada ACTCON: cek address, command type, qualifier, kesiapan device.", "ACTCON lambat: cek Class 1 congestion, timing serial, retry load.", "ACTCON OK tapi tidak ada feedback: cek interlock lapangan, feedback IOA, dan quality."),
                        A(),
                        A()),
                    new HelpSection(
                        "Class 1 congestion symptom",
                        "Gejala Class 1 congestion",
                        A("If Class 1 is full of cyclic analog values or repeated noisy events, command evidence may wait behind traffic that should not be in the priority lane."),
                        A("Jika Class 1 penuh analog cyclic atau event noise berulang, evidence command bisa tertahan di belakang traffic yang tidak seharusnya berada di jalur prioritas."),
                        A("Look at trace rows after the command TX.", "If analog/Class 1 rows dominate before confirmation, profile cleanup is needed.", "Move cyclic measured values to Class 2/background scan where possible."),
                        A("Lihat row trace setelah command TX.", "Jika row analog/Class 1 mendominasi sebelum confirmation, profile perlu dirapikan.", "Pindahkan measured value cyclic ke Class 2/background scan jika memungkinkan."),
                        A("TX  Command execute", "RX  Analog/Class 1 row", "RX  Analog/Class 1 row", "RX  Analog/Class 1 row", "RX  Late command confirmation"),
                        A("TX  Command execute", "RX  Row analog/Class 1", "RX  Row analog/Class 1", "RX  Row analog/Class 1", "RX  Confirmation command terlambat")),
                    new HelpSection(
                        "Retest after fixing",
                        "Retest setelah perbaikan",
                        A("After profile cleanup, test again with the same command and capture before/after timing. The strongest proof is not a statement; it is a shorter trace path from command to confirmation."),
                        A("Setelah profile dirapikan, uji lagi command yang sama dan capture timing sebelum/sesudah. Proof terkuat bukan pernyataan; proof terkuat adalah trace path yang lebih pendek dari command ke confirmation."),
                        A("Compare command latency before and after.", "Check if Class 1 ratio decreases.", "Confirm feedback arrives with expected CA/IOA.", "Export report with both finding and proof frames."),
                        A("Bandingkan latency command sebelum dan sesudah.", "Cek apakah rasio Class 1 turun.", "Pastikan feedback datang dengan CA/IOA yang benar.", "Export report dengan finding dan proof frames."),
                        A(),
                        A())
                }),
            new(
                "values",
                "IOA",
                "Values, CA, IOA, and quality", "Values, CA, IOA, dan quality",
                "Use Values as the live mapping and trustworthiness check.",
                "Gunakan Values sebagai pengecekan mapping live dan trustworthiness.",
                "values ioa ca quality invalid blocked substituted overflow non topical mapping live point status measurement",
                new[]
                {
                    new HelpSection(
                        "Values proves the profile",
                        "Values membuktikan profile",
                        A("The Values workspace tells whether the incoming protocol data actually lands on the expected engineering points. If trace frames exist but values stay empty, the problem is often CA, IOA, type, or quality—not the cable."),
                        A("Workspace Values menunjukkan apakah data protokol yang masuk benar-benar masuk ke engineering point yang diharapkan. Jika trace ada tetapi values kosong, masalahnya sering CA, IOA, type, atau quality—bukan kabel."),
                        A("Expected CA but wrong IOA: mapping problem.", "Unexpected CA: application address problem.", "Expected IOA but bad quality: value exists but is not trustworthy.", "No relevant trace rows: transport or polling problem."),
                        A("CA sesuai tetapi IOA salah: masalah mapping.", "CA tidak sesuai: masalah application address.", "IOA sesuai tetapi quality buruk: value ada tetapi tidak dipercaya.", "Tidak ada trace relevan: masalah transport atau polling."),
                        A(),
                        A()),
                    new HelpSection(
                        "Use quality before trusting the value",
                        "Gunakan quality sebelum percaya value",
                        A("A number on screen is not automatically good data. Quality flags explain whether the source considers the value invalid, blocked, substituted, overflowed, or not topical."),
                        A("Angka di layar tidak otomatis berarti data bagus. Quality flag menjelaskan apakah sumber menganggap value invalid, blocked, substituted, overflow, atau not topical."),
                        A("Invalid means the value should not be used as valid process evidence.", "Substituted means the value may not come from the real process source.", "Not topical means the value may be old.", "Overflow indicates measurement limit or encoding issue."),
                        A("Invalid berarti value tidak boleh dipakai sebagai process evidence valid.", "Substituted berarti value mungkin bukan dari source proses asli.", "Not topical berarti value mungkin sudah lama.", "Overflow menunjukkan limit measurement atau masalah encoding."),
                        A(),
                        A()),
                    new HelpSection(
                        "Mapping sanity workflow",
                        "Workflow sanity mapping",
                        A("When mapping is suspected, capture a simple repeatable change. A breaker status change or a measured value update is stronger than a static screen."),
                        A("Saat mapping dicurigai, capture perubahan sederhana yang bisa diulang. Perubahan status breaker atau update measured value lebih kuat daripada layar statis."),
                        A("Trigger one known point if safe.", "Watch Events and Values.", "Open the matching Trace row.", "Confirm CA, IOA, Type ID, COT, and quality."),
                        A("Trigger satu point yang diketahui jika aman.", "Pantau Events dan Values.", "Buka row Trace yang cocok.", "Konfirmasi CA, IOA, Type ID, COT, dan quality."),
                        A(),
                        A())
                }),
            new(
                "events",
                "EV",
                "Events, COT, and timestamps", "Events, COT, dan timestamp",
                "Read why data was transmitted, not only what changed.",
                "Baca mengapa data dikirim, bukan hanya apa yang berubah.",
                "events cot cause of transmission spontaneous timestamp activation confirmation termination quality soe relay event",
                new[]
                {
                    new HelpSection(
                        "COT tells why the row exists",
                        "COT menjelaskan mengapa row ada",
                        A("Cause of Transmission is critical. The same IOA can arrive because it changed spontaneously, because the master requested it, because a command was confirmed, or because interrogation is in progress."),
                        A("Cause of Transmission sangat penting. IOA yang sama bisa datang karena berubah spontan, karena diminta master, karena command dikonfirmasi, atau karena interrogation sedang berjalan."),
                        A("Spontaneous: the device reports a change or event.", "Interrogated: the value is part of an interrogation response.", "Activation confirmation: a command or interrogation was accepted.", "Activation termination: a workflow completed."),
                        A("Spontaneous: device melaporkan perubahan atau event.", "Interrogated: value bagian dari response interrogation.", "Activation confirmation: command atau interrogation diterima.", "Activation termination: workflow selesai."),
                        A(),
                        A()),
                    new HelpSection(
                        "Timestamps need context",
                        "Timestamp butuh konteks",
                        A("A timestamped event is only useful when the time source and quality are trusted. Compare event time, analyzer receive time, and device clock behavior if timing accuracy matters."),
                        A("Event bertimestamp hanya berguna jika time source dan quality dipercaya. Bandingkan event time, waktu terima analyzer, dan perilaku clock device jika akurasi timing penting."),
                        A("Check if clock sync was part of the session.", "Check time quality if available.", "Use report trace appendix to prove event order."),
                        A("Cek apakah clock sync ada dalam session.", "Cek time quality jika tersedia.", "Gunakan trace appendix report untuk membuktikan urutan event."),
                        A(),
                        A()),
                    new HelpSection(
                        "Events versus Values",
                        "Events versus Values",
                        A("Values shows current state. Events show change evidence. For commissioning, use both: Values for present condition, Events for transition proof."),
                        A("Values menunjukkan state saat ini. Events menunjukkan bukti perubahan. Untuk commissioning, gunakan keduanya: Values untuk kondisi sekarang, Events untuk proof transisi."),
                        A("If Events changes but Values does not, check mapping refresh.", "If Values changes without Event, check COT/classification.", "If both are absent but Trace exists, check CA/IOA profile."),
                        A("Jika Events berubah tetapi Values tidak, cek refresh mapping.", "Jika Values berubah tanpa Event, cek COT/classification.", "Jika keduanya kosong tetapi Trace ada, cek profile CA/IOA."),
                        A(),
                        A())
                }),
            new(
                "quality-flags",
                "Q",
                "Quality flags", "Quality flags",
                "Why a value can be present but still not acceptable as evidence.",
                "Mengapa value bisa ada tetapi tetap tidak layak menjadi evidence.",
                "quality flags invalid blocked substituted overflow not topical old data trust evidence measurement status",
                new[]
                {
                    new HelpSection(
                        "Quality is part of the data",
                        "Quality adalah bagian dari data",
                        A("Do not treat quality as decoration. Quality tells whether the value can be trusted for engineering decisions and evidence reports."),
                        A("Jangan anggap quality sebagai hiasan. Quality menunjukkan apakah value bisa dipercaya untuk keputusan engineering dan evidence report."),
                        A("Invalid: value should not be trusted.", "Blocked: acquisition or transmission may be intentionally blocked.", "Substituted: value may not come from the real field source.", "Not topical: value may be old.", "Overflow: measurement exceeded range or encoding limit."),
                        A("Invalid: value tidak boleh dipercaya.", "Blocked: acquisition atau transmisi mungkin sengaja diblok.", "Substituted: value mungkin bukan dari source lapangan asli.", "Not topical: value mungkin sudah lama.", "Overflow: measurement melewati range atau limit encoding."),
                        A(),
                        A()),
                    new HelpSection(
                        "How to use quality in reports",
                        "Cara memakai quality di report",
                        A("When a value has bad quality, the report should not present it as a normal successful measurement. Explain the flag and keep the proof frame."),
                        A("Saat value punya quality buruk, report tidak boleh menyajikannya sebagai measurement sukses normal. Jelaskan flag-nya dan simpan proof frame."),
                        A("Mark the value as present but not trustworthy.", "Include the trace row that carries the quality field.", "Retest after fixing device acquisition or mapping."),
                        A("Tandai value sebagai ada tetapi tidak trusted.", "Sertakan trace row yang membawa quality field.", "Retest setelah memperbaiki acquisition device atau mapping."),
                        A(),
                        A())
                }),
            new(
                "smart-findings",
                "✓",
                "Smart Findings and Solution", "Smart Findings and Solution",
                "Read findings as problem, proof, fix, and retest—not as magic.",
                "Baca findings sebagai problem, proof, fix, dan retest—bukan magic.",
                "smart findings solution problem proof fix retest ca mismatch unknown ioa command slow gi incomplete quality",
                new[]
                {
                    new HelpSection(
                        "How to read a finding",
                        "Cara membaca finding",
                        A("A finding is useful only when it points back to traffic evidence. Treat it as a structured field checklist: problem, why it matters, proof, fix, and retest."),
                        A("Finding berguna hanya jika menunjuk balik ke traffic evidence. Perlakukan sebagai checklist lapangan terstruktur: problem, why, proof, fix, dan retest."),
                        A("Problem: what symptom was detected.", "Why: why it affects commissioning or operation.", "Proof: which frames or counters support it.", "Fix: what to change or inspect.", "Retest: how to prove the fix worked."),
                        A("Problem: gejala apa yang terdeteksi.", "Why: mengapa berdampak pada commissioning atau operasi.", "Proof: frame atau counter apa yang mendukung.", "Fix: apa yang perlu diubah atau diperiksa.", "Retest: cara membuktikan perbaikan berhasil."),
                        A(),
                        A()),
                    new HelpSection(
                        "Typical findings",
                        "Finding yang sering muncul",
                        A("The first useful public rules focus on field-proven mistakes: addressing mismatch, unknown IOA, incomplete GI, missing command confirmation, bad quality, and Class 1 pressure."),
                        A("Rule publik awal yang paling berguna fokus pada kesalahan nyata lapangan: addressing mismatch, unknown IOA, GI incomplete, command confirmation hilang, quality buruk, dan tekanan Class 1."),
                        A("CA mismatch: observed ASDU CA differs from expected profile.", "Unknown IOA: point is present in traffic but not in mapping.", "Command slow: response competes with noisy Class 1 traffic.", "GI incomplete: start exists but termination is missing."),
                        A("CA mismatch: CA ASDU yang terlihat berbeda dari profile.", "Unknown IOA: point ada di traffic tetapi tidak ada di mapping.", "Command slow: response bersaing dengan traffic Class 1 yang noisy.", "GI incomplete: start ada tetapi termination hilang."),
                        A(),
                        A()),
                    new HelpSection(
                        "Do not over-trust automation",
                        "Jangan terlalu percaya otomatisasi",
                        A("Smart Findings helps you find likely causes faster. It does not replace engineering judgement, device manual checks, or safe field procedure."),
                        A("Smart Findings membantu menemukan kemungkinan penyebab lebih cepat. Ia tidak menggantikan judgement engineering, pengecekan manual device, atau prosedur lapangan yang aman."),
                        A("Always open the related frame.", "Check the device profile and test condition.", "Retest after each configuration change."),
                        A("Selalu buka frame terkait.", "Cek profile device dan kondisi test.", "Retest setelah setiap perubahan konfigurasi."),
                        A(),
                        A())
                }),
            new(
                "report",
                "PDF",
                "Evidence PDF report", "Evidence PDF report",
                "Create handover-ready reports that explain both conclusion and proof.",
                "Buat report siap handover yang menjelaskan kesimpulan dan proof.",
                "report pdf evidence export fat sat commissioning handover smart findings trace appendix executive summary",
                new[]
                {
                    new HelpSection(
                        "A report should tell a story",
                        "Report harus bercerita",
                        A("A strong commissioning report is not a dump of all rows. It tells what was tested, what was observed, what the important findings are, and which frames prove the conclusion."),
                        A("Report commissioning yang kuat bukan dump semua row. Report harus menceritakan apa yang diuji, apa yang terlihat, finding penting, dan frame mana yang membuktikan kesimpulan."),
                        A("Session summary gives scope.", "Smart Findings gives readable conclusion.", "Protocol evidence gives technical proof.", "Trace appendix keeps the raw audit trail."),
                        A("Session summary memberi scope.", "Smart Findings memberi kesimpulan yang mudah dibaca.", "Protocol evidence memberi proof teknis.", "Trace appendix menjaga raw audit trail."),
                        A(),
                        A()),
                    new HelpSection(
                        "When to export",
                        "Kapan export",
                        A("Export after the session contains enough representative traffic. If the issue is command delay, the report must include command rows. If the issue is CA mismatch, it must include the observed CA evidence."),
                        A("Export setelah session berisi traffic yang cukup representatif. Jika masalahnya command delay, report harus memuat row command. Jika masalahnya CA mismatch, report harus memuat evidence CA yang terlihat."),
                        A("Do not export too early.", "Capture before and after the issue.", "Use notes to preserve test condition.", "Reopen preview before sharing."),
                        A("Jangan export terlalu cepat.", "Capture sebelum dan sesudah masalah.", "Gunakan notes untuk menyimpan kondisi test.", "Buka preview sebelum dibagikan."),
                        A(),
                        A()),
                    new HelpSection(
                        "Report boundary",
                        "Batasan report",
                        A("The report is evidence support, not a certification statement. It helps discussion, FAT/SAT review, and troubleshooting handover."),
                        A("Report adalah evidence support, bukan pernyataan sertifikasi. Report membantu diskusi, review FAT/SAT, dan handover troubleshooting."),
                        A("Avoid claiming conformance certification from one report.", "State the device, profile, date, and test condition.", "Keep raw project evidence if the case is critical."),
                        A("Hindari klaim sertifikasi conformance dari satu report.", "Cantumkan device, profile, tanggal, dan kondisi test.", "Simpan raw evidence project jika case kritikal."),
                        A(),
                        A())
                }),
            new(
                "dual-link",
                "DL",
                "IEC-101 dual-link RTU workflow", "Workflow RTU IEC-101 dual-link",
                "Keep primary and standby link evidence separated during redundancy tests.",
                "Pisahkan evidence primary dan standby link saat uji redundancy.",
                "dual link redundancy primary backup standby rtu iec101 failover link a link b acd class 1 timeline",
                new[]
                {
                    new HelpSection(
                        "The question is not only “does it answer?”",
                        "Pertanyaannya bukan hanya “apakah menjawab?”",
                        A("In dual-link testing, you must know which link answered, whether the standby link remained quiet or supervised, and how long recovery took during failover."),
                        A("Pada test dual-link, Anda harus tahu link mana yang menjawab, apakah standby tetap quiet atau supervised, dan berapa lama recovery saat failover."),
                        A("Separate Link A and Link B evidence.", "Track active/standby role changes.", "Compare first response after failover.", "Check whether Class 1 pressure appears on one or both links."),
                        A("Pisahkan evidence Link A dan Link B.", "Pantau perubahan role active/standby.", "Bandingkan response pertama setelah failover.", "Cek apakah tekanan Class 1 muncul di salah satu atau kedua link."),
                        A(),
                        A()),
                    new HelpSection(
                        "Healthy redundancy pattern",
                        "Pola redundancy yang sehat",
                        A("A clean redundancy test has a clear active link, predictable standby behavior, and a measurable transition when failover is forced."),
                        A("Test redundancy yang bersih memiliki active link yang jelas, perilaku standby yang predictable, dan transisi yang terukur saat failover dipaksa."),
                        A("Primary link handles normal polling.", "Backup link does not fight the primary.", "Failover produces a short, explainable gap.", "Recovered scan pattern becomes stable again."),
                        A("Primary link menangani polling normal.", "Backup link tidak melawan primary.", "Failover menghasilkan gap pendek yang bisa dijelaskan.", "Pola scan setelah recovery stabil lagi."),
                        A("Link A active: Class 2 / GI / events normal", "Link B standby: supervised / no active polling", "Failover: Link A silent, Link B promoted", "Link B active: polling resumes"),
                        A("Link A active: Class 2 / GI / events normal", "Link B standby: supervised / tidak active polling", "Failover: Link A silent, Link B promoted", "Link B active: polling lanjut")),
                    new HelpSection(
                        "What to report",
                        "Yang perlu dilaporkan",
                        A("For dual-link evidence, the timeline matters. A report should show role, link state, silence period, recovered response, and whether the data path stayed correct after failover."),
                        A("Untuk evidence dual-link, timeline sangat penting. Report harus menunjukkan role, status link, periode silent, response recovery, dan apakah data path tetap benar setelah failover."),
                        A("Include both link timelines.", "Keep trace rows grouped by link.", "Mark the failover moment.", "Show first valid response and recovered scan."),
                        A("Sertakan timeline kedua link.", "Kelompokkan trace row berdasarkan link.", "Tandai momen failover.", "Tunjukkan response valid pertama dan scan setelah recovery."),
                        A(),
                        A())
                }),
            new(
                "iec103-relay",
                "103",
                "IEC-103 relay events", "Event relay IEC-103",
                "How to read protection relay event evidence without treating it like IEC-101 points.",
                "Cara membaca evidence event relay proteksi tanpa memperlakukannya seperti point IEC-101.",
                "iec103 relay protection event fun inf fault disturbance time tagged event class 1 class 2",
                new[]
                {
                    new HelpSection(
                        "IEC-103 is relay-oriented",
                        "IEC-103 berorientasi relay",
                        A("IEC-103 is commonly used for protection relay communication. The evidence often focuses on events, time tagged status, fault indications, and relay-specific function/information mapping."),
                        A("IEC-103 umum dipakai untuk komunikasi relay proteksi. Evidence biasanya fokus pada event, status bertimestamp, indikasi gangguan, dan mapping function/information yang spesifik relay."),
                        A("Do not expect the same point model as IEC-101.", "Check relay profile and event interpretation.", "Class 1 often carries important relay events.", "Class 2 may carry background or general data."),
                        A("Jangan mengharapkan model point yang sama seperti IEC-101.", "Cek profile relay dan interpretasi event.", "Class 1 sering membawa event relay penting.", "Class 2 bisa membawa data background atau general."),
                        A(),
                        A()),
                    new HelpSection(
                        "What to check in relay tests",
                        "Yang dicek saat test relay",
                        A("For relay evidence, timestamp, event classification, and mapping are critical. A relay event without correct interpretation can be technically present but useless for commissioning notes."),
                        A("Untuk evidence relay, timestamp, klasifikasi event, dan mapping sangat penting. Event relay tanpa interpretasi benar bisa secara teknis ada tetapi tidak berguna untuk catatan commissioning."),
                        A("Confirm relay address and serial settings.", "Check whether event rows appear in Events and Trace.", "Verify FUN/INF or profile mapping when available.", "Keep raw frame evidence for protection event review."),
                        A("Pastikan address relay dan serial setting.", "Cek apakah row event muncul di Events dan Trace.", "Verifikasi FUN/INF atau profile mapping jika tersedia.", "Simpan raw frame evidence untuk review event proteksi."),
                        A(),
                        A()),
                    new HelpSection(
                        "Common IEC-103 symptoms",
                        "Gejala IEC-103 umum",
                        A("If the relay appears connected but events are not meaningful, the problem may be profile interpretation rather than link health."),
                        A("Jika relay terlihat connected tetapi event tidak bermakna, masalahnya bisa interpretasi profile bukan kesehatan link."),
                        A("Link alive but no event: check polling/class behavior.", "Event exists but label is wrong: check profile mapping.", "Timestamp odd: check relay clock and time interpretation."),
                        A("Link hidup tetapi tidak ada event: cek polling/class behavior.", "Event ada tetapi label salah: cek profile mapping.", "Timestamp aneh: cek clock relay dan interpretasi waktu."),
                        A(),
                        A())
                }),
            new(
                "iec104-session",
                "104",
                "IEC-104 session basics", "Dasar session IEC-104",
                "Read TCP data transfer state, I/S/U frames, and sequence health.",
                "Membaca status data-transfer TCP, I/S/U frame, dan sequence health.",
                "iec104 tcp startdt stopdt testfr i frame s frame u frame sequence number apci asdu",
                new[]
                {
                    new HelpSection(
                        "IEC-104 adds a TCP session layer",
                        "IEC-104 menambahkan layer session TCP",
                        A("IEC-104 carries IEC 60870 ASDUs over TCP, but the TCP connection alone is not enough. Data transfer state and APCI sequence behavior matter."),
                        A("IEC-104 membawa ASDU IEC 60870 lewat TCP, tetapi koneksi TCP saja belum cukup. Status data-transfer dan behavior sequence APCI penting."),
                        A("STARTDT starts data transfer.", "STOPDT stops data transfer.", "TESTFR checks communication health.", "I-frames carry ASDUs and sequence numbers.", "S-frames acknowledge received I-frames."),
                        A("STARTDT memulai data transfer.", "STOPDT menghentikan data transfer.", "TESTFR mengecek kesehatan komunikasi.", "I-frame membawa ASDU dan sequence number.", "S-frame mengakui I-frame yang diterima."),
                        A(),
                        A()),
                    new HelpSection(
                        "Common IEC-104 confusion",
                        "Kebingungan IEC-104 umum",
                        A("A socket can be open while application data is still not flowing. Look for STARTDT confirmation and sequence progress before declaring the session healthy."),
                        A("Socket bisa terbuka sementara application data belum mengalir. Cari STARTDT confirmation dan sequence progress sebelum menyatakan session sehat."),
                        A("TCP connected but no ASDU: check STARTDT.", "Repeated TESTFR: check idle/keepalive behavior.", "Sequence mismatch: check lost or duplicated I-frame path.", "Wrong CA/IOA still looks like mapping issue, just like IEC-101."),
                        A("TCP connected tetapi tidak ada ASDU: cek STARTDT.", "TESTFR berulang: cek idle/keepalive behavior.", "Sequence mismatch: cek jalur I-frame hilang atau duplikat.", "CA/IOA salah tetap terlihat sebagai masalah mapping, seperti IEC-101."),
                        A(),
                        A()),
                    new HelpSection(
                        "How to report IEC-104 evidence",
                        "Cara melaporkan evidence IEC-104",
                        A("Good IEC-104 evidence separates connection health, data-transfer state, ASDU content, and application mapping."),
                        A("Evidence IEC-104 yang baik memisahkan kesehatan koneksi, status data-transfer, isi ASDU, dan mapping aplikasi."),
                        A("Show TCP/session state if available.", "Show STARTDT/STOPDT/TESTFR evidence.", "Keep sequence-related rows.", "Connect ASDU CA/IOA to Values and Events."),
                        A("Tunjukkan status TCP/session jika tersedia.", "Tunjukkan evidence STARTDT/STOPDT/TESTFR.", "Simpan row terkait sequence.", "Hubungkan CA/IOA ASDU ke Values dan Events."),
                        A(),
                        A())
                }),
            new(
                "troubleshooting",
                "!",
                "Troubleshooting recipes", "Resep troubleshooting",
                "Fast symptom-to-checklist guidance for common field problems.",
                "Panduan cepat dari gejala ke checklist untuk masalah lapangan umum.",
                "troubleshooting no response no data unknown ca unknown ioa gi incomplete command timeout bad quality link alive silent",
                new[]
                {
                    new HelpSection(
                        "No response",
                        "Tidak ada response",
                        A("When there is no response, do not start with application mapping. First prove physical/transport and link addressing."),
                        A("Saat tidak ada response, jangan mulai dari application mapping. Buktikan physical/transport dan link addressing lebih dulu."),
                        A("Check cable, port, baud, parity, and TCP reachability.", "Check link address or station address.", "Look for any RX bytes, even invalid ones.", "Reduce scan pressure and test a simple status request."),
                        A("Cek kabel, port, baud, parity, dan TCP reachability.", "Cek link address atau station address.", "Cari byte RX apa pun, bahkan yang invalid.", "Kurangi tekanan scan dan test request status sederhana."),
                        A(),
                        A()),
                    new HelpSection(
                        "Link alive but data silent",
                        "Link hidup tetapi data diam",
                        A("This is usually where address and mapping mistakes hide. The device may answer, but not with the CA/IOA you expect."),
                        A("Di sinilah biasanya kesalahan address dan mapping bersembunyi. Device bisa menjawab, tetapi bukan dengan CA/IOA yang diharapkan."),
                        A("Open Trace and find the first valid ASDU.", "Compare observed CA with configured CA.", "Compare observed IOA with mapping profile.", "Check COT: requested, spontaneous, or interrogation."),
                        A("Buka Trace dan cari ASDU valid pertama.", "Bandingkan CA terlihat dengan CA konfigurasi.", "Bandingkan IOA terlihat dengan mapping profile.", "Cek COT: requested, spontaneous, atau interrogation."),
                        A(),
                        A()),
                    new HelpSection(
                        "Bad or unbelievable value",
                        "Value buruk atau tidak masuk akal",
                        A("If a value exists but looks wrong, separate scaling/mapping problems from protocol quality problems."),
                        A("Jika value ada tetapi terlihat salah, pisahkan masalah scaling/mapping dari masalah quality protokol."),
                        A("Check Type ID and value encoding.", "Check IOA and signal mapping.", "Check quality flags.", "Compare with another known engineering tool or device display."),
                        A("Cek Type ID dan encoding value.", "Cek IOA dan mapping signal.", "Cek quality flags.", "Bandingkan dengan tool engineering lain atau display device."),
                        A(),
                        A())
                }),
            new(
                "evidence-workflow",
                "EV",
                "FAT/SAT evidence workflow", "Workflow evidence FAT/SAT",
                "How to capture enough proof without drowning the report in noise.",
                "Cara menangkap proof yang cukup tanpa membuat report penuh noise.",
                "fat sat commissioning evidence workflow baseline retest proof report handover trace notes",
                new[]
                {
                    new HelpSection(
                        "Start with a baseline",
                        "Mulai dari baseline",
                        A("Before testing abnormal conditions, capture a short healthy baseline. This helps reviewers understand what normal looked like before the fault or failover was introduced."),
                        A("Sebelum menguji kondisi abnormal, capture baseline sehat yang singkat. Ini membantu reviewer memahami seperti apa kondisi normal sebelum fault atau failover dibuat."),
                        A("Record device/protocol/profile.", "Capture normal connection and polling.", "Confirm Values and Events are sane.", "Keep baseline report separate if needed."),
                        A("Catat device/protocol/profile.", "Capture koneksi dan polling normal.", "Pastikan Values dan Events masuk akal.", "Pisahkan baseline report jika perlu."),
                        A(),
                        A()),
                    new HelpSection(
                        "Capture the symptom",
                        "Capture gejala",
                        A("A good issue capture includes before, during, and after. If you only capture the final state, timing and sequence proof are weak."),
                        A("Capture issue yang baik berisi sebelum, saat, dan sesudah. Jika hanya capture final state, proof timing dan urutan menjadi lemah."),
                        A("Mark when the test action happened.", "Keep trace rows around the action.", "Use Smart Findings to summarize the likely cause.", "Export after the session settles."),
                        A("Tandai kapan test action terjadi.", "Simpan trace row di sekitar action.", "Gunakan Smart Findings untuk merangkum kemungkinan penyebab.", "Export setelah session stabil."),
                        A(),
                        A()),
                    new HelpSection(
                        "Retest and compare",
                        "Retest dan bandingkan",
                        A("The strongest engineering evidence is before/after comparison using the same action and same device."),
                        A("Evidence engineering terkuat adalah perbandingan sebelum/sesudah dengan action dan device yang sama."),
                        A("Use same command or event stimulus.", "Compare latency, ACD/Class ratio, and feedback.", "State what changed in the profile or device."),
                        A("Gunakan command atau stimulus event yang sama.", "Bandingkan latency, rasio ACD/Class, dan feedback.", "Tuliskan apa yang berubah di profile atau device."),
                        A(),
                        A())
                }),
            new(
                "glossary",
                "ABC",
                "Quick glossary", "Glosarium cepat",
                "Short explanations for terms you see in the analyzer.",
                "Penjelasan singkat istilah yang terlihat di analyzer.",
                "glossary acd dfc ca ioa cot gi actcon actterm class 1 class 2 type id quality",
                new[]
                {
                    new HelpSection(
                        "Address and object terms",
                        "Istilah address dan object",
                        A("Use this page when reading trace columns quickly."),
                        A("Gunakan halaman ini saat membaca kolom trace secara cepat."),
                        A("Link address: link-layer station address in IEC-101.", "CA: common address of ASDU/application object group.", "IOA: information object address, the point identity.", "Type ID: what kind of information object is carried."),
                        A("Link address: address station link-layer pada IEC-101.", "CA: common address ASDU/grup object aplikasi.", "IOA: information object address, identitas point.", "Type ID: jenis information object yang dibawa."),
                        A(),
                        A()),
                    new HelpSection(
                        "Workflow terms",
                        "Istilah workflow",
                        A("These words explain why data was sent and where the exchange sits in the workflow."),
                        A("Istilah ini menjelaskan mengapa data dikirim dan posisinya dalam workflow."),
                        A("COT: cause of transmission, why the ASDU was sent.", "GI: general interrogation, a request for a wider data snapshot.", "ACTCON: activation confirmation.", "ACTTERM: activation termination."),
                        A("COT: cause of transmission, alasan ASDU dikirim.", "GI: general interrogation, request snapshot data lebih luas.", "ACTCON: activation confirmation.", "ACTTERM: activation termination."),
                        A(),
                        A()),
                    new HelpSection(
                        "Link-control terms",
                        "Istilah link-control",
                        A("Small link-layer bits often explain big field symptoms."),
                        A("Bit kecil di link-layer sering menjelaskan gejala lapangan besar."),
                        A("ACD: secondary has Class 1 data pending.", "DFC: secondary indicates data flow control/busy condition.", "FCB/FCV: master-side frame count control bits used in balanced request behavior.", "Class 1: priority data.", "Class 2: background/process data."),
                        A("ACD: secondary punya data Class 1 pending.", "DFC: secondary menunjukkan data flow control/busy.", "FCB/FCV: bit kontrol frame count sisi master dalam request balanced.", "Class 1: data prioritas.", "Class 2: data background/process."),
                        A(),
                        A())
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
            "addressing" => "IEC-101",
            "acd-dfc" => "IEC-101",
            "class-polling" => "IEC-101",
            "general-interrogation" => "IEC-101",
            "command-flow" => "Commands",
            "command-timeout" => "Troubleshooting",
            "values" => "Values",
            "events" => "Events",
            "quality-flags" => "Protocol",
            "smart-findings" => "Smart Findings",
            "report" => "Report",
            "dual-link" => "Redundancy",
            "iec103-relay" => "IEC-103",
            "iec104-session" => "IEC-104",
            "troubleshooting" => "Troubleshooting",
            "evidence-workflow" => "Workflow",
            "glossary" => "Reference",
            _ => "Help"
        };

        public string CategoryId => Key switch
        {
            "overview" => "Bantuan Cepat",
            "setup" => "Koneksi",
            "frame-trace" => "Trace",
            "addressing" => "IEC-101",
            "acd-dfc" => "IEC-101",
            "class-polling" => "IEC-101",
            "general-interrogation" => "IEC-101",
            "command-flow" => "Command",
            "command-timeout" => "Troubleshooting",
            "values" => "Values",
            "events" => "Events",
            "quality-flags" => "Protokol",
            "smart-findings" => "Smart Findings",
            "report" => "Report",
            "dual-link" => "Redundancy",
            "iec103-relay" => "IEC-103",
            "iec104-session" => "IEC-104",
            "troubleshooting" => "Troubleshooting",
            "evidence-workflow" => "Workflow",
            "glossary" => "Referensi",
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
