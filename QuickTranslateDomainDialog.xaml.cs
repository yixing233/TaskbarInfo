using System.Windows;

namespace TaskbarInfo;

public partial class QuickTranslateDomainDialog : Window
{
    private readonly HashSet<string> _existingDomains;

    public string Domain { get; private set; } = string.Empty;

    public QuickTranslateDomainDialog(IEnumerable<string>? existingDomains)
    {
        _existingDomains = new HashSet<string>(
            TranslationDomainCatalog.Normalize(existingDomains),
            StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
        Loaded += (_, _) => DomainTextBox.Focus();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!TranslationDomainCatalog.TryNormalizeCustomDomain(DomainTextBox.Text, out string domain))
        {
            ShowValidation("请输入 1 到 40 个字符的领域名称，通用领域无需重复添加。");
            return;
        }

        if (_existingDomains.Contains(domain))
        {
            ShowValidation("该领域已存在，请直接从领域列表中选择。");
            return;
        }

        Domain = domain;
        DialogResult = true;
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
